using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AssetLock.Editor
{
	/// <summary>
	/// Helper class for running processes.
	/// </summary>
	public class ProcessWrapper
	{
		public const string POWERSHELL = "WindowsPowerShell\\v1.0\\powershell.exe";
		public const string CMD = "cmd.exe";

		public const int PROCESS_TIMEOUT_MS = 5000;

		public readonly string ExePath;
		public string WorkingPath { get; set; } = Environment.CurrentDirectory;
		public DateTime LastRun { get; private set; } = DateTime.MinValue;
		public string LastCommand { get; private set; }

		private ProcessResult m_lastResult;

		private static readonly StringDictionary s_envVars = GetAllEnvVars();

		private static StringDictionary GetAllEnvVars()
		{
			IDictionary envVars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine);
			StringDictionary envVarsDict = new StringDictionary();

			foreach (DictionaryEntry entry in envVars)
			{
				envVarsDict.Add((string)entry.Key, (string)entry.Value);
			}

			envVars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User);

			foreach (DictionaryEntry entry in envVars)
			{
				if (!envVarsDict.ContainsKey((string)entry.Key))
				{
					envVarsDict.Add((string)entry.Key, (string)entry.Value);
				}
			}

			return envVarsDict;
		}

		public ProcessWrapper(string exePath)
		{
			ExePath = exePath;
		}

		public ProcessResult RunCommand(params string[] args)
		{
			ProcessResult result;
			using var profiler = new AssetLockUtility.Logging.Profiler();
			args = ProcessArgs(args);

			if (!ShouldRunAgain(args))
			{
				AssetLockUtility.Logging.LogVerboseFormat(
					"Reusing last result - Process: {0}\nAge: {1}ms\nResult: {2}",
					Path.GetFileName(ExePath),
					(DateTime.Now - LastRun).Milliseconds,
					m_lastResult.ToDebugString()
				);

				result = m_lastResult;
			}
			else
			{
				LastCommand = string.Join(" ", args);
				result = ProcessInstance.Run(ExePath, WorkingPath, args);
				LastRun = DateTime.Now;
				m_lastResult = result;
			}

			profiler.SetMessageFormat("Process: {0}\nResult: {1}", ExePath, result.ToDebugString());

			return result;
		}

		public async Task<ProcessResult> RunCommandAsync(params string[] args)
		{
			return await RunCommandAsync(default, args);
		}

		public async Task<ProcessResult> RunCommandAsync(CancellationToken ct, params string[] args)
		{
			using var profiler = new AssetLockUtility.Logging.Profiler();
			ProcessResult result;
			args = ProcessArgs(args);

			if (!ShouldRunAgain(args))
			{
				AssetLockUtility.Logging.LogVerboseFormat(
					"Reusing last result - Process: {0}\nAge: {1}ms\nResult: {2}",
					Path.GetFileName(ExePath),
					(DateTime.Now - LastRun).Milliseconds,
					m_lastResult.ToDebugString()
				);

				result = m_lastResult;
			}
			else
			{
				LastCommand = string.Join(" ", args);
				result = AssetLockSettings.ForceSynchronousProcessHandling
					? ProcessInstance.Run(ExePath, WorkingPath, args)
					: await ProcessInstance.RunAsync(ExePath, WorkingPath, ct, args);
				LastRun = DateTime.Now;
				m_lastResult = result;
			}

			profiler.SetMessageFormat("Process: {0}\nResult: {1}", ExePath, result.ToDebugString());

			return result;
		}

		private static string[] ProcessArgs(string[] args)
		{
			return args.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
		}

		private bool IsSameCommand(string[] args)
		{
			return LastCommand == string.Join(" ", args);
		}

		private int TimeSinceLastRun()
		{
			return Math.Abs((int)(DateTime.Now - LastRun).TotalMilliseconds);
		}

		private bool ShouldRunAgain(string[] args)
		{
			return !IsSameCommand(args) || TimeSinceLastRun() > PROCESS_TIMEOUT_MS;
		}
	}

	/// <summary>
	/// Holds the result of a process execution.
	/// </summary>
	public readonly struct ProcessResult
	{
		public readonly string[] ArgsIn;
		public readonly int ExitCode;
		public readonly string StdOut;
		public readonly string StdErr;

		public bool HasErrText => !string.IsNullOrWhiteSpace(StdErr);
		public bool HasOutText => !string.IsNullOrWhiteSpace(StdOut);

		public ProcessResult(string[] argsIn, int exitCode, string stdOut, string stdErr)
		{
			ArgsIn = argsIn;
			ExitCode = exitCode;
			StdOut = stdOut;
			StdErr = stdErr;
		}

		public override string ToString()
		{
			return StdOut;
		}

		public string ToDebugString()
		{
			return $"Args: {string.Join(' ', ArgsIn)}\nExitCode: {ExitCode}\nStdOut: {StdOut}\nStdErr: {StdErr}";
		}
	}

	/// <summary>
	/// A wrapper around a process instance.
	/// </summary>
	internal class ProcessInstance : IDisposable
	{
		private readonly Process m_process;
		private readonly StringBuilder m_stdOut = new StringBuilder();
		private readonly StringBuilder m_stdErr = new StringBuilder();
		public int Timeout { get; set; } = ProcessWrapper.PROCESS_TIMEOUT_MS;

		private ProcessInstance(string exePath, string workingPath, params string[] args)
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = exePath,
				LoadUserProfile = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true,
				CreateNoWindow = true,
				UseShellExecute = false,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = workingPath
			};
			SetEnvVars(startInfo);
			SetArgs(startInfo, args);

			m_process = new Process { StartInfo = startInfo };

			m_process.EnableRaisingEvents = true;
			m_process.ErrorDataReceived += OnErrorDataReceived;
			m_process.OutputDataReceived += OnOutputDataReceived;
		}

		private void StartProcess()
		{
			m_process.Start();
			m_process.BeginOutputReadLine();
			m_process.BeginErrorReadLine();
		}

		internal static ProcessResult Run(string exePath, string workingPath, params string[] args)
		{
			using ProcessInstance instance = new ProcessInstance(exePath, workingPath, args);

			instance.StartProcess();
			instance.m_process.WaitForExit(instance.Timeout);

			int exitCode = -1;

			try
			{
				exitCode = instance.m_process.ExitCode;
			}
			catch (InvalidOperationException)
			{
				AssetLockUtility.Logging.LogVerboseFormat(
					"Process: {0}\nResult: {1}",
					Path.GetFileName(exePath),
					"Failed to get exit code"
				);
			}

			return new ProcessResult(args, exitCode, instance.m_stdOut.ToString(), instance.m_stdErr.ToString());
		}

		internal static async Task<ProcessResult> RunAsync(
			string exePath,
			string workingPath,
			CancellationToken ct = default,
			params string[] args
		)
		{
			ct = ct == CancellationToken.None ? new CancellationTokenSource(ProcessWrapper.PROCESS_TIMEOUT_MS).Token : ct;

			using ProcessInstance instance = new ProcessInstance(exePath, workingPath, args);

			instance.StartProcess();
			await WaitForExitAsync(instance.m_process, ct);

			int exitCode = -1;

			try
			{
				exitCode = instance.m_process.ExitCode;
			}
			catch (InvalidOperationException)
			{
				AssetLockUtility.Logging.LogVerboseFormat(
					"Process: {0}\nResult: {1}",
					Path.GetFileName(exePath),
					"Failed to get exit code"
				);
			}

			return new ProcessResult(args, exitCode, instance.m_stdOut.ToString(), instance.m_stdErr.ToString());
		}

		private static void SetEnvVars(ProcessStartInfo info, params KeyValuePair<string, string>[] vars)
		{
			foreach (KeyValuePair<string, string> kvp in vars)
			{
				info.EnvironmentVariables[kvp.Key] = kvp.Value;
			}
		}

		private static void SetArgs(ProcessStartInfo info, params string[] args)
		{
			foreach (string arg in args)
			{
				info.ArgumentList.Add(arg);
			}
		}

		private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
		{
			m_stdErr.AppendLine(e.Data);
		}

		private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
		{
			m_stdOut.AppendLine(e.Data);
		}

		/// <inheritdoc />
		public void Dispose()
		{
			m_process.Dispose();
		}

		private static Task WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
		{
			if (process.HasExited) return Task.CompletedTask;

			TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
			process.EnableRaisingEvents = true;
			process.Exited += (sender, args) => tcs.TrySetResult(null);
			if (cancellationToken != new CancellationToken())
			{
				cancellationToken.Register(() => tcs.SetCanceled());
			}

			return !process.HasExited ? tcs.Task : Task.CompletedTask;
		}
	}
}