using System;
using System.IO;
using AssetLock.Editor.Data;
using NUnit.Framework;

namespace AssetLock.Editor.Tests
{
	[TestFixture, Category("AssetLockUnit")]
	internal class PushUnlockTests : ManagerFixture
	{
		[Test]
		public void MatchesOnlyConfiguredDestinationBranchesAndIgnoresDeletesAndTags()
		{
			const string input = "refs/heads/topic abc123 refs/heads/main 000000\n" +
				"refs/heads/master abc123 refs/heads/release 123456\n" +
				"(delete) 000000 refs/heads/master abc123\n" +
				"refs/tags/main abc123 refs/tags/main 000000\n" +
				"refs/heads/topic abc123 refs/heads/main 123456\n";
			Assert.That(
				PushUnlockPrompt.MatchingBranches(input, new[] { "main", "master" }),
				Is.EqualTo(new[] { "main" })
			);
			Assert.That(PushUnlockPrompt.MatchingBranches(input, new[] { "Main" }), Is.Empty);
		}

		[Test]
		public void SuppressingFromKeepLocksNeverQueuesUnlock()
		{
			Initialize();
			var file = FileReference.FromPath("Assets/push-test.bin");
			Manager.TestRepo[file] = file.ToLock(true, "lock-id", "tester");
			AssetLockSettings.SuppressPushUnlockPrompts.SetValue(false);

			try
			{
				PushUnlockWindow.ApplyChoice(true, false, Manager);
				Assert.That(AssetLockSettings.SuppressPushUnlockPrompts.value, Is.True);
				Assert.That(Manager.HasPendingCommands, Is.False);
				Assert.That(Manager.TestRepo[file].LockedByMe, Is.True);
			}
			finally
			{
				AssetLockSettings.SuppressPushUnlockPrompts.SetValue(false);
			}
		}

		[Test]
		public void ExplicitUnlockQueuesOnlyOwnedLocksEvenWhenSuppressingFuturePrompts()
		{
			Initialize();
			var mine = FileReference.FromPath("Assets/PushMine-" + Guid.NewGuid().ToString("N") + ".bin");
			var theirs = FileReference.FromPath("Assets/PushTheirs-" + Guid.NewGuid().ToString("N") + ".bin");
			File.WriteAllText(mine.AbsolutePath, "test");
			File.WriteAllText(theirs.AbsolutePath, "test");
			Manager.TestRepo[mine] = mine.ToLock(true, "mine", "tester");
			Manager.TestRepo[theirs] = theirs.ToLock(true, "theirs", "someone-else");

			try
			{
				Lfs.Run = args => FakeProcess.Result(args, args[0] == "locks" ? "[]" : "");
				PushUnlockWindow.ApplyChoice(true, true, Manager);
				Assert.That(Manager.HasPendingCommands, Is.True);
				Assert.That(AssetLockSettings.SuppressPushUnlockPrompts.value, Is.True);
				Manager.InnerLoopAsync().GetAwaiter().GetResult();
				var calls = Lfs.Calls.FindAll(call => call[0] == "unlock");
				Assert.That(calls, Has.Count.EqualTo(1));
				Assert.That(calls[0], Does.Contain(mine.AsProcessArg()));
			}
			finally
			{
				AssetLockSettings.SuppressPushUnlockPrompts.SetValue(false);
				File.Delete(mine.AbsolutePath);
				File.Delete(theirs.AbsolutePath);
			}
		}

		[Test]
		public void RefusesToRemoveAModifiedHookAndPreservesOriginal()
		{
			var directory = Path.Combine(Path.GetTempPath(), "AssetLockHook-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var hook = Path.Combine(directory, "pre-push");

			try
			{
				File.WriteAllText(hook, "#!/bin/sh\nexit 0\n");
				PushUnlockPrompt.Install(hook, directory);
				File.AppendAllText(hook, "# user change\n");
				Assert.Throws<InvalidOperationException>(() => PushUnlockPrompt.Remove(hook));
				Assert.That(File.ReadAllText(hook), Does.Contain("user change"));
				Assert.That(File.ReadAllText(hook + ".assetlock-original"), Is.EqualTo("#!/bin/sh\nexit 0\n"));
			}
			finally
			{
				Directory.Delete(directory, true);
			}
		}
	}
}