﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AssetLock.Editor.Data;
using Newtonsoft.Json;

namespace AssetLock.Editor
{
	/// <summary>
	/// Slightly modified dictionary for storing locks.
	/// </summary>
	internal class LockRepo : IDictionary<FileReference, LockInfo>, IReadOnlyDictionary<FileReference, LockInfo>
	{
		private readonly IDictionary<FileReference, LockInfo> m_locks;

		/// <summary>
		/// Same as <see cref="Values"/>.
		/// </summary>
		public IEnumerable<LockInfo> Locks => m_locks.Values;

		public LockRepo()
		{
			m_locks = new Dictionary<FileReference, LockInfo>();
		}

		public LockRepo(IEnumerable<LockInfo> locks)
		{
			m_locks = new Dictionary<FileReference, LockInfo>();
		}

		/// <summary>
		/// Deserialize a json string into a lock repo.
		/// </summary>
		/// <param name="json">json string</param>
		/// <returns>repo with items from json string</returns>
		public static LockRepo Deserialize(string json)
		{
			using var profiler = new AssetLockUtility.Logging.Profiler();
			var locks = JsonConvert.DeserializeObject<LockInfo[]>(json) ?? Array.Empty<LockInfo>();
			AssetLockUtility.Logging.LogVerboseFormat(
				"Lock Repo Deserialized {0} locks\nResults:\n\t{1}",
				locks?.Length ?? 0,
				string.Join("\n\t", locks)
			);

			return new LockRepo(locks.Where(l => l.HasValue).Distinct());
		}

		/// <summary>
		/// Serialize the lock repo to a json string.
		/// </summary>
		/// <returns>json string</returns>
		public string Serialize()
		{
			string results = JsonConvert.SerializeObject(Locks.ToArray());
			AssetLockUtility.Logging.LogVerboseFormat("Lock Repo Serialized {0} locks\nResults:\n\t{1}", Locks.Count(), results);
			return results;
		}

		public IEnumerator<KeyValuePair<FileReference, LockInfo>> GetEnumerator()
		{
			return m_locks.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable)m_locks).GetEnumerator();
		}
		
		public void Add(KeyValuePair<FileReference, LockInfo> item)
		{
			m_locks.Add(item);
		}
		
		public void Clear()
		{
			m_locks.Clear();
		}
		
		public bool Contains(KeyValuePair<FileReference, LockInfo> item)
		{
			return m_locks.Contains(item);
		}
		
		public void CopyTo(KeyValuePair<FileReference, LockInfo>[] array, int arrayIndex)
		{
			m_locks.CopyTo(array, arrayIndex);
		}
		
		public bool Remove(KeyValuePair<FileReference, LockInfo> item)
		{
			return m_locks.Remove(item);
		}

		public int Count => m_locks.Count;
		
		public bool IsReadOnly => m_locks.IsReadOnly;

		public void Add(FileReference key, LockInfo value)
		{
			m_locks.Add(key, value);
		}

		public bool ContainsKey(FileReference key)
		{
			return m_locks.ContainsKey(key);
		}

		public bool Remove(FileReference key)
		{
			return m_locks.Remove(key);
		}

		public bool TryGetValue(FileReference key, out LockInfo value)
		{
			return m_locks.TryGetValue(key, out value);
		}

		public LockInfo this[FileReference key]
		{
			get => m_locks[key];
			set => m_locks[key] = value;
		}

		/// <summary>
		/// Update the lock repo with new lock infos. Any locks not in the new list will be reset (unlocked).
		/// </summary>
		/// <param name="infos">All updated locks</param>
		public void Update(IEnumerable<LockInfo> infos)
		{
			var locks = infos.ToDictionary((l => (FileReference)l));
			foreach (var value in m_locks.Values.ToList())
			{
				if (locks.TryGetValue(value, out var info))
				{
					m_locks[value] = info;
				}
				else
				{
					value.Reset();
					m_locks[value] = value;
				}
			}
		}

		IEnumerable<FileReference> IReadOnlyDictionary<FileReference, LockInfo>.Keys => Keys;

		IEnumerable<LockInfo> IReadOnlyDictionary<FileReference, LockInfo>.Values => Values;

		public ICollection<FileReference> Keys => m_locks.Keys;

		public ICollection<LockInfo> Values => m_locks.Values;
	}
}