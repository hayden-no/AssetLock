using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using AssetLock.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Tests
{
    [TestFixture, Category("AssetLockUnit")]
    internal class BrowserAndWarningTests : ManagerFixture
    {
        private string m_path;
        private FileReference m_file;

        private static object Invoke(string method, params object[] args) =>
            typeof(AssetLockProcessor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

        [SetUp]
        public void CreateAsset()
        {
            m_path = "Assets/AssetLockWarning-" + Guid.NewGuid().ToString("N") + ".bin";
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, m_path), new byte[] { 1, 2, 3 });
            AssetDatabase.ImportAsset(m_path, ImportAssetOptions.ForceSynchronousImport);
            m_file = FileReference.FromPath(m_path);
            Initialize();
        }

        [TearDown]
        public void RemoveAsset()
        {
            MasterEnable.SetValue(false);
            AssetDatabase.DeleteAsset(m_path);
        }

        [Test]
        public void WarningModeAllowsLockedAssetOperationsAndReportsOwner()
        {
            WarnOnly.SetValue(true);
            Manager.TestRepo[m_file] = m_file.ToLock(true, "lock-id", "another-user");
            var denied = new List<string>();
            Assert.That((bool)Invoke("CanOpenForEdit", new[] { m_path }, denied, StatusQueryOptions.ForceUpdate), Is.True);
            Assert.That((bool)Invoke("IsOpenForEdit", new[] { m_path }, denied, StatusQueryOptions.UseCachedIfPossible), Is.True);
            Assert.That((bool)Invoke("MakeEditable", new[] { m_path }, "", denied), Is.True);
            Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { m_path, m_path + ".meta" }), Has.Length.EqualTo(2));
            Assert.That((AssetDeleteResult)Invoke("OnWillDeleteAsset", m_path, (RemoveAssetOptions)0), Is.EqualTo(AssetDeleteResult.DidNotDelete));
            Assert.That((AssetMoveResult)Invoke("OnWillMoveAsset", m_path, "Assets/new.bin"), Is.EqualTo(AssetMoveResult.DidNotMove));
            Assert.That(denied, Is.Empty);
            Assert.That(AssetLockInspectorWarning.GetWarning(m_file), Does.Contain("another-user"));
            Assert.That(Manager.TestRepo.ContainsKey(m_file), Is.True);
        }

        [Test]
        public void WarningModeReportsUnknownStatusWithoutWaiting()
        {
            WarnOnly.SetValue(true);
            Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { m_path }), Has.Length.EqualTo(1));
            Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { m_path }), Has.Length.EqualTo(1));
            Assert.That(Lfs.Calls, Has.Count.EqualTo(2));
            var queue = typeof(AssetLock.Editor.Manager.AssetLockManager).GetField("m_commandQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Manager);
            Assert.That((int)queue.GetType().GetProperty("Count").GetValue(queue), Is.Zero);
            Assert.That(AssetLockInspectorWarning.GetWarning(m_file), Does.Contain("not yet available"));
        }

        [Test]
        public void WarningModeDoesNotWarnForYourOwnLock()
        {
            WarnOnly.SetValue(true);
            Manager.TestRepo[m_file] = m_file.ToLock(true, "lock-id", "tester");
            Assert.That(AssetLockInspectorWarning.GetWarning(m_file), Is.Null);
            Assert.That((bool)Invoke("IsOpenForEdit", new[] { m_path }, new List<string>(), StatusQueryOptions.UseCachedIfPossible), Is.True);
            Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { m_path }), Has.Length.EqualTo(1));
        }

        [Test]
        public void WarningModeCanQueueAutoLockAndSwitchBackToEnforcement()
        {
            WarnOnly.SetValue(true);
            AutoLock.SetValue(true);
            Manager.TestRepo[m_file] = m_file.ToLock();
            Assert.That((bool)Invoke("MakeEditable", new[] { m_path }, "", new List<string>()), Is.True);
            Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { m_path }), Has.Length.EqualTo(1));
            Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks" ? "[]" : "");
            Manager.InnerLoopAsync().GetAwaiter().GetResult();
            Assert.That(Lfs.Calls[2][0], Is.EqualTo("lock"));
            Assert.That(Lfs.Calls, Has.Count.EqualTo(4));
            WarnOnly.SetValue(false);
            var denied = new List<string>();
            Assert.That((bool)Invoke("IsOpenForEdit", new[] { m_path }, denied, StatusQueryOptions.UseCachedIfPossible), Is.False);
            Assert.That(denied, Has.Count.EqualTo(1));
        }

        [Test]
        public void WarningAutoLockTracksUnknownAssetAndLocksOnlyOnce()
        {
            WarnOnly.SetValue(true);
            AutoLock.SetValue(true);
            Assert.That(AssetLockInspectorWarning.GetWarning(m_file), Does.Contain("not yet available"));
            Assert.That((bool)Invoke("MakeEditable", new[] { m_path }, "", new List<string>()), Is.True);
            Assert.That((string[])Invoke("OnWillSaveAssets", (object)new[] { m_path }), Has.Length.EqualTo(1));
            Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks"
                ? "[{\"id\":\"id\",\"path\":\"" + m_path + "\",\"owner\":{\"name\":\"tester\"}}]" : "");
            Manager.InnerLoopAsync().GetAwaiter().GetResult();
            Assert.That(Lfs.Calls.FindAll(call => call[0] == "track"), Has.Count.EqualTo(1));
            Assert.That(Lfs.Calls.FindAll(call => call[0] == "lock"), Has.Count.EqualTo(1));
            Assert.That(AssetLockInspectorWarning.GetWarning(m_file), Is.Null);
        }

        [Test]
        public void WarningAutoLockReportsFailureAndKeepsInspectorWarning()
        {
            WarnOnly.SetValue(true);
            Manager.TestRepo[m_file] = m_file.ToLock();
            string failure = null;
            Manager.QueueWarningAutoLock(m_file, (_, reason) => failure = reason);
            Lfs.Run = args => FakeProcess.Result(args, exitCode: args[0] == "lock" ? 1 : 0, error: "denied");
            LogAssert.Expect(LogType.Error, new Regex("failed to lock file"));
            Assert.Throws<Exception>(() => Manager.InnerLoopAsync().GetAwaiter().GetResult());
            Assert.That(failure, Does.Contain("failed"));
            Assert.That(AssetLockInspectorWarning.GetWarning(m_file), Does.Contain("Not locked"));
        }

        [Test]
        public void WarningAutoLockReportsExistingOwnerWithoutForcingLock()
        {
            WarnOnly.SetValue(true);
            Manager.TestRepo[m_file] = m_file.ToLock(true, "id", "other-user");
            string failure = null;
            Manager.QueueWarningAutoLock(m_file, (_, reason) => failure = reason);
            Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks" ? "[]" : "");
            Manager.InnerLoopAsync().GetAwaiter().GetResult();
            Assert.That(failure, Does.Contain("other-user"));
            Assert.That(Lfs.Calls.FindAll(call => call[0] == "lock"), Is.Empty);
        }

        [Test]
        public void WarningAutoLockReportsFileRemovedBeforeRequest()
        {
            var missing = FileReference.FromPath("Assets/asset-lock-missing.bin");
            string failure = null;
            Manager.QueueWarningAutoLock(missing, (_, reason) => failure = reason);
            Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks" ? "[]" : "");
            Manager.InnerLoopAsync().GetAwaiter().GetResult();
            Assert.That(failure, Does.Contain("moved or deleted"));
            Assert.That(Lfs.Calls.FindAll(call => call[0] == "lock"), Is.Empty);
        }

        [Test]
        public void ToolkitBrowserDisplaysSetupStateWhenManagerIsNotReady()
        {
            Manager.Dispose();
            var window = ScriptableObject.CreateInstance<LockBrowserToolkit>();
            try
            {
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<ListView>("lock-list").itemsSource.Count, Is.Zero);
                Assert.That(window.rootVisualElement.Query<Label>().ToList().Exists(label => label.text == "Setup required"), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void ToolkitBrowserExplainsWhenAssetLockIsDisabled()
        {
            MasterEnable.SetValue(false);
            var window = ScriptableObject.CreateInstance<LockBrowserToolkit>();
            try
            {
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<Label>("empty-state").text, Does.Contain("disabled"));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void ToolkitBrowserShowsPausedRefreshAndKeepsRetryAvailable()
        {
            Lfs.RunAsync = _ => Task.FromException<ProcessResult>(new TimeoutException());
            Manager.Refresh();
            LogAssert.Expect(LogType.Error, new Regex("Refresh failed \\(TimeoutException\\)"));
            typeof(AssetLock.Editor.Manager.AssetLockManager).GetMethod("EditorLoop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Manager, null);
            Assert.That(Manager.AutomaticRefreshPaused, Is.True);
            var window = ScriptableObject.CreateInstance<LockBrowserToolkit>();
            try
            {
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<Label>("refresh-status").text, Is.EqualTo("Refresh paused"));
                var buttons = window.rootVisualElement.Query<Button>().ToList();
                Assert.That(buttons.Find(button => button.text == "Refresh").enabledSelf, Is.True);
                Assert.That(buttons.Find(button => button.text == "Scan assets").enabledSelf, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator ToolkitBrowserShowsInitializationUntilGitLfsIsReady()
        {
            var pending = new TaskCompletionSource<ProcessResult>();
            Lfs.RunAsync = args => args[0] == "install" ? pending.Task : Task.FromResult(Lfs.Run(args));
            Manager = new AssetLock.Editor.Manager.AssetLockManager(Git, Lfs);
            AssetLock.Editor.Manager.AssetLockManager.SetTestInstance(Manager);
            var initialization = Manager.InitializeForTests();
            var window = ScriptableObject.CreateInstance<LockBrowserToolkit>();
            try
            {
                window.CreateGUI();
                var status = window.rootVisualElement.Q<Label>("refresh-status");
                Assert.That(status.text, Is.EqualTo("Initializing"));
                pending.SetResult(FakeProcess.Result(new[] { "install" }));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!initialization.IsCompleted && DateTime.UtcNow < deadline) yield return null;
                Assert.That(initialization.IsCompletedSuccessfully, Is.True);
                typeof(LockBrowserToolkit).GetMethod("UpdateStatus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                Assert.That(status.text, Is.EqualTo("Refreshing locks"));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator ToolkitBrowserShowsWorkUntilQueuedAssetsAndRefreshComplete()
        {
            Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks" ? "[]" : "");
            var window = ScriptableObject.CreateInstance<LockBrowserToolkit>();
            var update = typeof(LockBrowserToolkit).GetMethod("UpdateStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                window.CreateGUI();
                var status = window.rootVisualElement.Q<Label>("refresh-status");
                Assert.That(status.text, Is.EqualTo("Refreshing locks"));
                Assert.That(window.rootVisualElement.Q<Label>("empty-state").text, Does.Contain("Loading"));
                Manager.InnerLoopAsync().GetAwaiter().GetResult();
                update.Invoke(window, null);
                Assert.That(status.text, Is.EqualTo("Ready"));

                Manager.TrackFile(m_path, true);
                update.Invoke(window, null);
                Assert.That(status.text, Is.EqualTo("Updating assets"));

                var tracking = new TaskCompletionSource<ProcessResult>();
                var refreshing = new TaskCompletionSource<ProcessResult>();
                Lfs.RunAsync = args => args[0] == "track" ? tracking.Task : refreshing.Task;
                var loop = Manager.InnerLoopAsync();
                update.Invoke(window, null);
                Assert.That(status.text, Is.EqualTo("Updating assets"));
                tracking.SetResult(FakeProcess.Result(new[] { "track" }));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (Lfs.Calls.FindAll(call => call[0] == "locks").Count < 2 && DateTime.UtcNow < deadline) yield return null;
                update.Invoke(window, null);
                Assert.That(status.text, Is.EqualTo("Refreshing locks"));
                refreshing.SetResult(FakeProcess.Result(new[] { "locks" }, "[]"));
                while (!loop.IsCompleted && DateTime.UtcNow < deadline) yield return null;
                Assert.That(loop.IsCompletedSuccessfully, Is.True);
                update.Invoke(window, null);
                Assert.That(status.text, Is.EqualTo("Ready"));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void ToolkitBrowserDisplaysTrackedAssetsAndFiltersSearch()
        {
            Manager.TestRepo[m_file] = m_file.ToLock(true, "lock-id", "another-user");
            var window = ScriptableObject.CreateInstance<LockBrowserToolkit>();
            try
            {
                window.CreateGUI();
                Assert.That(window.rootVisualElement.styleSheets.count, Is.GreaterThan(0));
                Assert.That(window.rootVisualElement.Q<Label>("browser-title").text, Is.EqualTo("ASSET LOCK"));
                var list = window.rootVisualElement.Q<ListView>("lock-list");
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                var search = window.rootVisualElement.Q<ToolbarSearchField>("asset-search");
                search.value = "not present";
                typeof(LockBrowserToolkit).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true });
                Assert.That(list.itemsSource.Count, Is.EqualTo(0));
                search.value = "another-user";
                typeof(LockBrowserToolkit).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true });
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                search.value = "AssetLockWarning-*.BIN";
                typeof(LockBrowserToolkit).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true });
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                search.value = "Assets\\*.bin";
                typeof(LockBrowserToolkit).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true });
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                search.value = "*other-user";
                typeof(LockBrowserToolkit).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true });
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                search.value = "Warning-*.bin";
                typeof(LockBrowserToolkit).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true });
                Assert.That(list.itemsSource.Count, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void LegacyBrowserSearchSupportsNamesAndPathsWithWildcards()
        {
            var window = ScriptableObject.CreateInstance<LockBrowser>();
            try
            {
                var match = typeof(LockBrowser).GetMethod("IsFileMatching", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That((bool)match.Invoke(window, new object[] { m_file, "AssetLockWarning-*.BIN" }), Is.True);
                Assert.That((bool)match.Invoke(window, new object[] { m_file, "Assets/*.bin" }), Is.True);
                Assert.That((bool)match.Invoke(window, new object[] { m_file, "Warning-*.bin" }), Is.False);
                Assert.That((bool)match.Invoke(window, new object[] { m_file, "warning" }), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }
    }
}
