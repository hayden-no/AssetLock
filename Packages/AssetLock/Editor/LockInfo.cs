using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using AssetLock.Editor.Manager;
using Newtonsoft.Json;
using UnityEditor;

namespace AssetLock.Editor
{
	[Serializable]
	internal struct LockInfo
	{
		public string guid;
		public string path;
		public string name;
		public string FileName => Path.GetFileName(this.path);
		public string Extension => Path.GetExtension(this.path);
		public bool locked;
		[MaybeNull] public string lockId;
		[MaybeNull] public string owner;
		[MaybeNull] public string lockedAt;

		public bool HasValue => !string.IsNullOrEmpty(this.guid);
		public bool LockedByMe => this.locked && this.owner == AssetLockManager.Instance.User;

		public static implicit operator FileReference(LockInfo lockInfo)
		{
			return FileReference.FromLock(lockInfo);
		}

		public void Reset()
		{
			locked = false;
			lockId = null;
			owner = null;
			lockedAt = null;
		}

		public override string ToString()
		{
			if (!HasValue)
			{
				return "default";
			}

			return string.Format(
				"File: {0} | Status: {1}{2}",
				FileName,
				locked,
				locked ? $" by {owner} at {lockedAt}" : ""
			);
		}
	}

	internal readonly struct FileReference
	{
		public readonly string AbsolutePath;

		private FileReference(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("Path is null or empty.");
			}

			path = AssetLockUtility.GetPathWithoutMeta(path);
			
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

		public FileStream OpenRead()
		{
			return File.OpenRead(this.AbsolutePath);
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

		public static FileReference FromGuid(string guid)
		{
			return new FileReference(AssetDatabase.GUIDToAssetPath(guid));
		}

		public static FileReference FromPath(string path)
		{
			return new FileReference(path);
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
			return new FileReference(file.FullName);
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

			if (Exists)
			{
				// will throw if the file is not found
				sb.AppendLine($"\tGuid: {this.Guid}");
			}
			else
			{
				sb.AppendLine("\tGuid: <file not found>");
			}

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

	[JsonObject]
	internal struct LocksResponseDataJson
	{
		[JsonProperty("id")] public string ID;

		[JsonProperty("path")] public string Path;

		[JsonProperty("locked_at")] public string LockedAt;

		[JsonProperty("owner")] public LocksResponseOwnerDataJson Owner;

		public string Name => this.Owner.Name;

		public static implicit operator LockInfo(LocksResponseDataJson data)
		{
			return FileReference.FromPath(data.Path).ToLock(true, data.ID, data.Name, data.LockedAt);
		}
	}

	[JsonObject]
	internal struct LocksResponseOwnerDataJson
	{
		[JsonProperty("name")] public string Name;
	}
}