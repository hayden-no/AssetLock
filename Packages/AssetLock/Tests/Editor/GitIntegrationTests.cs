using System;
using System.Collections;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AssetLock.Editor.Tests
{
    [TestFixture, Category("AssetLockIntegration")]
    internal class GitIntegrationTests
    {
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
            }
            finally { Directory.Delete(repository, true); }
        }
    }
}
