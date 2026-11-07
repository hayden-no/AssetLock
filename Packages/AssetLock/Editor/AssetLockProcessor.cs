using System;
using System.Collections.Generic;
using System.Linq;
using AssetLock.Editor.Data;
using AssetLock.Editor.Manager;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;
using static AssetLock.Editor.AssetLockUtility;
using FileMode = UnityEditor.VersionControl.FileMode;

namespace AssetLock.Editor
{
	/// <summary>
	/// How the Unity Editor interacts with AssetLock.
	/// </summary>
	public class AssetLockProcessor : AssetModificationProcessor
	{
		private static bool IsActive => MasterEnable && AssetLockManager.Instance?.IsReady == true;
		private static readonly Dictionary<string, double> s_autoLockTimes = new();
		private static readonly HashSet<string> s_autoLockPending = new();
		private static readonly Dictionary<string, string> s_dialogFailures = new();

		private static bool CanOpenForEdit(
			string[] assetOrMetaFilePaths,
			List<string> outNotEditablePaths,
			StatusQueryOptions statusQueryOptions
		)
		{
			using var profiler = new Logging.Profiler(message: string.Join('\n', assetOrMetaFilePaths));

			if (!IsActive) return true;

			if (WarnOnly)
			{
				WarnForPaths(assetOrMetaFilePaths, "open", false);

				return true;
			}

			HandleRefresh(statusQueryOptions);
			var result = true;

			foreach (var path in GetAllBinaryPaths(assetOrMetaFilePaths))
			{
				if (!GetLock(path, false, out var info))
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
				else if (info is { locked: true, LockedByMe: false })
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
			}

			if (outNotEditablePaths.Count > 0)
				Logging.LogWarningFormat(
					"Cannot open for edit the following locked assets: {0}",
					string.Join(", ", outNotEditablePaths)
				);

			return result;
		}

		private static bool IsOpenForEdit(
			string[] assetOrMetaFilePaths,
			List<string> outNotEditablePaths,
			StatusQueryOptions statusQueryOptions
		)
		{
			using var profiler = new Logging.Profiler(message: string.Join('\n', assetOrMetaFilePaths));

			if (!IsActive) return true;

			if (WarnOnly)
			{
				WarnForPaths(assetOrMetaFilePaths, "edit", true);

				return true;
			}

			HandleRefresh(statusQueryOptions);
			var result = true;

			foreach (var path in GetAllBinaryPaths(assetOrMetaFilePaths))
			{
				if (!GetLock(path, false, out var info))
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
				else if (!info.LockedByMe)
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
			}

			return result;
		}

		private static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths)
		{
			using var profiler = new Logging.Profiler(message: string.Join('\n', paths));

			if (!IsActive) return true;

			if (WarnOnly)
			{
				WarnForPaths(paths, "edit", true, true);

				return true;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			var result = true;

			foreach (var path in GetAllBinaryPaths(paths))
			{
				if (!GetLock(path, UseBlockingCallsInProcessor, out var info))
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
				else if (info is { locked: true, LockedByMe: false })
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
				else if (!TryAutoLock(info, false, "edit"))
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
			}

			return result;
		}

		private static string[] OnWillSaveAssets(string[] paths)
		{
			using var profiler = new Logging.Profiler(message: string.Join('\n', paths));

			if (!IsActive) return paths;

			if (WarnOnly)
			{
				WarnForPaths(paths, "save", true, true);

				return paths;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			var unsaved = new HashSet<FileReference>();

			foreach (var path in GetAllBinaryPaths(paths))
			{
				if (!GetLock(path, true, out var info))
					unsaved.Add(path);
				else if (info is { locked: true, LockedByMe: false })
					unsaved.Add(path);
				else if (!TryAutoLock(info, true, "save")) unsaved.Add(path);
			}

			if (unsaved.Any())
				Logging.LogWarningFormat(
					"Unsaved assets: {0}",
					string.Join(", ", unsaved.Select(file => file.UnityPath))
				);
			else
				return paths;

			return paths.Where(path => !unsaved.Contains(FileReference.FromPath(path))).ToArray();
		}

		private static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
		{
			using var profiler = new Logging.Profiler(message: assetPath);

			if (!IsActive) return AssetDeleteResult.DidNotDelete;

			if (WarnOnly)
			{
				WarnForPaths(new[] { assetPath }, "delete", true, true);

				return AssetDeleteResult.DidNotDelete;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			var path = FileReference.FromPath(assetPath);

			if (!ShouldTrack(path)) return AssetDeleteResult.DidNotDelete;

			if (!GetLock(path, true, out var info))
			{
				return AssetDeleteResult.FailedDelete;
			}
			else if (info is { locked: true, LockedByMe: false })
			{
				LogReferenceWarningFormat(
					path,
					"Cannot delete locked asset {0} because it is locked by {1}.",
					assetPath,
					info.owner
				);

				return AssetDeleteResult.FailedDelete;
			}
			else if (!info.LockedByMe && !TryAutoLock(info, true, "delete"))
			{
				return AssetDeleteResult.FailedDelete;
			}

			return AssetDeleteResult.DidNotDelete;
		}

		private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
		{
			using var profiler = new Logging.Profiler(message: sourcePath);

			if (!IsActive) return AssetMoveResult.DidNotMove;

			if (WarnOnly)
			{
				WarnForPaths(new[] { sourcePath }, "move", true, true);

				return AssetMoveResult.DidNotMove;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			var path = FileReference.FromPath(sourcePath);

			if (!path.ShouldTrack) return AssetMoveResult.DidNotMove;

			if (!GetLock(path, true, out var info))
			{
				return AssetMoveResult.FailedMove;
			}
			else if (info is { locked: true, LockedByMe: false })
			{
				LogReferenceWarningFormat(
					path,
					"Cannot move locked asset {0} because it is locked by {1}.",
					sourcePath,
					info.owner
				);

				return AssetMoveResult.FailedMove;
			}
			else if (!TryAutoLock(info, true, "move"))
			{
				return AssetMoveResult.FailedMove;
			}

			AssetLockManager.Instance.RegisterMove(path, destinationPath);

			return AssetMoveResult.DidNotMove;
		}

		private static void FileModeChanged(string[] paths, FileMode mode)
		{
			using var profiler = new Logging.Profiler(message: string.Join('\n', paths));

			if (!IsActive || AssetLockManager.Instance.AutomaticRefreshPaused) return;

			foreach (var path in GetAllBinaryPaths(paths))
			{
				if ((mode & FileMode.Text) != 0)
					path.UnlockFile();
				else if ((mode & FileMode.Binary) != 0) path.TrackFile();

				LogReferenceVerboseFormat(path, "File mode changed for {0} to {1}", path, mode);
			}
		}

		private static void OnStatusUpdated()
		{
			if (!IsActive) return;

			AssetLockManager.Instance.RequestAutomaticRefresh();
		}

		private static bool GetLock(FileReference reference, bool blocking, out LockInfo info)
		{
			if (reference.TryGetLock(out info)) return true;
			if (AssetLockManager.Instance.AutomaticRefreshPaused) return false;

			if (blocking)
			{
				if (!reference.TrackFileAsync().Wait(Constants.DEFAULT_LOCK_TIMEOUT)) return false;

				return GetLock(reference, false, out info);
			}
			else
			{
				reference.TrackFile();
			}

			return false;
		}

		private static void WarnForPaths(string[] paths, string action, bool requireOwned, bool autoLock = false)
		{
			AssetLockManager.Instance.RequestAutomaticRefresh();

			foreach (var file in GetAllBinaryPaths(paths))
			{
				var known = file.TryGetLock(out var info);

				if (known && info.LockedByMe)
				{
					s_autoLockTimes.Remove(file.AbsolutePath);

					continue;
				}

				if (autoLock && AutoLock && !s_autoLockPending.Contains(file.AbsolutePath))
				{
					var now = EditorApplication.timeSinceStartup;

					if (s_autoLockTimes.TryGetValue(file.AbsolutePath, out var lastAttempt) && now - lastAttempt < 15)
						continue;

					if (s_autoLockTimes.Count >= 1024) s_autoLockTimes.Clear();
					s_autoLockTimes[file.AbsolutePath] = now;

					if (AssetLockManager.Instance.AutomaticRefreshPaused)
					{
						AutoLockCompleted(file, "Lock refresh is paused. Check credentials and refresh to retry.");
					}
					else
					{
						s_autoLockPending.Add(file.AbsolutePath);
						AssetLockManager.Instance.QueueWarningAutoLock(file, AutoLockCompleted);
					}
				}
			}
		}

		private static void AutoLockCompleted(FileReference file, string failure)
		{
			s_autoLockPending.Remove(file.AbsolutePath);
			InternalEditorUtility.RepaintAllViews();

			if (failure == null || SuppressAutoLockFailureDialogs || Application.isBatchMode || TestMode) return;

			s_dialogFailures[file.UnityPath] = failure;
			EditorApplication.delayCall -= ShowAutoLockFailures;
			EditorApplication.delayCall += ShowAutoLockFailures;
		}

		private static void ShowAutoLockFailures()
		{
			if (s_dialogFailures.Count == 0) return;

			var details = string.Join("\n", s_dialogFailures.Select(pair => $"{pair.Key}: {pair.Value}"));
			s_dialogFailures.Clear();

			if (SuppressAutoLockFailureDialogs) return;

			if (EditorUtility.DisplayDialogComplex(
					"AssetLock: Automatic Lock Failed",
					"Editing is allowed, but these files could not be locked:\n\n" + details,
					"OK",
					"Don't show again",
					""
				) ==
				1)
				SuppressAutoLockFailureDialogs.SetValue(true);
		}

		private static bool TryAutoLock(FileReference reference, bool blocking = true, string action = "modify")
		{
			if (!IsActive) return false;

			if (reference.TryGetLock(out var current) && current.LockedByMe) return true;
			if (AssetLockManager.Instance.AutomaticRefreshPaused) return false;

			if (AutoLock)
			{
				if (!blocking)
				{
					reference.LockFile();
				}
				else
				{
					if (!reference.LockFileAsync().Wait(Constants.DEFAULT_LOCK_TIMEOUT) ||
						!reference.TryGetLock(out var acquired) ||
						!acquired.LockedByMe)
						return false;
				}

				LogReferenceFormat(
					reference,
					"Automatically locked asset {0} because it is binary.",
					reference.UnityPath
				);

				return true;
			}
			else
			{
				LogReferenceWarningFormat(
					reference,
					"Cannot {0} lockable asset {1}. Please check it out first!",
					action,
					reference.UnityPath
				);
			}

			return false;
		}

		private static void HandleRefresh(StatusQueryOptions option)
		{
			using var profiler = new Logging.Profiler(message: $"Query Type: {option}");

			switch (option)
			{
				case StatusQueryOptions.ForceUpdate:
					AssetLockManager.Instance.RequestAutomaticRefresh(true);

					break;
				case StatusQueryOptions.UseCachedIfPossible:
					AssetLockManager.Instance.RequestAutomaticRefresh();

					break;
				case StatusQueryOptions.UseCachedAsync:
					AssetLockManager.Instance.RequestAutomaticRefresh();

					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(option), option, null);
			}
		}

		[StringFormatMethod("format")]
		private static void LogReferenceFormat(FileReference reference, string format, params object[] args)
		{
			if (AddAssetReferenceToLogs)
				Logging.LogFormat(reference.MainAsset, format, args);
			else
				Logging.LogFormat(format, args);
		}

		[StringFormatMethod("format")]
		private static void LogReferenceVerboseFormat(FileReference reference, string format, params object[] args)
		{
			if (AddAssetReferenceToLogs)
				Logging.LogVerboseFormat(reference.MainAsset, format, args);
			else
				Logging.LogVerboseFormat(format, args);
		}

		[StringFormatMethod("format")]
		private static void LogReferenceWarningFormat(FileReference reference, string format, params object[] args)
		{
			if (AddAssetReferenceToLogs)
				Logging.LogWarningFormat(reference.MainAsset, format, args);
			else
				Logging.LogWarningFormat(format, args);
		}
	}

	[InitializeOnLoad]
	internal static class AssetLockInspectorWarning
	{
		static AssetLockInspectorWarning() => UnityEditor.Editor.finishedDefaultHeaderGUI += Draw;

		internal static string GetWarning(FileReference file)
		{
			if (!MasterEnable || !WarnOnly || AssetLockManager.Instance?.IsReady != true || !ShouldTrack(file))
				return null;
			if (!file.TryGetLock(out var info))
				return AssetLockManager.Instance.AutomaticRefreshPaused
					? "Lock status is unavailable because refresh is paused."
					: "Lock status is not yet available.";
			if (info.LockedByMe) return null;

			return info.locked
				? $"Locked by {info.owner ?? "another user"}. Editing is allowed in warning mode."
				: "Not locked by you. Editing is allowed in warning mode.";
		}

		private static void Draw(UnityEditor.Editor editor)
		{
			if (!MasterEnable || !WarnOnly || AssetLockManager.Instance?.IsReady != true) return;

			var paths = new HashSet<string>();

			foreach (var target in editor.targets)
			{
				if (target == null || target is Component) continue;

				var path = AssetDatabase.GetAssetPath(target);
				if (string.IsNullOrEmpty(path) && target is GameObject gameObject) path = gameObject.scene.path;
				if (!string.IsNullOrEmpty(path)) paths.Add(path);
			}

			foreach (var path in paths)
			{
				var message = GetWarning(FileReference.FromPath(path));
				if (message != null) EditorGUILayout.HelpBox($"AssetLock: {path}\n{message}", MessageType.Warning);
			}
		}
	}
}