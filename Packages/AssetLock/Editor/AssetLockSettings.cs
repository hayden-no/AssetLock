using System.Collections.Generic;
using System.Linq;
using AssetLock.Editor.UI;
using UnityEditor;
using UnityEditor.SettingsManagement;
using UnityEngine;
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

		internal static ALUserSetting<bool> MasterEnable = new("MasterEnable", true);

		internal static ALUserSetting<bool> AutoLock = new("AutoLock", true);

		internal static ALUserSetting<double> RefreshRate = new("RefreshRate", 10f);

		internal static ALUserSetting<bool> DebugMode = new("DebugMode", false);

		internal static ALUserSetting<bool> ForceSynchronousProcessHandling = new(
			"ForceSynchronousProcessHandling",
			false
		);

		internal static ALUserSetting<bool> EnableProfiling = new("EnableProfiling", false);
		
		internal static ALUserSetting<double> ProfilingMinTimeMs = new("ProfilingMinTime", 500);

		internal static ALProjectSetting<List<string>> TrackedFileEndings = new(
			"TrackedFileEndings",
			Constants.DEFAULT_TRACKED_EXTENSIONS.ToList()
		);

		internal static ALProjectSetting<int> QuickCheckSize = new(
			"QuickCheckSize",
			Constants.DEFAULT_QUICK_CHECK_SIZE
		);

		internal static ALUserSetting<bool> VerboseLogging = new("VerboseLogging", true);

		internal static ALUserSetting<bool> InfoLogging = new("InfoLogging", true);

		internal static ALUserSetting<bool> WarningLogging = new("WarningLogging", true);

		internal static ALUserSetting<bool> ErrorLogging = new("ErrorLogging", true);

		internal static ALUserSetting<string> GitPath = new(
			"GitPath",
			TryGetDefaultGitPath(out var path) ? path : Constants.DEFAULT_GIT_EXE
		);

		internal static ALUserSetting<string> GitLfsPath = new(
			"GitLfsPath",
			TryGetDefaultGitLfsPath(out var path) ? path : Constants.DEFAULT_GIT_LFS_EXE
		);
	}
}