using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using AssetLock.Editor.Manager;
using UnityEditor;

namespace AssetLock.Editor.Data
{
	/// <summary>
	/// Holds information about a file lock.
	/// </summary>
	[Serializable]
	internal struct LockInfo : IEquatable<LockInfo>
	{
		public string guid;
		public string path;
		public string name;
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

		public bool TryUpdateGuid()
		{
			if (!string.IsNullOrWhiteSpace(guid))
			{
				// already has guid
				return AssetDatabase.GUIDToAssetPath(guid) != null;
			}
			
			FileReference reference = FileReference.FromPath(path);

			if (!reference.Exists)
			{
				AssetLockUtility.Logging.LogVerboseFormat("File does not exist: {0}", path);
			}
			
			var g = AssetDatabase.GUIDFromAssetPath(reference.UnityPath);

			if (g.Empty())
			{
				AssetLockUtility.Logging.LogVerboseFormat("File does not have a guid: {0}", path);
				return false;
			}
			
			guid = g.ToString();
			
			return true;
		}

		public override string ToString()
		{
			if (!HasValue)
			{
				return $"Error file unknown | Name: {name ?? "null"} | Locked: {locked} | Id: {lockId ?? "null"} | Owner: {owner ?? "null"} | Locked At: {lockedAt ?? "null"}";
			}

			return locked ? $"{name} (id: {lockId}): locked by {owner} at {lockedAt}" : $"{name}: unlocked";
		}

		public bool Equals(LockInfo other)
		{
			return ((FileReference)this).Equals((FileReference)other);
		}

		public override bool Equals(object obj)
		{
			return obj is LockInfo other && Equals(other);
		}

		public override int GetHashCode()
		{
			return ((FileReference)this).GetHashCode();
		}
	}
}