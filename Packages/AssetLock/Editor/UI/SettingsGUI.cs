using System.Collections.Generic;
using System.Linq;
using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;
using static AssetLock.Editor.AssetLockUtility;
using static UnityEditor.EditorGUILayout;
using static UnityEngine.GUILayout;
using static AssetLock.Editor.UI.ALGUI;

namespace AssetLock.Editor.UI
{
	internal abstract class AbstractSettingsProvider : SettingsProvider
	{
		protected virtual string Title => Constants.SETTINGS_TITLE;
		protected abstract IEnumerable<string> GetSettingNames();

		protected AbstractSettingsProvider(string path, SettingsScope scope, string[] keywords = null)
			: base(path, scope, keywords)
		{
			base.guiHandler = ContentGUI;
			base.titleBarGuiHandler = TitleGUI;
			base.footerBarGuiHandler = FooterGUI;
			base.hasSearchInterestHandler = HasSearchContext;
		}

		protected virtual void TitleGUI()
		{
			
		}

		protected abstract void ContentGUI(string ctx);

		protected virtual void FooterGUI()
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				FlexibleSpace();
				Label("AssetLock v" + Constants.VERSION, EditorStyles.miniLabel);
			}
		}

		protected virtual bool HasSearchContext(string ctx)
		{
			if (string.IsNullOrEmpty(ctx))
			{
				return false;
			}

			if (Title.Contains(ctx))
			{
				return true;
			}

			if (GetSettingNames().Any(name => name.Contains(ctx)))
			{
				return true;
			}

			return false;
		}
	}

	internal class ProjectSettingsProvider : AbstractSettingsProvider
	{
		readonly GUIContent m_configLabel = new("Configuration");

		readonly GUIContent m_trackedExtensionsLabel = new(
			"Tracked File Extensions",
			"Only files with these extensions will be considered for tracking"
		);

		readonly GUIContent m_quickCheckLabel = new("Quick Check Size", "The size of the quick check buffer");
		readonly GUIContent m_gitRemoteUrlLabel = new("Git Remote URL", "Remote URL for git operations");
		readonly GUIContent m_gitLfsServerLabel = new("Git LFS Server", "Server for git-lfs operations");
		readonly GUIContent m_gitLfsServerLocksLabel = new("Git LFS Server Locks", "Server endpoint for git-lfs lock api operations");

		IEnumerable<GUIContent> GUIContents()
		{
			yield return m_configLabel;
			yield return m_trackedExtensionsLabel;
			yield return m_quickCheckLabel;
			yield return m_gitRemoteUrlLabel;
			yield return m_gitLfsServerLabel;
			yield return m_gitLfsServerLocksLabel;
		}

		protected override IEnumerable<string> GetSettingNames()
		{
			foreach (var content in GUIContents())
			{
				yield return content.text;
			}
		}

		public ProjectSettingsProvider()
			: base(Constants.PROJECT_SETTINGS_PROVIDER_PATH, SettingsScope.Project) { }

		protected override void ContentGUI(string ctx)
		{
			BeginSearchableGroup(m_configLabel, ctx);
			SearchableNumericField(m_quickCheckLabel, QuickCheckSize, ctx);
			SearchableStringField(m_gitRemoteUrlLabel, GitRemoteUrl, ctx);
			SearchableStringField(m_gitLfsServerLabel, GitLfsServerUrl, ctx);
			SearchableStringField(m_gitLfsServerLocksLabel, GitLfsServerLocksApiUrl, ctx);
			Space();
			SearchableStringList(m_trackedExtensionsLabel, TrackedFileEndings, ctx);
			EndSearchableGroup(m_configLabel, ctx);
		}
	}

	internal class UserSettingsProvider : AbstractSettingsProvider
	{
		readonly GUIContent m_configLabel = new("Configuration");
		readonly GUIContent m_masterEnableLabel = new("Master Enable", "Enable/disable AssetLock");
		readonly GUIContent m_autoLockLabel = new("Auto Lock", "Enable/disable auto-locking of assets");
		readonly GUIContent m_refreshRateLabel = new("Refresh Rate", "Rate in seconds to refresh lock status");
		readonly GUIContent m_gitExeLabel = new("Git Executable", "Path to git executable");
		readonly GUIContent m_gitDownloadLabel = new("Download Git", Constants.GIT_DOWNLOAD_URL);
		readonly GUIContent m_gitLfsExeLabel = new("Git LFS Executable", "Path to git-lfs executable");
		readonly GUIContent m_gitLfsDownloadLabel = new("Download Git LFS", Constants.GIT_LFS_DOWNLOAD_URL);
		readonly GUIContent m_gitAuthTokenLabel = new("Git Remote Auth Token", "Remote auth token for git operations");

		readonly GUIContent m_debugLabel = new("Debug");
		readonly GUIContent m_debugModeLabel = new("Debug Mode", "Enable/disable debug mode");

		readonly GUIContent m_forceSyncLabel = new(
			"Force Synchronous Process Handling",
			"Enable/disable asynchronous process handling"
		);
		readonly GUIContent m_useHttpLabel = new("Use HTTP", "Enable/disable HTTP for git operations");

		readonly GUIContent m_verboseLoggingLabel = new("Verbose Logging", "Enable/disable verbose logging");
		readonly GUIContent m_enableProfilingLabel = new("Enable Profiling", "Enable/disable various debug profiling");
		readonly GUIContent m_profilingMinTimeLabel = new("Profiling Min Time", "Minimum time in ms to log profile");
		readonly GUIContent m_rebootLabel = new("Reboot", "Reboot AssetLock");
		readonly GUIContent m_printLocksLabel = new("Print Locks", "Print all locks");
		readonly GUIContent m_clearLocksLabel = new("Clear Locks", "Clear all locks");
		readonly GUIContent m_parseAllAssetsLabel = new("Parse All Assets", "Parse all assets for lockable files");

		readonly GUIContent m_loggingLabel = new("Logging");
		readonly GUIContent m_infoLoggingLabel = new("Info Logging", "Enable/disable info logging");
		readonly GUIContent m_warningLoggingLabel = new("Warning Logging", "Enable/disable warning logging");
		readonly GUIContent m_errorLoggingLabel = new("Error Logging", "Enable/disable error logging");

		IEnumerable<GUIContent> GetLabels()
		{
			yield return m_masterEnableLabel;
			yield return m_autoLockLabel;
			yield return m_refreshRateLabel;
			yield return m_gitExeLabel;
			yield return m_gitDownloadLabel;
			yield return m_gitLfsExeLabel;
			yield return m_gitLfsDownloadLabel;
			yield return m_gitAuthTokenLabel;
			yield return m_debugLabel;
			yield return m_debugModeLabel;
			yield return m_useHttpLabel;
			yield return m_forceSyncLabel;
			yield return m_verboseLoggingLabel;
			yield return m_infoLoggingLabel;
			yield return m_warningLoggingLabel;
			yield return m_errorLoggingLabel;
			yield return m_enableProfilingLabel;
			yield return m_rebootLabel;
			yield return m_printLocksLabel;
			yield return m_clearLocksLabel;
		}

		protected override IEnumerable<string> GetSettingNames()
		{
			foreach (var content in GetLabels())
			{
				yield return content.text;
			}
		}

		public UserSettingsProvider()
			: base(Constants.USER_SETTINGS_PROVIDER_PATH, SettingsScope.User) { }

		protected override void ContentGUI(string ctx)
		{
			BeginSearchableGroup(m_configLabel, ctx);
			SearchableToggle(m_masterEnableLabel, MasterEnable, ctx);
			SearchableToggle(m_autoLockLabel, AutoLock, ctx);
			SearchableNumericField(m_refreshRateLabel, AssetLockSettings.RefreshRate, ctx);

			if (UseHttp)
			{
				SearchableStringField(m_gitAuthTokenLabel, GitRemoteAuthToken, ctx);
			}
			
			Space();
			SearchableFilePicker(
				m_gitExeLabel,
				GitPath,
				ctx,
				Constants.GIT_EXE_DIRECTORY_PATH,
				Constants.DEFAULT_GIT_EXE,
				m_gitDownloadLabel,
				Constants.GIT_DOWNLOAD_URL
			);
			SearchableFilePicker(
				m_gitLfsExeLabel,
				GitLfsPath,
				ctx,
				Constants.GIT_EXE_DIRECTORY_PATH,
				Constants.DEFAULT_GIT_LFS_EXE,
				m_gitLfsDownloadLabel,
				Constants.GIT_LFS_DOWNLOAD_URL
			);
			EndSearchableGroup(m_configLabel, ctx);

			Space();

			BeginSearchableGroup(m_loggingLabel, ctx);
			SearchableToggle(m_infoLoggingLabel, InfoLogging, ctx);
			SearchableToggle(m_warningLoggingLabel, WarningLogging, ctx);
			SearchableToggle(m_errorLoggingLabel, ErrorLogging, ctx);
			EndSearchableGroup(m_loggingLabel, ctx);

			Space();

			BeginSearchableGroup(m_debugLabel, ctx);
			SearchableToggle(m_debugModeLabel, DebugMode, ctx);

			if (DebugMode)
			{
				SearchableToggle(m_useHttpLabel, UseHttp, ctx);
				SearchableToggle(m_forceSyncLabel, ForceSynchronousProcessHandling, ctx);
				SearchableToggle(m_enableProfilingLabel, EnableProfiling, ctx);
				SearchableNumericField(m_profilingMinTimeLabel, ProfilingMinTimeMs, ctx);
				SearchableToggle(m_verboseLoggingLabel, VerboseLogging, ctx);

				using (new EditorGUILayout.HorizontalScope())
				{
					Indent();
					Label("Debug Actions:");
					SearchableButton(m_rebootLabel, ctx, () => AssetLockManager.Reboot());
					SearchableButton(m_printLocksLabel, ctx, () => AssetLockManager.Instance.PrintLockRepo());
					SearchableButton(m_clearLocksLabel, ctx, () => AssetLockManager.Instance.ResetLockRepo());
					SearchableButton(m_parseAllAssetsLabel, ctx,  () => _ = AssetLockManager.Instance.ParseAllAsync());
				}
			}

			EndSearchableGroup(m_debugLabel, ctx);
		}
	}
}