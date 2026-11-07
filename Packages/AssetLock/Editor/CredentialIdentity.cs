using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using static AssetLock.Editor.AssetLockSettings;
using static AssetLock.Editor.AssetLockUtility;

namespace AssetLock.Editor
{
	// These are account references only. Passwords and tokens belong to Git's helper.
	internal sealed class CredentialIdentity
	{
		public string Name = "";
		public string Username = "";
		public string Authority = "";
	}

	internal static class CredentialIdentities
	{
		internal const string SharedKey = Constants.PACKAGE_NAME + ".SavedCredentialIdentities";
		private const string CurrentKey = "CurrentCredentialIdentity";

		private static UnityEditor.SettingsManagement.ISettingsRepository Repository =>
			UserSettings.GetRepository(SettingsScope.User);

		internal static CredentialIdentity Current
		{
			get => Read<CredentialIdentity>(ProjectUserKey(CurrentKey)) ?? new CredentialIdentity();
			set =>
				Repository.Set(
					ProjectUserKey(CurrentKey),
					JsonConvert.SerializeObject(value ?? new CredentialIdentity())
				);
		}

		internal static List<CredentialIdentity> Saved =>
			Read<List<CredentialIdentity>>(SharedKey) ?? new List<CredentialIdentity>();

		private static T Read<T>(string key)
			where T : class
		{
			var json = Repository.Get(key, string.Empty);

			if (string.IsNullOrEmpty(json)) return null;

			try
			{
				return JsonConvert.DeserializeObject<T>(json);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		internal static void SaveProfile(CredentialIdentity identity)
		{
			if (string.IsNullOrWhiteSpace(identity?.Name) ||
				string.IsNullOrWhiteSpace(identity.Username) ||
				string.IsNullOrWhiteSpace(identity.Authority))
				throw new InvalidOperationException(
					"Enter a profile name, account username, and a valid LFS server URL first."
				);

			var saved = Saved;
			var index = saved.FindIndex(p => string.Equals(p.Name, identity.Name, StringComparison.OrdinalIgnoreCase));
			if (index < 0)
				saved.Add(identity);
			else
				saved[index] = identity;
			Repository.Set(SharedKey, JsonConvert.SerializeObject(saved));
		}

		internal static void DeleteProfile(string name)
		{
			var saved = Saved;
			saved.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			Repository.Set(SharedKey, JsonConvert.SerializeObject(saved));
		}

		internal static string AuthorityOf(string url)
		{
			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
				(uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
				!string.IsNullOrEmpty(uri.UserInfo))
				return string.Empty;

			return uri.GetLeftPart(UriPartial.Authority);
		}
	}
}