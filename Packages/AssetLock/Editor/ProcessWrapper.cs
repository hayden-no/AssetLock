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
	internal interface IProcessRunner
	{
		Task<ProcessResult> RunCommandAsync(params string[] args);
	}

	/// <summary>
	/// Helper class for running processes.
	/// </summary>
	public class ProcessWrapper : IProcessRunner
	{
		public const string POWERSHELL = "WindowsPowerShell\\v1.0\\powershell.exe";
		public const string CMD = "cmd.exe";

		public const int PROCESS_TIMEOUT_MS = 5000;
		internal const int REMOTE_PROCESS_TIMEOUT_MS = 120000;

		internal static bool IsRemoteCommand(string[] args) =>
			args.Length > 0 && (args[0] == "locks" || args[0] == "lock" || args[0] == "unlock");

		public readonly string ExePath;
		public string WorkingPath { get; set; } = Environment.CurrentDirectory;
		public DateTime LastRun { get; private set; } = DateTime.MinValue;
		public string LastCommand { get; private set; }

		private ProcessResult m_lastResult;

		private static readonly StringDictionary s_envVars = GetAllEnvVars();

		private static StringDictionary GetAllEnvVars()
		{
			var envVars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine);
			var envVarsDict = new StringDictionary();

			foreach (DictionaryEntry entry in envVars)
			{
				envVarsDict.Add((string)entry.Key, (string)entry.Value);
			}

			envVars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User);

			foreach (DictionaryEntry entry in envVars)
			{
				if (!envVarsDict.ContainsKey((string)entry.Key))
					envVarsDict.Add((string)entry.Key, (string)entry.Value);
			}

			return envVarsDict;
		}

		public ProcessWrapper(string exePath) => ExePath = exePath;

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
				result = ProcessInstance.Run(ExePath, WorkingPath, args);
				LastCommand = string.Join(" ", args);
				LastRun = DateTime.Now;
				m_lastResult = result;
			}

			profiler.SetMessageFormat("Process: {0}\nResult: {1}", ExePath, result.ToDebugString());

			return result;
		}

		public async Task<ProcessResult> RunCommandAsync(params string[] args) => await RunCommandAsync(default, args);

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
				result = AssetLockSettings.ForceSynchronousProcessHandling && !IsRemoteCommand(args)
					? ProcessInstance.Run(ExePath, WorkingPath, args)
					: await ProcessInstance.RunAsync(ExePath, WorkingPath, ct, args);
				LastCommand = string.Join(" ", args);
				LastRun = DateTime.Now;
				m_lastResult = result;
			}

			profiler.SetMessageFormat("Process: {0}\nResult: {1}", ExePath, result.ToDebugString());

			return result;
		}

		private static string[] ProcessArgs(string[] args) => args.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

		private bool IsSameCommand(string[] args) => LastCommand == string.Join(" ", args);

		private int TimeSinceLastRun() => Math.Abs((int)(DateTime.Now - LastRun).TotalMilliseconds);

		private bool ShouldRunAgain(string[] args) =>
			!IsSameCommand(args) || m_lastResult.ExitCode != 0 || TimeSinceLastRun() > PROCESS_TIMEOUT_MS;
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

		public override string ToString() => StdOut;

		public string ToDebugString() =>
			$"Args: {string.Join(' ', ArgsIn)}\nExitCode: {ExitCode}\nStdOut: {StdOut}\nStdErr: {StdErr}";
	}

	/// <summary>
	/// A wrapper around a process instance.
	/// </summary>
	internal class ProcessInstance : IDisposable
	{
		private readonly Process m_process;
		private readonly StringBuilder m_stdOut = new();
		private readonly StringBuilder m_stdErr = new();
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
			using var instance = new ProcessInstance(exePath, workingPath, args);

			instance.StartProcess();

			if (!instance.m_process.WaitForExit(instance.Timeout))
			{
				try
				{
					instance.m_process.Kill();
				}
				catch (InvalidOperationException) { }

				instance.m_process.WaitForExit(1000);

				return new ProcessResult(args, -1, instance.m_stdOut.ToString(), "Process timed out.");
			}

			instance.m_process.WaitForExit(); // Drain redirected output callbacks.

			var exitCode = -1;

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
			using var timeout = ct == CancellationToken.None
				? new CancellationTokenSource(
					ProcessWrapper.IsRemoteCommand(args)
						? ProcessWrapper.REMOTE_PROCESS_TIMEOUT_MS
						: ProcessWrapper.PROCESS_TIMEOUT_MS
				)
				: null;
			if (timeout != null) ct = timeout.Token;

			using var instance = new ProcessInstance(exePath, workingPath, args);

			instance.StartProcess();

			try
			{
				await WaitForExitAsync(instance.m_process, ct);
			}
			catch (OperationCanceledException)
			{
				try
				{
					if (!instance.m_process.HasExited) instance.m_process.Kill();
				}
				catch (InvalidOperationException) { }

				instance.m_process.WaitForExit(1000);

				throw;
			}

			instance.m_process.WaitForExit(); // Drain redirected output callbacks.

			var exitCode = -1;

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
			foreach (var kvp in vars)
			{
				info.EnvironmentVariables[kvp.Key] = kvp.Value;
			}
		}

		private static void SetArgs(ProcessStartInfo info, params string[] args)
		{
			foreach (var arg in args)
			{
				info.ArgumentList.Add(arg);
			}
		}

		private void OnErrorDataReceived(object sender, DataReceivedEventArgs e) => m_stdErr.AppendLine(e.Data);

		private void OnOutputDataReceived(object sender, DataReceivedEventArgs e) => m_stdOut.AppendLine(e.Data);

		/// <inheritdoc />
		public void Dispose() => m_process.Dispose();

		private static Task WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
		{
			if (process.HasExited) return Task.CompletedTask;

			var tcs = new TaskCompletionSource<object>();
			process.EnableRaisingEvents = true;
			process.Exited += (sender, args) => tcs.TrySetResult(null);
			if (cancellationToken != new CancellationToken()) cancellationToken.Register(() => tcs.TrySetCanceled());

			return !process.HasExited ? tcs.Task : Task.CompletedTask;
		}
	}
}