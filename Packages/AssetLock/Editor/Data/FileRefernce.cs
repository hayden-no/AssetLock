using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AssetLock.Editor.Manager;
using UnityEditor;

namespace AssetLock.Editor.Data
{
	/// <summary>
	/// Represents a file on the file system.
	/// </summary>
	internal readonly struct FileReference
	{
		public readonly string AbsolutePath;

		private FileReference(string path, bool allowMeta)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("Path is null or empty.");
			}

			if (!allowMeta)
			{
				path = AssetLockUtility.GetPathWithoutMeta(path);
			}

			// sanitize path
			path = path.Replace("\"", string.Empty);

			this.AbsolutePath = Path.GetFullPath(path);
		}

		public string Name => Path.GetFileNameWithoutExtension(this.AbsolutePath);
		public string NameWithExtension => Path.GetFileName(this.AbsolutePath);
		public string UnityPath => AssetLockUtility.ToUnityRelativePath(this.AbsolutePath);
		public string AssetPath => AssetLockUtility.ToUnityAssetsRelativePath(this.AbsolutePath);
		public string GitPath => AssetLockUtility.ToGitRelativePath(this.AbsolutePath);
		public string UnityMetaPath => $"{this.UnityPath}.meta";
		public string Guid => AssetDatabase.AssetPathToGUID(this.UnityPath);
		public bool Exists => File.Exists(this.AbsolutePath);
		public string Extension => Path.GetExtension(this.AbsolutePath);
		public bool ShouldTrack => AssetLockUtility.ShouldTrack(this);
		public bool Tracked => AssetLockManager.Instance.IsTracked(this);
		public bool Locked => AssetLockManager.Instance.IsLocked(this);
		public LockInfo Lock => AssetLockManager.Instance.Repo[this];
		public DirectoryReference Directory => DirectoryReference.FromFile(this);
		public FileReference Meta => new FileReference(UnityMetaPath, true);
		public bool IsMeta => this.AbsolutePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);

		public FileStream OpenRead()
		{
			return File.OpenRead(this.AbsolutePath);
		}
		
		public bool TryGetLock(out LockInfo lockInfo)
		{
			return AssetLockManager.Instance.Repo.TryGetValue(this, out lockInfo);
		}

		public LockInfo ToLock(bool locked = false, string lockId = null, string owner = null, string lockedAt = null)
		{
			return new LockInfo()
			{
				guid = this.Guid,
				path = this.UnityPath,
				name = Path.GetFileNameWithoutExtension(this.AbsolutePath),
				locked = locked,
				lockId = lockId,
				owner = owner,
				lockedAt = lockedAt
			};
		}

		public string AsProcessArg()
		{
			return $"\"{this.GitPath}\"";
		}

		public void TrackFile(bool force = false)
		{
			if (!force && (!ShouldTrack || Tracked))
			{
				return;
			}

			AssetLockManager.Instance.TrackFile(this, force);
		}
		
		public async Task TrackFileAsync(bool force = false)
		{
			if (!force && (!ShouldTrack || Tracked))
			{
				return;
			}

			await AssetLockManager.Instance.TrackFileAsync(this, force);
		}

		public void UntrackFile(bool force = false)
		{
			if (!force && (!ShouldTrack || !Tracked))
			{
				return;
			}

			AssetLockManager.Instance.UntrackFile(this, force);
		}
		
		public async Task UntrackFileAsync(bool force = false)
		{
			if (!force && (!ShouldTrack || !Tracked))
			{
				return;
			}

			await AssetLockManager.Instance.UntrackFileAsync(this, force);
		}

		public void LockFile(bool force = false)
		{
			if (!force && Locked)
			{
				return;
			}

			AssetLockManager.Instance.LockFile(this, force);
		}
		
		public async Task LockFileAsync(bool force = false)
		{
			if (!force && Locked)
			{
				return;
			}

			await AssetLockManager.Instance.LockFileAsync(this, force);
		}

		public void UnlockFile(bool force = false)
		{
			if (!force && !Locked)
			{
				return;
			}

			AssetLockManager.Instance.UnlockFile(this, force);
		}
		
		public async Task UnlockFileAsync(bool force = false)
		{
			if (!force && !Locked)
			{
				return;
			}

			await AssetLockManager.Instance.UnlockFileAsync(this, force);
		}

		public void RefreshLock()
		{
			if (!Exists)
			{
				return;
			}

			AssetLockManager.Instance.RefreshFile(this);
		}
		
		public async Task RefreshLockAsync()
		{
			if (!Exists)
			{
				return;
			}

			await AssetLockManager.Instance.RefreshFileAsync(this);
		}

		public static FileReference FromGuid(string guid)
		{
			return new FileReference(AssetDatabase.GUIDToAssetPath(guid), false);
		}
		
		public static FileReference FromPath(string path)
		{
			return new FileReference(path, false);
		}

		public static FileReference FromPath(string path, bool allowMeta)
		{
			return new FileReference(path, allowMeta);
		}

		public static FileReference FromLock(LockInfo lockInfo)
		{
			if (!string.IsNullOrWhiteSpace(lockInfo.guid))
			{
				return FromGuid(lockInfo.guid);
			}

			if (!string.IsNullOrWhiteSpace(lockInfo.path))
			{
				return FromPath(lockInfo.path);
			}

			throw new ArgumentException("LockInfo does not contain a valid path or guid.");
		}

		public static implicit operator FileReference(FileInfo file)
		{
			return new FileReference(file.FullName, true);
		}

		public static implicit operator FileInfo(FileReference file)
		{
			return new FileInfo(file.AbsolutePath);
		}

		public override string ToString()
		{
			StringBuilder sb = new();
			sb.AppendLine($"FileReference: {NameWithExtension}");
			sb.AppendLine($"\tAbsolutePath: {this.AbsolutePath}");
			sb.AppendLine($"\tGitPath: {this.GitPath}");
			sb.AppendLine($"\tUnityPath: {this.UnityPath}");
			sb.AppendLine($"\tAssetPath: {this.AssetPath}");
			sb.AppendLine($"\tUnityMetaPath: {this.UnityMetaPath}");
			sb.AppendLine($"\tExists: {this.Exists}");
			sb.AppendLine($"\tExtension: {this.Extension}");

			// will throw if the file is not found
			sb.AppendLine(Exists ? $"\tGuid: {this.Guid}" : "\tGuid: <file not found>");

			return sb.ToString();
		}

		public override bool Equals(object obj)
		{
			return obj is FileReference other && this.AbsolutePath == other.AbsolutePath;
		}

		public override int GetHashCode()
		{
			return this.AbsolutePath.GetHashCode();
		}
	}
}