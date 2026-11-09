using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using AssetLock.Editor.Data;
using AssetLock.Editor.Manager;
using JetBrains.Annotations;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("haydenno.assetlock.tests")]

namespace AssetLock.Editor
{
	/// <summary>
	/// Utility class for the AssetLock package.
	/// </summary>
	internal static class AssetLockUtility
	{
		public static class Constants
		{
			public const string VERSION = "1.0.0";

			public const string JSON_FLAG = "--json";

			public const string DEFAULT_GIT_EXE = "git.exe";
			public const string DEFAULT_GIT_LFS_EXE = "git-lfs.exe";

			// unity adds the '.' to the extension
			public const string EXE_FILE_KIND = "exe";

			public const string GIT_EXE_DIRECTORY_PATH = "C\\Program Files";

			public const string GIT_DOWNLOAD_URL = "https://git-scm.com/downloads";
			public const string GIT_LFS_DOWNLOAD_URL = "https://git-lfs.github.com/";

			public const string GIT_LFS_SERVER_EXT = ".git/info/lfs";
			public const string GIT_LFS_SERVER_LOCKS_API_EXT = "/locks";

			public const string GIT_LFS_ENV_ENDPOINT = "Endpoint=";
			public const string GIT_LFS_ENV_WORKING_DIR = "LocalWorkingDir=";

			public static readonly string[] DEFAULT_TRACKED_EXTENSIONS = new[] { ".prefab", ".unity", ".asset" };
			public const int DEFAULT_QUICK_CHECK_SIZE = 4096;

			public const string PACKAGE_NAME = "com.haydenno.assetlock";

			public const string PROJECT_SETTINGS_PROVIDER_PATH = "Project/AssetLock";
			public const string USER_SETTINGS_PROVIDER_PATH = "Preferences/AssetLock";

			public const string BROWSER_MENU_PATH = "Window/" + BROWSER_TITLE;
			public const string BROWSER_TITLE = "Asset Lock Browser";
			public const string BROWSER_ICON = "AssemblyLock";

			public const string SETTINGS_TITLE = "AssetLock";

			public static readonly string PATH =
				Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) +
				";" +
				Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);

			public const string FILE_SYSTEM_UP_DIR = "..";

			public const int DEFAULT_LOCK_TIMEOUT = 2500;

			public const int CONTEXT_MENU_BASE_PRIORITY = 9000;
			public const int CONTEXT_MENU_SEPARATOR = 100;
		}

		public static class Logging
		{
			private const string LOG_PREFIX = "<b><color=cyan>[AssetLock]</color></b> ";

			[HideInCallstack]
			private static string GetMessage(string message) => LOG_PREFIX + message;

			private static bool HasReference(object[] args, out FileReference reference)
			{
				reference = default;

				if (!AddAssetReferenceToLogs) return false;

				foreach (var arg in args)
				{
					if (arg is FileReference)
					{
						reference = (FileReference)arg;

						return true;
					}
				}

				return false;
			}

			[RuntimeInitializeOnLoadMethod]
			static void Init()
			{
				// reset statics here for domain reload compat
				s_ErrorOnce = new HashSet<string>();
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[HideInCallstack]
			public static void LogVerbose(string message)
			{
				if (DebugMode && VerboseLogging) UnityEngine.Debug.Log(GetMessage(message));
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[HideInCallstack]
			public static void LogVerbose(Object context, string message)
			{
				if (DebugMode && VerboseLogging) UnityEngine.Debug.Log(GetMessage(message), context);
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[StringFormatMethod("format"), HideInCallstack]
			public static void LogVerboseFormat(string format, params object[] args)
			{
				if (DebugMode && VerboseLogging)
				{
					if (HasReference(args, out var reference))
						LogVerboseFormat(reference.MainAsset, format, args);
					else
						UnityEngine.Debug.LogFormat(GetMessage(format), args);
				}
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[StringFormatMethod("format"), HideInCallstack]
			public static void LogVerboseFormat(Object context, string format, params object[] args)
			{
				if (DebugMode && VerboseLogging) UnityEngine.Debug.LogFormat(context, GetMessage(format), args);
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[HideInCallstack]
			public static void Log(string message)
			{
				if (InfoLogging) UnityEngine.Debug.Log(GetMessage(message));
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[HideInCallstack]
			public static void Log(Object context, string message)
			{
				if (InfoLogging) UnityEngine.Debug.Log(GetMessage(message), context);
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[StringFormatMethod("format"), HideInCallstack]
			public static void LogFormat(string format, params object[] args)
			{
				if (InfoLogging)
				{
					if (HasReference(args, out var reference))
						LogFormat(reference.MainAsset, format, args);
					else
						UnityEngine.Debug.LogFormat(GetMessage(format), args);
				}
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[StringFormatMethod("format"), HideInCallstack]
			public static void LogFormat(Object context, string format, params object[] args)
			{
				if (InfoLogging) UnityEngine.Debug.LogFormat(context, GetMessage(format), args);
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[HideInCallstack]
			public static void LogWarning(string message)
			{
				if (WarningLogging) UnityEngine.Debug.LogWarning(GetMessage(message));
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[HideInCallstack]
			public static void LogWarning(Object context, string message)
			{
				if (WarningLogging) UnityEngine.Debug.LogWarning(GetMessage(message), context);
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[StringFormatMethod("format"), HideInCallstack]
			public static void LogWarningFormat(string format, params object[] args)
			{
				if (WarningLogging)
				{
					if (HasReference(args, out var reference))
						LogWarningFormat(reference.MainAsset, format, args);
					else
						UnityEngine.Debug.LogWarningFormat(GetMessage(format), args);
				}
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[StringFormatMethod("format"), HideInCallstack]
			public static void LogWarningFormat(Object context, string format, params object[] args)
			{
				if (WarningLogging) UnityEngine.Debug.LogWarningFormat(context, GetMessage(format), args);
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[HideInCallstack]
			public static void LogError(string message)
			{
				if (ErrorLogging) UnityEngine.Debug.LogError(GetMessage(message));
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[HideInCallstack]
			public static void LogError(Object context, string message)
			{
				if (ErrorLogging) UnityEngine.Debug.LogError(GetMessage(message), context);
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[StringFormatMethod("format"), HideInCallstack]
			public static void LogErrorFormat(string format, params object[] args)
			{
				if (ErrorLogging)
				{
					if (HasReference(args, out var reference))
						LogErrorFormat(reference.MainAsset, format, args);
					else
						UnityEngine.Debug.LogErrorFormat(GetMessage(format), args);
				}
			}

			// ReSharper disable Unity.PerformanceAnalysis
			[StringFormatMethod("format"), HideInCallstack]
			public static void LogErrorFormat(Object context, string format, params object[] args)
			{
				if (ErrorLogging) UnityEngine.Debug.LogErrorFormat(context, GetMessage(format), args);
			}

			[StringFormatMethod("format"), HideInCallstack]
			public static void LogErrorOnce(string format, params object[] args)
			{
				string msg;
				if (ErrorLogging && s_ErrorOnce.Add(msg=string.Format(format, args)))
				{
					LogError(msg);
				}
			}
			
			static HashSet<string> s_ErrorOnce = new HashSet<string>();

			public struct Profiler : IDisposable
			{
				private readonly Stopwatch m_stopwatch;
				private readonly string m_name;
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

				public Profiler(string message, [CallerMemberName] string callerName = null) : this(callerName) =>
					m_message = message;

				public void SetMessage(string msg) => m_message = msg;

				[StringFormatMethod("format")]
				public void SetMessageFormat(string format, params object[] args) =>
					m_message = string.Format(format, args);

				[HideInCallstack]
				public void Dispose()
				{
					if (string.IsNullOrEmpty(m_name)) return;

					m_stopwatch?.Stop();

					if (m_stopwatch?.ElapsedMilliseconds < ProfilingMinTimeMs) return;

					LogVerboseFormat(
						"[Profiling] {0} | {1}ms{2}",
						m_name,
						m_stopwatch?.ElapsedMilliseconds,
						$"\n{m_message}"
					);
				}
			}
		}

		public enum CredentialsKind { GitHub, GitLab, UserPass }

		private static class ControlChars
		{
			public const char NUL = (char)0; // Null
			public const char BS = (char)8; // Backspace
			public const char CR = (char)13; // Carriage Return
			public const char SUB = (char)26; // Substitute
		}

		public static bool IsControlChar(char c) =>
			(c >= ControlChars.NUL && c < ControlChars.BS) || (c > ControlChars.CR && c < (char)32);

		public static IEnumerable<FileReference> GetAllBinaryPaths(string[] assetOrMetaFilePaths) =>
			assetOrMetaFilePaths.Select(FileReference.FromPath).Distinct().Where(ShouldTrack);

		public static bool ShouldTrack(string path) => ShouldTrack(FileReference.FromPath(path));

		public static bool ShouldTrack(FileReference info)
		{
			if (!info.Exists) return false;

			if (!IsTrackedExtension(info)) return false;

			if (TrackNonBinaryFiles) return true;

			return !IsUnityYaml(info) && IsBinary(info, QuickCheckSize);
		}

		// https://stackoverflow.com/questions/910873/how-can-i-determine-if-a-file-is-binary-or-text-in-c
		public static bool IsBinary(FileReference info, long max = long.MaxValue)
		{
			using (var fs = info.OpenRead())
			{
				using (var reader = new StreamReader(fs))
				{
					var buffer = new char[1024];

					var remaining = max;

					while (remaining > 0)
					{
						var read = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));

						if (read == 0) break;

						remaining -= read;

						for (var i = 0; i < read; i++)
						{
							if (IsControlChar(buffer[i])) return true;
						}
					}
				}
			}

			return false;
		}

		public static bool IsUnityYaml(FileReference info)
		{
			string line1;
			string line2;

			using (var fs = info.OpenRead())
			{
				using (var reader = new StreamReader(fs))
				{
					line1 = reader.ReadLine();
					line2 = reader.ReadLine();
				}
			}

			if (line1 == null || line2 == null) return false;

			if (line1.Contains("%YAML 1.1") && line2.Contains("%TAG !u! tag:unity3d.com,2011:")) return true;

			return false;
		}

		public static bool IsTrackedExtension(FileReference info) => TrackedFileEndings.value.Contains(info.Extension);

		public static bool TryGetDefaultGitPath(out string path)
		{
			var paths = Constants.PATH.Split(';');
			path = paths.Select(x => Path.Combine(x, Constants.DEFAULT_GIT_EXE)).FirstOrDefault(File.Exists);

			return path != null;
		}

		public static bool TryGetDefaultGitLfsPath(out string path)
		{
			var paths = Constants.PATH.Split(';');
			path = paths.Select(x => Path.Combine(x, Constants.DEFAULT_GIT_LFS_EXE)).FirstOrDefault(File.Exists);

			return path != null;
		}

		public static string GetDefaultLfsEndpoint(string remoteUrl)
		{
			if (string.IsNullOrWhiteSpace(remoteUrl)) return string.Empty;

			if (remoteUrl.StartsWith("http"))
			{
				if (remoteUrl.EndsWith(".git"))
				{
					var ext = Constants.GIT_LFS_SERVER_EXT.Replace(".git", "");

					return remoteUrl + ext;
				}

				return remoteUrl + Constants.GIT_LFS_SERVER_EXT;
			}

			if (remoteUrl.StartsWith("git"))
			{
				remoteUrl = remoteUrl.Replace("git@", "https://");

				if (remoteUrl.EndsWith(".git"))
				{
					var ext = Constants.GIT_LFS_SERVER_EXT.Replace(".git", "");

					return remoteUrl + ext;
				}

				return remoteUrl + Constants.GIT_LFS_SERVER_EXT;
			}

			if (remoteUrl.StartsWith("ssh"))
			{
				remoteUrl = remoteUrl.Replace("ssh://", "https://");

				if (remoteUrl.EndsWith(".git"))
				{
					var ext = Constants.GIT_LFS_SERVER_EXT.Replace(".git", "");

					return remoteUrl + ext;
				}

				return remoteUrl + Constants.GIT_LFS_SERVER_EXT;
			}

			return string.Empty;
		}

		public static bool TryGetLfsEndpointFromEnv(string env, out string endpointUrl)
		{
			endpointUrl = string.Empty;

			var from = env.IndexOf(Constants.GIT_LFS_ENV_ENDPOINT, StringComparison.OrdinalIgnoreCase);

			if (from == -1) return false;

			var to1 = env.IndexOf(" ", from, StringComparison.OrdinalIgnoreCase);
			var to2 = env.IndexOf("\n", from, StringComparison.OrdinalIgnoreCase);
			var to = to1 == -1 ? to2 : to2 == -1 ? to1 : Math.Min(to1, to2);
			if (to == -1) to = env.Length;

			from += Constants.GIT_LFS_ENV_ENDPOINT.Length;

			endpointUrl = env.Substring(from, to - from).TrimEnd('\r');

			return !string.IsNullOrWhiteSpace(endpointUrl);
		}

		public static bool TryGetLfsWorkingDirFromEnv(string env, out string workingDir)
		{
			workingDir = string.Empty;

			var from = env.IndexOf(Constants.GIT_LFS_ENV_WORKING_DIR, StringComparison.OrdinalIgnoreCase);

			if (from == -1) return false;

			from += Constants.GIT_LFS_ENV_WORKING_DIR.Length;
			var to = env.IndexOf('\n', from);
			if (to == -1) to = env.Length;

			workingDir = env.Substring(from, to - from).TrimEnd('\r');

			return !string.IsNullOrWhiteSpace(workingDir);
		}

		public static string NormalizePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return string.Empty;

			path = Path.GetFullPath(path);

			return GetPathWithoutMeta(path);
		}

		public static string NormalizePathOrThrow(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) throw new Exception("Path is null or empty.");

			path = Path.GetFullPath(path);

			return GetPathWithoutMeta(path);
		}

		public static string ToUnityRelativePath(string path) =>
			Path.GetRelativePath(Path.Combine(Application.dataPath, "..\\"), path);

		public static string ToUnityAssetsRelativePath(string path) => Path.GetRelativePath(Application.dataPath, path);

		public static string ToGitRelativePath(string path) => Path.GetRelativePath(GitWorkingDirectory, path);

		public static string GetFullPath(string path)
		{
			if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

			var instance = AssetLockManager.Instance;
			var gitDirectory = instance?.GitWorkingDirectoryReference.AbsolutePath;

			if (!string.IsNullOrEmpty(gitDirectory) && StartsWithDirectory(path, Path.GetFileName(gitDirectory)))
				return Path.GetFullPath(Path.Combine(Directory.GetParent(gitDirectory).FullName, path));

			var dataDirectory = Path.GetFullPath(Application.dataPath);
			var projectDirectory = Directory.GetParent(dataDirectory).FullName;

			if (StartsWithDirectory(path, Path.GetFileName(projectDirectory)))
				return Path.GetFullPath(Path.Combine(Directory.GetParent(projectDirectory).FullName, path));

			if (StartsWithDirectory(path, Path.GetFileName(dataDirectory)) || StartsWithDirectory(path, "Packages"))
				return Path.GetFullPath(Path.Combine(projectDirectory, path));
			
			if (StartsWithDirectory(path, "ProjectSettings")) return Path.GetFullPath(Path.Combine(projectDirectory, path));

			Logging.LogErrorOnce(
				"Could not determine the full path for {0}.\nUnityProj: {1}\nUnityData: {2}",
				path,
				projectDirectory,
				dataDirectory
			);

			return string.Empty;
		}

		private static bool StartsWithDirectory(string path, string directoryName)
		{
			var comparison = Path.DirectorySeparatorChar == '\\'
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal;

			return !string.IsNullOrEmpty(directoryName) &&
				path.StartsWith(directoryName, comparison) &&
				(path.Length == directoryName.Length ||
					path[directoryName.Length] == '/' ||
					path[directoryName.Length] == '\\');
		}

		public static string GetPathWithoutMeta(string path) =>
			path.EndsWith(".meta") ? path.Substring(0, path.Length - 5) : path;

		public static List<LockInfo> FromJson(string json)
		{
			var locks = new List<LockInfo>();

			foreach (var l in JsonConvert.DeserializeObject<List<LocksResponseDataJson>>(json))
			{
				locks.Add(l);
			}

			return locks;
		}

		public static bool IsGitHubUrl(string url) => HasHost(url, "github.com");

		public static bool IsGitLabUrl(string url) => HasHost(url, "gitlab.com");

		private static bool HasHost(string url, string host) =>
			Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
			(string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) ||
				uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
	}
}