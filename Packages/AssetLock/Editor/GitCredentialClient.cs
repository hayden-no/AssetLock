using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor
{
	internal sealed class GitCredential
	{
		internal string Username;
		internal string Password;
	}

	internal static class GitCredentialClient
	{
		internal static Func<string, Uri, string, string, bool, Task<string>> TestCommand;
		private static GitCredential s_connecting;
		private static string s_connectingScope;
		private static bool s_signInRunning;

		internal static Uri ScopeFor(string requestUrl, CredentialIdentity identity)
		{
			string authority = CredentialIdentities.AuthorityOf(requestUrl);
			string configured = CredentialIdentities.AuthorityOf(GitLfsServerUrl.value);
			if (string.IsNullOrEmpty(authority) || !string.Equals(authority, configured, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The lock request does not match the configured LFS server.");
			if (string.IsNullOrWhiteSpace(identity?.Username) || string.IsNullOrEmpty(identity.Authority) ||
			    !string.Equals(authority, identity.Authority, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Select an account for this LFS server in AssetLock Preferences.");
			return new Uri(GitLfsServerUrl.value);
		}

		internal static async Task<GitCredential> GetForRequestAsync(Uri scope, CredentialIdentity identity)
		{
			if (s_connecting != null && s_connectingScope == ScopeKey(scope, identity.Username)) return s_connecting;
			return await GetAsync(scope, identity.Username, false);
		}

		internal static async Task ConnectAsync(Uri scope, CredentialIdentity identity, Func<Task<bool>> testConnection)
		{
			if (s_signInRunning) throw new InvalidOperationException("A sign-in is already in progress.");
			s_signInRunning = true;
			try
			{
				var credential = await GetAsync(scope, identity.Username, true);
				s_connecting = credential;
				s_connectingScope = ScopeKey(scope, identity.Username);
				if (!await testConnection()) throw new InvalidOperationException("The lock server did not accept the selected account.");
				await RunAsync("approve", scope, credential.Username, credential.Password, false);
			}
			finally { s_connecting = null; s_connectingScope = null; s_signInRunning = false; }
		}

		private static string ScopeKey(Uri uri, string username) => uri.AbsoluteUri + "\n" + username.ToUpperInvariant();

		private static async Task<GitCredential> GetAsync(Uri scope, string username, bool interactive)
		{
			string output = await RunAsync("fill", scope, username, null, interactive);
			var result = new GitCredential();
			foreach (string line in output.Split('\n'))
			{
				string item = line.TrimEnd('\r');
				if (item.StartsWith("username=", StringComparison.Ordinal)) result.Username = item.Substring(9);
				else if (item.StartsWith("password=", StringComparison.Ordinal)) result.Password = item.Substring(9);
			}
			if (string.IsNullOrEmpty(result.Username) || string.IsNullOrEmpty(result.Password) ||
			    !string.Equals(username, result.Username, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Git did not return credentials for the selected account. Sign in from AssetLock Preferences.");
			return result;
		}

		private static Task<string> RunAsync(string operation, Uri scope, string username, string password, bool interactive)
		{
			if (username.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
			    (password != null && password.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
				throw new InvalidOperationException("Credential fields cannot contain line breaks.");
			if (TestMode && TestCommand != null) return TestCommand(operation, scope, username, password, interactive);
			string gitPath = GitPath.value;
			string workingDirectory = Path.GetDirectoryName(Application.dataPath);
			return Task.Run(() => RunProcess(operation, scope, username, password, interactive, gitPath, workingDirectory));
		}

		private static string RunProcess(string operation, Uri scope, string username, string password, bool interactive, string gitPath, string workingDirectory)
		{
			var start = new ProcessStartInfo
			{
				FileName = gitPath, WorkingDirectory = workingDirectory,
				UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
				RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
			};
			if (!interactive)
			{
				start.ArgumentList.Add("-c"); start.ArgumentList.Add("credential.interactive=never");
				start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
				start.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
			}
			var traceVariables = new List<string>();
			foreach (DictionaryEntry variable in start.EnvironmentVariables)
			{
				string name = (string)variable.Key;
				if (name.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase) ||
				    name.StartsWith("GCM_TRACE", StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(name, "GIT_CURL_VERBOSE", StringComparison.OrdinalIgnoreCase)) traceVariables.Add(name);
			}
			foreach (string name in traceVariables) start.EnvironmentVariables.Remove(name);
			start.EnvironmentVariables["GIT_TRACE_REDACT"] = "1";
			start.ArgumentList.Add("credential"); start.ArgumentList.Add(operation);
			try
			{
				using var process = new Process { StartInfo = start };
				process.Start();
				var output = process.StandardOutput.ReadToEndAsync();
				var error = process.StandardError.ReadToEndAsync();
				var input = new StringBuilder().Append("protocol=").Append(scope.Scheme).Append('\n')
					.Append("host=").Append(scope.Authority).Append('\n')
					.Append("path=").Append(scope.AbsolutePath.TrimStart('/')).Append('\n')
					.Append("username=").Append(username).Append('\n');
				if (password != null) input.Append("password=").Append(password).Append('\n');
				process.StandardInput.Write(input.Append('\n').ToString());
				process.StandardInput.Close();
				if (!process.WaitForExit(interactive ? 180000 : 30000))
				{
					try { process.Kill(); } catch (InvalidOperationException) { }
					throw new TimeoutException("Git credential lookup timed out.");
				}
				Task.WaitAll(output, error);
				if (process.ExitCode != 0) throw new InvalidOperationException("Git credential helper failed. Check your account and helper configuration.");
				return output.Result;
			}
			catch (System.ComponentModel.Win32Exception)
			{
				throw new InvalidOperationException("Could not start Git. Check the Git executable in AssetLock Preferences.");
			}
		}
	}
}
