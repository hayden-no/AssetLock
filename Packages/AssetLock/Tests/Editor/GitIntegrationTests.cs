using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AssetLock.Editor.Tests
{
    [TestFixture, Category("AssetLockIntegration")]
    internal class GitIntegrationTests
    {
        [Test]
        public void PushHookPreservesExistingHookAndRecordsProposedUpdates()
        {
            string directory = Path.Combine(Path.GetTempPath(), "AssetLockPush-" + Guid.NewGuid().ToString("N"));
            string repo = Path.Combine(directory, "work");
            string remote = Path.Combine(directory, "remote.git");
            Directory.CreateDirectory(repo);
            try
            {
                void Git(string cwd, params string[] args)
                {
                    var result = ProcessInstance.Run("git", cwd, args);
                    Assert.That(result.ExitCode, Is.Zero, result.StdErr);
                }
                Git(repo, "-c", "init.templateDir=", "init");
                Git(directory, "-c", "init.templateDir=", "init", "--bare", remote);
                Git(repo, "config", "user.name", "Test User");
                Git(repo, "config", "user.email", "test@example.test");
                string hook = Path.Combine(repo, ".git", "hooks", "pre-push");
                Directory.CreateDirectory(Path.GetDirectoryName(hook));
                Git(repo, "config", "core.hooksPath", Path.GetDirectoryName(hook));
                string original = "#!/bin/sh\ncat > hook-input\nexit 0\n";
                File.WriteAllText(hook, original);
                if (Path.DirectorySeparatorChar != '\\') Assert.That(ProcessInstance.Run("chmod", repo, "+x", hook).ExitCode, Is.Zero);
                PushUnlockPrompt.Install(hook, repo);
                File.WriteAllText(Path.Combine(repo, "file.txt"), "test");
                Git(repo, "add", "file.txt");
                Git(repo, "commit", "-m", "test");
                Git(repo, "push", remote, "HEAD:refs/heads/main");
                string input = File.ReadAllText(Path.Combine(repo, "hook-input"));
                Assert.That(input, Does.Contain("refs/heads/main"));
                var events = Directory.GetFiles(Path.Combine(repo, ".git", "assetlock-push-events"), "*.event");
                Assert.That(events, Has.Length.EqualTo(1));
                Assert.That(File.ReadAllText(events[0]), Is.EqualTo(input));
                string remoteHook = Path.Combine(remote, "hooks", "pre-receive");
                Directory.CreateDirectory(Path.GetDirectoryName(remoteHook));
                File.WriteAllText(remoteHook, "#!/bin/sh\nexit 1\n");
                if (Path.DirectorySeparatorChar != '\\') Assert.That(ProcessInstance.Run("chmod", repo, "+x", remoteHook).ExitCode, Is.Zero);
                var serverRejected = ProcessInstance.Run("git", repo, "push", remote, "HEAD:refs/heads/master");
                Assert.That(serverRejected.ExitCode, Is.Not.Zero);
                Assert.That(Directory.GetFiles(Path.Combine(repo, ".git", "assetlock-push-events"), "*.event"), Has.Length.EqualTo(2), "Pre-push cannot know the server will reject the push.");
                File.WriteAllText(hook + ".assetlock-original", "#!/bin/sh\nexit 23\n");
                var rejected = ProcessInstance.Run("git", repo, "push", remote, "HEAD:refs/heads/master");
                Assert.That(rejected.ExitCode, Is.Not.Zero);
                Assert.That(Directory.GetFiles(Path.Combine(repo, ".git", "assetlock-push-events"), "*.event"), Has.Length.EqualTo(2));
                File.Delete(hook + ".assetlock-original");
                var missing = ProcessInstance.Run("git", repo, "push", remote, "HEAD:refs/heads/master");
                Assert.That(missing.ExitCode, Is.Not.Zero);
                Assert.That(missing.StdErr, Does.Contain("original pre-push hook is missing"));
                Assert.Throws<InvalidOperationException>(() => PushUnlockPrompt.Remove(hook));
                File.WriteAllText(hook + ".assetlock-original", original);
                PushUnlockPrompt.Remove(hook);
                Assert.That(File.ReadAllText(hook), Is.EqualTo(original));
            }
            finally
            {
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(directory, true);
            }
        }
        [UnityTest]
        public IEnumerator CredentialHelperReadsFromIsolatedGitConfiguration()
        {
            var temp = Path.GetFullPath(Path.GetTempPath());
            var directory = Path.Combine(temp, "AssetLockCredential-" + Guid.NewGuid().ToString("N"));
            Assert.That(Path.GetFullPath(directory).StartsWith(temp, StringComparison.OrdinalIgnoreCase), Is.True);
            Directory.CreateDirectory(directory);
            var config = Path.Combine(directory, "gitconfig");
            File.WriteAllText(config, "[credential]\n\thelper =\n\thelper = \"!f() { printf 'username=alice\\\\npassword=integration-token\\\\n'; }; f\"\n");
            string originalGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
            string originalNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
            string originalGit = AssetLockSettings.GitPath.value;
            try
            {
                Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", config);
                Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
                AssetLockSettings.GitPath.SetValue("git");
                var identity = new CredentialIdentity { Username = "alice", Authority = "https://example.test" };
                var lookup = GitCredentialClient.GetForRequestAsync(new Uri("https://example.test/repo/info/lfs"), identity);
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!lookup.IsCompleted && DateTime.UtcNow < deadline) yield return null;
                Assert.That(lookup.IsCompleted, Is.True);
                var credential = lookup.GetAwaiter().GetResult();
                Assert.That(credential.Username, Is.EqualTo("alice"));
                Assert.That(credential.Password, Is.EqualTo("integration-token"));
            }
            finally
            {
                AssetLockSettings.GitPath.SetValue(originalGit);
                Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", originalGlobal);
                Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", originalNoSystem);
                Directory.Delete(directory, true);
            }
        }

        [UnityTest]
        public IEnumerator ProcessCancellationCompletesWithoutLeavingItRunning()
        {
            AssetLockSettings.ForceSynchronousProcessHandling.SetValue(false);
            using var cancellation = new CancellationTokenSource(500);
            var process = new ProcessWrapper("powershell.exe");
            var task = process.RunCommandAsync(cancellation.Token, "-NoProfile", "-Command", "Start-Sleep -Seconds 30");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Process did not stop after cancellation.");
            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public void GitAndLfsRunInTemporaryRepository()
        {
            var temp = Path.GetFullPath(Path.GetTempPath());
            var repository = Path.Combine(temp, "AssetLockGit-" + Guid.NewGuid().ToString("N"));
            Assert.That(Path.GetFullPath(repository).StartsWith(temp, StringComparison.OrdinalIgnoreCase), Is.True);
            Directory.CreateDirectory(repository);
            try
            {
                var git = new ProcessWrapper("git") { WorkingPath = repository };
                var init = git.RunCommand("--git-dir=" + Path.Combine(repository, ".git"), "--work-tree=" + repository, "-c", "init.templateDir=", "init");
                Assert.That(init.ExitCode, Is.EqualTo(0), init.StdErr);
                var version = git.RunCommand("lfs", "version");
                Assert.That(version.ExitCode, Is.EqualTo(0), version.StdErr);
                Assert.That(version.StdOut, Does.Contain("git-lfs"));

                var reader = new ProcessWrapper("git") { WorkingPath = repository };
                var missing = reader.RunCommand("config", "--local", "assetlock.retry-test");
                Assert.That(missing.ExitCode, Is.Not.Zero);
                Assert.That(git.RunCommand("config", "--local", "assetlock.retry-test", "ready").ExitCode, Is.Zero);
                var retry = reader.RunCommand("config", "--local", "assetlock.retry-test");
                Assert.That(retry.ExitCode, Is.Zero, retry.StdErr);
                Assert.That(retry.StdOut.Trim(), Is.EqualTo("ready"));
            }
            finally { Directory.Delete(repository, true); }
        }

        [UnityTest]
        public IEnumerator RemoteLocksCanTakeLongerThanFiveSeconds()
        {
            var temp = Path.GetFullPath(Path.GetTempPath());
            var repository = Path.Combine(temp, "AssetLockSlowLfs-" + Guid.NewGuid().ToString("N"));
            Assert.That(Path.GetFullPath(repository).StartsWith(temp, StringComparison.OrdinalIgnoreCase), Is.True);
            Directory.CreateDirectory(repository);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var originalPrompt = Environment.GetEnvironmentVariable("GIT_TERMINAL_PROMPT");
            var originalGitDir = Environment.GetEnvironmentVariable("GIT_DIR");
            var originalWorkTree = Environment.GetEnvironmentVariable("GIT_WORK_TREE");
            try
            {
                Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0");
                Environment.SetEnvironmentVariable("GIT_DIR", null);
                Environment.SetEnvironmentVariable("GIT_WORK_TREE", null);
                var git = new ProcessWrapper("git") { WorkingPath = repository };
                var init = git.RunCommand("--git-dir=" + Path.Combine(repository, ".git"), "--work-tree=" + repository, "-c", "init.templateDir=", "init");
                Assert.That(init.ExitCode, Is.Zero, init.StdErr);
                string url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/repo";
                var configure = git.RunCommand("config", "--local", "lfs.url", url);
                Assert.That(configure.ExitCode, Is.Zero, configure.StdErr);
                var server = Task.Run(async () =>
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    string request = await reader.ReadLineAsync();
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                    await Task.Delay(6500);
                    byte[] body = Encoding.UTF8.GetBytes("{\"locks\":[]}");
                    byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/vnd.git-lfs+json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length);
                    await stream.WriteAsync(body, 0, body.Length);
                    return request;
                });
                var lfs = new ProcessWrapper("git-lfs") { WorkingPath = repository };
                var operation = lfs.RunCommandAsync("locks", "--json");
                var deadline = DateTime.UtcNow.AddSeconds(25);
                while (!operation.IsCompleted && DateTime.UtcNow < deadline) yield return null;
                Assert.That(operation.IsCompleted, Is.True, "Git LFS did not finish the delayed local request.");
                var result = operation.GetAwaiter().GetResult();
                Assert.That(result.ExitCode, Is.Zero, result.StdErr);
                Assert.That(server.GetAwaiter().GetResult(), Does.StartWith("GET /repo/locks"));
            }
            finally
            {
                listener.Stop();
                Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", originalPrompt);
                Environment.SetEnvironmentVariable("GIT_DIR", originalGitDir);
                Environment.SetEnvironmentVariable("GIT_WORK_TREE", originalWorkTree);
                Directory.Delete(repository, true);
            }
        }
    }
}
