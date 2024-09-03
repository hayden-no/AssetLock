using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AssetLock.Editor
{
	public class ProcessWrapper
	{
		public const string POWERSHELL = "WindowsPowerShell\\v1.0\\powershell.exe";
		public const string CMD = "cmd.exe";

		public const int PROCESS_WAIT_MS = 5000;

		public readonly string ExePath;
		public string WorkingPath { get; set; } = Environment.CurrentDirectory;
		public DateTime LastRun { get; private set; } = DateTime.MinValue;
		public string LastCommand { get; private set; }

		private ProcessResult _lastResult;

		private static readonly StringDictionary envVars = GetAllEnvVars();

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
			this.ExePath = exePath;
		}

		public ProcessResult RunCommand(params string[] args)
		{
			ProcessResult result;
			using var profiler = new AssetLockUtility.Logging.Profiler();

			if (!this.ShouldRunAgain(args))
			{
				AssetLockUtility.Logging.LogVerboseFormat(
					"Reusing last result - Process: {0}\nAge: {1}ms\nResult: {2}",
					Path.GetFileName(this.ExePath),
					(DateTime.Now - LastRun).Milliseconds,
					_lastResult.ToDebugString()
				);

				result = this._lastResult;
			}
			else
			{
				this.LastCommand = string.Join(" ", args);
				result = ProcessInstance.Run(this.ExePath, this.WorkingPath, args);
				this.LastRun = DateTime.Now;
				this._lastResult = result;
			}

			profiler.SetMessageFormat("Process: {0}\nResult: {1}", ExePath, result.ToDebugString());

			return result;
		}

		public async Task<ProcessResult> RunCommandAsync(params string[] args)
		{
			return await this.RunCommandAsync(default, args);
		}

		public async Task<ProcessResult> RunCommandAsync(CancellationToken ct, params string[] args)
		{
			using var profiler = new AssetLockUtility.Logging.Profiler();
			ProcessResult result;

			if (!this.ShouldRunAgain(args))
			{
				AssetLockUtility.Logging.LogVerboseFormat(
					"Reusing last result - Process: {0}\nAge: {1}ms\nResult: {2}",
					Path.GetFileName(this.ExePath),
					(DateTime.Now - LastRun).Milliseconds,
					_lastResult.ToDebugString()
				);

				result = this._lastResult;
			}
			else
			{
				this.LastCommand = string.Join(" ", args);
				result = AssetLockSettings.ForceSynchronousProcessHandling
					? ProcessInstance.Run(this.ExePath, this.WorkingPath, args)
					: await ProcessInstance.RunAsync(this.ExePath, this.WorkingPath, ct, args);
				this.LastRun = DateTime.Now;
				this._lastResult = result;
			}

			profiler.SetMessageFormat("Process: {0}\nResult: {1}", ExePath, result.ToDebugString());

			return result;
		}

		public bool IsSameCommand(string[] args)
		{
			return this.LastCommand == string.Join(" ", args);
		}

		public int TimeSinceLastRun()
		{
			return Math.Abs((int)(DateTime.Now - this.LastRun).TotalMilliseconds);
		}

		private bool ShouldRunAgain(string[] args)
		{
			return !this.IsSameCommand(args) || this.TimeSinceLastRun() > PROCESS_WAIT_MS;
		}
	}

	public struct ProcessResult
	{
		public readonly string[] ArgsIn;
		public readonly int ExitCode;
		public readonly string StdOut;
		public readonly string StdErr;

		public bool HasErrText => !string.IsNullOrWhiteSpace(this.StdErr);
		public bool HasOutText => !string.IsNullOrWhiteSpace(this.StdOut);

		public ProcessResult(string[] argsIn, int exitCode, string stdOut, string stdErr)
		{
			this.ArgsIn = argsIn;
			this.ExitCode = exitCode;
			this.StdOut = stdOut;
			this.StdErr = stdErr;
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

	internal class ProcessInstance : IDisposable
	{
		private const int TIMEOUT_DEFAULT = 2000;

		private readonly Process process;
		private readonly ProcessStartInfo startInfo;
		private readonly StringBuilder stdOut = new StringBuilder();
		private readonly StringBuilder stdErr = new StringBuilder();
		public int Timeout { get; set; } = TIMEOUT_DEFAULT;

		private ProcessInstance(string exePath, string workingPath, params string[] args)
		{
			this.startInfo = new ProcessStartInfo
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
			SetEnvVars(this.startInfo);
			SetArgs(this.startInfo, args);

			this.process = new Process { StartInfo = this.startInfo };
		}

		internal static ProcessResult Run(string exePath, string workingPath, params string[] args)
		{
			using ProcessInstance instance = new ProcessInstance(exePath, workingPath, args);

			instance.process.EnableRaisingEvents = true;
			instance.process.ErrorDataReceived += instance.OnErrorDataReceived;
			instance.process.OutputDataReceived += instance.OnOutputDataReceived;

			instance.process.Start();
			instance.process.BeginOutputReadLine();
			instance.process.BeginErrorReadLine();
			instance.process.WaitForExit(instance.Timeout);

			int exitCode = -1;

			try
			{
				exitCode = instance.process.ExitCode;
			}
			catch (InvalidOperationException)
			{
				AssetLockUtility.Logging.LogVerboseFormat(
					"Process: {0}\nResult: {1}",
					Path.GetFileName(exePath),
					"Failed to get exit code"
				);
			}

			return new ProcessResult(args, exitCode, instance.stdOut.ToString(), instance.stdErr.ToString());
		}

		internal static async Task<ProcessResult> RunAsync(
			string exePath,
			string workingPath,
			CancellationToken ct = default,
			params string[] args
		)
		{
			ct = ct == CancellationToken.None ? new CancellationTokenSource(TIMEOUT_DEFAULT).Token : ct;

			using ProcessInstance instance = new ProcessInstance(exePath, workingPath, args);

			instance.process.EnableRaisingEvents = true;
			instance.process.ErrorDataReceived += instance.OnErrorDataReceived;
			instance.process.OutputDataReceived += instance.OnOutputDataReceived;

			instance.process.Start();
			instance.process.BeginOutputReadLine();
			instance.process.BeginErrorReadLine();
			await WaitForExitAsync(instance.process, ct);

			int exitCode = -1;

			try
			{
				exitCode = instance.process.ExitCode;
			}
			catch (InvalidOperationException)
			{
				AssetLockUtility.Logging.LogVerboseFormat(
					"Process: {0}\nResult: {1}",
					Path.GetFileName(exePath),
					"Failed to get exit code"
				);
			}

			return new ProcessResult(args, exitCode, instance.stdOut.ToString(), instance.stdErr.ToString());
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
			this.stdErr.AppendLine(e.Data);
		}

		private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
		{
			this.stdOut.AppendLine(e.Data);
		}

		/// <inheritdoc />
		public void Dispose()
		{
			this.process.Dispose();
		}

		public static Task WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
		{
			if (process.HasExited) return Task.CompletedTask;

			TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
			process.EnableRaisingEvents = true;
			process.Exited += (EventHandler)((sender, args) => tcs.TrySetResult(null));
			if (cancellationToken != new CancellationToken())
				cancellationToken.Register((Action)(() => tcs.SetCanceled()));

			return !process.HasExited ? tcs.Task : Task.CompletedTask;
		}
	}
}