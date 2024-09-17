using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using AssetLock.Editor.UI;
using UnityEditor;
using UnityEditor.SettingsManagement;
using UnityEngine;
using static AssetLock.Editor.AssetLockUtility;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Manager
{
	/// <summary>
	/// The AssetLockManager is the main class for the AssetLock package.
	/// Here you can track files, lock and unlock files, and manage the lock repository.
	/// It handles all the communication with Git and Git-LFS.
	/// </summary>
	[InitializeOnLoad]
	public partial class AssetLockManager : IDisposable
	{
		private LockRepo m_lockRepo;
		internal IReadOnlyDictionary<FileReference, LockInfo> Repo => m_lockRepo;

		private ProcessWrapper m_gitProcess;
		private ProcessWrapper m_lfsProcess;

		private string m_user;
		internal string User => m_user;
		private bool m_lfsInitialized;
		private bool m_lfsFailed;

		private DirectoryReference m_projectDir;
		internal DirectoryReference ProjectDir => m_projectDir;

		private bool m_disposed;
		
		/// <summary>
		/// The total number of files being tracked by Asset Lock.
		/// </summary>
		public int TrackedCount => m_lockRepo.Count;
		/// <summary>
		/// Total number of files currently locked in the repository.
		/// </summary>
		public int LockedCount => m_lockRepo.Locks.Count(l => l.locked);
		/// <summary>
		/// Total number of files currently locked by the current user.
		/// </summary>
		/// <remarks>
		/// The current user as determined by the Git configuration.
		/// </remarks>
		public int LockedByMeCount => m_lockRepo.Locks.Count(l => l is { locked: true, LockedByMe: true });
		/// <summary>
		/// Total number of files currently locked by other users.
		/// </summary>
		/// /// <remarks>
		/// The current user as determined by the Git configuration.
		/// </remarks>
		public int LockedByOthersCount => m_lockRepo.Locks.Count(l => l is { locked: true, LockedByMe: false });
		/// <summary>
		/// Total number of files that are not locked, but are being tracked.
		/// </summary>
		public int UnlockedCount => m_lockRepo.Locks.Count(l => !l.locked);

		private static UserSetting<string> s_repoSerialized = new(
			UserSettings,
			"LockRepoSerialized",
			string.Empty,
			SettingsScope.User
		);

		/// <summary>
		/// The singleton instance of the AssetLockManager.
		/// </summary>
		public static AssetLockManager Instance { get; private set; }

		/// <summary>
		/// Reboots the AssetLockManager.
		/// </summary>
		public static void Reboot()
		{
			Instance.Dispose();
			Instance = new AssetLockManager();
		}

		/// <summary>
		/// Unity entry point for the AssetLockManager.
		/// </summary>
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

			m_projectDir = DirectoryReference.FromPath(Path.GetFullPath(Application.dataPath));

			m_gitProcess = new ProcessWrapper(GitPath);
			m_lfsProcess = new ProcessWrapper(GitLfsPath);

			EditorApplication.projectWindowItemOnGUI += ProjectWindowGUI.DrawOnProjectWindowGUI;
			EditorApplication.update += EditorLoop;
			EditorApplication.quitting += Dispose;

			_ = InitGitLfs();
		}
		
		~AssetLockManager()
		{
			if (!m_disposed)
			{
				Logging.LogWarning("AssetLockManager was not disposed, disposing now.");
				Dispose();
			}
		}

		///<inheritdoc/>
		public void Dispose()
		{
			s_repoSerialized.SetValue(m_lockRepo.Serialize(), true);
			
			EditorApplication.projectWindowItemOnGUI -= ProjectWindowGUI.DrawOnProjectWindowGUI;
			EditorApplication.update -= EditorLoop;
			EditorApplication.quitting -= Dispose;
			
			m_disposed = true;
		}

		private void ThrowOnProcessError(ProcessResult result, string msg = "")
		{
			if (result.HasErrText)
			{
				if (string.IsNullOrWhiteSpace(msg))
				{
					Logging.LogError(result.StdErr);
				}
				else
				{
					Logging.LogErrorFormat("{0}\n{1}", msg, result.StdErr);
				}

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

		private void ParseDirectory(DirectoryReference dir, List<DirectoryReference> dirs)
		{
			dirs.Add(dir);
			foreach (var file in dir.Files)
			{
				file.TrackFile();
			}

			foreach (var directory in dir.Directories)
			{
				ParseDirectory(directory, dirs);
			}
		}
	}
}