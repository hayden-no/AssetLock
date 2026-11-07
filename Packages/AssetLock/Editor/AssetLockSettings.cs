using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AssetLock.Editor.UI;
using UnityEditor;
using UnityEditor.SettingsManagement;
using static AssetLock.Editor.AssetLockUtility;

namespace AssetLock.Editor
{
	/// <summary>
	/// Handles the settings for the AssetLock package.
	/// </summary>
	internal static class AssetLockSettings
	{
		internal static bool TestMode =>
			System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-assetLockTestMode") >= 0;

		// SettingsManager dispatches by repository type. Reimplement the interface
		// so test settings remain in memory instead of touching EditorPrefs.
		private sealed class MemorySettingsRepository : UserSettingsRepository, ISettingsRepository
		{
			private readonly Dictionary<(System.Type, string), object> m_values = new();
			void ISettingsRepository.Save() { }
			void ISettingsRepository.Set<T>(string key, T value) => m_values[(typeof(T), key)] = value;

			T ISettingsRepository.Get<T>(string key, T fallback) =>
				m_values.TryGetValue((typeof(T), key), out var value) ? (T)value : fallback;

			bool ISettingsRepository.ContainsKey<T>(string key) => m_values.ContainsKey((typeof(T), key));
			void ISettingsRepository.Remove<T>(string key) => m_values.Remove((typeof(T), key));
		}

		private static Settings CreateSettings() =>
			TestMode
				? new Settings(new ISettingsRepository[] { new MemorySettingsRepository() })
				: new Settings(Constants.PACKAGE_NAME);

		private static Settings s_projectInstance;

		public static Settings ProjectSettings => s_projectInstance ??= CreateSettings();

		private static Settings s_userInstance;

		public static Settings UserSettings => s_userInstance ??= CreateSettings();

		[SettingsProvider]
		private static SettingsProvider ProjectSettingsProvider() => new ProjectSettingsProvider();

		[SettingsProvider]
		private static SettingsProvider UserSettingsProvider() => new UI.UserSettingsProvider();

		internal class ALProjectSetting<T> : UserSetting<T>
		{
			public ALProjectSetting(string key, T defaultValue) : base(
				ProjectSettings,
				key,
				defaultValue,
				TestMode ? SettingsScope.User : SettingsScope.Project
			) { }
		}

		internal class ALUserSetting<T> : UserSetting<T>
		{
			public ALUserSetting(string key, T defaultValue) : base(
				UserSettings,
				key,
				defaultValue,
				SettingsScope.User
			) { }
		}

		internal static string ProjectUserKey(string key, string projectPath = null)
		{
			var path = System.IO.Path
				.GetFullPath(projectPath ?? System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath))
				.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
			if (System.IO.Path.DirectorySeparatorChar == '\\') path = path.ToUpperInvariant();
			using var hash = SHA256.Create();

			return Constants.PACKAGE_NAME +
				".Project." +
				System.BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "") +
				"." +
				key;
		}

		internal class ALProjectUserSetting<T> : ALUserSetting<T>
		{
			public ALProjectUserSetting(string key, T defaultValue) : base(ProjectUserKey(key), defaultValue) { }
		}

	#region User Settings

		internal static ALUserSetting<bool> IsFirstTimeExperience = new(nameof(IsFirstTimeExperience), true);

		internal static ALUserSetting<bool> MasterEnable = new(nameof(MasterEnable), true);

		internal static ALUserSetting<bool> AutoLock = new(nameof(AutoLock), true);

		internal static ALUserSetting<bool> UseLegacyBrowser = new(nameof(UseLegacyBrowser), false);
		internal static ALUserSetting<bool> WarnOnly = new(nameof(WarnOnly), false);

		internal static ALUserSetting<bool> SuppressAutoLockFailureDialogs = new(
			nameof(SuppressAutoLockFailureDialogs),
			false
		);

		internal static ALProjectUserSetting<bool> EnablePushUnlockPrompts = new(
			nameof(EnablePushUnlockPrompts),
			false
		);

		internal static ALUserSetting<bool> SuppressPushUnlockPrompts = new(nameof(SuppressPushUnlockPrompts), false);

		internal static ALUserSetting<bool> ParseFilesOnStartup = new(nameof(ParseFilesOnStartup), true);

		internal static ALUserSetting<bool> UseBlockingCallsInProcessor = new(
			nameof(UseBlockingCallsInProcessor),
			false
		);

		internal static ALUserSetting<double> RefreshRate = new(nameof(RefreshRate), 10f);

		internal static ALUserSetting<bool> DebugMode = new(nameof(DebugMode), false);

		internal static ALUserSetting<bool> ForceSynchronousProcessHandling = new(
			nameof(ForceSynchronousProcessHandling),
			false
		);

		internal static ALUserSetting<bool> UseHttp = new(nameof(UseHttp), true);

		internal static ALUserSetting<bool> LogHttp = new(nameof(LogHttp), false);

		internal static ALUserSetting<bool> EnableProfiling = new(nameof(EnableProfiling), false);

		internal static ALUserSetting<double> ProfilingMinTimeMs = new(nameof(ProfilingMinTimeMs), 500);

		internal static ALUserSetting<bool> VerboseLogging = new(nameof(VerboseLogging), false);

		internal static ALUserSetting<bool> InfoLogging = new(nameof(InfoLogging), true);

		internal static ALUserSetting<bool> WarningLogging = new(nameof(WarningLogging), true);

		internal static ALUserSetting<bool> ErrorLogging = new(nameof(ErrorLogging), true);

		internal static ALUserSetting<bool> AddAssetReferenceToLogs = new(nameof(AddAssetReferenceToLogs), true);

		internal static ALUserSetting<string> GitPath = new(
			nameof(GitPath),
			TryGetDefaultGitPath(out var path) ? path : Constants.DEFAULT_GIT_EXE
		);

		internal static ALUserSetting<string> GitLfsPath = new(
			nameof(GitLfsPath),
			TryGetDefaultGitLfsPath(out var path) ? path : Constants.DEFAULT_GIT_LFS_EXE
		);

		internal static ALProjectUserSetting<bool> UseGitCredentialHelper = new(nameof(UseGitCredentialHelper), true);

		internal static ALProjectUserSetting<CredentialsKind> CredentialsType = new(
			nameof(CredentialsType),
			CredentialsKind.UserPass
		);

		internal static ALUserSetting<string> GitWorkingDirectory = new(nameof(GitWorkingDirectory), string.Empty);

		internal static ALProjectUserSetting<string> GitHubRemoteAuthToken = new(
			nameof(GitHubRemoteAuthToken),
			string.Empty
		);

		internal static ALProjectUserSetting<string> GitLabRemoteUsername = new(
			nameof(GitLabRemoteUsername),
			string.Empty
		);

		internal static ALProjectUserSetting<string> GitLabRemoteAuthToken = new(
			nameof(GitLabRemoteAuthToken),
			string.Empty
		);

		internal static ALProjectUserSetting<string> CredentialsUsername = new(
			nameof(CredentialsUsername),
			string.Empty
		);

		internal static ALProjectUserSetting<string> CredentialsPassword = new(
			nameof(CredentialsPassword),
			string.Empty
		);

	#endregion

	#region Project Settings

		internal static ALProjectSetting<List<string>> TrackedFileEndings = new(
			nameof(TrackedFileEndings),
			Constants.DEFAULT_TRACKED_EXTENSIONS.ToList()
		);

		internal static ALProjectSetting<int> QuickCheckSize = new(
			nameof(QuickCheckSize),
			Constants.DEFAULT_QUICK_CHECK_SIZE
		);

		internal static ALProjectSetting<bool> TrackNonBinaryFiles = new(nameof(TrackNonBinaryFiles), false);

		internal static ALProjectSetting<string> GitRemoteUrl = new(nameof(GitRemoteUrl), string.Empty);

		internal static ALProjectSetting<List<string>> PushUnlockBranches = new(
			nameof(PushUnlockBranches),
			new List<string> { "main", "master" }
		);

		internal static ALProjectSetting<string> GitLfsServerUrl = new(nameof(GitLfsServerUrl), string.Empty);

		internal static ALProjectSetting<string> GitLfsServerLocksApiUrl = new(
			nameof(GitLfsServerLocksApiUrl),
			string.Empty
		);

	#endregion
	}
}