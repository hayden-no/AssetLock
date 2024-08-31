using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SettingsManagement;
using UnityEngine;
using static AssetLock.Editor.AssetLockUtility;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor
{
	[InitializeOnLoad]
	public class AssetLockManager
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

			_ = InitGitLfs();
		}

		~AssetLockManager()
		{
			s_repoSerialized.SetValue(m_lockRepo.Serialize());
		}

	#region Command Interface

		private async Task InitGitLfs()
		{
			const string installCmd = "install";
			const string envCmd = "env";

			ThrowOnProcessError(await m_lfsProcess.RunCommandAsync(installCmd), "failed to initialize git-lfs");

			string user = await GetGitUser();

			if (!string.IsNullOrEmpty(user))
			{
				m_user = user;
				m_lfsInitialized = true;
				Logging.LogFormat("Git LFS initialized for user {0}", m_user);
			}

			Logging.LogFormat("Git LFS environment: {0}", await m_lfsProcess.RunCommandAsync(envCmd));
		}

		private async Task<string> GetGitUser()
		{
			string cmd = "config";
			string arg1 = "--global";
			string arg2 = "user.name";

			var result = await m_gitProcess.RunCommandAsync(cmd, arg1, arg2);
			ThrowOnProcessError(result, "failed to get git user");

			return result.StdOut.Trim();
		}

		private async Task TrackFileAsync(string path)
		{
			const string cmd = "track";
			const string arg1 = "--filename";
			const string arg2 = "--lockable";

			ThrowIfNotInitialized();
			path = NormalizePath(path);

			if (m_lockRepo.GetByPath(path).path == path)
			{
				// already tracked
				Logging.LogVerboseFormat("Tried tracking file {0} but it is already tracked", path);

				return;
			}

			var result = await m_lfsProcess.RunCommandAsync(cmd, arg1, arg2, GetFileArg());
			ThrowOnProcessError(result);
			m_lockRepo.AddLock(LockInfo.FromPath(path));
			Logging.LogVerboseFormat("Tracked file {0}", path);

			string GetFileArg() => $"{NormalizePath(path)}";
		}

		private async Task LockFileAsync(string path)
		{
			const string cmd = "lock";

			ThrowIfNotInitialized();
			path = NormalizePath(path);
			var result = await m_lfsProcess.RunCommandAsync(cmd, GetFileArg());
			ThrowOnProcessError(result);
			m_lockRepo.SetLockByPath(path, true, m_user, DateTime.Now.ToString());
			Logging.LogVerboseFormat("Locked file {0}", path);

			string GetFileArg() => $"{NormalizePath(path)}";
		}

		private async Task UnlockFileAsync(string path)
		{
			const string cmd = "unlock";

			ThrowIfNotInitialized();

			path = NormalizePath(path);

			var result = await m_lfsProcess.RunCommandAsync(cmd, GetFileArg());
			ThrowOnProcessError(result);
			m_lockRepo.SetLockByPath(path, false);
			Logging.LogVerboseFormat("Unlocked file {0}", path);

			string GetFileArg() => $"{NormalizePath(path)}";
		}

		private async Task<bool> IsFileLockedAsync(string path)
		{
			const string cmd = "locks";
			const string arg1 = "--path=";

			ThrowIfNotInitialized();

			path = NormalizePath(path);

			var result = await m_lfsProcess.RunCommandAsync(cmd, Constants.JSON_FLAG, GetFileArg());
			ThrowOnProcessError(result, "failed to check if file is locked");
			string json = result.StdOut;

			if (string.IsNullOrWhiteSpace(json))
			{
				m_lockRepo.SetLockByPath(path, false);
				Logging.LogVerboseFormat("File {0} is not locked", path);

				return false;
			}

			bool locked = false;

			foreach (var info in FromJson(json))
			{
				m_lockRepo.UpdateOrAddLock(info);
				locked = true;

				Logging.LogVerboseFormat("File {0} is locked by {1}", path, info.owner);
			}

			return locked;

			string GetFileArg() => $"{arg1}{NormalizePath(path)}";
		}

		private async Task<List<LockInfo>> GetAllLocksAsync()
		{
			const string cmd = "locks";

			ThrowIfNotInitialized();

			var result = await m_lfsProcess.RunCommandAsync(cmd, Constants.JSON_FLAG);
			ThrowOnProcessError(result);

			return FromJson(result.StdOut);
		}

	#endregion

	#region Controls

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
				Debug.LogError("Failed to get project directory.");

				return;
			}

			await ParseDirectoryAsync(dir);
		}

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

		private async Task ParseDirectoryAsync(DirectoryInfo dir)
		{
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
				await ParseDirectoryAsync(subdir);
			}
		}
	}
}