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

		private string m_projectPath;

		private bool m_disposed;

		private static UserSetting<string> s_repoSerialized = new(
			UserSettings,
			"LockRepoSerialized",
			string.Empty,
			SettingsScope.User
		);

		public static AssetLockManager Instance { get; private set; }

		public static void Reboot()
		{
			Instance.Dispose();
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

		private FileInfo ThrowIfDoesNotExist(string path)
		{
			path = NormalizePathOrThrow(path);
			var file = new FileInfo(path);

			if (!file.Exists)
			{
				throw new FileNotFoundException("File does not exist", path);
			}

			return file;
		}

		private async Task ParseDirectoryAsync(DirectoryInfo dir, List<DirectoryInfo> dirs)
		{
			dirs.Add(dir);
			foreach (FileInfo file in dir.GetFiles())
			{
				if (file.Extension.EndsWith("meta"))
				{
					continue;
				}
				
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

				await InternalTrackFileAsync(file);
			}

			foreach (DirectoryInfo subdir in dir.GetDirectories())
			{
				await ParseDirectoryAsync(subdir, dirs);
			}
		}
	}
}