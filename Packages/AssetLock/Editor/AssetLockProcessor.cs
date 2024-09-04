using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEditor.SettingsManagement;
using static AssetLock.Editor.AssetLockSettings;
using static AssetLock.Editor.AssetLockUtility;
using FileMode = UnityEditor.VersionControl.FileMode;

namespace AssetLock.Editor
{
	public class AssetLockProcessor : AssetModificationProcessor
	{
		private static bool CanOpenForEdit(
			string[] assetOrMetaFilePaths,
			List<string> outNotEditablePaths,
			StatusQueryOptions statusQueryOptions
		)
		{
			using var profiler = new Logging.Profiler(message: string.Join('\n', assetOrMetaFilePaths));

			if (!MasterEnable)
			{
				return true;
			}

			HandleRefresh(statusQueryOptions);
			bool result = true;

			foreach (var path in GetAllBinaryPaths(assetOrMetaFilePaths))
			{
				if (!AssetLockManager.Instance.Repo.TryGetValue(path, out var info))
				{
					AssetLockManager.Instance.TrackFile(path);
				}
				else if (info is { locked: true, LockedByMe: false })
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
			}

			if (outNotEditablePaths.Count > 0)
			{
				Logging.LogWarningFormat(
					"Cannot open for edit the following locked assets: {0}",
					string.Join(", ", outNotEditablePaths)
				);
			}

			return result;
		}

		private static bool IsOpenForEdit(
			string[] assetOrMetaFilePaths,
			List<string> outNotEditablePaths,
			StatusQueryOptions statusQueryOptions
		)
		{
			using var profiler = new Logging.Profiler(message: string.Join('\n', assetOrMetaFilePaths));

			if (!MasterEnable)
			{
				return true;
			}

			HandleRefresh(statusQueryOptions);
			bool result = true;

			foreach (var path in GetAllBinaryPaths(assetOrMetaFilePaths))
			{
				if (!AssetLockManager.Instance.Repo.TryGetValue(path, out var info))
				{
					AssetLockManager.Instance.TrackFile(path);
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

			if (!MasterEnable)
			{
				return true;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			bool result = true;

			foreach (var path in GetAllBinaryPaths(paths))
			{
				if (!AssetLockManager.Instance.Repo.TryGetValue(path, out var info))
				{
					AssetLockManager.Instance.TrackFile(path);
				}
				else if (info is { locked: true, LockedByMe: false })
				{
					result = false;
					outNotEditablePaths.Add(path.UnityPath);
				}
				else if (!TryAutoLock(info, "edit"))
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

			if (!MasterEnable)
			{
				return paths;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			List<string> unsaved = new List<string>();

			foreach (var path in GetAllBinaryPaths(paths))
			{
				if (!AssetLockManager.Instance.Repo.TryGetValue(path, out var info))
				{
					AssetLockManager.Instance.TrackFile(path);
				}
				else if (info is { locked: true, LockedByMe: false })
				{
					unsaved.Add(path.UnityPath);
				}
				else if (!TryAutoLock(info, "save"))
				{
					unsaved.Add(path.UnityPath);
				}
			}

			Logging.LogWarningFormat("Unsaved assets: {0}", string.Join(", ", unsaved));

			return paths.Except(unsaved).ToArray();
		}

		private static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
		{
			using var profiler = new Logging.Profiler(message: assetPath);

			if (!MasterEnable)
			{
				return AssetDeleteResult.DidNotDelete;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			var path = FileReference.FromPath(assetPath);

			if (!ShouldTrack(path))
			{
				return AssetDeleteResult.DidNotDelete;
			}

			if (!AssetLockManager.Instance.Repo.TryGetValue(path, out var info))
			{
				AssetLockManager.Instance.TrackFile(path);

				return AssetDeleteResult.FailedDelete;
			}
			else if (info is { locked: true, LockedByMe: false })
			{
				Logging.LogWarningFormat(
					"Cannot delete locked asset {0} because it is locked by {1}.",
					assetPath,
					info.owner
				);

				return AssetDeleteResult.FailedDelete;
			}
			else if (TryAutoLock(info, "delete"))
			{
				return AssetDeleteResult.FailedDelete;
			}

			return AssetDeleteResult.DidNotDelete;
		}

		private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
		{
			using var profiler = new Logging.Profiler(message: sourcePath);

			if (!MasterEnable)
			{
				return AssetMoveResult.DidNotMove;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			var path = FileReference.FromPath(sourcePath);
			
			if (!ShouldTrack(path))
			{
				return AssetMoveResult.DidNotMove;
			}

			if (!AssetLockManager.Instance.Repo.TryGetValue(path, out var info))
			{
				AssetLockManager.Instance.TrackFile(path);

				return AssetMoveResult.FailedMove;
			}
			else if (info is { locked: true, LockedByMe: false })
			{
				Logging.LogWarningFormat(
					"Cannot move locked asset {0} because it is locked by {1}.",
					sourcePath,
					info.owner
				);

				return AssetMoveResult.FailedMove;
			}
			else if (!TryAutoLock(info, "move"))
			{
				return AssetMoveResult.FailedMove;
			}
			
			AssetLockManager.Instance.RegisterMove(path, destinationPath);
			return AssetMoveResult.DidNotMove;
		}

		private static void FileModeChanged(string[] paths, FileMode mode)
		{
			using var profiler = new Logging.Profiler(message: string.Join('\n', paths));

			if (!MasterEnable)
			{
				return;
			}

			foreach (var path in GetAllBinaryPaths(paths))
			{
				if ((mode & FileMode.Text) != 0)
				{
					AssetLockManager.Instance.UntrackFile(path);
				}
				else if ((mode & FileMode.Binary) != 0)
				{
					AssetLockManager.Instance.TrackFile(path);
				}

				Logging.LogVerboseFormat("File mode changed for {0} to {1}", path, mode);
			}
		}

		private static void OnStatusUpdated()
		{
			using var profiler = new Logging.Profiler();

			if (!MasterEnable)
			{
				return;
			}

			_ = AssetLockManager.Instance.RefreshAsync();
		}

		private static bool TryAutoLock(FileReference reference, string action = "modify")
		{
			if (!MasterEnable)
			{
				return false;
			}

			if (AutoLock)
			{
				AssetLockManager.Instance.LockFile(reference);
				Logging.LogFormat("Automatically locked asset {0} because it is binary.", reference.UnityPath);

				return true;
			}
			else
			{
				Logging.LogWarningFormat(
					"Cannot {0} lockable asset {1}.  Please check it out first!",
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
					AssetLockManager.Instance.ForceRefresh();

					break;
				case StatusQueryOptions.UseCachedIfPossible:
					//AssetLockManager.Instance.Refresh();

					break;
				case StatusQueryOptions.UseCachedAsync:
					//_ = AssetLockManager.Instance.RefreshAsync();

					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(option), option, null);
			}
		}
	}
}