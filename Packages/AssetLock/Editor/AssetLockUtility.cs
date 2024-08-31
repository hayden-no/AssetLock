using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SettingsManagement;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor
{
	internal static class AssetLockUtility
	{
		public static class Constants
		{
			public const string JSON_FLAG = "--json";
			
			public const string DEFAULT_GIT_EXE = "git.exe";
			public const string DEFAULT_GIT_LFS_EXE = "git-lfs.exe";

			public static readonly string[] DEFAULT_TRACKED_EXTENSIONS = new[] { ".prefab", ".unity", ".asset" };
			public const int DEFAULT_QUICK_CHECK_SIZE = 1024;
			
			public const string PACKAGE_NAME = "com.haydenno.assetlock";
			
			public const string PROJECT_SETTINGS_PROVIDER_PATH = "Project/AssetLock";
			public const string USER_SETTINGS_PROVIDER_PATH = "Preferences/AssetLock";
			
			public static readonly string PATH =
				Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) +
				";" +
				Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
			
			public const int DEFAULT_LOCK_TIMEOUT = 1000;
		}
		
		public static class Logging
		{
			const string LOG_PREFIX = "[AssetLock] ";
			
			static string GetMessage(string message)
			{
				return LOG_PREFIX + message;
			}
			
			[HideInCallstack]
			public static void LogVerbose(string message)
			{
				if (DebugMode && VerboseLogging)
				{
					UnityEngine.Debug.Log(GetMessage(message));
				}
			}
			
			[StringFormatMethod("format")]
			[HideInCallstack]
			public static void LogVerboseFormat(string format, params object[] args)
			{
				if (DebugMode && VerboseLogging)
				{
					UnityEngine.Debug.LogFormat(GetMessage(format), args);
				}
			}
			
			[HideInCallstack]
			public static void Log(string message)
			{
				if (InfoLogging)
				{
					UnityEngine.Debug.Log(GetMessage(message));
				}
			}
			
			[StringFormatMethod("format")]
			[HideInCallstack]
			public static void LogFormat(string format, params object[] args)
			{
				if (InfoLogging)
				{
					UnityEngine.Debug.LogFormat(GetMessage(format), args);
				}
			}

			[HideInCallstack]
			public static void LogWarning(string message)
			{
				if (WarningLogging)
				{
					UnityEngine.Debug.LogWarning(GetMessage(message));
				}
			}
			
			[StringFormatMethod("format")]
			[HideInCallstack]
			public static void LogWarningFormat(string format, params object[] args)
			{
				if (WarningLogging)
				{
					UnityEngine.Debug.LogWarningFormat(GetMessage(format), args);
				}
			}

			[HideInCallstack]
			public static void LogError(string message)
			{
				if (ErrorLogging)
				{
					UnityEngine.Debug.LogError(GetMessage(message));
				}
			}
			
			[StringFormatMethod("format")]
			[HideInCallstack]
			public static void LogErrorFormat(string format, params object[] args)
			{
				if (ErrorLogging)
				{
					UnityEngine.Debug.LogErrorFormat(GetMessage(format), args);
				}
			}

			public struct Profiler : IDisposable
			{
				private Stopwatch m_stopwatch;
				private string m_name;
				private string m_message;

				public Profiler([CallerMemberName] string callerName = null)
				{
					if (!DebugMode || !EnableProfiling)
					{
						m_stopwatch = null;
						m_name = null;
						m_message = null;
						return;
					}

					m_name = callerName;
					m_stopwatch = Stopwatch.StartNew();
					m_message = null;
				}

				public Profiler(string message, [CallerMemberName] string callerName = null)
					: this(callerName)
				{
					m_message = message;
				}

				public void SetMessage(string msg)
				{
					m_message = msg;
				}

				[StringFormatMethod("format")]
				public void SetMessageFormat(string format, params object[] args)
				{
					m_message = string.Format(format, args);
				}

				[HideInCallstack]
				public void Dispose()
				{
					if (string.IsNullOrEmpty(m_name))
					{
						return;
					}
					
					m_stopwatch?.Stop();
					
					LogVerboseFormat("[Profiling] {0} | {1}ms{2}", m_name, m_stopwatch?.ElapsedMilliseconds, $"\n{m_message}");
				}
			}
		}
		
		public static class GUIUtil
		{
			private static HashSet<string> s_keywords = null;
			public static bool MatchSearchGroups(string searchContext, string label)
			{
				if (s_keywords != null)
				{
					foreach (var keyword in label.Split(' '))
					{
						s_keywords.Add(keyword);
					}
				}

				if (searchContext == null)
				{
					return true;
				}

				var context = searchContext.Trim();

				if (string.IsNullOrEmpty(context))
				{
					return true;
				}
			
				var split = context.Split(' ');

				return split.Any(
					x => !string.IsNullOrEmpty(x) && label.IndexOf(x, StringComparison.InvariantCultureIgnoreCase) > -1
				);
			}

			public static void SearchableLabel(GUIContent label, string searchContext)
			{
				if (!MatchSearchGroups(searchContext, label.text))
				{
					return;
				}
				
				GUILayout.Label(label);
			}

			public static void SearchableLabel(
				GUIContent label,
				GUIStyle style,
				string searchContext,
				params GUILayoutOption[] options
			)
			{
				if (!MatchSearchGroups(searchContext, label.text))
				{
					return;
				}
				
				GUILayout.Label(label, style, options);
			}

			public static bool SearchableButton(GUIContent label, string searchContext)
			{
				if (!MatchSearchGroups(searchContext, label.text))
				{
					return false;
				}

				return GUILayout.Button(label);
			}

			public static bool SearchableButton(
				GUIContent label,
				GUIStyle style,
				string searchContext,
				params GUILayoutOption[] options
			)
			{
				if (!MatchSearchGroups(searchContext, label.text))
				{
					return false;
				}

				return GUILayout.Button(label, style, options);
			}
		}
		
		public static class ControlChars
		{
			public const char NUL = (char)0; // Null
			public const char BS = (char)8; // Backspace
			public const char CR = (char)13; // Carriage Return
			public const char SUB = (char)26; // Substitute
		}

		public static bool IsControlChar(char c)
		{
			return (c > ControlChars.NUL && c < ControlChars.BS) || (c > ControlChars.CR && c < ControlChars.SUB);
		}
		
		public static IEnumerable<string> GetAllBinaryPaths(string[] assetOrMetaFilePaths)
		{
			return assetOrMetaFilePaths.Where(ShouldTrack);
		}

		public static bool ShouldTrack(string path)
		{
			return ShouldTrack(new FileInfo(NormalizePath(path)));
		}

		public static bool ShouldTrack(FileInfo info)
		{
			if (info == null)
			{
				return false;
			}

			if (!info.Exists)
			{
				return false;
			}

			if (info.Attributes.HasFlag(FileAttributes.Directory))
			{
				return false;
			}

			if (!IsTrackedExtension(info))
			{
				return false;
			}

			if (IsUnityYaml(info))
			{
				return false;
			}

			return IsBinary(info);
		}

		// https://stackoverflow.com/questions/910873/how-can-i-determine-if-a-file-is-binary-or-text-in-c
		public static bool IsBinary(FileInfo info, long max = Int64.MaxValue)
		{
			using (var fs = info.OpenRead())
			{
				using (var reader = new StreamReader(fs))
				{
					char[] buffer = new char[1024];

					while (!reader.EndOfStream && fs.Position < max)
					{
						int read = reader.Read(buffer, 0, buffer.Length);

						for (int i = 0; i < read; i++)
						{
							if (IsControlChar(buffer[i]))
							{
								return true;
							}
						}
					}
				}
			}

			return false;
		}

		public static bool IsUnityYaml(FileInfo info)
		{
			string line1 = null;
			string line2 = null;

			using (var fs = info.OpenRead())
			{
				using (var reader = new StreamReader(fs))
				{
					line1 = reader.ReadLine();
					line2 = reader.ReadLine();
				}
			}

			if (line1 == null || line2 == null)
			{
				return false;
			}

			if (line1.Contains("%YAML 1.1") && line2.Contains("%TAG !u! tag:unity3d.com,2011:"))
			{
				return true;
			}

			return false;
		}

		public static bool IsTrackedExtension(FileInfo info)
		{
			return TrackedFileEndings.value.Contains(info.Extension);
		}
		
		public static bool TryGetDefaultGitPath(out string path)
		{
			string[] paths = Constants.PATH.Split(';');
			path= paths.Select(x => Path.Combine(x, Constants.DEFAULT_GIT_EXE)).FirstOrDefault(File.Exists);
			return path != null;
		}
		
		public static bool TryGetDefaultGitLfsPath(out string path)
		{
			string[] paths = Constants.PATH.Split(';');
			path = paths.Select(x => Path.Combine(x, Constants.DEFAULT_GIT_LFS_EXE)).FirstOrDefault(File.Exists);
			return path != null;
		}

		public static string NormalizePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return string.Empty;
			}
			
			path = Path.GetFullPath(path);

			return GetPathWithoutMeta(path);
		}
		
		public static string ToRelativePath(string path)
		{
			return Path.GetRelativePath(Application.dataPath, path);
		}
		
		public static string GetPathWithoutMeta(string path)
		{
			return path.EndsWith(".meta") ? path.Substring(0, path.Length - 5) : path;
		}

		public static List<LockInfo> FromJson(string json)
		{
			List<LockInfo> locks = new List<LockInfo>();

			foreach (var l in JsonConvert.DeserializeObject<List<LocksResponseDataJson>>(json))
			{
				locks.Add(l);
			}

			return locks;
		}
	}
}