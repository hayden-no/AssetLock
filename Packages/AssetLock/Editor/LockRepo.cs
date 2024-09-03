using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetLock.Editor
{
	internal class LockRepo
	{
		private DualIndexedDictionary<string, string, LockInfo> m_locks; // guid, path

		public IEnumerable<LockInfo> Locks => m_locks;

		public LockRepo()
		{
			m_locks = new((info => info.guid), (info => info.path));
		}

		public LockRepo(IEnumerable<LockInfo> locks)
		{
			m_locks = new(locks, (info => info.guid), (info => info.path));
		}

		public static LockRepo Deserialize(string json)
		{
			using var profiler = new AssetLockUtility.Logging.Profiler();
			var locks = JsonUtility.FromJson<LockInfo[]>(json);
			AssetLockUtility.Logging.LogVerboseFormat(
				"Lock Repo Deserialized {0} locks\nResults:\n\t{1}",
				locks?.Length ?? 0,
				locks == null ? "null" : string.Join("\n\t", locks)
			);

			return new LockRepo(locks);
		}

		public string Serialize()
		{
			return JsonUtility.ToJson(Locks.ToArray());
		}

		public void Reset()
		{
			m_locks.Clear();
		}

		public void Set(IEnumerable<LockInfo> locks)
		{
			foreach (var info in locks)
			{
				m_locks[key2: info.path] = info;
			}
		}

		private LockInfo GetOrAddByGuid(string guid)
		{
			if (m_locks.TryGetValue(key1: guid, out LockInfo l))
			{
				return l;
			}

			l = LockInfo.FromGuid(guid);
			m_locks.Add(l);

			return l;
		}

		private LockInfo GetOrAddByPath(string path)
		{
			if (m_locks.TryGetValue(key2: path, out LockInfo l))
			{
				return l;
			}

			l = LockInfo.FromPath(path);
			m_locks.Add(l);

			return l;
		}

		public LockInfo GetByGuid(string guid)
		{
			return m_locks.TryGetValue(key1: guid, out LockInfo l) ? l : default;
		}

		public LockInfo GetByPath(string path)
		{
			return m_locks.TryGetValue(key2: path, out LockInfo l) ? l : default;
		}

		public bool IsTracked(LockInfo lockInfo)
		{
			return m_locks.Contains(lockInfo);
		}

		public bool IsTrackedByGuid(string guid)
		{
			return m_locks.ContainsKey(key1: guid);
		}

		public bool IsTrackedByPath(string path)
		{
			return m_locks.ContainsKey(key2: path);
		}

		public void UpdateLock(LockInfo old, LockInfo newValue)
		{
			m_locks.Remove(old);
			m_locks.Add(newValue);
		}

		public void UpdateOrAddLock(LockInfo lockInfo)
		{
			if (m_locks.TryGetValue(key1: lockInfo.guid, out LockInfo l))
			{
				UpdateLock(l, lockInfo);
			}
			else
			{
				AddLock(lockInfo);
			}
		}

		public void RemoveLock(LockInfo lockInfo)
		{
			m_locks.Remove(lockInfo);
		}

		public void RemoveLockByPath(string path)
		{
			m_locks.Remove(key2: path);
		}

		public void RemoveLockByGuid(string guid)
		{
			m_locks.Remove(key1: guid);
		}

		public void AddLock(LockInfo lockInfo)
		{
			m_locks.Add(lockInfo);
			AssetLockUtility.Logging.LogVerboseFormat("Added Lock: {0}", lockInfo);
		}

		public void SetLockByPath(
			string path,
			bool locked,
			string owner = null,
			string lockedAt = null,
			string lockId = null
		)
		{
			LockInfo l = GetOrAddByPath(path);
			l.locked = locked;
			l.owner = owner;
			l.lockedAt = lockedAt;
			l.lockId = lockId;
			m_locks[key2: path] = l;
		}

		public void SetLockByGuid(
			string guid,
			bool locked,
			string owner = null,
			string lockedAt = null,
			string lockId = null
		)
		{
			LockInfo l = GetOrAddByGuid(guid);
			l.locked = locked;
			l.owner = owner;
			l.lockedAt = lockedAt;
			l.lockId = lockId;
			m_locks[key1: guid] = l;
		}
	}
}