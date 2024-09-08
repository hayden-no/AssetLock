using System.Collections.Generic;
using System.Linq;
using AssetLock.Editor.UI;
using UnityEditor;
using UnityEditor.SettingsManagement;
using static AssetLock.Editor.AssetLockUtility;

namespace AssetLock.Editor
{
	internal class AssetLockSettings
	{
		private static Settings s_projectInstance;

		public static Settings ProjectSettings => s_projectInstance ??= new Settings(Constants.PACKAGE_NAME);

		private static Settings s_userInstance;

		public static Settings UserSettings => s_userInstance ??= new Settings(Constants.PACKAGE_NAME);

		[SettingsProvider]
		static SettingsProvider ProjectSettingsProvider()
		{
			return new ProjectSettingsProvider();
		}

		[SettingsProvider]
		static SettingsProvider UserSettingsProvider()
		{
			return new UI.UserSettingsProvider();
		}

		internal class ALProjectSetting<T> : UserSetting<T>
		{
			public ALProjectSetting(string key, T defaultValue)
				: base(ProjectSettings, key, defaultValue) { }
		}

		internal class ALUserSetting<T> : UserSetting<T>
		{
			public ALUserSetting(string key, T defaultValue)
				: base(UserSettings, key, defaultValue, SettingsScope.User) { }
		}

	#region User Settings

		internal static ALUserSetting<bool> MasterEnable = new(nameof(MasterEnable), true);

		internal static ALUserSetting<bool> AutoLock = new(nameof(AutoLock), true);

		internal static ALUserSetting<double> RefreshRate = new(nameof(RefreshRate), 10f);

		internal static ALUserSetting<bool> DebugMode = new(nameof(DebugMode), false);

		internal static ALUserSetting<bool> ForceSynchronousProcessHandling = new(
			nameof(ForceSynchronousProcessHandling),
			false
		);
		
		internal static ALUserSetting<bool> UseHttp = new(nameof(UseHttp), true);

		internal static ALUserSetting<bool> EnableProfiling = new(nameof(EnableProfiling), false);

		internal static ALUserSetting<double> ProfilingMinTimeMs = new(nameof(ProfilingMinTimeMs), 500);

		internal static ALUserSetting<bool> VerboseLogging = new(nameof(VerboseLogging), true);

		internal static ALUserSetting<bool> InfoLogging = new(nameof(InfoLogging), true);

		internal static ALUserSetting<bool> WarningLogging = new(nameof(WarningLogging), true);

		internal static ALUserSetting<bool> ErrorLogging = new(nameof(ErrorLogging), true);

		internal static ALUserSetting<string> GitPath = new(
			nameof(GitPath),
			TryGetDefaultGitPath(out var path) ? path : Constants.DEFAULT_GIT_EXE
		);

		internal static ALUserSetting<string> GitLfsPath = new(
			nameof(GitLfsPath),
			TryGetDefaultGitLfsPath(out var path) ? path : Constants.DEFAULT_GIT_LFS_EXE
		);
		
		internal static ALUserSetting<string> GitWorkingDirectory = new(nameof(GitWorkingDirectory), string.Empty);
		
		internal static ALUserSetting<string> GitRemoteAuthToken = new(nameof(GitRemoteAuthToken), string.Empty);

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
		
		internal static ALProjectSetting<string> GitRemoteUrl = new(nameof(GitRemoteUrl), string.Empty);
		internal static ALProjectSetting<string> GitLfsServerUrl = new(nameof(GitLfsServerUrl), string.Empty);
		internal static ALProjectSetting<string> GitLfsServerLocksApiUrl = new(nameof(GitLfsServerLocksApiUrl), string.Empty);

	#endregion
	}
}