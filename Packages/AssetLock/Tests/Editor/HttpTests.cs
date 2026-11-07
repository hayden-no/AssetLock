using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using AssetLock.Editor.Manager;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Tests
{
    [TestFixture, Category("AssetLockUnit")]
    internal class HttpTests : ManagerFixture
    {
        [Test]
        public void GitHubAuthorizationHasOneBearerPrefixAndLogsAreRedacted()
        {
            GitHubRemoteAuthToken.SetValue("secret-token");
            using var request = UnityWebRequest.Get("https://github.com/example/locks?token=secret-query");
            Manager.AppendHeaders(request);
            Assert.That(request.GetRequestHeader("Authorization"), Is.EqualTo("Bearer secret-token"));
            var message = AssetLock.Editor.Manager.AssetLockManager.GetWebRequestLogMessage(request);
            Assert.That(message, Does.Not.Contain("secret-token"));
            Assert.That(message, Does.Not.Contain("secret-query"));
            Assert.That(message, Does.Contain("[redacted]"));
        }

        [Test]
        public void GitLabAndGenericCredentialsUseBasicAuthorization()
        {
            GitLabRemoteUsername.SetValue("gitlab-user");
            GitLabRemoteAuthToken.SetValue("gitlab-token");
            CredentialsUsername.SetValue("user");
            CredentialsPassword.SetValue("password");
            using var gitlab = UnityWebRequest.Get("https://gitlab.com/example/locks");
            using var generic = UnityWebRequest.Get("https://example.test/locks");
            Manager.AppendHeaders(gitlab);
            Manager.AppendHeaders(generic);
            Assert.That(gitlab.GetRequestHeader("Authorization"), Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("gitlab-user:gitlab-token"))));
            Assert.That(generic.GetRequestHeader("Authorization"), Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("user:password"))));
        }

        [Test]
        public void ProviderNamesInUnrelatedHostsDoNotSelectProviderCredentials()
        {
            Assert.That(AssetLockUtility.IsGitHubUrl("https://github.com.example.test/locks"), Is.False);
            Assert.That(AssetLockUtility.IsGitLabUrl("https://example.test/locks?next=gitlab.com"), Is.False);
            Assert.That(AssetLockUtility.IsGitHubUrl("https://api.github.com/locks"), Is.True);
        }

        [Test]
        public void CreateAndUnlockSendExpectedRequests()
        {
            GitLfsServerLocksApiUrl.SetValue("https://example.test/locks");
            var requests = new List<(string url, string body)>();
            Manager.HttpTransport = request =>
            {
                Assert.That(request.timeout, Is.EqualTo(30));
                requests.Add((request.url, Encoding.UTF8.GetString(request.uploadHandler.data)));
                var json = "{\"lock\":{\"id\":\"lock-id\",\"path\":\"Assets/example.asset\",\"owner\":{\"name\":\"tester\"}}}";
                return Task.FromResult(new HttpResult(requests.Count == 1 ? 201 : 200, json));
            };
            var created = Manager.CreateLockHttp(FileReference.FromPath("Assets/example.asset"), "feature/test").GetAwaiter().GetResult();
            Assert.That(created.success, Is.True);
            Assert.That(created.info.lockId, Is.EqualTo("lock-id"));
            var deleted = Manager.DeleteLockHttp(created.info, true, "feature/test").GetAwaiter().GetResult();
            Assert.That(deleted.success, Is.True);
            Assert.That(requests[0].body, Does.Contain("Assets/example.asset").And.Contain("feature/test"));
            Assert.That(requests[1].url, Does.EndWith("/lock-id/unlock"));
            Assert.That(requests[1].body, Does.Contain("\"force\":true").And.Contain("feature/test"));
        }

        [TestCase(403)]
        [TestCase(409)]
        [TestCase(500)]
        public void LockErrorsReturnFailure(int status)
        {
            GitLfsServerLocksApiUrl.SetValue("https://example.test/locks");
            Manager.HttpTransport = _ => Task.FromResult(new HttpResult(status, "{\"message\":\"already locked\"}"));
            LogAssert.Expect(LogType.Error, new Regex("Failed to lock file"));
            var result = Manager.CreateLockHttp(FileReference.FromPath("Assets/example.asset")).GetAwaiter().GetResult();
            Assert.That(result.success, Is.False);
        }

        [TestCase("not-json")]
        [TestCase("{}")]
        public void MalformedListDoesNotClearCachedLocks(string body)
        {
            Initialize();
            UseHttp.SetValue(true);
            GitLfsServerLocksApiUrl.SetValue("https://example.test/locks");
            var file = FileReference.FromPath("Assets/example.asset");
            Manager.TestRepo[file] = new LockInfo { path = "Assets/example.asset", locked = true, owner = "other" };
            Manager.HttpTransport = _ => Task.FromResult(new HttpResult(200, body));
            LogAssert.Expect(LogType.Error, "[AssetLock] [HTTP] Invalid list locks response.");
            Assert.Throws<InvalidOperationException>(() => Manager.InnerLoopAsync().GetAwaiter().GetResult());
            Assert.That(Manager.TestRepo[file].locked, Is.True);
        }

        [Test]
        public void TransportFailurePreservesLocksAndAllowsAnotherRefresh()
        {
            Initialize();
            UseHttp.SetValue(true);
            GitLfsServerLocksApiUrl.SetValue("https://example.test/locks");
            var file = FileReference.FromPath("Assets/example.asset");
            Manager.TestRepo[file] = new LockInfo { path = "Assets/example.asset", locked = true, owner = "other" };
            Manager.HttpTransport = _ => Task.FromException<HttpResult>(new TimeoutException());
            Assert.Throws<TimeoutException>(() => Manager.InnerLoopAsync().GetAwaiter().GetResult());
            Assert.That(Manager.TestRepo[file].locked, Is.True);
            Manager.HttpTransport = _ => Task.FromResult(new HttpResult(200, "{\"locks\":[]}"));
            Assert.DoesNotThrow(() => Manager.InnerLoopAsync().GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator ListsAllPagesUsingUnityWebRequest()
        {
            Initialize();
            UseHttp.SetValue(true);
            using var server = new LocalServer((200, "{\"locks\":[],\"next_cursor\":\"a+b\"}"), (200, "{\"locks\":[]}"));
            GitLfsServerLocksApiUrl.SetValue(server.Url);
            var task = Manager.InnerLoopAsync();
            yield return WaitFor(task);
            task.GetAwaiter().GetResult();
            Assert.That(server.Requests, Has.Count.EqualTo(2));
            Assert.That(server.Requests[1], Does.Contain("cursor=a%2Bb"));
        }

        [UnityTest]
        public IEnumerator FailedRefreshPreservesCachedLocks()
        {
            Initialize();
            UseHttp.SetValue(true);
            var file = FileReference.FromPath("Assets/example.asset");
            Manager.TestRepo[file] = new LockInfo { path = "Assets/example.asset", locked = true, owner = "other" };
            using var server = new LocalServer((403, "{\"message\":\"forbidden\"}"));
            GitLfsServerLocksApiUrl.SetValue(server.Url);
            LogAssert.Expect(LogType.Error, new Regex("Failed to list locks"));
            var task = Manager.InnerLoopAsync();
            yield return WaitFor(task);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.That(Manager.TestRepo[file].locked, Is.True);
        }

        private static IEnumerator WaitFor(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.That(task.IsCompleted, Is.True, "HTTP operation timed out");
        }

        private sealed class LocalServer : IDisposable
        {
            private readonly TcpListener m_listener;
            private readonly Task m_task;
            internal readonly List<string> Requests = new();
            internal string Url { get; }

            internal LocalServer(params (int status, string body)[] responses)
            {
                m_listener = new TcpListener(IPAddress.Loopback, 0);
                m_listener.Start();
                Url = $"http://127.0.0.1:{((IPEndPoint)m_listener.LocalEndpoint).Port}/locks";
                m_task = Task.Run(() => Serve(responses));
            }

            private void Serve((int status, string body)[] responses)
            {
                foreach (var response in responses)
                {
                    using var client = m_listener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    Requests.Add(reader.ReadLine());
                    string line;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine())) { }
                    var body = Encoding.UTF8.GetBytes(response.body);
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {response.status} {(response.status == 200 ? "OK" : "Forbidden")}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    stream.Write(body, 0, body.Length);
                }
            }

            public void Dispose()
            {
                m_listener.Stop();
                try { m_task.Wait(1000); } catch (AggregateException) { }
            }
        }
    }
}
