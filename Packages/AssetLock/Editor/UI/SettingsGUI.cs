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
	/// <summary>
	/// Abstract class for settings providers.
	/// </summary>
	internal abstract class AbstractSettingsProvider : SettingsProvider
	{
		protected virtual string Title => Constants.SETTINGS_TITLE;
		protected abstract IEnumerable<string> GetSettingNames();

		protected AbstractSettingsProvider(string path, SettingsScope scope, string[] keywords = null)
			: base(path, scope, keywords) { }

		public override void OnTitleBarGUI()
		{
			// do nothing
		}

		public override void OnGUI(string searchContext)
		{
			ContentGUI(searchContext);
		}

		public override bool HasSearchInterest(string searchContext)
		{
			if (string.IsNullOrEmpty(searchContext))
			{
				return false;
			}

			if (Title.Contains(searchContext))
			{
				return true;
			}

			return GetSettingNames().Any(name => name.Contains(searchContext));
		}

		protected abstract void ContentGUI(string ctx);
	}

	/// <summary>
	/// Project settings provider.
	/// </summary>
	internal class ProjectSettingsProvider : AbstractSettingsProvider
	{
		readonly GUIContent m_configLabel = new("Configuration");

		readonly GUIContent m_trackedExtensionsLabel = new(
			"Tracked File Extensions",
			"Only files with these extensions will be considered for tracking"
		);
		
		readonly GUIContent m_trackNonBinaryFilesLabel = new(
			"Track Non-Binary Files",
			"Enable/disable tracking of non-binary files (only enable if you know what you're doing)"
		);

		readonly GUIContent m_quickCheckLabel = new("Quick Check Size", "The size of the quick check buffer");
		readonly GUIContent m_gitRemoteUrlLabel = new("Git Remote URL", "Remote URL for git operations");
		readonly GUIContent m_gitLfsServerLabel = new("Git LFS Server", "Server for git-lfs operations");
		readonly GUIContent m_pushUnlockBranchesLabel = new("Push Unlock Prompt Branches", "Destination branch names that may trigger an unlock prompt.");

		readonly GUIContent m_gitLfsServerLocksLabel = new(
			"Git LFS Server Locks",
			"Server endpoint for git-lfs lock api operations"
		);
		
		readonly GUIContent m_testConnectionLabel = new("Test Connection", "Test connection to the git server");

		IEnumerable<GUIContent> GUIContents()
		{
			yield return m_configLabel;
			yield return m_trackedExtensionsLabel;
			yield return m_quickCheckLabel;
			yield return m_gitRemoteUrlLabel;
			yield return m_gitLfsServerLabel;
			yield return m_pushUnlockBranchesLabel;
			yield return m_gitLfsServerLocksLabel;
			yield return m_testConnectionLabel;
		}

		protected override IEnumerable<string> GetSettingNames()
		{
			return GUIContents().SelectMany(content => content.text.Split(' '));
		}

		public ProjectSettingsProvider()
			: base(Constants.PROJECT_SETTINGS_PROVIDER_PATH, SettingsScope.Project) { }

		protected override void ContentGUI(string ctx)
		{
			BeginSearchableGroup(m_configLabel, ctx);
			SearchableNumericField(m_quickCheckLabel, QuickCheckSize, ctx);
			SearchableToggle(m_trackNonBinaryFilesLabel, TrackNonBinaryFiles, ctx);
			EditorGUI.BeginChangeCheck();
			SearchableStringField(m_gitRemoteUrlLabel, GitRemoteUrl, ctx);
			if (EditorGUI.EndChangeCheck())
			{
				if (string.IsNullOrEmpty(GitLfsServerUrl))
				{
					GitLfsServerUrl.value = GetDefaultLfsEndpoint(GitRemoteUrl); 
					GitLfsServerUrl.ApplyModifiedProperties();
				}
				if (string.IsNullOrEmpty(GitLfsServerLocksApiUrl))
				{
					GitLfsServerLocksApiUrl.value = GitLfsServerUrl + Constants.GIT_LFS_SERVER_LOCKS_API_EXT;
					GitLfsServerLocksApiUrl.ApplyModifiedProperties();
				}
			}
			SearchableStringField(m_gitLfsServerLabel, GitLfsServerUrl, ctx);
			SearchableStringField(m_gitLfsServerLocksLabel, GitLfsServerLocksApiUrl, ctx);
			if (MatchSearchGroups(ctx, m_pushUnlockBranchesLabel.text))
			{
				LabelField(m_pushUnlockBranchesLabel);
				SearchableStringList(m_pushUnlockBranchesLabel, PushUnlockBranches, ctx);
			}
			if (Button(m_testConnectionLabel))
			{
				TestConnection();
			}
			Space();
			SearchableStringList(m_trackedExtensionsLabel, TrackedFileEndings, ctx);
			EndSearchableGroup(m_configLabel, ctx);
		}

		async void TestConnection()
		{
			(var successs, var err) = await AssetLockManager.Instance.TestConnection();
			
			if (successs)
			{
				EditorUtility.DisplayDialog("Connection Test", "Connection successful!", "OK");
			}
			else
			{
				EditorUtility.DisplayDialog("Connection Test", $"Connection failed: {err}", "OK");
			}
		}
	}

	/// <summary>
	/// User settings provider.
	/// </summary>
	internal class UserSettingsProvider : AbstractSettingsProvider
	{
		readonly GUIContent m_configLabel = new("Configuration");
		readonly GUIContent m_masterEnableLabel = new("Master Enable", "Enable/disable AssetLock");
		readonly GUIContent m_autoLockLabel = new("Auto Lock", "Enable/disable auto-locking of assets");
		readonly GUIContent m_legacyBrowserLabel = new("Use Legacy Lock Browser", "Use the original Lock Browser instead of the UI Toolkit browser.");
		readonly GUIContent m_warnOnlyLabel = new("Warn Instead of Blocking", "Allow asset operations and warn when lock ownership is missing or belongs to another user.");
		readonly GUIContent m_suppressAutoLockFailureDialogsLabel = new("Suppress Auto Lock Failure Dialogs", "Do not show a dialog when an automatic lock attempt fails in warning mode.");
		readonly GUIContent m_pushPromptLabel = new("Prompt to Unlock on Push", "Monitor pushes to configured branches while the editor is open. Install the hook below to enable detection.");
		readonly GUIContent m_suppressPushPromptLabel = new("Suppress Push Unlock Prompts", "Skip prompts and keep locks. Suppression never unlocks files.");
		private string m_pushHookMessage;
		private bool m_pushHookBusy;
		readonly GUIContent m_parseFilesOnStartupLabel = new("Parse Files On Startup", "Enable/disable parsing of files on startup");
		readonly GUIContent m_useblockingCallsInProcessorLabel = new(
			"Use Blocking Calls In Processor",
			"When enabled, certain editor locking operations will block the main thread"
		);
		readonly GUIContent m_refreshRateLabel = new("Refresh Rate", "Rate in seconds to refresh lock status");
		readonly GUIContent m_gitExeLabel = new("Git Executable", "Path to git executable");
		readonly GUIContent m_gitDownloadLabel = new("Download Git", Constants.GIT_DOWNLOAD_URL);
		readonly GUIContent m_gitLfsExeLabel = new("Git LFS Executable", "Path to git-lfs executable");
		readonly GUIContent m_gitLfsDownloadLabel = new("Download Git LFS", Constants.GIT_LFS_DOWNLOAD_URL);
		
		readonly GUIContent m_gitCredentialsHeader = new("Git Credentials");
		readonly GUIContent m_useGitHelperLabel = new("Use Git Credential Helper", "Use the helper configured for this project for HTTP lock requests.");
		private string m_identityMessage = "";
		private int m_savedIdentityIndex;
		private bool m_signingIn;
		readonly GUIContent m_gitCredentialsTypeLabel = new("Git Credentials Type", "Type of credentials to use for git operations");
		readonly GUIContent m_gitHubAuthTokenLabel = new("GitHub Remote Auth Token", "Remote auth token for github operations");
		readonly GUIContent m_gitLabUsernameLabel = new("GitLab Username", "Username for gitlab operations");
		readonly GUIContent m_gitLabAuthTokenLabel = new("GitLab Auth Token", "Auth token for gitlab operations");
		readonly GUIContent m_gitCustomUsernameLabel = new("Username", "Username for git operations");
		readonly GUIContent m_gitCustomPasswordLabel = new("Password", "Password for git operations");

		readonly GUIContent m_debugLabel = new("Debug");
		readonly GUIContent m_debugModeLabel = new("Debug Mode", "Enable/disable debug mode");

		readonly GUIContent m_forceSyncLabel = new(
			"Force Synchronous Process Handling",
			"Enable/disable asynchronous process handling"
		);

		readonly GUIContent m_useHttpLabel = new("Use HTTP", "Enable/disable HTTP for git operations");
		readonly GUIContent m_logHttpLabel = new("Log HTTP", "Enable/disable HTTP logging");

		readonly GUIContent m_verboseLoggingLabel = new("Verbose Logging", "Enable/disable verbose logging");
		readonly GUIContent m_enableProfilingLabel = new("Enable Profiling", "Enable/disable various debug profiling");
		readonly GUIContent m_profilingMinTimeLabel = new("Profiling Min Time", "Minimum time in ms to log profile");
		readonly GUIContent m_rebootLabel = new("Reboot", "Reboot AssetLock");
		readonly GUIContent m_printLocksLabel = new("Print Locks", "Print all locks");
		readonly GUIContent m_clearLocksLabel = new("Clear Locks", "Clear all locks");
		readonly GUIContent m_parseAllAssetsLabel = new("Parse All Assets", "Parse all assets for lockable files");
		readonly GUIContent m_gitLfsHooksFix = new("Invalid Git LFS Hooks Fix", "Attempt to fix the invalid git lfs hooks error by force updating git lfs");

		readonly GUIContent m_loggingLabel = new("Logging");
		readonly GUIContent m_infoLoggingLabel = new("Info Logging", "Enable/disable info logging");
		readonly GUIContent m_warningLoggingLabel = new("Warning Logging", "Enable/disable warning logging");
		readonly GUIContent m_errorLoggingLabel = new("Error Logging", "Enable/disable error logging");
		readonly GUIContent m_addAssetReferenceToLogsLabel = new("Add Asset Reference To Logs", "Enable/disable adding asset reference to logs (this involves loading the asset)");

		IEnumerable<GUIContent> GUIContents()
		{
			yield return m_masterEnableLabel;
			yield return m_autoLockLabel;
			yield return m_legacyBrowserLabel;
			yield return m_warnOnlyLabel;
			yield return m_suppressAutoLockFailureDialogsLabel;
			yield return m_pushPromptLabel;
			yield return m_suppressPushPromptLabel;
			yield return m_parseFilesOnStartupLabel;
			yield return m_useblockingCallsInProcessorLabel;
			yield return m_refreshRateLabel;
			yield return m_gitExeLabel;
			yield return m_gitDownloadLabel;
			yield return m_gitLfsExeLabel;
			yield return m_gitLfsDownloadLabel;
			yield return m_gitCredentialsHeader;
			yield return m_useGitHelperLabel;
			yield return new GUIContent("Identity Account Profile Username Sign In");
			yield return m_gitCredentialsTypeLabel;
			yield return m_gitHubAuthTokenLabel;
			yield return m_gitLabUsernameLabel;
			yield return m_gitLabAuthTokenLabel;
			yield return m_gitCustomUsernameLabel;
			yield return m_gitCustomPasswordLabel;
			yield return m_debugLabel;
			yield return m_debugModeLabel;
			yield return m_useHttpLabel;
			yield return m_logHttpLabel;
			yield return m_forceSyncLabel;
			yield return m_verboseLoggingLabel;
			yield return m_infoLoggingLabel;
			yield return m_warningLoggingLabel;
			yield return m_errorLoggingLabel;
			yield return m_addAssetReferenceToLogsLabel;
			yield return m_enableProfilingLabel;
			yield return m_rebootLabel;
			yield return m_printLocksLabel;
			yield return m_clearLocksLabel;
			yield return m_parseAllAssetsLabel;
			yield return m_gitLfsHooksFix;
		}

		protected override IEnumerable<string> GetSettingNames()
		{
			return GUIContents().SelectMany(content => content.text.Split(' '));
		}

		public UserSettingsProvider()
			: base(Constants.USER_SETTINGS_PROVIDER_PATH, SettingsScope.User) { }

		protected override void ContentGUI(string ctx)
		{
			BeginSearchableGroup(m_configLabel, ctx);
			SearchableToggle(m_masterEnableLabel, MasterEnable, ctx);
			SearchableToggle(m_autoLockLabel, AutoLock, ctx);
			EditorGUI.BeginChangeCheck();
			SearchableToggle(m_legacyBrowserLabel, UseLegacyBrowser, ctx);
			if (EditorGUI.EndChangeCheck()) LockBrowser.ApplyBrowserPreference();
			SearchableToggle(m_warnOnlyLabel, WarnOnly, ctx);
			SearchableToggle(m_suppressAutoLockFailureDialogsLabel, SuppressAutoLockFailureDialogs, ctx);
			SearchableToggle(m_pushPromptLabel, EnablePushUnlockPrompts, ctx);
			SearchableToggle(m_suppressPushPromptLabel, SuppressPushUnlockPrompts, ctx);
			if (MatchSearchGroups(ctx, "Push Unlock Prompt Hook"))
			{
				EditorGUILayout.HelpBox("Install the local Git hook to detect push attempts. Git cannot confirm success from this hook; locks are released only when you choose to unlock. Suppressing prompts keeps locks.", MessageType.Info);
				using (new EditorGUI.DisabledScope(m_pushHookBusy))
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Install Push Hook")) ChangePushHook(true);
					if (GUILayout.Button("Remove Push Hook")) ChangePushHook(false);
				}
				if (!string.IsNullOrEmpty(m_pushHookMessage)) EditorGUILayout.HelpBox(m_pushHookMessage, MessageType.Info);
			}
			SearchableToggle(m_parseFilesOnStartupLabel, ParseFilesOnStartup, ctx);
			SearchableToggle(m_useblockingCallsInProcessorLabel, UseBlockingCallsInProcessor, ctx);
			SearchableNumericField(m_refreshRateLabel, AssetLockSettings.RefreshRate, ctx);

			Space();
			
			BeginSearchableGroup(m_gitCredentialsHeader, ctx);
			if (MatchSearchGroups(ctx, m_gitCredentialsHeader.text) || MatchSearchGroups(ctx, "Identity Account Profile Username Sign In"))
			{
				SearchableToggle(m_useGitHelperLabel, UseGitCredentialHelper, "");
				if (UseGitCredentialHelper) DrawGitIdentities();
				else DrawLegacyCredentials();
			}
			EndSearchableGroup(m_gitCredentialsHeader, ctx);

			Space();
			
			SearchableFilePicker(
				m_gitExeLabel,
				GitPath,
				ctx,
				Constants.GIT_EXE_DIRECTORY_PATH,
				Constants.EXE_FILE_KIND,
				m_gitDownloadLabel,
				Constants.GIT_DOWNLOAD_URL
			);
			SearchableFilePicker(
				m_gitLfsExeLabel,
				GitLfsPath,
				ctx,
				Constants.GIT_EXE_DIRECTORY_PATH,
				Constants.EXE_FILE_KIND,
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
				SearchableToggle(m_logHttpLabel, LogHttp, ctx);
				SearchableToggle(m_forceSyncLabel, ForceSynchronousProcessHandling, ctx);
				SearchableToggle(m_enableProfilingLabel, EnableProfiling, ctx);
				SearchableNumericField(m_profilingMinTimeLabel, ProfilingMinTimeMs, ctx);
				SearchableToggle(m_verboseLoggingLabel, VerboseLogging, ctx);
				SearchableToggle(m_addAssetReferenceToLogsLabel, AddAssetReferenceToLogs, ctx);

				using (new EditorGUILayout.HorizontalScope())
				{
					Indent();
					Label("Debug Actions:");
					SearchableButton(m_rebootLabel, ctx, () => AssetLockManager.Reboot());
					SearchableButton(m_printLocksLabel, ctx, () => AssetLockManager.Instance.PrintLockRepo());
					SearchableButton(m_clearLocksLabel, ctx, () => AssetLockManager.Instance.ResetLockRepo());
					SearchableButton(m_parseAllAssetsLabel, ctx, () => AssetLockManager.Instance.ParseAll());
					SearchableButton(m_gitLfsHooksFix, ctx, () => AssetLockManager.Instance.InvalidGitHooksFix());
				}
			}

			EndSearchableGroup(m_debugLabel, ctx);
		}

		private async void ChangePushHook(bool install)
		{
			m_pushHookBusy = true;
			try
			{
				m_pushHookMessage = install ? await PushUnlockPrompt.InstallAsync() : await PushUnlockPrompt.RemoveAsync();
			}
			catch (System.Exception error) { m_pushHookMessage = error.Message; }
			finally { m_pushHookBusy = false; UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }
		}

		private void DrawGitIdentities()
		{
			EditorGUILayout.HelpBox("Account details are private to this project. Passwords and tokens are managed by Git's configured credential helper.", MessageType.Info);
			var identity = CredentialIdentities.Current;
			EditorGUI.BeginChangeCheck();
			identity.Name = EditorGUILayout.TextField("Profile Name", identity.Name ?? "");
			identity.Username = EditorGUILayout.TextField("Account Username", identity.Username ?? "");
			if (EditorGUI.EndChangeCheck())
			{
				if (string.IsNullOrEmpty(identity.Authority)) identity.Authority = CredentialIdentities.AuthorityOf(GitLfsServerUrl.value);
				CredentialIdentities.Current = identity;
			}
			EditorGUILayout.LabelField("Account Server", string.IsNullOrEmpty(identity.Authority) ? "Not set" : identity.Authority);
			if (GUILayout.Button("Use Current LFS Server"))
			{
				identity.Authority = CredentialIdentities.AuthorityOf(GitLfsServerUrl.value);
				CredentialIdentities.Current = identity;
			}
			using (new EditorGUI.DisabledScope(m_signingIn))
			{
				if (GUILayout.Button("Sign In and Test Connection")) SignIn();
				if (GUILayout.Button("Save Profile for Other Projects"))
				{
					try { CredentialIdentities.SaveProfile(identity); m_identityMessage = "Profile saved."; }
					catch (System.Exception error) { m_identityMessage = error.Message; }
				}
			}
			var saved = CredentialIdentities.Saved;
			if (saved.Count > 0)
			{
				m_savedIdentityIndex = Mathf.Clamp(m_savedIdentityIndex, 0, saved.Count - 1);
				m_savedIdentityIndex = EditorGUILayout.Popup("Saved Profiles", m_savedIdentityIndex, saved.Select(p => p.Name).ToArray());
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Import Profile")) { CredentialIdentities.Current = saved[m_savedIdentityIndex]; m_identityMessage = "Profile imported for this project."; }
					if (GUILayout.Button("Delete Saved Profile")) { CredentialIdentities.DeleteProfile(saved[m_savedIdentityIndex].Name); m_identityMessage = "Saved profile removed."; }
				}
			}
			if (!string.IsNullOrEmpty(m_identityMessage)) EditorGUILayout.HelpBox(m_identityMessage, MessageType.Info);
		}

		private async void SignIn()
		{
			m_signingIn = true;
			m_identityMessage = "Signing in...";
			try
			{
				var manager = AssetLockManager.Instance;
				if (manager == null || !manager.IsReady) throw new System.InvalidOperationException("AssetLock must be initialized before testing the connection.");
				var identity = CredentialIdentities.Current;
				var scope = GitCredentialClient.ScopeFor(GitLfsServerLocksApiUrl.value, identity);
				await GitCredentialClient.ConnectAsync(scope, identity, async () => (await manager.TestConnection()).success);
				m_identityMessage = "Connection successful. The Git credential helper can reuse this account.";
				manager.Refresh();
			}
			catch (System.Exception error) { m_identityMessage = error.Message; }
			finally { m_signingIn = false; UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }
		}

		private void DrawLegacyCredentials()
		{
			EditorGUILayout.HelpBox("These project-specific legacy credentials are stored in Unity user preferences, not a credential vault.", MessageType.Warning);
			SearchableEnumPopup(m_gitCredentialsTypeLabel, CredentialsType, "");
			switch (CredentialsType.value)
			{
				case CredentialsKind.GitHub:
					PasswordField(m_gitHubAuthTokenLabel, GitHubRemoteAuthToken);
					break;
				case CredentialsKind.GitLab:
					SearchableStringField(m_gitLabUsernameLabel, GitLabRemoteUsername, "");
					PasswordField(m_gitLabAuthTokenLabel, GitLabRemoteAuthToken);
					break;
				case CredentialsKind.UserPass:
					SearchableStringField(m_gitCustomUsernameLabel, CredentialsUsername, "");
					PasswordField(m_gitCustomPasswordLabel, CredentialsPassword);
					break;
			}
			if (GUILayout.Button("Import Previous Shared Credentials"))
			{
				var repository = UserSettings.GetRepository(SettingsScope.User);
				CredentialsType.SetValue(repository.Get(nameof(CredentialsType), CredentialsKind.UserPass));
				GitHubRemoteAuthToken.SetValue(repository.Get(nameof(GitHubRemoteAuthToken), ""));
				GitLabRemoteUsername.SetValue(repository.Get(nameof(GitLabRemoteUsername), ""));
				GitLabRemoteAuthToken.SetValue(repository.Get(nameof(GitLabRemoteAuthToken), ""));
				CredentialsUsername.SetValue(repository.Get(nameof(CredentialsUsername), ""));
				CredentialsPassword.SetValue(repository.Get(nameof(CredentialsPassword), ""));
			}
		}

		private static void PasswordField(GUIContent label, UnityEditor.SettingsManagement.UserSetting<string> setting)
		{
			EditorGUI.BeginChangeCheck();
			string value = EditorGUILayout.PasswordField(label, setting.value);
			if (EditorGUI.EndChangeCheck()) setting.SetValue(value);
		}
	}
}
