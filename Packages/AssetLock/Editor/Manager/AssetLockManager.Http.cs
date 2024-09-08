using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

		private async Task<UnityWebRequest> SendWebRequest(UnityWebRequest request)
		{
			var operation = request.SendWebRequest();

			while (!operation.isDone)
			{
				await Task.Yield();
			}

			return operation.webRequest;
		}
		
		private void AppendHeaders(UnityWebRequest request)
		{
			request.SetRequestHeader("Accept", CONTENT_TYPE);
			request.SetRequestHeader("Authorization", $"Bearer {GitRemoteAuthToken.value}");
		}

		private async Task<(bool, LockInfo)> CreateLockHttp(FileReference file, string serverRef = null)
		{
			var request = UnityWebRequest.Post(GitLfsServerLocksApiUrl, GetPostData(), CONTENT_TYPE);
			AppendHeaders(request);
			Logging.LogVerboseFormat("[HTTP] Sending lock request: {0}", GetWebRequestLogMessage(request));
			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendWebRequest(request);
			var result = (false, default(LockInfo));

			if (webResult.responseCode == HTTP_CREATED)
			{
				LockSuccessResponse response =
					JsonConvert.DeserializeObject<LockSuccessResponse>(webResult.downloadHandler.text);
				result = (true, response.Lock);

				goto finalize;
			}

			if (webResult.responseCode == HTTP_CONFILCT)
			{
				LockExistsResponse response =
					JsonConvert.DeserializeObject<LockExistsResponse>(webResult.downloadHandler.text);
				Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

				goto finalize;
			}

			if (webResult.responseCode == HTTP_FORBIDDEN)
			{
				LockUnauthorizedResponse response =
					JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.downloadHandler.text);
				Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

				goto finalize;
			}

			if (webResult.responseCode == HTTP_ERROR)
			{
				LockErrorResponse response =
					JsonConvert.DeserializeObject<LockErrorResponse>(webResult.downloadHandler.text);
				Logging.LogErrorFormat("[HTTP] Failed to lock file {0}: {1}", file.UnityPath, response);

				goto finalize;
			}

			// else
			HandleUnknownError(webResult);

		finalize:
			Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetWebRequestLogMessage(webResult));
			webResult.Dispose();

			return result;

			string GetPostData()
			{
				var data = new LockRequest { Path = file.GitPath, };

				if (serverRef != null)
				{
					data.Ref = new GitLfsRefObject { Name = serverRef };
				}

				return JsonConvert.SerializeObject(data);
			}
		}

		private async Task<(bool, string, IEnumerable<LockInfo>)> ListLocksHttp(ListLocksRequest requestData)
		{
			var request = UnityWebRequest.Get(requestData.ToURI(GitLfsServerLocksApiUrl));
			AppendHeaders(request);
			Logging.LogVerboseFormat("[HTTP] Sending list request: {0}", GetWebRequestLogMessage(request));
			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendWebRequest(request);
			var result = (false, default(string), default(IEnumerable<LockInfo>));

			if (webResult.responseCode == HTTP_OK)
			{
				ListLocksSuccessResponse response =
					JsonConvert.DeserializeObject<ListLocksSuccessResponse>(webResult.downloadHandler.text);
				result = (true, response.NextCursor, response.Locks.Select(l => (LockInfo)l));

				goto finalize;
			}

			if (webResult.responseCode == HTTP_FORBIDDEN)
			{
				LockUnauthorizedResponse response =
					JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.downloadHandler.text);
				Logging.LogErrorFormat("[HTTP] Failed to list locks: {0}", response);

				goto finalize;
			}

			if (webResult.responseCode == HTTP_ERROR)
			{
				LockErrorResponse response =
					JsonConvert.DeserializeObject<LockErrorResponse>(webResult.downloadHandler.text);
				Logging.LogErrorFormat("[HTTP] Failed to list locks: {0}", response);

				goto finalize;
			}

			// else
			HandleUnknownError(webResult);

		finalize:
			Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetWebRequestLogMessage(webResult));
			webResult.Dispose();

			return result;
		}

		private async Task<(bool, LockInfo)> DeleteLockHttp(LockInfo info, bool force = false, string refspec = null)
		{
			var request = UnityWebRequest.Post(GetDeleteUrl(), GetPostData(), CONTENT_TYPE);
			AppendHeaders(request);
			Logging.LogVerboseFormat("[HTTP] Sending unlock request: {0}", GetWebRequestLogMessage(request));
			request.downloadHandler = new DownloadHandlerBuffer();
			var webResult = await SendWebRequest(request);
			var result = (false, default(LockInfo));

			if (webResult.responseCode == HTTP_OK)
			{
				var response = JsonConvert.DeserializeObject<LockSuccessResponse>(webResult.downloadHandler.text);
				result = (true, response.Lock);
				
				goto finalize;
			}
			
			if (webResult.responseCode == HTTP_FORBIDDEN)
			{
				LockUnauthorizedResponse response =
					JsonConvert.DeserializeObject<LockUnauthorizedResponse>(webResult.downloadHandler.text);
				Logging.LogErrorFormat("[HTTP] Failed to unlock file {0}: {1}", info.path, response);

				goto finalize;
			}
			
			if (webResult.responseCode == HTTP_ERROR)
			{
				LockErrorResponse response =
					JsonConvert.DeserializeObject<LockErrorResponse>(webResult.downloadHandler.text);
				Logging.LogErrorFormat("[HTTP] Failed to unlock file {0}: {1}", info.path, response);

				goto finalize;
			}
			
			// else
			HandleUnknownError(webResult);

		finalize:
			Logging.LogVerboseFormat("[HTTP] Received response: {0}", GetWebRequestLogMessage(webResult));
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

		[JsonObject]
		private struct LockRequest
		{
			[JsonProperty("path")] public string Path;
			[JsonProperty("ref")] public GitLfsRefObject Ref;

			public override string ToString()
			{
				return Path;
			}
		}

		[JsonObject]
		private struct GitLfsRefObject
		{
			[JsonProperty("name")] public string Name;

			public override string ToString()
			{
				return Name;
			}
		}

		[JsonObject]
		private struct LockSuccessResponse
		{
			[JsonProperty("lock")] public LocksResponseDataJson Lock;

			public override string ToString()
			{
				return Lock.ToString();
			}
		}

		[JsonObject]
		private struct LockExistsResponse
		{
			[JsonProperty("lock")] public LocksResponseDataJson Lock;

			[JsonProperty("message")] public string Message;
			[JsonProperty("documentation_url")] public string DocumentationUrl;
			[JsonProperty("request_id")] public string RequestId;

			public override string ToString()
			{
				return $"{RequestId}: {Message} ({DocumentationUrl})";
			}
		}

		[JsonObject]
		private struct LockUnauthorizedResponse
		{
			[JsonProperty("message")] public string Message;
			[JsonProperty("documentation_url")] public string DocumentationUrl;
			[JsonProperty("request_id")] public string RequestId;
			
			public override string ToString()
			{
				return $"{RequestId}: {Message} ({DocumentationUrl})";
			}
		}

		[JsonObject]
		private struct LockErrorResponse
		{
			[JsonProperty("message")] public string Message;
			[JsonProperty("documentation_url")] public string DocumentationUrl;
			[JsonProperty("request_id")] public string RequestId;
			
			public override string ToString()
			{
				return $"{RequestId}: {Message} ({DocumentationUrl})";
			}
		}

		[JsonObject]
		private struct ListLocksRequest
		{
			[JsonProperty("path")] public string Path;

			[JsonProperty("id")] public string Id;

			[JsonProperty("cursor")] public string Cursor;

			[JsonProperty("limit")] public int Limit;

			[JsonProperty("ref")] public GitLfsRefObject Ref;

			public override string ToString()
			{
				return $"{Id}: {Path}";
			}

			public string ToURI(string baseUrl)
			{
				const string pathKey = "path";
				const string idKey = "id";
				const string cursorKey = "cursor";
				const string limitKey = "limit";
				const string refKey = "refspec";

				StringBuilder sb = new();
				sb.Append(baseUrl);
				bool any = false;

				if (!string.IsNullOrWhiteSpace(Path))
				{
					sb.Append("?");
					any = true;

					sb.Append($"{pathKey}={Path}");
				}

				if (!string.IsNullOrWhiteSpace(Id))
				{
					if (!any)
					{
						sb.Append("?");
						any = true;
					}
					else
					{
						sb.Append("&");
					}

					sb.Append($"{idKey}={Id}");
				}

				if (!string.IsNullOrWhiteSpace(Cursor))
				{
					if (!any)
					{
						sb.Append("?");
						any = true;
					}
					else
					{
						sb.Append("&");
					}

					sb.Append($"{cursorKey}={Cursor}");
				}

				if (Limit > 0)
				{
					if (!any)
					{
						sb.Append("?");
						any = true;
					}
					else
					{
						sb.Append("&");
					}

					sb.Append($"{limitKey}={Limit}");
				}

				if (!string.IsNullOrWhiteSpace(Ref.Name))
				{
					if (!any)
					{
						sb.Append("?");
						any = true;
					}
					else
					{
						sb.Append("&");
					}

					sb.Append($"{refKey}={Ref.Name}");
				}

				return sb.ToString();
			}
		}

		[JsonObject]
		private struct ListLocksSuccessResponse
		{
			[JsonProperty("locks")] public LocksResponseDataJson[] Locks;

			[JsonProperty("next_cursor")] public string NextCursor;

			public override string ToString()
			{
				return $"{Locks.Length} locks";
			}
		}

		[JsonObject]
		// send to /locks/:id/unlock
		private struct DeleteLockRequest
		{
			[JsonProperty("force")] public bool Force;
			[JsonProperty("ref")] public GitLfsRefObject Ref;

			public override string ToString()
			{
				return $"Force: {Force}";
			}
		}
	}
}