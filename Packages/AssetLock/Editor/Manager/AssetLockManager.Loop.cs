using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using static AssetLock.Editor.AssetLockUtility;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Manager
{
	public partial class AssetLockManager
	{
		ConcurrentQueue<Command> m_commandQueue = new();

		private double m_lastUpdate;
		private double m_refreshTimer;
		private long m_refreshing;

		private bool Refreshing
		{
			get => Interlocked.Read(ref m_refreshing) > 0;
			set => Interlocked.Exchange(ref m_refreshing, value ? 1 : 0);
		}
		
		private ConcurrentBag<TaskCompletionSource<bool>> m_refreshTasks = new();

		private void TriggerLoop(float delay = 0.25f)
		{
			if (Refreshing)
			{
				return;
			}

			m_refreshTimer = delay;
		}

		private Task<bool> GetRefreshTask()
		{
			var tcm = new TaskCompletionSource<bool>();
			m_refreshTasks.Add(tcm);
			return tcm.Task;
		}

		private async Task<bool> GetCommandTask()
		{
			if (Refreshing)
			{
				await GetRefreshTask();
			}
			
			var tcm = new TaskCompletionSource<bool>();
			m_refreshTasks.Add(tcm);
			return await tcm.Task;
		}

		private void EndRefresh(System.Exception failure = null)
		{
			var completed = new System.Collections.Generic.List<TaskCompletionSource<bool>>();
			while (m_refreshTasks.TryTake(out var tcm))
			{
				completed.Add(tcm);
			}
			foreach (var tcm in completed)
			{
				if (failure == null) tcm.TrySetResult(true);
				else tcm.TrySetException(failure);
			}
			EditorApplication.RepaintProjectWindow();
		}

		private async void EditorLoop()
		{
			if (!MasterEnable || !IsReady)
			{
				return;
			}
			
			if (!ShouldRunThisFrame())
			{
				return;
			}
			
			try
			{
				await InnerLoopAsync();
				EndRefresh();
			}
			catch (System.Exception error)
			{
				Logging.LogErrorFormat("Refresh failed ({0}).", error.GetType().Name);
				EndRefresh(error);
			}
		}

		internal async Task InnerLoopAsync()
		{
			using var profiler = new Logging.Profiler();
			
			Refreshing = true;

			try
			{
				await HandleCommandsAsync();
				var locks = await InternalRefreshAllLocksAsync();
				if (locks == null) throw new System.InvalidOperationException("Failed to refresh Git LFS locks.");
				m_lockRepo.Update(locks);
			}
			finally
			{
				m_refreshTimer = AssetLockSettings.RefreshRate;
				Refreshing = false;
			}
		}

		private bool ShouldRunThisFrame()
		{
			var time = m_lastUpdate;
			m_lastUpdate = EditorApplication.timeSinceStartup;
			time = m_lastUpdate - time;
			m_refreshTimer -= time;
			
			if (Refreshing)
			{
				return false;
			}
			
			if (m_refreshTimer > 0)
			{
				return false;
			}

			return true;
		}

		// TODO: merge commands by dependency and run them in parallel
		private async Task HandleCommandsAsync()
		{
			while (m_commandQueue.TryDequeue(out var command))
			{
				await command.ExecuteAsync();
			}
		}
	}
}
