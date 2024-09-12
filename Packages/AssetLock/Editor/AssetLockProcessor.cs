using System;
using System.Collections.Generic;
using System.Linq;
using AssetLock.Editor.Data;
using AssetLock.Editor.Manager;
using UnityEditor;
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

			if (!MasterEnable)
			{
				return true;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			bool result = true;

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

			if (!MasterEnable)
			{
				return paths;
			}

			HandleRefresh(StatusQueryOptions.UseCachedIfPossible);
			List<string> unsaved = new List<string>();

			foreach (var path in GetAllBinaryPaths(paths))
			{
				if (!GetLock(path, true, out var info))
				{
					unsaved.Add(path.UnityPath);
				}
				else if (info is { locked: true, LockedByMe: false })
				{
					unsaved.Add(path.UnityPath);
				}
				else if (!TryAutoLock(info, true, "save"))
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

			if (!GetLock(path, true, out var info))
			{
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
			else if (TryAutoLock(info, true, "delete"))
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

			if (!path.ShouldTrack)
			{
				return AssetMoveResult.DidNotMove;
			}

			if (!GetLock(path, true, out var info))
			{
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

			if (!MasterEnable)
			{
				return;
			}

			foreach (var path in GetAllBinaryPaths(paths))
			{
				if ((mode & FileMode.Text) != 0)
				{
					path.UnlockFile();
				}
				else if ((mode & FileMode.Binary) != 0)
				{
					path.TrackFile();
				}

				Logging.LogVerboseFormat("File mode changed for {0} to {1}", path, mode);
			}
		}

		private static void OnStatusUpdated()
		{
			if (!MasterEnable)
			{
				return;
			}

			AssetLockManager.Instance.Refresh();
		}

		private static bool GetLock(FileReference reference, bool blocking, out LockInfo info)
		{
			if (reference.TryGetLock(out info))
			{
				return true;
			}

			if (blocking)
			{
				reference.TrackFileAsync().Wait(Constants.DEFAULT_LOCK_TIMEOUT);
				return GetLock(reference, false, out info);
			}
			else
			{
				reference.TrackFile();
			}

			return false;
		}

		private static bool TryAutoLock(FileReference reference, bool blocking = true, string action = "modify")
		{
			if (!MasterEnable)
			{
				return false;
			}

			if (AutoLock)
			{
				if (!blocking)
				{
					reference.LockFile();
				}
				else
				{
					reference.LockFileAsync().Wait(Constants.DEFAULT_LOCK_TIMEOUT);
				}

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
					AssetLockManager.Instance.Refresh();

					break;
				case StatusQueryOptions.UseCachedAsync:
					AssetLockManager.Instance.Refresh();

					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(option), option, null);
			}
		}
	}
}