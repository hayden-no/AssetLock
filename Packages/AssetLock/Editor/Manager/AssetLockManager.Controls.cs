using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AssetLock.Editor.UI;
using UnityEditor;
using UnityEditor.SettingsManagement;
using UnityEngine;
using static AssetLock.Editor.AssetLockUtility;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Manager
{
	public partial class AssetLockManager
	{
		public async Task RefreshAsync()
		{
			ThrowIfNotInitialized();
			
			if (m_refreshing)
			{
				return;
			}

			if (m_refreshStartTime + AssetLockSettings.RefreshRate > EditorApplication.timeSinceStartup)
			{
				return;
			}

			m_refreshing = true;
			m_refreshStartTime = EditorApplication.timeSinceStartup;

			var locks = await GetAllLocksAsync();
			m_lockRepo.Set(locks);

			m_refreshing = false;
		}

		public async Task ParseAllAsync()
		{
			ThrowIfNotInitialized();

			DirectoryInfo dir = new DirectoryInfo(m_projectPath);

			if (!dir.Exists)
			{
				Logging.LogError("Failed to get project directory.");

				return;
			}
			
			Logging.LogVerboseFormat("Parsing directory {0}", m_projectPath);

			List<DirectoryInfo> dirs = new();
			await ParseDirectoryAsync(dir, dirs);
			
			Logging.LogVerboseFormat("Parsed {0} directories\n\t{1}", dirs.Count, string.Join("\n\t", dirs));
		}

		public async Task TrackFile(string path, bool force=false)
		{
			ThrowIfNotInitialized();
			path = NormalizePathOrThrow(path);

			if (!force && m_lockRepo.IsTrackedByPath(path))
			{
				return;
			}
		}
	}
}