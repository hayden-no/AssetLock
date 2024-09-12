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

		internal bool Refreshing
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

		private void EndRefresh()
		{
			foreach (var tcm in m_refreshTasks)
			{
				tcm.SetResult(true);
			}
			
			m_refreshTasks.Clear();
		}

		private async void EditorLoop()
		{
			if (!MasterEnable)
			{
				return;
			}
			
			if (!ShouldRunThisFrame())
			{
				return;
			}
			
			await InnerLoopAsync();
			EndRefresh();
		}

		private async Task InnerLoopAsync()
		{
			using var profiler = new Logging.Profiler();
			
			Refreshing = true;

			await HandleCommandsAsync();
			
			var locks = await InternalRefreshAllLocksAsync();
			m_lockRepo.Update(locks);
			
			m_refreshTimer = AssetLockSettings.RefreshRate;
			Refreshing = false;
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

		private async Task HandleCommandsAsync()
		{
			while (m_commandQueue.TryDequeue(out var command))
			{
				await command.ExecuteAsync();
			}
		}
	}
}