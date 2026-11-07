using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using AssetLock.Editor.Manager;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Tests
{
    [TestFixture, Category("AssetLockUnit")]
    internal class ManagerTests : ManagerFixture
    {
        private const string AssetPath = "Assets/AssetLockTestFixture.bin";

        private static object Invoke(string method, params object[] args) =>
            typeof(AssetLockProcessor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

        [Test]
        public void CallbacksPassThroughUntilManagerIsReady()
        {
            var denied = new List<string>();
            Assert.That((bool)Invoke("IsOpenForEdit", new[] { AssetPath }, denied, StatusQueryOptions.UseCachedIfPossible), Is.True);
            Assert.That(denied, Is.Empty);
            Assert.That(Manager.IsReady, Is.False);
            Initialize();
            Assert.That(Manager.IsReady, Is.True);
            Assert.That(Lfs.Calls[0], Is.EqualTo(new[] { "install" }));
            Assert.That(Lfs.Calls[1], Is.EqualTo(new[] { "env" }));
            Assert.That(Git.Calls[0], Is.EqualTo(new[] { "config", "user.name" }));
        }

        [Test]
        public void FailedInitializationDoesNotActivateCallbacks()
        {
            Lfs.Run = args => FakeProcess.Result(args, exitCode: 1, error: "failure");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[AssetLock] failed to initialize git-lfs\nfailure");
            Assert.Throws<Exception>(() => Initialize());
            Assert.That(Manager.IsReady, Is.False);
            Assert.That((bool)Invoke("IsOpenForEdit", new[] { AssetPath }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.True);
        }

        [Test]
        public void DisabledModeDoesNotBlockAnyCallback()
        {
            Initialize();
            MasterEnable.SetValue(false);
            var paths = new[] { AssetPath };
            Assert.That((bool)Invoke("CanOpenForEdit", paths, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.True);
            Assert.That((string[])Invoke("OnWillSaveAssets", (object)paths), Is.EqualTo(paths));
            Assert.That((AssetDeleteResult)Invoke("OnWillDeleteAsset", AssetPath, (RemoveAssetOptions)0), Is.EqualTo(AssetDeleteResult.DidNotDelete));
            Assert.That((AssetMoveResult)Invoke("OnWillMoveAsset", AssetPath, "Assets/new.bin"), Is.EqualTo(AssetMoveResult.DidNotMove));
        }

        [Test]
        public void OtherUsersLockBlocksEditSaveMoveAndDelete()
        {
            CreateAsset();
            try
            {
                Initialize();
                var file = FileReference.FromPath(AssetPath);
                Manager.TestRepo[file] = file.ToLock(true, "lock-id", "another-user");
                var denied = new List<string>();
                Assert.That((bool)Invoke("CanOpenForEdit", new[] { AssetPath, AssetPath + ".meta" }, denied, StatusQueryOptions.UseCachedIfPossible), Is.False);
                Assert.That(denied.Count, Is.EqualTo(1));
                Assert.That((bool)Invoke("IsOpenForEdit", new[] { AssetPath }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.False);
                Assert.That((bool)Invoke("MakeEditable", new[] { AssetPath }, "", new List<string>()), Is.False);
                Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { AssetPath }), Is.Empty);
                Assert.That((AssetDeleteResult)Invoke("OnWillDeleteAsset", AssetPath, (RemoveAssetOptions)0), Is.EqualTo(AssetDeleteResult.FailedDelete));
                Assert.That((AssetMoveResult)Invoke("OnWillMoveAsset", AssetPath, "Assets/new.bin"), Is.EqualTo(AssetMoveResult.FailedMove));
            }
            finally { MasterEnable.SetValue(false); AssetDatabase.DeleteAsset(AssetPath); }
        }

        [Test]
        public void OwnLockAllowsEditSaveAndDeleteWithoutAutoLock()
        {
            CreateAsset();
            try
            {
                Initialize();
                var file = FileReference.FromPath(AssetPath);
                Manager.TestRepo[file] = file.ToLock(true, "lock-id", "tester");
                Assert.That((bool)Invoke("IsOpenForEdit", new[] { AssetPath }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.True);
                Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { AssetPath }), Has.Length.EqualTo(1));
                Assert.That((AssetDeleteResult)Invoke("OnWillDeleteAsset", AssetPath, (RemoveAssetOptions)0), Is.EqualTo(AssetDeleteResult.DidNotDelete));
            }
            finally { MasterEnable.SetValue(false); AssetDatabase.DeleteAsset(AssetPath); }
        }

        [Test]
        public void QueuedTrackRunsBeforeRefreshWithExpectedArguments()
        {
            CreateAsset();
            try
            {
                Initialize();
                Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks" ? "[]" : "");
                Manager.TrackFile(AssetPath, true);
                Manager.InnerLoopAsync().GetAwaiter().GetResult();
                Assert.That(Lfs.Calls[2], Is.EqualTo(new[] { "track", "--filename", "--lockable", AssetPath }));
                Assert.That(Lfs.Calls[3], Is.EqualTo(new[] { "locks", "--json" }));
                Assert.That(Manager.TestRepo.ContainsKey(FileReference.FromPath(AssetPath)), Is.True);
            }
            finally { MasterEnable.SetValue(false); AssetDatabase.DeleteAsset(AssetPath); }
        }

        [Test]
        public void UnlockedAssetCanBeOpenedButRequiresLockToEdit()
        {
            CreateAsset();
            try
            {
                Initialize();
                var file = FileReference.FromPath(AssetPath);
                Manager.TestRepo[file] = file.ToLock();
                Assert.That((bool)Invoke("CanOpenForEdit", new[] { AssetPath }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.True);
                Assert.That((bool)Invoke("IsOpenForEdit", new[] { AssetPath }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.False);
                Assert.That((bool)Invoke("MakeEditable", new[] { AssetPath }, "", new List<string>()), Is.False);
                Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { AssetPath, AssetPath + ".meta" }), Is.Empty);
            }
            finally { MasterEnable.SetValue(false); AssetDatabase.DeleteAsset(AssetPath); }
        }

        [Test]
        public void AutoLockQueuesAndAcquiresLockForEditableAsset()
        {
            CreateAsset();
            try
            {
                Initialize();
                AutoLock.SetValue(true);
                UseBlockingCallsInProcessor.SetValue(false);
                var file = FileReference.FromPath(AssetPath);
                Manager.TestRepo[file] = file.ToLock();
                Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks"
                    ? "[{\"id\":\"id\",\"path\":\"Assets/AssetLockTestFixture.bin\",\"owner\":{\"name\":\"tester\"}}]"
                    : "");
                Assert.That((bool)Invoke("MakeEditable", new[] { AssetPath }, "", new List<string>()), Is.True);
                Manager.InnerLoopAsync().GetAwaiter().GetResult();
                Assert.That(Lfs.Calls[2], Is.EqualTo(new[] { "lock", AssetPath }));
                Assert.That(Manager.TestRepo[file].LockedByMe, Is.True);
            }
            finally { MasterEnable.SetValue(false); AssetDatabase.DeleteAsset(AssetPath); }
        }

        [Test]
        public void DisposingManagerDisablesCallbacks()
        {
            Initialize();
            Manager.Dispose();
            Assert.That(Manager.IsReady, Is.False);
            Assert.That((bool)Invoke("IsOpenForEdit", new[] { AssetPath }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.True);
        }

        [UnityTest]
        public IEnumerator CallbacksStayInactiveWhileInitializationIsPending()
        {
            var pending = new TaskCompletionSource<ProcessResult>();
            Lfs.RunAsync = args => args[0] == "install" ? pending.Task : Task.FromResult(Lfs.Run(args));
            var initialization = Manager.InitializeForTests();
            Assert.That(Manager.IsReady, Is.False);
            Assert.That((bool)Invoke("IsOpenForEdit", new[] { AssetPath }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.True);
            pending.SetResult(FakeProcess.Result(new[] { "install" }));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!initialization.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(initialization.IsCompleted, Is.True);
            initialization.GetAwaiter().GetResult();
            Assert.That(Manager.IsReady, Is.True);
        }

        [Test]
        public void RebootDisposesThePreviousManager()
        {
            Initialize();
            var previous = Manager;
            AssetLockManager.TestFactory = () => new AssetLockManager(Git, Lfs);
            AssetLockManager.Reboot();
            Assert.That(previous.IsReady, Is.False);
            Assert.That(AssetLockManager.Instance, Is.Not.SameAs(previous));
            Assert.That(AssetLockManager.Instance.IsReady, Is.False);
        }

        [Test]
        public void BatchFirstRunLeavesManagerInactiveAndCompletesSetupPrompt()
        {
            Assert.That(Application.isBatchMode, Is.True);
            IsFirstTimeExperience.SetValue(true);
            AssetLockManager.Reboot();
            Assert.That(IsFirstTimeExperience.value, Is.False);
            Assert.That(AssetLockManager.Instance.IsReady, Is.False);
            Assert.That((bool)Invoke("IsOpenForEdit", new[] { AssetPath }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.True);
        }

        [UnityTest]
        public IEnumerator ConcurrentRefreshWaitersCompleteTogether()
        {
            Initialize();
            Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks" ? "[]" : "");
            var first = Manager.RefreshAsync();
            var second = Manager.RefreshAsync();
            Assert.That(first.IsCompleted || second.IsCompleted, Is.False);
            Manager.InnerLoopAsync().GetAwaiter().GetResult();
            typeof(AssetLockManager).GetMethod("EndRefresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Manager, new object[] { null });
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((!first.IsCompleted || !second.IsCompleted) && DateTime.UtcNow < deadline) yield return null;
            Assert.That(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully, Is.True);
        }

        [UnityTest]
        public IEnumerator FailedRefreshCompletesWaitersWithTheFailure()
        {
            Initialize();
            Lfs.RunAsync = _ => Task.FromException<ProcessResult>(new TimeoutException());
            var waiting = Manager.RefreshAsync();
            LogAssert.Expect(LogType.Error, "[AssetLock] Refresh failed (TimeoutException).");
            typeof(AssetLockManager).GetMethod("EditorLoop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Manager, null);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!waiting.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(waiting.IsFaulted, Is.True);
            Assert.That(waiting.Exception.InnerException, Is.TypeOf<TimeoutException>());
        }

        private static void CreateAsset()
        {
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, AssetPath), new byte[] { 1, 2, 3 });
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
