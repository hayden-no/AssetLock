using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AssetLock.Editor.Manager;
using NUnit.Framework;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Tests
{
    internal sealed class FakeProcess : IProcessRunner
    {
        internal readonly List<string[]> Calls = new();
        internal Func<string[], ProcessResult> Run;
        internal Func<string[], Task<ProcessResult>> RunAsync;

        public Task<ProcessResult> RunCommandAsync(params string[] args)
        {
            Calls.Add(args);
            return RunAsync != null ? RunAsync(args) : Task.FromResult(Run(args));
        }

        internal static ProcessResult Result(string[] args, string output = "", int exitCode = 0, string error = "") =>
            new(args, exitCode, output, error);
    }

    internal abstract class ManagerFixture
    {
        protected AssetLockManager Manager;
        protected FakeProcess Git;
        protected FakeProcess Lfs;

        [SetUp]
        public void SetUpManager()
        {
            Assert.That(TestMode, Is.True, "Run the tests with -assetLockTestMode to isolate settings and prevent Git startup.");
            MasterEnable.SetValue(true);
            IsFirstTimeExperience.SetValue(false);
            ParseFilesOnStartup.SetValue(false);
            AutoLock.SetValue(false);
            TrackNonBinaryFiles.SetValue(true);
            TrackedFileEndings.SetValue(new List<string> { ".bin", ".asset" });
            GitRemoteUrl.SetValue("https://example.test/repo");
            GitWorkingDirectory.SetValue("");
            UseHttp.SetValue(false);
            LogHttp.SetValue(false);
            var project = Directory.GetParent(Application.dataPath).FullName;
            Git = new FakeProcess { Run = args => FakeProcess.Result(args, "tester\n") };
            Lfs = new FakeProcess { Run = args => FakeProcess.Result(args,
                args[0] == "env" ? $"LocalWorkingDir={project}\n" : "") };
            Manager = new AssetLockManager(Git, Lfs);
            AssetLockManager.SetTestInstance(Manager);
        }

        protected void Initialize() => Manager.InitializeForTests().GetAwaiter().GetResult();

        [TearDown]
        public void TearDownManager()
        {
            AssetLockManager.TestFactory = null;
            AssetLockManager.SetTestInstance(null);
        }
    }
}
