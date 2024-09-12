using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using static AssetLock.Editor.AssetLockUtility;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Manager
{
	public partial class AssetLockManager
	{
		private enum CommandKind
		{
			Track,
			Untrack,
			Lock,
			Unlock,
			Update
		}

		private struct Command
		{
			public CommandKind Kind;
			public FileReference File;
			public bool Force;

			public Command(CommandKind kind, FileReference reference, bool force = false)
			{
				Kind = kind;
				File = reference;
				Force = force;
			}

			public async Task ExecuteAsync()
			{
				if (!File.Exists)
				{
					throw new FileNotFoundException("File not found", File.GitPath);
				}

				switch (Kind)
				{
					case CommandKind.Track:
						await Track();

						break;
					case CommandKind.Untrack:
						await Untrack();

						break;
					case CommandKind.Lock:
						await Lock();

						break;
					case CommandKind.Unlock:
						await Unlock();

						break;
					case CommandKind.Update:
						await Update();

						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			private async Task Track()
			{
				await Instance.InternalTrackFileAsync(File, Force);
			}

			private async Task Untrack()
			{
				await Instance.InternalUntrackFileAsync(File, Force);
			}

			private async Task Lock()
			{
				await Instance.InternalLockFileAsync(File);
			}

			private async Task Unlock()
			{
				await Instance.InternalUnlockFileAsync(File, Force);
			}

			private async Task Update()
			{
				await Instance.InternalRefreshLockAsync(File);
			}
		}

		/// <summary>
		/// Initializes the Git LFS process and sets the user and remote URL.
		/// </summary>
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

			if (string.IsNullOrWhiteSpace(GitWorkingDirectory))
			{
				GitWorkingDirectory.SetValue(await GetGitWorkingDirectory());
			}

			if (string.IsNullOrWhiteSpace(GitRemoteUrl))
			{
				var gitRemote = await GetGitRemote();
				GitRemoteUrl.SetValue(gitRemote);
				GitLfsServerUrl.SetValue(gitRemote + "/info/lfs");
				GitLfsServerLocksApiUrl.SetValue(gitRemote + "/info/lfs/locks");
			}

			if (ParseFilesOnStartup)
			{
				ParseAll();
			}
		}

		/// <summary>
		/// Gets the username from the global git configuration.
		/// </summary>
		/// <returns>git username</returns>
		private async Task<string> GetGitUser()
		{
			const string cmd = "config";
			const string arg1 = "--global";
			const string arg2 = "user.name";

			var result = await m_gitProcess.RunCommandAsync(cmd, arg1, arg2);
			ThrowOnProcessError(result, "failed to get git user");

			return result.StdOut.Trim();
		}

		/// <summary>
		/// Gets the remote URL for the git repository.
		/// </summary>
		/// <returns>The remote URL</returns>
		private async Task<string> GetGitRemote()
		{
			const string cmd = "remote";
			const string arg1 = "-v";

			var result = await m_gitProcess.RunCommandAsync(cmd, arg1);
			ThrowOnProcessError(result);

			var lines = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			string repo = string.Empty;

			foreach (var line in lines)
			{
				if (!line.Contains("(fetch)"))
				{
					continue;
				}

				repo = line.Split('\t')[1]
					.Split(' ')[0]; // example: origin  https://github.com/hayden-no/AssetLock.git (fetch)

				break;
			}

			return repo;
		}

		/// <summary>
		/// Gets the working directory of the git repository.
		/// </summary>
		/// <returns>The working git directory</returns>
		private async Task<string> GetGitWorkingDirectory()
		{
			const string cmd = "rev-parse";
			const string arg1 = "--show-toplevel";

			var result = await m_gitProcess.RunCommandAsync(cmd, arg1);
			ThrowOnProcessError(result, "failed to get git working directory");

			return result.StdOut.Trim();
		}

		/// <summary>
		/// Internal method used by the AssetLockManager class to track a file for Git LFS.
		/// </summary>
		/// <param name="reference">The file reference of the file to track.</param>
		/// <param name="force">Optional. When true, ignores in memory repo. Default value is false.</param>
		/// <returns>A Task representing the asynchronous operation.</returns>
		private async Task InternalTrackFileAsync(FileReference reference, bool force = false)
		{
			const string cmd = "track";
			const string arg1 = "--filename";
			const string arg2 = "--lockable";

			ThrowIfNotInitialized();

			if (!force && m_lockRepo.ContainsKey(reference))
			{
				// already tracked
				Logging.LogVerboseFormat("File already tracked\n{0}\n", reference);

				return;
			}

			var result = await m_lfsProcess.RunCommandAsync(cmd, arg1, arg2, reference.AsProcessArg());
			ThrowOnProcessError(result);
			m_lockRepo[reference] = reference.ToLock();
			Logging.LogVerboseFormat("Tracked file \n{0}\n", reference);
		}

		/// <summary>
		/// Untracks a file in Git LFS. If the file is not being tracked, it will log a message and return.
		/// </summary>
		/// <param name="reference">The file reference.</param>
		/// <param name="force">Optional. When true, ignores in memory repo. Default value is false.</param>
		/// <returns>A Task representing the asynchronous operation.</returns>
		private async Task InternalUntrackFileAsync(FileReference reference, bool force = false)
		{
			const string cmd = "untrack";

			ThrowIfNotInitialized();

			if (!force && !m_lockRepo.ContainsKey(reference))
			{
				// not tracked
				Logging.LogVerboseFormat("Failed to untrack untracked file \n{0}\n", reference);

				return;
			}

			var result = await m_lfsProcess.RunCommandAsync(cmd, reference.AsProcessArg());
			ThrowOnProcessError(result);
			m_lockRepo.Remove(reference);
			Logging.LogVerboseFormat("Untracked file {0}", reference);
		}

		/// <summary>
		/// Locks a file asynchronously.
		/// </summary>
		/// <param name="reference">The reference to the file to be locked.</param>
		/// <returns>A task representing the asynchronous operation.</returns>
		private async Task InternalLockFileAsync(FileReference reference)
		{
			const string cmd = "lock";
			ThrowIfNotInitialized();

			if (UseHttp)
			{
				var response = await CreateLockHttp(reference);

				if (response.Item1)
				{
					m_lockRepo.Remove(reference);
					m_lockRepo[response.Item2] = response.Item2;
				}
			}
			else
			{
				var result = await m_lfsProcess.RunCommandAsync(cmd, reference.AsProcessArg());
				ThrowOnProcessError(result, $"failed to lock file '{reference.GitPath}'");
				m_lockRepo[reference] = reference.ToLock(
					true,
					null,
					m_user,
					DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ")
				);
			}

			Logging.LogVerboseFormat("Locked file {0}", reference);
		}

		/// <summary>
		/// Unlocks a file asynchronously.
		/// </summary>
		/// <param name="reference">The file reference.</param>
		/// <param name="force">Optional. Specifies whether to force the unlock. Default value is false.</param>
		/// <returns>A task representing the unlocking operation.</returns>
		private async Task InternalUnlockFileAsync(FileReference reference, bool force = false)
		{
			const string cmd = "unlock";
			const string frc = "--force";

			ThrowIfNotInitialized();

			if (UseHttp)
			{
				if (m_lockRepo.TryGetValue(reference, out var info))
				{
					var response = await DeleteLockHttp(info, force);

					if (response.Item1)
					{
						m_lockRepo.Remove(reference);
						// we know it's no longer locked
						var l = response.Item2;
						l.locked = false;
						m_lockRepo[l] = l;
					}
				}
				else
				{
					throw new InvalidOperationException("File is not tracked.");
				}
			}
			else
			{
				var result = await m_lfsProcess.RunCommandAsync(cmd, force ? frc : string.Empty, reference.AsProcessArg());
				ThrowOnProcessError(result);
				m_lockRepo[reference] = reference.ToLock();
			}

			Logging.LogVerboseFormat("Unlocked file {0}", reference);
		}

		/// <summary>
		/// Refreshes the lock status of a given file.
		/// </summary>
		/// <param name="reference">The file reference.</param>
		/// <returns>A boolean indicating whether the file is locked or not.</returns>
		private async Task<bool> InternalRefreshLockAsync(FileReference reference)
		{
			const string cmd = "locks";
			const string arg1 = "--path=";

			ThrowIfNotInitialized();

			if (UseHttp)
			{
				var request = new ListLocksRequest() { Path = reference.GitPath };
				var response = await ListLocksHttp(request);

				if (response.Item1)
				{
					foreach (var info in response.Item3)
					{
						m_lockRepo[info] = info;
					}

					return response.Item3.Any();
				}
				else
				{
					return true;
				}
			}
			else
			{
				var result = await m_lfsProcess.RunCommandAsync(cmd, Constants.JSON_FLAG, GetFileArg());
				ThrowOnProcessError(result, $"failed to check if file is locked\n{reference}\n");
				string json = result.StdOut;

				if (string.IsNullOrWhiteSpace(json))
				{
					m_lockRepo[reference] = reference.ToLock();
					Logging.LogVerboseFormat("File is not locked \n{0}\n", reference);

					return false;
				}

				bool locked = false;

				foreach (var info in FromJson(json))
				{
					m_lockRepo[info] = info;
					locked = true;

					Logging.LogVerboseFormat("File is locked by {1} \n{0}\n", reference, info.owner);
				}

				return locked;
			}

			string GetFileArg() => $"{arg1}{reference.AsProcessArg()}";
		}

		/// <summary>
		/// Retrieves all locks asynchronously from the Git LFS process.
		/// </summary>
		/// <returns>A task that represents the asynchronous operation. The task result contains a List of LockInfo objects representing the retrieved locks.</returns>
		private async Task<List<LockInfo>> InternalRefreshAllLocksAsync()
		{
			const string cmd = "locks";

			ThrowIfNotInitialized();

			if (UseHttp)
			{
				var response = await ListLocksHttp(new());

				if (response.Item1)
				{
					return response.Item3.ToList();
				}
				else
				{
					return new();
				}
			}
			else
			{
				var result = await m_lfsProcess.RunCommandAsync(cmd, Constants.JSON_FLAG);
				ThrowOnProcessError(result, "failed to list locks");

				return FromJson(result.StdOut);
			}
		}
	}
}