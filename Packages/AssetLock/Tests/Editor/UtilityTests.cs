using System;
using System.Collections.Generic;
using System.IO;
using AssetLock.Editor.Data;
using NUnit.Framework;
using UnityEngine;

namespace AssetLock.Editor.Tests
{
    [TestFixture, Category("AssetLockUnit")]
    internal class UtilityTests : ManagerFixture
    {
        [TestCase("Assets/example.asset")]
        [TestCase("Assets/a folder/example.asset")]
        [TestCase("Packages/com.example/file.asset")]
        public void ResolvesProjectPathsBeforeInitialization(string path)
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            Assert.That(AssetLockUtility.GetFullPath(path), Is.EqualTo(Path.GetFullPath(Path.Combine(root, path))));
        }

        [Test]
        public void AbsolutePathAndMetaAreNormalized()
        {
            var absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "one.asset"));
            Assert.That(AssetLockUtility.GetFullPath(absolute), Is.EqualTo(Path.GetFullPath(absolute)));
            Assert.That(FileReference.FromPath(absolute + ".meta").AbsolutePath, Is.EqualTo(absolute));
            Assert.That(FileReference.FromPath(absolute + ".meta", true).IsMeta, Is.True);
        }

        [TestCase("LocalWorkingDir=C:\\runner work\\repo\r\nOther=value", "C:\\runner work\\repo")]
        [TestCase("LocalWorkingDir=C:\\runner work\\repo", "C:\\runner work\\repo")]
        public void ParsesWorkingDirectoryIncludingSpaces(string input, string expected)
        {
            Assert.That(AssetLockUtility.TryGetLfsWorkingDirFromEnv(input, out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void MissingWorkingDirectoryIsRejected()
        {
            Assert.That(AssetLockUtility.TryGetLfsWorkingDirFromEnv("Other=value", out _), Is.False);
        }

        [Test]
        public void EndpointMayBeLastLine()
        {
            Assert.That(AssetLockUtility.TryGetLfsEndpointFromEnv("Endpoint=https://example.test/info/lfs", out var endpoint), Is.True);
            Assert.That(endpoint, Is.EqualTo("https://example.test/info/lfs"));
        }

        [Test]
        public void EncodesListQueryParameters()
        {
            var request = new ListLocksRequest { Path = "Assets/a & b.asset", Cursor = "a+b/", Ref = new GitLfsRefObject { Name = "feature/a b" }, Limit = 2 };
            var uri = new Uri(request.ToURI("https://example.test/locks"));
            Assert.That(uri.Query, Does.Contain("path=Assets%2Fa%20%26%20b.asset"));
            Assert.That(uri.Query, Does.Contain("cursor=a%2Bb%2F"));
            Assert.That(uri.Query, Does.Contain("refspec=feature%2Fa%20b"));
        }

        [Test]
        public void TracksOnlyConfiguredExistingFiles()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                Assert.That(AssetLockUtility.ShouldTrack(FileReference.FromPath(path)), Is.True);
                File.Delete(path);
                Assert.That(AssetLockUtility.ShouldTrack(FileReference.FromPath(path)), Is.False);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void BinaryDetectionIncludesNullBytesAndHonorsTheReadLimit()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
            try
            {
                File.WriteAllBytes(path, new byte[] { 65, 66, 67, 68, 0 });
                var file = FileReference.FromPath(path);
                Assert.That(AssetLockUtility.IsBinary(file, 4), Is.False);
                Assert.That(AssetLockUtility.IsBinary(file, 5), Is.True);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
