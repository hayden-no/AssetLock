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
		enum CommandKind
		{
			Track,
			Untrack,
			Lock,
			Unlock
		}

		struct Command
		{
			public CommandKind Kind;
			public FileInfo File;
			public bool Force;
			
			public Task ExecuteAsync()
			{
				return Kind switch
				{
					CommandKind.Track => Instance.TrackFileAsync(File.FullName, Force),
					CommandKind.Untrack => Instance.UntrackFileAsync(File.FullName, Force),
					CommandKind.Lock => Instance.LockFileAsync(File.FullName),
					CommandKind.Unlock => Instance.UnlockFileAsync(File.FullName),
					_ => throw new ArgumentOutOfRangeException()
				};
			}
		}
		
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

		private async Task TrackFileAsync(string path, bool force=false)
		{
			const string cmd = "track";
			const string arg1 = "--filename";
			const string arg2 = "--lockable";

			ThrowIfNotInitialized();
			path = NormalizePath(path);

			if (!force && m_lockRepo.GetByPath(path).path == path)
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

		private async Task UntrackFileAsync(string path, bool force = false)
		{
			const string cmd = "untrack";
			
			ThrowIfNotInitialized();
			path = NormalizePathOrThrow(path);

			if (!force && !m_lockRepo.GetByPath(path).HasValue)
			{
				// not tracked
				Logging.LogVerboseFormat("Tried untracking file {0} but it is not tracked", path);
				
				return;
			}
			
			var result = await m_lfsProcess.RunCommandAsync(cmd, GetFileArg());
			ThrowOnProcessError(result);
			m_lockRepo.RemoveLockByPath(path);
			Logging.LogVerboseFormat("Untracked file {0}", path);
			
			string GetFileArg() => $"{NormalizePath(path)}";
		}

		private async Task LockFileAsync(string path)
		{
			const string cmd = "lock";

			ThrowIfNotInitialized();
			path = NormalizePathOrThrow(path);
			var result = await m_lfsProcess.RunCommandAsync(cmd, GetFileArg());
			ThrowOnProcessError(result, $"failed to lock file '{path}'");
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
	}
}