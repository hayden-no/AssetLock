using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using Newtonsoft.Json;
using UnityEngine.Networking;
using static AssetLock.Editor.AssetLockUtility;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.Manager
{
	internal readonly struct HttpResult
	{
		internal readonly long StatusCode;
		internal readonly string Body;
		internal readonly string Error;
		internal HttpResult(long statusCode, string body, string error = null)
		{
			StatusCode = statusCode;
			Body = body;
			Error = error;
		}
	}

	public partial class AssetLockManager
	{
		const long HTTP_OK = 200;
		const long HTTP_CREATED = 201;
		const long HTTP_FORBIDDEN = 403;
		const long HTTP_NOT_FOUND = 404;
		const long HTTP_CONFILCT = 409;
		const long HTTP_ERROR = 500;

		const string CONTENT_TYPE = "application/vnd.git-lfs+json";

		internal Func<UnityWebRequest, Task<HttpResult>> HttpTransport { get; set; }

		private async Task<HttpResult> SendAsync(UnityWebRequest request)
		{
			request.timeout = 30;
			if (HttpTransport != null)
			{
				return await HttpTransport(request);
			}

			var operation = request.SendWebRequest();

			while (!operation.isDone)
			{
				await Task.Yield();
			}

			return new HttpResult(request.responseCode, request.downloadHandler?.text, request.error);
		}
		
		private string BearAuthHeader(string token)
		{
			return $"Bearer {token}";
		}
		
		private string BasicAuthHeader(string username, string password)
		{
			byte[] bytes = Encoding.ASCII.GetBytes($"{username}:{password}");

			return Convert.ToBase64String(bytes);
		}

		internal void AppendHeaders(UnityWebRequest request)
		{
			request.SetRequestHeader("Accept", CONTENT_TYPE);

			if (IsGitHubUrl(request.url))
			{
				request.SetRequestHeader("Authorization", BearAuthHeader(GitHubRemoteAuthToken));
			}
			else if (IsGitLabUrl(request.url))
			{
				request.SetRequestHeader("Authorization", $"Basic {BasicAuthHeader(GitLabRemoteUsername, GitLabRemoteAuthToken)}");
			}
			else
			{
				request.SetRequestHeader("Authorization", $"Basic {BasicAuthHeader(CredentialsUsername, CredentialsPassword)}");
			}
		}

		internal async Task<(bool success, LockInfo info)> CreateLockHttp(FileReference file, string serverRef = null)
		{
			using var request = UnityWebRequest.Post(GitLfsServerLocksApiUrl, GetPostData(), CONTENT_TYPE);
			AppendHeaders(request);

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Sending lock request: {0}", GetWebRequestLogMessage(request));
			}

			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendAsync(request);
			var result = (false, default(LockInfo));

			switch (webResult.StatusCode)
			{
				case HTTP_CREATED:
				{
					try
					{
						LockSuccessResponse response = JsonConvert.DeserializeObject<LockSuccessResponse>(webResult.Body);
						result = (true, response.Lock);
					}
					catch (JsonException)
					{
						Logging.LogError("[HTTP] Invalid lock response.");
					}

					goto finalize;
				}
				case HTTP_CONFILCT:
				{
					LockExistsResponse response =
						JsonConvert.DeserializeObject<LockExistsResponse>(webResult.Body);
					Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

					goto finalize;
				}
				case HTTP_FORBIDDEN:
				{
					LockUnauthorizedResponse response =
						JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.Body);
					Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

					goto finalize;
				}
				case HTTP_ERROR:
				{
					LockErrorResponse response =
						JsonConvert.DeserializeObject<LockErrorResponse>(webResult.Body);
					Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

					goto finalize;
				}
			}

			// else
			HandleUnknownError(request);

		finalize:

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetResponseLogMessage(webResult));
			}

			return result;

			string GetPostData()
			{
				var data = new LockRequest { Path = file.AsProcessArg(), };

				if (serverRef != null)
				{
					data.Ref = new GitLfsRefObject { Name = serverRef };
				}

				return JsonConvert.SerializeObject(data);
			}
		}

		internal async Task<(bool success, string nextCursor, IEnumerable<LockInfo> locks)> ListLocksHttp(ListLocksRequest requestData)
		{
			using var request = UnityWebRequest.Get(requestData.ToURI(GitLfsServerLocksApiUrl));
			AppendHeaders(request);

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Sending list request: {0}", GetWebRequestLogMessage(request));
			}

			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendAsync(request);
			var result = (false, default(string), default(IEnumerable<LockInfo>));

			switch (webResult.StatusCode)
			{
				case HTTP_OK:
				{
					try
					{
						ListLocksSuccessResponse response = JsonConvert.DeserializeObject<ListLocksSuccessResponse>(webResult.Body);
						if (response.Locks == null) throw new JsonSerializationException("Missing locks.");
						result = (true, response.NextCursor, response.Locks.Select(l => (LockInfo)l).ToArray());
					}
					catch (JsonException)
					{
						Logging.LogError("[HTTP] Invalid list locks response.");
					}

					goto finalize;
				}
				case HTTP_FORBIDDEN:
				{
					LockUnauthorizedResponse response =
						JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.Body);
					Logging.LogErrorFormat("[HTTP] Failed to list locks: {0}", response);

					goto finalize;
				}
				case HTTP_ERROR:
				{
					LockErrorResponse response =
						JsonConvert.DeserializeObject<LockErrorResponse>(webResult.Body);
					Logging.LogErrorFormat("[HTTP] Failed to list locks: {0}", response);

					goto finalize;
				}
			}

			// else
			HandleUnknownError(request);

		finalize:

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetResponseLogMessage(webResult));
			}

			return result;
		}

		internal async Task<(bool success, LockInfo info)> DeleteLockHttp(LockInfo info, bool force = false, string refspec = null)
		{
			using var request = UnityWebRequest.Post(GetDeleteUrl(), GetPostData(), CONTENT_TYPE);
			AppendHeaders(request);

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Sending unlock request: {0}", GetWebRequestLogMessage(request));
			}

			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendAsync(request);
			var result = (false, default(LockInfo));

			switch (webResult.StatusCode)
			{
				case HTTP_OK:
				{
					try
					{
						var response = JsonConvert.DeserializeObject<LockSuccessResponse>(webResult.Body);
						result = (true, response.Lock);
					}
					catch (JsonException)
					{
						Logging.LogError("[HTTP] Invalid unlock response.");
					}

					goto finalize;
				}
				case HTTP_FORBIDDEN:
				{
					LockUnauthorizedResponse response =
						JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.Body);
					Logging.LogErrorFormat("[HTTP] Failed to unlock file {0}: {1}", info.path, response);

					goto finalize;
				}
				case HTTP_ERROR:
				{
					LockErrorResponse response =
						JsonConvert.DeserializeObject<LockErrorResponse>(webResult.Body);
					Logging.LogErrorFormat("[HTTP] Failed to unlock file {0}: {1}", info.path, response);

					goto finalize;
				}
			}

			// else
			HandleUnknownError(request);

		finalize:

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetResponseLogMessage(webResult));
			}

			return result;

			string GetDeleteUrl() => $"{GitLfsServerLocksApiUrl.value}/{info.lockId}/unlock";

			string GetPostData()
			{
				var data = new DeleteLockRequest { Force = force, };

				if (refspec != null)
				{
					data.Ref = new GitLfsRefObject { Name = refspec };
				}

				return JsonConvert.SerializeObject(data);
			}
		}

		public async Task<(bool success, string err)> TestConnection()
		{
			using var request = UnityWebRequest.Get(GitLfsServerLocksApiUrl);
			AppendHeaders(request);

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Sending test request: {0}", GetWebRequestLogMessage(request));
			}

			var webResult = await SendAsync(request);
			var result = webResult.StatusCode == HTTP_OK;
			var err = result ? string.Empty : webResult.Error;

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetResponseLogMessage(webResult));
			}

			return (result, err);
		}

		private static void HandleUnknownError(UnityWebRequest request)
		{
			Logging.LogErrorFormat("[HTTP] Failed to access remote \n{0}\n", GetWebRequestLogMessage(request));
			Logging.LogWarning("Disabling HTTP mode.");
			UseHttp.SetValue(false, true);
		}

		private static string GetResponseLogMessage(HttpResult response) =>
			$"Code: {response.StatusCode}\nError: {(string.IsNullOrEmpty(response.Error) ? "none" : "[details omitted]")}";

		internal static string GetWebRequestLogMessage(UnityWebRequest request)
		{
			StringBuilder sb = new();
			sb.AppendLine("Request:");
			var uri = new UriBuilder(request.url) { UserName = "", Password = "", Query = "", Fragment = "" };
			sb.AppendLine($"\tURL: {uri.Uri.GetLeftPart(UriPartial.Path)}");
			sb.AppendLine($"\tMethod: {request.method}");
			sb.AppendLine("Request Headers:");
			sb.AppendLine($"\tAccept: {request.GetRequestHeader("Accept")}");
			sb.AppendLine("\tAuthorization: [redacted]");

			sb.AppendLine("Response:");

			if (request.responseCode != 0)
			{
				sb.AppendLine($"\tCode: {request.responseCode}");
			}

			if (!string.IsNullOrWhiteSpace(request.error))
			{
				sb.AppendLine("\tError: [details omitted]");
			}

			return sb.ToString();
		}
	}
}
