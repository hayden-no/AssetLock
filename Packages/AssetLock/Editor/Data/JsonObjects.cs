using System.Text;
using Newtonsoft.Json;

namespace AssetLock.Editor.Data
{
	[JsonObject]
	internal struct LocksResponseDataJson
	{
		[JsonProperty("id")] public string ID;

		[JsonProperty("path")] public string Path;

		[JsonProperty("locked_at")] public string LockedAt;

		[JsonProperty("owner")] public LocksResponseOwnerDataJson Owner;

		public string Name => this.Owner.Name;

		public static implicit operator LockInfo(LocksResponseDataJson data)
		{
			return FileReference.FromPath(data.Path).ToLock(true, data.ID, data.Name, data.LockedAt);
		}
	}

	[JsonObject]
	internal struct LocksResponseOwnerDataJson
	{
		[JsonProperty("name")] public string Name;
	}

	[JsonObject]
	internal struct LockRequest
	{
		[JsonProperty("path")] public string Path;
		[JsonProperty("ref")] public GitLfsRefObject Ref;

		public override string ToString()
		{
			return Path;
		}
	}

	[JsonObject]
	internal struct GitLfsRefObject
	{
		[JsonProperty("name")] public string Name;

		public override string ToString()
		{
			return Name;
		}
	}

	[JsonObject]
	internal struct LockSuccessResponse
	{
		[JsonProperty("lock")] public LocksResponseDataJson Lock;

		public override string ToString()
		{
			return Lock.ToString();
		}
	}

	[JsonObject]
	internal struct LockExistsResponse
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
	internal struct LockUnauthorizedResponse
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
	internal struct LockErrorResponse
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
	internal struct ListLocksRequest
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
	internal struct ListLocksSuccessResponse
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
	internal struct DeleteLockRequest
	{
		[JsonProperty("force")] public bool Force;
		[JsonProperty("ref")] public GitLfsRefObject Ref;

		public override string ToString()
		{
			return $"Force: {Force}";
		}
	}
}