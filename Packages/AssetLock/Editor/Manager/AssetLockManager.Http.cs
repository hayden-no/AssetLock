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
	public partial class AssetLockManager
	{
		const long HTTP_OK = 200;
		const long HTTP_CREATED = 201;
		const long HTTP_FORBIDDEN = 403;
		const long HTTP_NOT_FOUND = 404;
		const long HTTP_CONFILCT = 409;
		const long HTTP_ERROR = 500;

		const string CONTENT_TYPE = "application/vnd.git-lfs+json";

		private async Task<UnityWebRequest> SendAsync(UnityWebRequest request)
		{
			var operation = request.SendWebRequest();

			while (!operation.isDone)
			{
				await Task.Yield();
			}

			return operation.webRequest;
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

		private void AppendHeaders(UnityWebRequest request)
		{
			request.SetRequestHeader("Accept", CONTENT_TYPE);

			if (IsGitHubUrl(request.url))
			{
				request.SetRequestHeader("Authorization", $"Bearer {BearAuthHeader(GitHubRemoteAuthToken)}");
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

		private async Task<(bool success, LockInfo info)> CreateLockHttp(FileReference file, string serverRef = null)
		{
			var request = UnityWebRequest.Post(GitLfsServerLocksApiUrl, GetPostData(), CONTENT_TYPE);
			AppendHeaders(request);

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Sending lock request: {0}", GetWebRequestLogMessage(request));
			}

			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendAsync(request);
			var result = (false, default(LockInfo));

			switch (webResult.responseCode)
			{
				case HTTP_CREATED:
				{
					LockSuccessResponse response =
						JsonConvert.DeserializeObject<LockSuccessResponse>(webResult.downloadHandler.text);
					result = (true, response.Lock);

					goto finalize;
				}
				case HTTP_CONFILCT:
				{
					LockExistsResponse response =
						JsonConvert.DeserializeObject<LockExistsResponse>(webResult.downloadHandler.text);
					Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

					goto finalize;
				}
				case HTTP_FORBIDDEN:
				{
					LockUnauthorizedResponse response =
						JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.downloadHandler.text);
					Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

					goto finalize;
				}
				case HTTP_ERROR:
				{
					LockErrorResponse response =
						JsonConvert.DeserializeObject<LockErrorResponse>(webResult.downloadHandler.text);
					Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

					goto finalize;
				}
			}

			// else
			HandleUnknownError(webResult);

		finalize:

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetWebRequestLogMessage(webResult));
			}

			webResult.Dispose();

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

		private async Task<(bool success, string nextCursor, IEnumerable<LockInfo> locks)> ListLocksHttp(ListLocksRequest requestData)
		{
			var request = UnityWebRequest.Get(requestData.ToURI(GitLfsServerLocksApiUrl));
			AppendHeaders(request);

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Sending list request: {0}", GetWebRequestLogMessage(request));
			}

			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendAsync(request);
			var result = (false, default(string), default(IEnumerable<LockInfo>));

			switch (webResult.responseCode)
			{
				case HTTP_OK:
				{
					ListLocksSuccessResponse response =
						JsonConvert.DeserializeObject<ListLocksSuccessResponse>(webResult.downloadHandler.text);
					result = (true, response.NextCursor, response.Locks.Select(l => (LockInfo)l));

					goto finalize;
				}
				case HTTP_FORBIDDEN:
				{
					LockUnauthorizedResponse response =
						JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.downloadHandler.text);
					Logging.LogErrorFormat("[HTTP] Failed to list locks: {0}", response);

					goto finalize;
				}
				case HTTP_ERROR:
				{
					LockErrorResponse response =
						JsonConvert.DeserializeObject<LockErrorResponse>(webResult.downloadHandler.text);
					Logging.LogErrorFormat("[HTTP] Failed to list locks: {0}", response);

					goto finalize;
				}
			}

			// else
			HandleUnknownError(webResult);

		finalize:

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetWebRequestLogMessage(webResult));
			}

			webResult.Dispose();

			return result;
		}

		private async Task<(bool success, LockInfo info)> DeleteLockHttp(LockInfo info, bool force = false, string refspec = null)
		{
			var request = UnityWebRequest.Post(GetDeleteUrl(), GetPostData(), CONTENT_TYPE);
			AppendHeaders(request);

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Sending unlock request: {0}", GetWebRequestLogMessage(request));
			}

			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendAsync(request);
			var result = (false, default(LockInfo));

			switch (webResult.responseCode)
			{
				case HTTP_OK:
				{
					var response = JsonConvert.DeserializeObject<LockSuccessResponse>(webResult.downloadHandler.text);
					result = (true, response.Lock);

					goto finalize;
				}
				case HTTP_FORBIDDEN:
				{
					LockUnauthorizedResponse response =
						JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.downloadHandler.text);
					Logging.LogErrorFormat("[HTTP] Failed to unlock file {0}: {1}", info.path, response);

					goto finalize;
				}
				case HTTP_ERROR:
				{
					LockErrorResponse response =
						JsonConvert.DeserializeObject<LockErrorResponse>(webResult.downloadHandler.text);
					Logging.LogErrorFormat("[HTTP] Failed to unlock file {0}: {1}", info.path, response);

					goto finalize;
				}
			}

			// else
			HandleUnknownError(webResult);

		finalize:

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetWebRequestLogMessage(webResult));
			}

			webResult.Dispose();

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
			var request = UnityWebRequest.Get(GitLfsServerLocksApiUrl);
			AppendHeaders(request);

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Sending test request: {0}", GetWebRequestLogMessage(request));
			}

			var webResult = await SendAsync(request);
			var result = webResult.responseCode == HTTP_OK;
			var err = result ? string.Empty : webResult.error;

			if (LogHttp)
			{
				Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetWebRequestLogMessage(webResult));
			}

			webResult.Dispose();

			return (result, err);
		}

		private static void HandleUnknownError(UnityWebRequest request)
		{
			Logging.LogErrorFormat("[HTTP] Failed to access remote \n{0}\n", GetWebRequestLogMessage(request));
			Logging.LogWarning("Disabling HTTP mode.");
			UseHttp.SetValue(false, true);
		}

		private static string GetWebRequestLogMessage(UnityWebRequest request)
		{
			StringBuilder sb = new();
			sb.AppendLine("Request:");
			sb.AppendLine($"\tURL: {request.url}");
			sb.AppendLine($"\tMethod: {request.method}");
			sb.AppendLine("Request Headers:");
			sb.AppendLine($"\tAccept: {request.GetRequestHeader("Accept")}");
			sb.AppendLine($"\tAuthorization: {request.GetRequestHeader("Authorization")}");

			sb.AppendLine("Response Headers:");

			foreach ((var key, var value) in request.GetResponseHeaders() ?? new Dictionary<string, string>())
			{
				sb.AppendLine($"\t{key}: {value}");
			}

			sb.AppendLine("Response:");

			if (request.responseCode != 0)
			{
				sb.AppendLine($"\tCode: {request.responseCode}");
			}

			if (!string.IsNullOrWhiteSpace(request.error))
			{
				sb.AppendLine($"\tError: {request.error}");
			}

			if (!string.IsNullOrWhiteSpace(request.downloadHandler.text))
			{
				sb.AppendLine($"\tData: {request.downloadHandler.text}");
			}

			return sb.ToString();
		}
	}
}