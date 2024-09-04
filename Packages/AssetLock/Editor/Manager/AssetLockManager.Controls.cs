using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
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
			if (Refreshing)
			{
				while (Refreshing)
				{
					await Task.Yield();
				}

				return;
			}

			await InnerLoopAsync();
		}

		public void ForceRefresh()
		{
			ThrowIfNotInitialized();

			if (Refreshing)
			{
				SpinWait.SpinUntil(() => !Refreshing);

				return;
			}

			InnerLoopAsync().Wait();
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
				if (!m_lockRepo.ContainsKey(reference))
				{
					TrackFile(reference, true);
				}
			}
			
			m_commandQueue.Enqueue(new Command(CommandKind.Lock, reference, force));
			TriggerLoop();
		}

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

		internal void RegisterMove(FileReference source, string dest)
		{
			var temp = m_lockRepo[source];
			var destFile = FileReference.FromPath(dest);
			m_lockRepo.Remove(source);
			m_lockRepo[destFile] = destFile.ToLock(temp.locked, temp.lockId, temp.owner, temp.lockedAt);
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