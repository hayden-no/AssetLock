using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AssetLock.Editor.Manager;
using UnityEditor;
using Object = UnityEngine.Object;

namespace AssetLock.Editor.Data
{
	/// <summary>
	/// Represents a file on the file system.
	/// </summary>
	internal readonly struct FileReference : IEquatable<FileReference>
	{
		public readonly string AbsolutePath;

		private FileReference(string path, bool allowMeta)
		{
			if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is null or empty.");

			if (!allowMeta) path = AssetLockUtility.GetPathWithoutMeta(path);

			// sanitize path
			path = path.Replace("\"", string.Empty);

			AbsolutePath = AssetLockUtility.GetFullPath(path);
		}

		public string Name => Path.GetFileNameWithoutExtension(AbsolutePath);
		public string NameWithExtension => Path.GetFileName(AbsolutePath);
		public string UnityPath => AssetLockUtility.ToUnityRelativePath(AbsolutePath);
		public string AssetPath => AssetLockUtility.ToUnityAssetsRelativePath(AbsolutePath);
		public string GitPath => AssetLockUtility.ToGitRelativePath(AbsolutePath);
		public string UnityMetaPath => $"{UnityPath}.meta";
		public string Guid => AssetDatabase.AssetPathToGUID(UnityPath);
		public bool Exists => File.Exists(AbsolutePath);
		public string Extension => Path.GetExtension(AbsolutePath);
		public bool ShouldTrack => AssetLockUtility.ShouldTrack(this);
		public bool Tracked => AssetLockManager.Instance.IsTracked(this);
		public bool Locked => AssetLockManager.Instance.IsLocked(this);
		public LockInfo Lock => AssetLockManager.Instance.Repo[this];
		public DirectoryReference Directory => DirectoryReference.FromFile(this);
		public FileReference Meta => new(UnityMetaPath, true);
		public bool IsMeta => AbsolutePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
		public Object MainAsset => AssetDatabase.LoadMainAssetAtPath(UnityPath);

		public FileStream OpenRead() => File.OpenRead(AbsolutePath);

		public bool TryGetLock(out LockInfo lockInfo) => AssetLockManager.Instance.Repo.TryGetValue(this, out lockInfo);

		public LockInfo
			ToLock(bool locked = false, string lockId = null, string owner = null, string lockedAt = null) =>
			new()
			{
				guid = Guid,
				path = UnityPath,
				name = Path.GetFileNameWithoutExtension(AbsolutePath),
				locked = locked,
				lockId = lockId,
				owner = owner,
				lockedAt = lockedAt
			};

		public string AsProcessArg()
		{
			var path = UnityPath;
			path = path.Replace("\\", "/");

			return path;
		}

		public void TrackFile(bool force = false)
		{
			if (!force && (!ShouldTrack || Tracked)) return;

			AssetLockManager.Instance.TrackFile(this, force);
		}

		public async Task TrackFileAsync(bool force = false)
		{
			if (!force && (!ShouldTrack || Tracked)) return;

			await AssetLockManager.Instance.TrackFileAsync(this, force);
		}

		public void UntrackFile(bool force = false)
		{
			if (!force && (!ShouldTrack || !Tracked)) return;

			AssetLockManager.Instance.UntrackFile(this, force);
		}

		public async Task UntrackFileAsync(bool force = false)
		{
			if (!force && (!ShouldTrack || !Tracked)) return;

			await AssetLockManager.Instance.UntrackFileAsync(this, force);
		}

		public void LockFile(bool force = false)
		{
			if (!force && Locked) return;

			AssetLockManager.Instance.LockFile(this, force);
		}

		public async Task LockFileAsync(bool force = false)
		{
			if (!force && Locked) return;

			await AssetLockManager.Instance.LockFileAsync(this, force);
		}

		public void UnlockFile(bool force = false)
		{
			if (!force && !Locked) return;

			AssetLockManager.Instance.UnlockFile(this, force);
		}

		public async Task UnlockFileAsync(bool force = false)
		{
			if (!force && !Locked) return;

			await AssetLockManager.Instance.UnlockFileAsync(this, force);
		}

		public void RefreshLock()
		{
			if (!Exists) return;

			AssetLockManager.Instance.RefreshFile(this);
		}

		public async Task RefreshLockAsync()
		{
			if (!Exists) return;

			await AssetLockManager.Instance.RefreshFileAsync(this);
		}

		public static FileReference FromGuid(string guid) => new(AssetDatabase.GUIDToAssetPath(guid), false);

		public static FileReference FromPath(string path) => new(path, false);

		public static FileReference FromPath(string path, bool allowMeta) => new(path, allowMeta);

		public static FileReference FromLock(LockInfo lockInfo)
		{
			if (!string.IsNullOrWhiteSpace(lockInfo.guid)) return FromGuid(lockInfo.guid);

			if (!string.IsNullOrWhiteSpace(lockInfo.path)) return FromPath(lockInfo.path);

			throw new ArgumentException("LockInfo does not contain a valid path or guid.");
		}

		public static implicit operator FileReference(FileInfo file) => new(file.FullName, true);

		public static implicit operator FileInfo(FileReference file) => new(file.AbsolutePath);

		public override string ToString()
		{
			StringBuilder sb = new();
			sb.AppendLine($"FileReference: {NameWithExtension}");
			sb.AppendLine($"\tAbsolutePath: {AbsolutePath}");
			sb.AppendLine($"\tGitPath: {GitPath}");
			sb.AppendLine($"\tUnityPath: {UnityPath}");
			sb.AppendLine($"\tAssetPath: {AssetPath}");
			sb.AppendLine($"\tUnityMetaPath: {UnityMetaPath}");
			sb.AppendLine($"\tExists: {Exists}");
			sb.AppendLine($"\tExtension: {Extension}");

			// will throw if the file is not found
			sb.AppendLine(Exists ? $"\tGuid: {Guid}" : "\tGuid: <file not found>");

			return sb.ToString();
		}

		public override bool Equals(object obj) => obj is FileReference other && Equals(other);

		public override int GetHashCode() => AbsolutePath != null ? AbsolutePath.GetHashCode() : 0;

		public bool Equals(FileReference other) => AbsolutePath == other.AbsolutePath;
	}
}