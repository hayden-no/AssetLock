using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AssetLock.Editor.Manager;
using UnityEditor;
using static AssetLock.Editor.AssetLockUtility;

namespace AssetLock.Editor.Data
{
	/// <summary>
	/// Represents a directory on the file system.
	/// </summary>
	internal readonly struct DirectoryReference
	{
		public readonly string AbsolutePath;

		private DirectoryReference(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("Path is null or empty.");
			}

			// sanitize path
			path = path.Replace("\"", string.Empty);

			if (path.EndsWith(Path.PathSeparator))
			{
				path = path[..^2]; // remove trailing slash
			}

			AbsolutePath = Path.GetFullPath(path);
		}

		public string Name => AbsolutePath.Substring(AbsolutePath.LastIndexOf(Path.DirectorySeparatorChar) + 1);
		public string UnityPath => ToUnityRelativePath(AbsolutePath);
		public string AssetPath => ToUnityAssetsRelativePath(AbsolutePath);
		public string GitPath => ToGitRelativePath(AbsolutePath);
		public string UnityMetaPath => $"{UnityPath}.meta";
		public string Guid => AssetDatabase.AssetPathToGUID(UnityPath);
		public bool Exists => Directory.Exists(AbsolutePath);

		public DirectoryReference Parent => FromPath(Path.Combine(AbsolutePath, Constants.FILE_SYSTEM_UP_DIR));
		public IEnumerable<DirectoryReference> Directories => EnumerateChildDirectories();
		public IEnumerable<FileReference> Files => EnumerateChildFiles();

		public bool ContainsAnyTracked => AnyTrackedFiles || AnyTrackedDirectories;
		public bool AnyTrackedFiles => EnumerateTrackedChildFiles().Any();
		public bool AnyTrackedDirectories => EnumerateTrackedChildDirectories().Any();

		public static DirectoryReference FromPath(string path)
		{
			return new DirectoryReference(path);
		}
		
		public static DirectoryReference FromFile(FileReference file)
		{
			return new DirectoryReference(Path.GetDirectoryName(file.AbsolutePath));
		}

		public static implicit operator DirectoryReference(DirectoryInfo directoryInfo)
		{
			return new DirectoryReference(directoryInfo.FullName);
		}

		public static implicit operator DirectoryInfo(DirectoryReference directoryReference)
		{
			return new DirectoryInfo(directoryReference.AbsolutePath);
		}

		public IEnumerable<DirectoryReference> EnumerateChildDirectories()
		{
			if (!Exists)
			{
				throw new DirectoryNotFoundException($"Directory not found: {AbsolutePath}");
			}

			foreach (var directory in Directory.EnumerateDirectories(AbsolutePath))
			{
				yield return FromPath(directory);
			}
		}

		public IEnumerable<DirectoryReference> EnumerateTrackedChildDirectories(short searchDepth = Int16.MaxValue)
		{
			if (!Exists)
			{
				throw new DirectoryNotFoundException($"Directory not found: {AbsolutePath}");
			}

			if (searchDepth < 1)
			{
				yield break;
			}

			foreach (DirectoryReference directory in EnumerateChildDirectories())
			{
				if (directory.AnyTrackedFiles)
				{
					yield return directory;
				}
				else if (directory.EnumerateTrackedChildDirectories(searchDepth).Any())
				{
					yield return directory;
				}
			}
		}

		public IEnumerable<FileReference> EnumerateChildFiles(bool allowMeta = true)
		{
			if (!Exists)
			{
				throw new DirectoryNotFoundException($"Directory not found: {AbsolutePath}");
			}

			foreach (var file in Directory.EnumerateFiles(AbsolutePath))
			{
				yield return FileReference.FromPath(file, allowMeta);
			}
		}

		public IEnumerable<FileReference> EnumerateTrackedChildFiles()
		{
			if (!Exists)
			{
				throw new DirectoryNotFoundException($"Directory not found: {AbsolutePath}");
			}

			foreach (var file in EnumerateChildFiles(false).Distinct())
			{
				if (file.Tracked && !file.IsMeta)
				{
					yield return file;
				}
			}
		}
		
		public IEnumerable<FileReference> EnumerateLockedChildFiles()
		{
			if (!Exists)
			{
				throw new DirectoryNotFoundException($"Directory not found: {AbsolutePath}");
			}

			foreach (var file in EnumerateChildFiles(false).Distinct())
			{
				if (file.Locked && !file.IsMeta)
				{
					yield return file;
				}
			}
		}
		
		public void TrackChildren(bool force = false)
		{
			foreach (var file in EnumerateChildFiles(false).Distinct())
			{
				if (file.ShouldTrack)
				{
					file.TrackFile(force);
				}
			}
		}
		
		public async Task TrackChildrenAsync(bool force = false)
		{
			await Task.WhenAll(EnumerateChildFiles(false).Distinct().Where(f => f.ShouldTrack).Select(f => f.TrackFileAsync(force)));
		}
		
		public void TrackChildrenRecursively(bool force = false)
		{
			TrackChildren(force);
			
			foreach (var directory in EnumerateChildDirectories())
			{
				directory.TrackChildrenRecursively(force);
			}
		}
		
		public void UntrackChildren(bool force = false)
		{
			foreach (var file in EnumerateChildFiles(false).Distinct())
			{
				if (file.Tracked)
				{
					file.UntrackFile(force);
				}
			}
		}
		
		public async Task UntrackChildrenAsync(bool force = false)
		{
			await Task.WhenAll(EnumerateChildFiles(false).Distinct().Where(f => f.Tracked).Select(f => f.UntrackFileAsync(force)));
		}
		
		public void UntrackChildrenRecursively(bool force = false)
		{
			UntrackChildren(force);
			
			foreach (var directory in EnumerateChildDirectories())
			{
				directory.UntrackChildrenRecursively(force);
			}
		}
		
		public void LockChildren(bool force = false)
		{
			foreach (var file in EnumerateTrackedChildFiles().Distinct())
			{
				if (!file.Locked)
				{
					file.LockFile(force);
				}
			}
		}
		
		public async Task LockChildrenAsync(bool force = false)
		{
			await Task.WhenAll(EnumerateTrackedChildFiles().Distinct().Where(f => !f.Locked).Select(f => f.LockFileAsync(force)));
		}
		
		public void LockChildrenRecursively(bool force = false)
		{
			LockChildren(force);
			
			foreach (var directory in EnumerateChildDirectories())
			{
				directory.LockChildrenRecursively(force);
			}
		}
		
		public void UnlockChildren(bool force = false)
		{
			foreach (var file in EnumerateTrackedChildFiles().Distinct())
			{
				if (file.Locked)
				{
					file.UnlockFile(force);
				}
			}
		}
		
		public async Task UnlockChildrenAsync(bool force = false)
		{
			await Task.WhenAll(EnumerateTrackedChildFiles().Distinct().Where(f => f.Locked).Select(f => f.UnlockFileAsync(force)));
		}
		
		public void UnlockChildrenRecursively(bool force = false)
		{
			UnlockChildren(force);
			
			foreach (var directory in EnumerateChildDirectories())
			{
				directory.UnlockChildrenRecursively(force);
			}
		}
		
		public void RefreshChildrenLocks()
		{
			// FIXME: for now just refresh all files
			AssetLockManager.Instance.Refresh();
		}
		
		public async Task RefreshChildrenLocksAsync()
		{
			// FIXME: for now just refresh all files
			await AssetLockManager.Instance.RefreshAsync();
		}
		
		public void RefreshChildrenLocksRecursively()
		{
			// FIXME: for now just refresh all files
			AssetLockManager.Instance.Refresh();
			
			// RefreshChildrenLocks();
			//
			// foreach (var directory in EnumerateChildDirectories())
			// {
			// 	directory.RefreshChildrenLocksRecursively();
			// }
		}

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine($"DirectoryReference: {Name}");
			sb.AppendLine($"\tAbsolutePath: {AbsolutePath}");
			sb.AppendLine($"\tGitPath: {GitPath}");
			sb.AppendLine($"\tUnityPath: {UnityPath}");
			sb.AppendLine($"\tAssetPath: {AssetPath}");
			sb.AppendLine($"\tUnityMetaPath: {UnityMetaPath}");
			sb.AppendLine($"\tExists: {Exists}");

			if (Exists)
			{
				sb.AppendLine($"\tGuid: {Guid}");
				sb.AppendLine("\tContains:");
				sb.AppendLine($"\t\tDirectory count: {Directory.GetDirectories(AbsolutePath).Length}");
				sb.AppendLine($"\t\tFile count: {Directory.GetFiles(AbsolutePath).Length}");
			}
			else
			{
				sb.AppendLine($"\tGuid: <directory not found>");
				sb.AppendLine("\tContains:");
				sb.AppendLine("\t\tDirectory count: 0");
				sb.AppendLine("\t\tFile count: 0");
			}

			return sb.ToString();
		}

		public override bool Equals(object obj)
		{
			return obj is DirectoryReference other && this.AbsolutePath == other.AbsolutePath;
		}

		public override int GetHashCode()
		{
			return this.AbsolutePath.GetHashCode();
		}
	}
}