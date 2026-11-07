using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEngine;

namespace AssetLock.Editor
{
	// The pre-push hook cannot know whether the subsequent push succeeds. It only
	// records proposed updates; the editor never releases locks without a click.
	[InitializeOnLoad]
	internal static class PushUnlockPrompt
	{
		private const string OriginalSuffix = ".assetlock-original";
		private const string Hook = "#!/bin/sh\n" +
			"# AssetLock cooperative pre-push hook v1\n" +
			"dir=$(dirname \"$0\")\n" +
			"if [ ! -f \"$dir/pre-push.assetlock-original\" ]; then echo >&2 'AssetLock: original pre-push hook is missing; restore it before pushing.'; exit 1; fi\n" +
			"events=$(git rev-parse --git-path assetlock-push-events 2>/dev/null)\n" +
			"if [ -z \"$events\" ] || ! mkdir -p \"$events\" 2>/dev/null || ! tmp=$(mktemp \"$events/.push.XXXXXXXX\" 2>/dev/null); then\n" +
			"  \"$dir/pre-push.assetlock-original\" \"$@\"; exit $?\n" +
			"fi\n" +
			"if ! cat > \"$tmp\"; then rm -f \"$tmp\"; echo >&2 'AssetLock: could not read pre-push input.'; exit 1; fi\n" +
			"\"$dir/pre-push.assetlock-original\" \"$@\" < \"$tmp\"\n" +
			"status=$?\n" +
			"if [ \"$status\" -ne 0 ]; then rm -f \"$tmp\"; exit \"$status\"; fi\n" +
			"mv \"$tmp\" \"$tmp.event\" 2>/dev/null || rm -f \"$tmp\"\n" +
			"exit 0\n";

		private static readonly DateTime Started = DateTime.UtcNow;
		private static DateTime s_acceptAfter = Started;
		private static string s_eventsPath;
		private static bool s_resolving;
		private static bool s_prompting;
		private static double s_nextPoll;

		static PushUnlockPrompt()
		{
			if (!AssetLockSettings.TestMode && !Application.isBatchMode) EditorApplication.update += Poll;
		}

		private static string ProjectPath => Path.GetDirectoryName(Application.dataPath);

		private static async Task<string> GitPathAsync(string name)
		{
			var result = await ProcessInstance.RunAsync(AssetLockSettings.GitPath.value, ProjectPath, default,
				"rev-parse", "--git-path", name);
			if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
				throw new InvalidOperationException("Could not find the Git hook path for this project: " + result.StdErr);
			string path = result.StdOut.Trim();
			return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectPath, path));
		}

		internal static async Task<bool> IsInstalledAsync()
		{
			try { var path = await GitPathAsync("hooks/pre-push"); return File.Exists(path) && File.ReadAllText(path) == Hook; }
			catch { return false; }
		}

		internal static async Task<string> InstallAsync()
		{
			if (AssetLockManager.Instance == null || !AssetLockManager.Instance.IsReady)
				throw new InvalidOperationException("Initialize AssetLock and Git LFS before installing the push hook.");
			string path = await GitPathAsync("hooks/pre-push");
			return Install(path, ProjectPath);
		}

		internal static string Install(string path, string workingPath)
		{
			string original = path + OriginalSuffix;
			if (File.Exists(path) && File.ReadAllText(path) == Hook) return "Push hook is already installed.";
			if (File.Exists(original)) throw new InvalidOperationException("The original-hook backup already exists. Resolve it before installing the AssetLock hook.");
			if (!File.Exists(path)) throw new InvalidOperationException("No pre-push hook was found. Install Git LFS hooks before installing the AssetLock hook.");
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			bool moved = false;
			try
			{
				if (File.Exists(path)) { File.Move(path, original); moved = true; }
				File.WriteAllText(path, Hook, new UTF8Encoding(false));
				if (Path.DirectorySeparatorChar != '\\')
				{
					var chmod = ProcessInstance.Run("chmod", workingPath, "+x", path);
					if (chmod.ExitCode != 0) throw new InvalidOperationException("Could not make the hook executable: " + chmod.StdErr);
				}
			}
			catch
			{
				if (File.Exists(path)) File.Delete(path);
				if (moved) File.Move(original, path);
				throw;
			}
			s_eventsPath = null;
			return "Push hook installed. Enable Prompt to Unlock on Push to receive prompts in this project.";
		}

		internal static async Task<string> RemoveAsync()
		{
			string path = await GitPathAsync("hooks/pre-push");
			return Remove(path);
		}

		internal static string Remove(string path)
		{
			string original = path + OriginalSuffix;
			if (!File.Exists(path) || File.ReadAllText(path) != Hook)
				throw new InvalidOperationException("The installed hook is missing or has changed; no files were removed. Check the hook and backup manually.");
			if (!File.Exists(original)) throw new InvalidOperationException("The original-hook backup is missing; no files were removed. Restore the Git LFS hook manually.");
			string temporary = path + ".assetlock-removing";
			if (File.Exists(temporary)) throw new InvalidOperationException("An AssetLock hook removal file already exists; resolve it manually.");
			File.Move(path, temporary);
			try { File.Move(original, path); }
			catch { File.Move(temporary, path); throw; }
			File.Delete(temporary);
			return "Push hook removed and the previous hook restored.";
		}

		internal static List<string> MatchingBranches(string input, IEnumerable<string> configured)
		{
			var branches = new HashSet<string>((configured ?? Enumerable.Empty<string>()).Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()), StringComparer.Ordinal);
			var matches = new HashSet<string>(StringComparer.Ordinal);
			foreach (string line in (input ?? "").Split('\n'))
			{
				var fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
				if (fields.Length != 4 || fields[0] == "(delete)" || fields[1].All(c => c == '0') || !fields[2].StartsWith("refs/heads/", StringComparison.Ordinal)) continue;
				string branch = fields[2].Substring("refs/heads/".Length);
				if (branches.Contains(branch)) matches.Add(branch);
			}
			return matches.OrderBy(b => b, StringComparer.Ordinal).ToList();
		}

		private static async void Poll()
		{
			if (EditorApplication.timeSinceStartup < s_nextPoll || s_resolving || s_prompting || PushUnlockWindow.IsOpen) return;
			s_nextPoll = EditorApplication.timeSinceStartup + 1;
			if (!AssetLockSettings.EnablePushUnlockPrompts || !AssetLockSettings.MasterEnable || AssetLockSettings.SuppressPushUnlockPrompts)
			{
				s_acceptAfter = DateTime.UtcNow;
				return;
			}
			var manager = AssetLockManager.Instance;
			if (manager == null || !manager.IsReady) { s_acceptAfter = DateTime.UtcNow; return; }
			if (s_eventsPath == null)
			{
				s_resolving = true;
				try { s_eventsPath = await GitPathAsync("assetlock-push-events"); }
				catch { s_nextPoll = EditorApplication.timeSinceStartup + 30; }
				finally { s_resolving = false; }
				return;
			}
			try
			{
				if (!Directory.Exists(s_eventsPath)) return;
				foreach (string file in Directory.GetFiles(s_eventsPath, "*.event"))
				{
					bool recent = File.GetLastWriteTimeUtc(file) >= s_acceptAfter;
					string input = File.ReadAllText(file);
					File.Delete(file);
					var branches = MatchingBranches(input, AssetLockSettings.PushUnlockBranches.value);
					if (!recent || branches.Count == 0 || AssetLockSettings.SuppressPushUnlockPrompts) continue;
					s_prompting = true;
					try
					{
						if (manager.AutomaticRefreshPaused) continue;
						await manager.RefreshAsync();
						if (manager.IsReady && !manager.AutomaticRefreshPaused && AssetLockSettings.EnablePushUnlockPrompts && !AssetLockSettings.SuppressPushUnlockPrompts)
							PushUnlockWindow.ShowPrompt(branches, manager);
					}
					finally { s_prompting = false; }
					break;
				}
			}
			catch (Exception error) { AssetLockUtility.Logging.LogWarning("Push unlock prompt could not be processed: " + error.Message); }
		}
	}

	internal sealed class PushUnlockWindow : EditorWindow
	{
		internal static bool IsOpen { get; private set; }
		private AssetLockManager m_manager;
		private string m_branches;
		private bool m_suppress;

		internal static void ShowPrompt(List<string> branches, AssetLockManager manager)
		{
			var window = CreateInstance<PushUnlockWindow>();
			window.titleContent = new GUIContent("Unlock after push?");
			window.m_manager = manager;
			window.m_branches = string.Join(", ", branches);
			window.minSize = new Vector2(460, 210);
			window.maxSize = new Vector2(600, 260);
			IsOpen = true;
			window.ShowModalUtility();
		}

		private void OnDestroy() { IsOpen = false; }

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Push detected: " + m_branches, EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Git reports this push before it completes. Confirm the push succeeded before unlocking. Unlocking releases ALL locks currently owned by you, including files outside this push.", MessageType.Warning);
			m_suppress = EditorGUILayout.ToggleLeft("Don't show again (always keep locks)", m_suppress);
			GUILayout.FlexibleSpace();
			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Keep Locks")) Finish(false);
				using (new EditorGUI.DisabledScope(m_manager == null || !m_manager.IsReady || m_manager.AutomaticRefreshPaused))
					if (GUILayout.Button("Unlock All My Locks")) Finish(true);
			}
		}

		private void Finish(bool unlock)
		{
			ApplyChoice(m_suppress, unlock, m_manager);
			Close();
		}

		internal static void ApplyChoice(bool suppress, bool unlock, AssetLockManager manager)
		{
			if (suppress) AssetLockSettings.SuppressPushUnlockPrompts.SetValue(true);
			if (unlock && manager != null && manager == AssetLockManager.Instance && manager.IsReady && !manager.AutomaticRefreshPaused)
				manager.UnlockAll();
		}
	}
}
