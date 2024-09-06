using System;
using System.Collections.Concurrent;
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
		ConcurrentQueue<Command> m_commandQueue = new();

		private double m_lastUpdate;
		private double m_refreshTimer;
		private long m_refreshing;

		internal bool Refreshing
		{
			get => Interlocked.Read(ref m_refreshing) > 0;
			set => Interlocked.Exchange(ref m_refreshing, value ? 1 : 0);
		}

		private void TriggerLoop(float delay = 0.25f)
		{
			if (Refreshing)
			{
				return;
			}

			m_refreshTimer = delay;
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