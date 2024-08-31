using System;
using System.Collections;
using System.Collections.Generic;

namespace AssetLock.Editor
{
	public class DualIndexedDictionary<TKey1, TKey2, TValue> : IEnumerable<TValue>
	{
		private Dictionary<TKey1, TValue> m_dict1 = new Dictionary<TKey1, TValue>();
		private Dictionary<TKey2, TValue> m_dict2 = new Dictionary<TKey2, TValue>();
		
		private Func<TValue, TKey1> m_key1Selector;
		private Func<TValue, TKey2> m_key2Selector;

		IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
		{
			return m_dict1.Values.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable)m_dict1).GetEnumerator();
		}

		public void Add(TValue item)
		{
			if (m_dict1.ContainsKey(m_key1Selector(item)) || m_dict2.ContainsKey(m_key2Selector(item)))
			{
				throw new ArgumentException("An item with the same key has already been added.");
			}
			
			m_dict1.Add(m_key1Selector(item), item);
			m_dict2.Add(m_key2Selector(item), item);
		}
		
		public void AddRange(IEnumerable<TValue> items)
		{
			foreach (var item in items)
			{
				Add(item);
			}
		}
		
		public void Remove(TValue item)
		{
			m_dict1.Remove(m_key1Selector(item));
			m_dict2.Remove(m_key2Selector(item));
		}
		
		public void Remove(TKey1 key1)
		{
			var item = m_dict1[key1];
			m_dict1.Remove(key1);
			m_dict2.Remove(m_key2Selector(item));
		}
		
		public void Remove(TKey2 key2)
		{
			var item = m_dict2[key2];
			m_dict2.Remove(key2);
			m_dict1.Remove(m_key1Selector(item));
		}

		public void Clear()
		{
			m_dict1.Clear();
			m_dict2.Clear();
		}

		public bool ContainsKey(TKey1 key1)
		{
			return m_dict1.ContainsKey(key1);
		}

		public bool ContainsKey(TKey2 key2)
		{
			return m_dict2.ContainsKey(key2);
		}
		
		public bool TryGetValue(TKey1 key1, out TValue value)
		{
			return m_dict1.TryGetValue(key1, out value);
		}
		
		public bool TryGetValue(TKey2 key2, out TValue value)
		{
			return m_dict2.TryGetValue(key2, out value);
		}
		
		public void Update(TValue old, TValue newValue)
		{
			Remove(old);
			Add(newValue);
		}
		
		public TValue Get(TKey1 key1)
		{
			return m_dict1[key1];
		}
		
		public TValue Get(TKey2 key2)
		{
			return m_dict2[key2];
		}
		
		public TValue this[TKey1 key1]
		{
			get => m_dict1[key1];
			set
			{
				m_dict1[key1] = value;
				m_dict2[m_key2Selector(value)] = value;
			}
		}
		
		public TValue this[TKey2 key2]
		{
			get => m_dict2[key2];
			set
			{
				m_dict2[key2] = value;
				m_dict1[m_key1Selector(value)] = value;
			}
		}
		
		public DualIndexedDictionary(Func<TValue, TKey1> key1Selector, Func<TValue, TKey2> key2Selector)
		{
			m_key1Selector = key1Selector;
			m_key2Selector = key2Selector;
		}
		
		public DualIndexedDictionary(IEnumerable<TValue> items, Func<TValue, TKey1> key1Selector, Func<TValue, TKey2> key2Selector)
		{
			m_key1Selector = key1Selector;
			m_key2Selector = key2Selector;

			if (items == null)
			{
				return;
			}
			
			foreach (var item in items)
			{
				Add(item);
			}
		}
	}
}