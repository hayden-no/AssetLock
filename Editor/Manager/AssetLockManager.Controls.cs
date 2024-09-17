using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using static AssetLock.Editor.AssetLockUtility;

namespace AssetLock.Editor.Manager
{
	public partial class AssetLockManager
	{
		/// <summary>
		/// Refreshes the lock repository.
		/// </summary>
		public void Refresh()
		{
			ThrowIfNotInitialized();
			
			if (Refreshing)
			{
				return;
			}
			
			TriggerLoop(0f);
		}
		
		/// <summary>
		/// Refreshes the lock repository asynchronously.
		/// </summary>
		public async Task RefreshAsync()
		{
			ThrowIfNotInitialized();

			if (!Refreshing)
			{
				TriggerLoop(0f);
			}
			
			await GetRefreshTask();
		}

		/// <summary>
		/// Forces a synchronous refresh of the lock repository.
		/// </summary>
		/// <remarks>
		///	This is a BLOCKING operation and should be used with caution - as it could take a long time to complete.
		/// </remarks>
		public void ForceRefresh()
		{
			ThrowIfNotInitialized();

			if (m_disposed)
			{
				return;
			}

			if (Refreshing)
			{
				SpinWait.SpinUntil(() => !Refreshing);

				return;
			}

			InnerLoopAsync().Wait(Constants.DEFAULT_LOCK_TIMEOUT);
		}

		/// <summary>
		/// Parses all files in the project directory and tracks any files that match the tracking patterns.
		/// </summary>
		public void ParseAll()
		{
			ThrowIfNotInitialized();

			var dir = m_projectDir;

			if (!dir.Exists)
			{
				Logging.LogError("Failed to get project directory.");

				return;
			}

			Logging.LogVerboseFormat("Parsing directory {0}", dir.Name);

			List<DirectoryReference> dirs = new();
			ParseDirectory(dir, dirs);

			Logging.LogVerboseFormat("Parsed {0} directories\n\t{1}", dirs.Count, string.Join("\n\t", dirs));
		}

		/// <summary>
		/// Tracks a file in the lock repository.
		/// </summary>
		/// <param name="path">Path to the file</param>
		/// <param name="force">Optional force flag, when true ignores both whether the file matches the patterns set and the in memory status.</param>
		/// <remarks>
		///	Without the force flag, the external command won't be called if the file is already registered as tracked in memory.
		/// </remarks>
		public void TrackFile(string path, bool force = false)
		{
			TrackFile(FileReference.FromPath(path), force);
		}

		internal void TrackFile(FileReference reference, bool force = false)
		{
			ThrowIfNotInitialized();

			if (!force && !ShouldTrack(reference))
			{
				throw new InvalidOperationException("File is not trackable.");
			}
			
			m_commandQueue.Enqueue(new Command(CommandKind.Track, reference, force));
			TriggerLoop();
		}
		
		public async Task TrackFileAsync(string path, bool force = false)
		{
			await TrackFileAsync(FileReference.FromPath(path), force);
		}

		internal async Task TrackFileAsync(FileReference reference, bool force = false)
		{
			TrackFile(reference, force);

			await GetCommandTask();
		}

		/// <summary>
		/// Untracks a file in the lock repository.
		/// </summary>
		/// <param name="path">Path to the file</param>
		/// <param name="force">Optional force flag, when true ignores the in memory status of the file.</param>
		/// <remarks>
		///	Without the force flag, the external command won't be called if the file is not registered as tracked in memory.
		/// </remarks>
		public void UntrackFile(string path, bool force = false)
		{
			UnlockFile(FileReference.FromPath(path), force);
		}

		internal void UntrackFile(FileReference reference, bool force = false)
		{
			ThrowIfNotInitialized();

			if (!force && !m_lockRepo.ContainsKey(reference))
			{
				return;
			}
			
			m_commandQueue.Enqueue(new Command(CommandKind.Untrack, reference, force));
			TriggerLoop();
		}

		public async Task UntrackFileAsync(string path, bool force = false)
		{
			await UntrackFileAsync(FileReference.FromPath(path), force);
		}
		
		internal async Task UntrackFileAsync(FileReference reference, bool force = false)
		{
			UntrackFile(reference, force);

			await GetCommandTask();
		}

		/// <summary>
		/// Tracks a file in the lock repository.
		/// </summary>
		/// <param name="path">Path to the file</param>
		/// <param name="force">Optional force flag, when true ignores whether the file is tracked and if it's already locked</param>
		public void LockFile(string path, bool force = false)
		{
			LockFile(FileReference.FromPath(path), force);
		}

		internal void LockFile(FileReference reference, bool force = false)
		{
			ThrowIfNotInitialized();

			if (!force)
			{
				if (!m_lockRepo.TryGetValue(reference, out var info))
				{
					throw new InvalidOperationException("File is not tracked.");
				}

				if (info.locked)
				{
					throw new InvalidOperationException(
						$"File is already locked by {(info.LockedByMe ? "user" : info.owner)}."
					);
				}
			}
			else
			{
				if (!m_lockRepo.TryGetValue(reference, out var info))
				{
					TrackFile(reference, true);
				}
				else
				{
					if (info.locked)
					{
						UnlockFile(reference, true);
					}
				}
			}
			
			m_commandQueue.Enqueue(new Command(CommandKind.Lock, reference, force));
			TriggerLoop();
		}

		public async Task LockFileAsync(string path, bool force = false)
		{
			await LockFileAsync(FileReference.FromPath(path), force);
		}
		
		internal async Task LockFileAsync(FileReference reference, bool force = false)
		{
			LockFile(reference, force);

			await GetCommandTask();
		}

		/// <summary>
		/// Unlocks a file in the lock repository.
		/// </summary>
		/// <param name="path">Path to the file</param>
		/// <param name="force">Optional force flag, when true ignores if the file is locked by someone else</param>
		public void UnlockFile(string path, bool force = false)
		{
			UnlockFile(FileReference.FromPath(path), force);
		}

		internal void UnlockFile(FileReference reference, bool force = false)
		{
			ThrowIfNotInitialized();

			if (!force)
			{
				if (!m_lockRepo.TryGetValue(reference, out var info))
				{
					throw new InvalidOperationException("File is not tracked.");
				}

				if (!info.LockedByMe)
				{
					throw new InvalidOperationException($"File is locked by {info.owner} - can only unlock files owned by user.");
				}
			}
			
			m_commandQueue.Enqueue(new Command(CommandKind.Unlock, reference, force));
			TriggerLoop();
		}
		
		public async Task UnlockFileAsync(string path, bool force = false)
		{
			await UnlockFileAsync(FileReference.FromPath(path), force);
		}
		
		internal async Task UnlockFileAsync(FileReference reference, bool force = false)
		{
			UnlockFile(reference, force);

			await GetCommandTask();
		}

		/// <summary>
		/// Refreshes the lock status of a file in the lock repository.
		/// </summary>
		/// <param name="path">Path to the file</param>
		public void RefreshFile(string path)
		{
			RefreshFile(FileReference.FromPath(path));
		}
		
		internal void RefreshFile(FileReference reference)
		{
			ThrowIfNotInitialized();
			m_commandQueue.Enqueue(new Command(CommandKind.Update, reference));
			TriggerLoop();
		}
		
		public async Task RefreshFileAsync(string path)
		{
			await RefreshFileAsync(FileReference.FromPath(path));
		}
		
		internal async Task RefreshFileAsync(FileReference reference)
		{
			RefreshFile(reference);
			await GetCommandTask();
		}

		internal void RegisterMove(FileReference source, string dest)
		{
			var temp = m_lockRepo[source];
			var destFile = FileReference.FromPath(dest);
			m_lockRepo.Remove(source);
			m_lockRepo[destFile] = destFile.ToLock(temp.locked, temp.lockId, temp.owner, temp.lockedAt);
		}

		/// <summary>
		/// Unlocks all files locked by the current user.
		/// </summary>
		public void UnlockAll()
		{
			ThrowIfNotInitialized();

			foreach (var info in Repo.Values.Where(l => l.LockedByMe))
			{
				UnlockFile(info);
			}
		}
		
		internal bool IsTracked(FileReference reference)
		{
			return m_lockRepo.ContainsKey(reference);
		}
		
		/// <summary>
		/// Checks if a file is tracked in the in memory lock repository.
		/// </summary>
		/// <param name="path">Path to the file</param>
		/// <returns>True if tracked, false otherwise</returns>
		public bool IsTracked(string path)
		{
			return IsTracked(FileReference.FromPath(path));
		}
		
		internal bool IsLocked(FileReference reference)
		{
			return m_lockRepo.TryGetValue(reference, out var info) && info.locked;
		}
		
		/// <summary>
		/// Checks if a file is locked in the in memory lock repository.
		/// </summary>
		/// <param name="path">Path to the file</param>
		/// <returns>True if locked, false otherwise</returns>
		public bool IsLocked(string path)
		{
			return IsLocked(FileReference.FromPath(path));
		}

		internal void PrintLockRepo()
		{
			StringBuilder sb = new();
			
			foreach (var l in Repo.Values)
			{
				sb.AppendLine('\t' + l.ToString());
			}
			
			Logging.LogFormat("Current Lock Cache ({0}):\n{1}", Repo.Count, sb);
		}

		internal void ResetLockRepo()
		{
			m_lockRepo.Clear();
			Logging.Log("Reset Lock Cache");
		}
	}
}