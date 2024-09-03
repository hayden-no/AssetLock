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
	[InitializeOnLoad]
	public partial class AssetLockManager
	{
		private LockRepo m_lockRepo;

		private ProcessWrapper m_gitProcess;
		private ProcessWrapper m_lfsProcess;

		private string m_user;
		internal string User => m_user;
		private bool m_lfsInitialized;
		private bool m_lfsFailed;

		private string m_projectPath;

		private double m_refreshStartTime;
		private bool m_refreshing;

		private static UserSetting<string> s_repoSerialized = new(
			UserSettings,
			"LockRepoSerialized",
			string.Empty,
			SettingsScope.User
		);

		public static AssetLockManager Instance { get; private set; }

		public static void Reboot()
		{
			Instance = null;
			Instance = new AssetLockManager();
		}

		static AssetLockManager()
		{
			Instance ??= new AssetLockManager();
		}

		private AssetLockManager()
		{
			if (!MasterEnable)
			{
				return;
			}
			
			m_lockRepo = LockRepo.Deserialize(s_repoSerialized.value);

			m_projectPath = Path.GetFullPath(Application.dataPath);

			m_gitProcess = new ProcessWrapper(GitPath);
			m_lfsProcess = new ProcessWrapper(GitLfsPath);

			EditorApplication.projectWindowItemOnGUI += ProjectWindowGUI.DrawOnProjectWindowGUI;
			EditorApplication.update += UpdateLoop;

			_ = InitGitLfs();
		}

		private async void UpdateLoop()
		{
			if (m_refreshing)
			{
				return;
			}
			
			if (m_refreshStartTime + AssetLockSettings.RefreshRate > EditorApplication.timeSinceStartup)
			{
				return;
			}
			
			await RefreshAsync();
		}

		~AssetLockManager()
		{
			s_repoSerialized.SetValue(m_lockRepo.Serialize());
			
			EditorApplication.projectWindowItemOnGUI -= ProjectWindowGUI.DrawOnProjectWindowGUI;
			EditorApplication.update -= UpdateLoop;
		}
		

	#region Controls

		

		internal bool TryGetLockInfoByPath(string path, out LockInfo info)
		{
			ThrowIfNotInitialized();
			
			path = NormalizePath(path);
			info = m_lockRepo.GetByPath(path);

			return info.HasValue;
		}

		internal LockInfo GetOrTrack(string path)
		{
			ThrowIfNotInitialized();
			
			path = NormalizePath(path);
			LockInfo info = m_lockRepo.GetByPath(path);
			Logging.LogVerboseFormat("GetOrTrack:\npath: {0}\nresult: {1}", path, info);

			if (!info.HasValue)
			{
				_ = TrackFileAsync(path);
				info = m_lockRepo.GetByPath(path);
			}
			else
			{
				IsFileLockedAsync(info.path)
					.ContinueWith(
						(task =>
						{
							if (task.Result != info.locked)
							{
								var l = info;
								l.locked = task.Result;
								m_lockRepo.UpdateLock(info, l);
							}
						})
					);
			}

			return info;
		}

		internal bool TryGetLockInfoByGuid(string guid, out LockInfo info)
		{
			ThrowIfNotInitialized();
			
			info = m_lockRepo.GetByGuid(guid);

			return info.HasValue;
		}

		internal LockInfo GetOrTrackByGuid(string guid)
		{
			ThrowIfNotInitialized();
			
			LockInfo info = m_lockRepo.GetByGuid(guid);

			if (!info.HasValue)
			{
				TrackFileAsync(AssetDatabase.GUIDToAssetPath(guid)).Wait(Constants.DEFAULT_LOCK_TIMEOUT);
				info = m_lockRepo.GetByGuid(guid);
			}

			return info;
		}

		public void Refresh()
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

			RefreshAsync().Wait(Constants.DEFAULT_LOCK_TIMEOUT);
		}

		internal void ForceRefresh()
		{
			ThrowIfNotInitialized();
			
			if (m_refreshing)
			{
				return;
			}

			m_refreshStartTime = 0;
			Refresh();
		}

		public void SetLockByPath(string path, bool locked)
		{
			ThrowIfNotInitialized();
			
			path = NormalizePath(path);
			TrackFileAsync(path)
				.ContinueWith(
					async (t) =>
					{
						if (locked)
						{
							await LockFileAsync(path);
						}
						else
						{
							await UnlockFileAsync(path);
						}
					}
				);
		}

		public void UntrackFile(string path)
		{
			ThrowIfNotInitialized();
			
			path = NormalizePath(path);

			if (m_lockRepo.IsTrackedByPath(path))
			{
				_ = UnlockFileAsync(path);
				m_lockRepo.RemoveLockByPath(path);
			}
		}

		internal async Task SetLockAsync(LockInfo info)
		{
			ThrowIfNotInitialized();
			
			if (!m_lockRepo.IsTracked(info))
			{
				await TrackFileAsync(info.path);
			}

			if (info.locked)
			{
				await LockFileAsync(info.path);
			}
			else
			{
				await UnlockFileAsync(info.path);
			}
		}

		internal void SetLock(LockInfo info)
		{
			ThrowIfNotInitialized();
			
			if (!m_lockRepo.IsTracked(info))
			{
				TrackFileAsync(info.path)
					.ContinueWith(
						async (t) =>
						{
							if (info.locked)
							{
								await LockFileAsync(info.path);
							}
							else
							{
								await UnlockFileAsync(info.path);
							}
						}
					);
			}
			else
			{
				if (info.locked)
				{
					LockFileAsync(info.path).Wait(Constants.DEFAULT_LOCK_TIMEOUT);
				}
				else
				{
					UnlockFileAsync(info.path).Wait(Constants.DEFAULT_LOCK_TIMEOUT);
				}
			}
		}

		internal void PrintLockRepo()
		{
			StringBuilder sb = new();

			int cnt = 0;
			foreach (var l in m_lockRepo.Locks)
			{
				sb.AppendLine('\t' + l.ToString());
				cnt++;
			}
			
			Logging.LogFormat("Current Lock Cache ({0}):\n{1}", cnt, sb);
		}

		internal void ResetLockRepo()
		{
			m_lockRepo.Reset();
			Logging.Log("Reset Lock Cache");
		}

	#endregion

		private void ThrowOnProcessError(ProcessResult result, string msg = "")
		{
			if (result.HasErrText)
			{
				Logging.LogErrorFormat("{0} {1}", msg, result.StdErr);

				throw new Exception(result.StdErr);
			}
		}

		private async void ThrowIfNotInitialized()
		{
			if (!m_lfsInitialized)
			{
				if (m_lfsFailed)
				{
					Throw();
				}
				
				int iter = 0;

				while (iter < Constants.DEFAULT_LOCK_TIMEOUT)
				{
					await Task.Delay(Constants.DEFAULT_LOCK_TIMEOUT / 10);
					iter+= Constants.DEFAULT_LOCK_TIMEOUT / 10;
				}

				if (!m_lfsInitialized)
				{
					m_lfsFailed = true;
					Throw();
				}
			}

			void Throw()
			{
				throw new InvalidOperationException("Git-LFS not initialized, please reboot AssetLock");
			}
		}

		private async Task ParseDirectoryAsync(DirectoryInfo dir, List<DirectoryInfo> dirs)
		{
			dirs.Add(dir);
			foreach (FileInfo file in dir.GetFiles())
			{
				if (!IsTrackedExtension(file))
				{
					continue;
				}

				if (IsUnityYaml(file))
				{
					continue;
				}

				if (!IsBinary(file))
				{
					continue;
				}

				await TrackFileAsync(file.FullName);
			}

			foreach (DirectoryInfo subdir in dir.GetDirectories())
			{
				await ParseDirectoryAsync(subdir, dirs);
			}
		}
	}
}