using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEngine;
using static AssetLock.Editor.AssetLockUtility;
using static AssetLock.Editor.AssetLockSettings;
using static AssetLock.Editor.UI.ALGUI;
using static UnityEditor.EditorGUILayout;
using static UnityEditor.EditorStyles;

namespace AssetLock.Editor.UI
{
	/// <summary>
	/// Custom editor window for managing locks.
	/// </summary>
	public class LockBrowser : EditorWindow
	{
		private static bool s_busy;

		private string m_searchInput = string.Empty;
		private bool m_force;

		private Dictionary<DirectoryReference, bool> m_expanded = new();
		private Dictionary<FileReference, bool> m_busy = new();
		private Dictionary<DirectoryReference, bool> m_dirBusy = new();

		[MenuItem(Constants.BROWSER_MENU_PATH)]
		private static void ShowWindow()
		{
			var window = GetWindow<LockBrowser>();
			window.titleContent = EditorGUIUtility.TrTextContentWithIcon(
				Constants.BROWSER_TITLE,
				Constants.BROWSER_ICON
			);
			window.Show();
		}

		private void OnGUI()
		{
			InfoGUI();
			ActionsGUI();
			Separator();
			FilesGUI();
		}

		private void InfoGUI()
		{
			GUIContent labelContent = new GUIContent("File Count:");
			GUIContent trackedCount = new GUIContent(
				AssetLockManager.Instance.TrackedCount.ToString(),
				"Total number of files being tracked by Asset Lock"
			);
			GUIContent lockedCount = new GUIContent(
				AssetLockManager.Instance.LockedCount.ToString(),
				"Total number of files currently locked by Asset Lock"
			);
			GUIContent userLockCount = new GUIContent(
				AssetLockManager.Instance.LockedByMeCount.ToString(),
				"Total number of files locked by the current user"
			);
			GUIContent otherLockCount = new GUIContent(
				AssetLockManager.Instance.LockedByOthersCount.ToString(),
				"Total number of files locked by other users"
			);
			GUIContent unlockedCount = new GUIContent(
				AssetLockManager.Instance.UnlockedCount.ToString(),
				"Total number of files that are not locked"
			);

			GUIContent statusLabel = new GUIContent("Master Status:", "Enable or disable all Asset Lock features");
			GUIContent autoLockLabel = new GUIContent("Auto-Lock:", "Automatically lock files when opening them");

			GUILayout.Label("Info:", boldLabel);
			EditorGUI.indentLevel++;

			LabelAndToggleButton(statusLabel, MasterEnable);
			LabelAndToggleButton(autoLockLabel, AutoLock);

			using (new HorizontalScope())
			{
				Indent();
				GUILayout.Label(labelContent);
				GUILayout.FlexibleSpace();
				GUILayout.Label(lockedCount);
				GUILayout.Label("(");
				GUILayout.Label(userLockCount, new GUIStyle(label) { normal = { textColor = Color.green } });
				GUILayout.Label(" / ");
				GUILayout.Label(otherLockCount, new GUIStyle(label) { normal = { textColor = Color.red } });
				GUILayout.Label(") / ");
				GUILayout.Label(unlockedCount, new GUIStyle(label) { normal = { textColor = Color.cyan } });
				GUILayout.Label(" / ");
				GUILayout.Label(trackedCount, new GUIStyle(label) { normal = { textColor = Color.yellow } });
			}

			EditorGUI.indentLevel--;
		}

		private void ActionsGUI()
		{
			GUIContent refreshLabel = new GUIContent("Refresh All", "Refresh the lock status of all tracked files");
			GUIContent parseLabel = new GUIContent("Parse All", "Parse all files in the project for tracking");
			GUIContent unlockAllLabel = new GUIContent("Unlock All", "Unlock all files locked by you");
			GUIContent forceRefreshLabel = new GUIContent(
				"Force Refresh",
				"Force a refresh of all tracked files (May be slow)"
			);
			GUIContent rebootLabel = new GUIContent("Reboot", "Reboot the AssetLockManager");
			GUIContent expandAllLabel = new GUIContent("Expand All", "Expand all directories");
			GUIContent collapseAllLabel = new GUIContent("Collapse All", "Collapse all directories");

			GUILayout.Label("Actions:", boldLabel);
			EditorGUI.BeginDisabledGroup(s_busy);

			using (new HorizontalScope())
			{
				if (GUILayout.Button(refreshLabel))
				{
					DoAsync(() => AssetLockManager.Instance.RefreshAsync());
				}

				if (GUILayout.Button(parseLabel))
				{
					AssetLockManager.Instance.ParseAll();
				}

				if (GUILayout.Button(unlockAllLabel))
				{
					AssetLockManager.Instance.UnlockAll();
				}
			}

			if (m_force)
			{
				using (new HorizontalScope())
				{
					if (GUILayout.Button(forceRefreshLabel))
					{
						AssetLockManager.Instance.ForceRefresh();
					}

					if (GUILayout.Button(rebootLabel))
					{
						AssetLockManager.Reboot();
					}
				}
			}

			EditorGUI.EndDisabledGroup();

			using (new HorizontalScope())
			{
				if (GUILayout.Button(expandAllLabel))
				{
					SetAllExpanded(true);
				}

				if (GUILayout.Button(collapseAllLabel))
				{
					SetAllExpanded(false);
				}
			}

			GUIContent forceLabel = new GUIContent("Show Force Controls", "Show force lock/unlock controls");
			m_force = Toggle(forceLabel, m_force);
		}

		private void FilesGUI()
		{
			GUIContent refreshLabel = EditorGUIUtility.IconContent("d_Refresh");

			using (new HorizontalScope())
			{
				EditorGUI.BeginChangeCheck();
				m_searchInput = DelayedTextField("Search: ", m_searchInput);

				if (EditorGUI.EndChangeCheck() || !m_expanded.Any())
				{
					ParseDirectory(AssetLockManager.Instance.ProjectDir, m_searchInput, out _);
				}

				if (GUILayout.Button(refreshLabel, GUILayout.Width(30)))
				{
					m_busy.Clear();
					m_dirBusy.Clear();
					ParseDirectory(AssetLockManager.Instance.ProjectDir, m_searchInput, out _);
				}
			}

			EditorGUI.indentLevel++;

			DisplayFolder(AssetLockManager.Instance.ProjectDir, m_searchInput);

			EditorGUI.indentLevel--;
		}

		private static async void DoAsync(Func<Task> action)
		{
			s_busy = true;
			await action();
			s_busy = false;
		}

		private void SetAllExpanded(bool value)
		{
			foreach (var key in m_expanded.Keys.ToList())
			{
				m_expanded[key] = value;
			}
		}

		private bool ParseDirectory(DirectoryReference dir, string ctx, out bool valid)
		{
			if (!dir.Exists)
			{
				m_expanded.Remove(dir);

				valid = false;

				return false;
			}

			valid = dir.Files.Any(f => f.Tracked);
			bool open = IsDirectoryMatching(dir, ctx);

			if (!open)
			{
				open |= dir.EnumerateTrackedChildFiles().Any(f => IsFileMatching(f, ctx));
			}

			foreach (var directory in dir.Directories)
			{
				open |= ParseDirectory(directory, ctx, out var childValid);
				valid |= childValid;
			}

			if (valid)
			{
				m_expanded[dir] = open;
			}
			else
			{
				m_expanded.Remove(dir);
			}

			return open;
		}

		private IEnumerable<DirectoryReference> GetMatchingDirectories(DirectoryReference dir)
		{
			if (!dir.Exists)
			{
				yield break;
			}

			foreach (var directory in dir.Directories)
			{
				if (m_expanded.ContainsKey(directory))
				{
					yield return directory;
				}
			}
		}

		private bool IsFileMatching(FileReference file, string ctx)
		{
			return MatchSearchGroups(ctx, file.NameWithExtension);
		}

		private bool IsDirectoryMatching(DirectoryReference dir, string ctx)
		{
			return MatchSearchGroups(ctx, dir.Name);
		}

		private void DisplayFolder(DirectoryReference dir, string ctx, string prefix = "")
		{
			if (!dir.Exists)
			{
				return;
			}

			string key = prefix + dir.Name;

			if (!m_expanded.TryGetValue(dir, out var exp))
			{
				return;
			}

			GUIContent nameLabel = new GUIContent(prefix + dir.Name, dir.AbsolutePath);
			GUIContent refreshLabel = new GUIContent(
				"Refresh",
				"Refresh the lock status of all files in this directory"
			);
			GUIContent lockLabel = new GUIContent("Lock All", "Lock all files in this directory");
			GUIContent unlockLabel = new GUIContent("Unlock All", "Unlock all files in this directory");

			var subdirs = GetMatchingDirectories(dir).ToList();
			var files = dir.EnumerateTrackedChildFiles().Distinct().ToList();

			if (files.Count == 0 && subdirs.Count == 1)
			{
				// combine single child directory with parent
				DisplayFolder(subdirs.Single(), ctx, key + "/");

				return;
			}

			bool dirMatch = IsDirectoryMatching(dir, ctx);

			using (new HorizontalScope())
			{
				exp = Foldout(exp, nameLabel);
				GUILayout.FlexibleSpace();

				if (!m_dirBusy.TryGetValue(dir, out var busy))
				{
					m_dirBusy[dir] = false;
				}

				if (files.Any())
				{
					EditorGUI.BeginDisabledGroup(busy);

					if (GUILayout.Button(refreshLabel))
					{
						DoDirAction(dir.RefreshChildrenLocksAsync);
					}

					if (GUILayout.Button(lockLabel))
					{
						DoDirAction(() => dir.LockChildrenAsync(m_force));
					}

					if (GUILayout.Button(unlockLabel))
					{
						DoDirAction(() => dir.UnlockChildrenAsync(m_force));
					}

					EditorGUI.EndDisabledGroup();
				}
			}

			m_expanded[dir] = exp;

			if (!exp)
			{
				return;
			}

			EditorGUI.indentLevel += 2;

			foreach (var subdir in subdirs)
			{
				DisplayFolder(subdir, ctx);
			}

			foreach (var file in files)
			{
				if (file.IsMeta)
				{
					continue;
				}

				// if the directory matches the search, display all files
				DisplayLock(file.Lock, dirMatch ? string.Empty : ctx, m_dirBusy[dir]);
			}

			EditorGUI.indentLevel -= 2;

			async void DoDirAction(Func<Task> action)
			{
				m_dirBusy[dir] = true;
				await action();
				m_dirBusy[dir] = false;
			}
		}

		private void DisplayLock(FileReference file, string ctx, bool forceBusy = false)
		{
			if (!MatchSearchGroups(ctx, file.NameWithExtension))
			{
				return;
			}

			if (!file.TryGetLock(out var info))
			{
				return;
			}

			GUIContent nameLabel = new GUIContent(file.NameWithExtension, file.AssetPath);
			GUIContent refreshLabel = new GUIContent("Refresh", "Refresh the lock status of this file");
			GUIContent lockLabel = new GUIContent("Lock", "Lock this file");
			GUIContent unlockLabel = new GUIContent("Unlock", "Unlock this file");
			GUIContent lockedLabel = new GUIContent("Locked", "This file is locked");
			GUIContent ownerLabel = new GUIContent(info.owner, info.lockedAt);
			GUIContent meLabel = new GUIContent("Me", info.lockedAt);
			GUIContent forceLockLabel = new GUIContent(
				"Force Lock",
				"Requires administrator privileges for this git repository"
			);
			GUIContent forceUnlockLabel = new GUIContent(
				"Force Unlock",
				"Requires administrator privileges for this git repository"
			);

			using (new HorizontalScope())
			{
				Indent();

				if (GUILayout.Button(nameLabel, label))
				{
					Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(info.path);
				}

				GUILayout.FlexibleSpace();
				DrawLockStatus();

				if (!m_busy.TryGetValue(file, out var busy))
				{
					m_busy[file] = false;
				}

				busy |= forceBusy;

				EditorGUI.BeginDisabledGroup(busy);

				if (m_force)
				{
					if (GUILayout.Button(forceLockLabel))
					{
						DoFileAction(() => file.LockFileAsync(true));
					}

					if (GUILayout.Button(forceUnlockLabel))
					{
						DoFileAction(() => file.UnlockFileAsync(true));
					}
				}
				else if (info.locked)
				{
					if (info.LockedByMe)
					{
						if (GUILayout.Button(unlockLabel))
						{
							DoFileAction(() => file.UnlockFileAsync());
						}
					}
				}
				else
				{
					if (GUILayout.Button(lockLabel))
					{
						DoFileAction(() => file.LockFileAsync());
					}
				}

				if (GUILayout.Button(refreshLabel))
				{
					DoFileAction(file.RefreshLockAsync);
				}

				EditorGUI.EndDisabledGroup();
			}

			void DrawLockStatus()
			{
				if (!m_busy.TryGetValue(file, out var busy))
				{
					m_busy[file] = false;
				}

				if (busy)
				{
					GUILayout.Label("Busy", new GUIStyle(label) { normal = { textColor = Color.yellow } });
				}
				else if (info.locked)
				{
					GUILayout.Label(lockedLabel, new GUIStyle(label) { normal = { textColor = Color.red } });
					GUILayout.Label(info.LockedByMe ? meLabel : ownerLabel);
				}
				else
				{
					GUILayout.Label("Unlocked", new GUIStyle(label) { normal = { textColor = Color.green } });
				}

				GUILayout.FlexibleSpace();
			}

			async void DoFileAction(Func<Task> action)
			{
				m_busy[file] = true;
				await action();
				m_busy[file] = false;
			}
		}
	}
}