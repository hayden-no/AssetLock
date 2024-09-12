using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using AssetLock.Editor.Manager;

namespace AssetLock.Editor.Data
{
	/// <summary>
	/// Holds information about a file lock.
	/// </summary>
	[Serializable]
	internal struct LockInfo
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

		public override string ToString()
		{
			if (!HasValue)
			{
				return $"Error file unknown | Name: {name ?? "null"} | Locked: {locked} | Id: {lockId ?? "null"} | Owner: {owner ?? "null"} | Locked At: {lockedAt ?? "null"}";
			}

			return locked ? $"{name} (id: {lockId}): locked by {owner} at {lockedAt}" : $"{name}: unlocked";
		}
	}
}