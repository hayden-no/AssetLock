using System;
using System.IO;
using AssetLock.Editor.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AssetLock.Editor.Tests
{
	[TestFixture, Category("AssetLockUnit")]
	internal class LockRepoTests : ManagerFixture
	{
		private string m_path;
		private FileReference m_file;

		[SetUp]
		public void CreateAsset()
		{
			m_path = "Assets/AssetLockRepo-" + Guid.NewGuid().ToString("N") + ".bin";
			File.WriteAllBytes(
				Path.Combine(Directory.GetParent(Application.dataPath).FullName, m_path),
				new byte[] { 1, 2, 3 }
			);
			AssetDatabase.ImportAsset(m_path, ImportAssetOptions.ForceSynchronousImport);
			m_file = FileReference.FromPath(m_path);
		}

		[TearDown]
		public void RemoveAsset() => AssetDatabase.DeleteAsset(m_path);

		[Test]
		public void UpdateReplacesLockAndResetsMissingEntries()
		{
			var repo = new LockRepo();
			var locked = m_file.ToLock(true, "id", "owner");
			repo.Update(new[] { locked, locked });
			Assert.That(repo.Count, Is.EqualTo(1));
			Assert.That(repo[m_file].owner, Is.EqualTo("owner"));
			repo.Update(Array.Empty<LockInfo>());
			Assert.That(repo.Count, Is.EqualTo(1));
			Assert.That(repo[m_file].locked, Is.False);
			Assert.That(repo[m_file].owner, Is.Null);
		}

		[Test]
		public void SerializationRoundTripsLockState()
		{
			var repo = new LockRepo();
			repo[m_file] = m_file.ToLock(true, "id", "owner", "2026-01-01T00:00:00Z");
			var restored = LockRepo.Deserialize(repo.Serialize());
			Assert.That(restored.Count, Is.EqualTo(1));
			Assert.That(restored[m_file].lockId, Is.EqualTo("id"));
			Assert.That(restored[m_file].owner, Is.EqualTo("owner"));
		}
	}
}