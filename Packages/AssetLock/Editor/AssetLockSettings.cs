using System.Collections.Generic;
using System.Linq;
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
			var provider = new UserSettingsProvider(
				Constants.PROJECT_SETTINGS_PROVIDER_PATH,
				ProjectSettings,
				new[] { typeof(AssetLockSettings).Assembly },
				SettingsScope.Project
			);

			return provider;
		}

		[SettingsProvider]
		static SettingsProvider UserSettingsProvider()
		{
			var provider = new UserSettingsProvider(
				Constants.USER_SETTINGS_PROVIDER_PATH,
				UserSettings,
				new[] { typeof(AssetLockSettings).Assembly }
			);

			return provider;
		}

		[UserSetting("Configuration", "Master Enable", "Enable/disable AssetLock")]
		internal static UserSetting<bool> MasterEnable = new(UserSettings, "MasterEnable", true, SettingsScope.User);

		[UserSetting("Configuration", "Auto Lock", "Enable/disable auto-locking")]
		internal static UserSetting<bool> AutoLock = new(UserSettings, "AutoLock", true, SettingsScope.User);

		[UserSetting("Configuration", "Refresh Rate", "Rate in seconds to refresh lock status")]
		internal static UserSetting<double> RefreshRate = new(UserSettings, "RefreshRate", 1f, SettingsScope.User);

		[UserSetting("Debug", "Debug Mode", "Enable/disable debug mode")]
		internal static UserSetting<bool> DebugMode = new UserSetting<bool>(
			UserSettings,
			"DebugMode",
			false,
			SettingsScope.User
		);

		[UserSetting("Debug", "Force Synchronous Process Handling", "Enable/disable synchronous process handling")]
		internal static UserSetting<bool> ForceSynchronousProcessHandling = new UserSetting<bool>(
			UserSettings,
			"ForceSynchronousProcessHandling",
			false,
			SettingsScope.User
		);

		[UserSetting("Debug", "Enable Profiling", "Enable/disable various debug profiling")]
		internal static UserSetting<bool> EnableProfiling = new UserSetting<bool>(
			UserSettings,
			"EnableProfiling",
			false,
			SettingsScope.User
		);

		[UserSetting]
		internal static UserSetting<List<string>> TrackedFileEndings = new UserSetting<List<string>>(
			ProjectSettings,
			"TrackedFileEndings",
			Constants.DEFAULT_TRACKED_EXTENSIONS.ToList()
		);

		[UserSetting("Configuration", "Quick Check Size", "Size in bytes to check for binary files")]
		internal static UserSetting<int> QuickCheckSize = new UserSetting<int>(
			ProjectSettings,
			"QuickCheckSize",
			Constants.DEFAULT_QUICK_CHECK_SIZE
		);

		[UserSetting("Logging", "Log Verbose", "Enable/disable verbose logging (DEBUG)")]
		internal static UserSetting<bool> VerboseLogging = new UserSetting<bool>(
			UserSettings,
			"VerboseLogging",
			true,
			SettingsScope.User
		);

		[UserSetting("Logging", "Log Info", "Enable/disable info logging")]
		internal static UserSetting<bool> InfoLogging = new UserSetting<bool>(
			UserSettings,
			"InfoLogging",
			true,
			SettingsScope.User
		);

		[UserSetting("Logging", "Log Warnings", "Enable/disable warning logging")]
		internal static UserSetting<bool> WarningLogging = new UserSetting<bool>(
			UserSettings,
			"WarningLogging",
			true,
			SettingsScope.User
		);

		[UserSetting("Logging", "Log Errors", "Enable/disable error logging")]
		internal static UserSetting<bool> ErrorLogging = new UserSetting<bool>(
			UserSettings,
			"ErrorLogging",
			true,
			SettingsScope.User
		);

		[UserSetting]
		internal static UserSetting<string> GitPath = new UserSetting<string>(
			UserSettings,
			"GitPath",
			TryGetDefaultGitPath(out var path) ? path : Constants.DEFAULT_GIT_EXE,
			SettingsScope.User
		);

		[UserSetting]
		internal static UserSetting<string> GitLfsPath = new UserSetting<string>(
			UserSettings,
			"GitLfsPath",
			TryGetDefaultGitLfsPath(out var path) ? path : Constants.DEFAULT_GIT_LFS_EXE,
			SettingsScope.User
		);

		static class GUI
		{
			[UserSettingBlock("Configuration")]
			static void FileExtensionsGUI(string searchContext)
			{
				GUIContent content = new GUIContent(
					"Tracked File Extensions",
					"Only files with these extensions will be considered for tracking"
				);

				if (!GUIUtil.MatchSearchGroups(searchContext, content.text))
				{
					return;
				}

				EditorGUI.BeginChangeCheck();

				GUILayout.Label(content, EditorStyles.boldLabel);

				using (new SettingsGUILayout.IndentedGroup())
				{
					for (var index = 0; index < TrackedFileEndings.value.Count; index++)
					{
						var ending = TrackedFileEndings.value[index];

						using (new EditorGUILayout.HorizontalScope())
						{
							ending = EditorGUILayout.TextField(ending, GUILayout.ExpandWidth(true));

							if (GUILayout.Button("X", GUILayout.Width(20)))
							{
								TrackedFileEndings.value.Remove(ending);
							}
						}
					}

					using (new EditorGUILayout.HorizontalScope())
					{
						if (GUILayout.Button("Add", GUILayout.Width(50)))
						{
							TrackedFileEndings.value.Add("");
						}
					}
				}

				if (EditorGUI.EndChangeCheck())
				{
					TrackedFileEndings.ApplyModifiedProperties();
				}
			}

			[UserSettingBlock("Git")]
			static void GitPathsGUI(string searchContext)
			{
				GUIContent git = new GUIContent("Git Path", "Path to the git executable");
				GUIContent lfs = new GUIContent("Git LFS Path", "Path to the git-lfs executable");
				GUIContent label = new GUIContent("Paths", "Paths to the git and git-lfs executables");

				bool showGit = GUIUtil.MatchSearchGroups(searchContext, git.text);
				bool showLfs = GUIUtil.MatchSearchGroups(searchContext, lfs.text);
				bool showLabel = GUIUtil.MatchSearchGroups(searchContext, label.text) || showGit || showLfs;

				if (!showLabel)
				{
					return;
				}

				EditorGUI.BeginChangeCheck();
				GUILayout.Label(label, EditorStyles.boldLabel);

				using (new SettingsGUILayout.IndentedGroup())
				{
					if (showGit)
					{
						using (new EditorGUILayout.HorizontalScope())
						{
							GUILayout.Label(git);
							GitPath.value = GUILayout.TextField(GitPath.value, GUILayout.ExpandWidth(true));

							if (GUILayout.Button("Browse"))
							{
								GitPath.value = EditorUtility.OpenFilePanel(
									"Select git executable",
									"C\\Program Files",
									"exe"
								);
							}

							if (GUILayout.Button("Auto-Find"))
							{
								if (TryGetDefaultGitPath(out var path))
								{
									GitPath.value = path;
								}
							}

							if (GUILayout.Button("Download"))
							{
								Application.OpenURL("https://git-scm.com/downloads");
							}
						}
					}

					if (showLfs)
					{
						using (new EditorGUILayout.HorizontalScope())
						{
							GUILayout.Label(lfs);
							GitLfsPath.value = GUILayout.TextField(GitLfsPath.value, GUILayout.ExpandWidth(true));

							if (GUILayout.Button("Browse"))
							{
								GitLfsPath.value = EditorUtility.OpenFilePanel(
									"Select git-lfs executable",
									"C\\Program Files",
									"exe"
								);
							}

							if (GUILayout.Button("Auto-Find"))
							{
								if (TryGetDefaultGitLfsPath(out var path))
								{
									GitLfsPath.value = path;
								}
							}

							if (GUILayout.Button("Download"))
							{
								Application.OpenURL("https://git-lfs.github.com/");
							}
						}
					}
				}

				if (EditorGUI.EndChangeCheck())
				{
					GitPath.ApplyModifiedProperties();
					GitLfsPath.ApplyModifiedProperties();
				}
			}

			[UserSettingBlock("Debug")]
			static void RebootButtonGUI(string searchContext)
			{
				using (new GUILayout.HorizontalScope())
				{
					GUILayout.Label("Reboot the Asset Lock system:");

					if (GUIUtil.SearchableButton(new("Reboot"), searchContext))
					{
						AssetLockManager.Reboot();
					}
				}

				using (new GUILayout.HorizontalScope())
				{
					GUILayout.Label("Print the contents of the lock cache:");

					if (GUIUtil.SearchableButton(new("Print Cache"), searchContext))
					{
						AssetLockManager.Instance.PrintLockRepo();
					}
				}
				
				using (new GUILayout.HorizontalScope())
				{
					GUILayout.Label("Reset the contents of the lock cache:");

					if (GUIUtil.SearchableButton(new("Reset Cache"), searchContext))
					{
						AssetLockManager.Instance.ResetLockRepo();
					}
				}
			}
		}
	}
}