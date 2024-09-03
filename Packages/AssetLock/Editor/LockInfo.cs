using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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

		internal static LockInfo FromGuid(string guid)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			path = AssetLockUtility.NormalizePath(path);

			return new LockInfo()
			{
				guid = guid,
				path = path,
				name = Path.GetFileNameWithoutExtension(path),
				locked = false,
				owner = null
			};
		}

		internal static LockInfo FromPath(string path)
		{
			string guid = AssetDatabase.AssetPathToGUID(AssetLockUtility.ToRelativePath(path));

			if (string.IsNullOrEmpty(guid))
			{
				throw new Exception(
					string.Format("Failed to find guid for {0} ({1})", path, AssetLockUtility.ToRelativePath(path))
				);
			}

			return new LockInfo()
			{
				guid = guid,
				path = path,
				name = Path.GetFileNameWithoutExtension(path),
				locked = false,
				owner = null
			};
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

	[JsonObject]
	internal struct LocksResponseDataJson
	{
		[JsonProperty("id")] public string id;

		[JsonProperty("path")] public string path;

		[JsonProperty("locked_at")] public string locked_at;

		[JsonProperty("owner")] public LocksResponseOwnerDataJson owner;

		public string name => this.owner.name;

		public static implicit operator LockInfo(LocksResponseDataJson data)
		{
			var info = LockInfo.FromPath(AssetLockUtility.NormalizePath(data.path));
			info.lockId = data.id;
			info.lockedAt = data.locked_at;
			info.owner = data.name;

			return info;
		}
	}

	[JsonObject]
	internal struct LocksResponseOwnerDataJson
	{
		[JsonProperty("name")] public string name;
	}
}