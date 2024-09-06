using System;
using System.Threading.Tasks;
using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEngine;
using static AssetLock.Editor.AssetLockUtility;
using static AssetLock.Editor.AssetLockSettings;
using static AssetLock.Editor.UI.ALGUI;
using static UnityEditor.EditorGUILayout;
using static UnityEditor.EditorStyles;

namespace AssetLock.Editor.UI
{
	public class LockBrowser : EditorWindow
	{
		private static bool s_busy;

		private string m_searchInput = string.Empty;

		[MenuItem(Constants.BROWSER_MENU_PATH)]
		private static void ShowWindow()
		{
			var window = GetWindow<LockBrowser>();
			window.titleContent = EditorGUIUtility.TrTextContentWithIcon(
				Constants.BROWSER_TITLE,
				Constants.BROWSER_ICON
			);
			window.Show();
		}

		private void OnGUI()
		{
			InfoGUI();
			ActionsGUI();
			Separator();
			FilesGUI();
		}

		private void InfoGUI()
		{
			GUIContent labelContent = new GUIContent("File Count:");
			GUIContent trackedCount = new GUIContent(
				AssetLockManager.Instance.TrackedCount.ToString(),
				"Total number of files being tracked by Asset Lock"
			);
			GUIContent lockedCount = new GUIContent(
				AssetLockManager.Instance.LockedCount.ToString(),
				"Total number of files currently locked by Asset Lock"
			);
			GUIContent userLockCount = new GUIContent(
				AssetLockManager.Instance.LockedByMeCount.ToString(),
				"Total number of files locked by the current user"
			);
			GUIContent otherLockCount = new GUIContent(
				AssetLockManager.Instance.LockedByOthersCount.ToString(),
				"Total number of files locked by other users"
			);
			GUIContent unlockedCount = new GUIContent(
				AssetLockManager.Instance.UnlockedCount.ToString(),
				"Total number of files that are not locked"
			);

			GUIContent statusLabel = new GUIContent("Master Status:", "Enable or disable all Asset Lock features");
			GUIContent autoLockLabel = new GUIContent("Auto-Lock:", "Automatically lock files when opening them");

			GUILayout.Label("Info:", boldLabel);
			EditorGUI.indentLevel++;

			LabelAndToggleButton(statusLabel, MasterEnable);
			LabelAndToggleButton(autoLockLabel, AutoLock);

			using (new HorizontalScope())
			{
				Indent();
				GUILayout.Label(labelContent);
				GUILayout.FlexibleSpace();
				GUILayout.Label(lockedCount);
				GUILayout.Label("(");
				GUILayout.Label(userLockCount, new GUIStyle(label) { normal = { textColor = Color.green } });
				GUILayout.Label(" / ");
				GUILayout.Label(otherLockCount, new GUIStyle(label) { normal = { textColor = Color.red } });
				GUILayout.Label(") / ");
				GUILayout.Label(unlockedCount, new GUIStyle(label) { normal = { textColor = Color.blue } });
				GUILayout.Label(" / ");
				GUILayout.Label(trackedCount, new GUIStyle(label) { normal = { textColor = Color.yellow } });
			}

			EditorGUI.indentLevel--;
		}

		private void ActionsGUI()
		{
			GUIContent refreshLabel = new GUIContent("Refresh All", "Refresh the lock status of all tracked files");
			GUIContent parseLabel = new GUIContent("Parse All", "Parse all files in the project for tracking");
			GUIContent unlockAllLabel = new GUIContent("Unlock All", "Unlock all files locked by you");

			GUILayout.Label("Actions:", boldLabel);
			EditorGUI.BeginDisabledGroup(s_busy);

			using (new HorizontalScope())
			{
				if (GUILayout.Button(refreshLabel))
				{
					DoAsync(() => AssetLockManager.Instance.RefreshAsync());
				}

				if (GUILayout.Button(parseLabel))
				{
					DoAsync(() => AssetLockManager.Instance.ParseAllAsync());
				}

				if (GUILayout.Button(unlockAllLabel))
				{
					AssetLockManager.Instance.UnlockAll();
				}
			}

			EditorGUI.EndDisabledGroup();
		}

		private void FilesGUI()
		{
			m_searchInput = TextField("Search: ",m_searchInput);

			EditorGUI.indentLevel++;

			foreach (var info in AssetLockManager.Instance.Repo.Values)
			{
				DisplayLock(info, m_searchInput);
			}

			EditorGUI.indentLevel--;
		}

		private static async void DoAsync(Func<Task> action)
		{
			s_busy = true;
			await action();
			s_busy = false;
		}

		private static void DisplayLock(LockInfo info, string ctx)
		{
			if (!MatchSearchGroups(ctx, info.path))
			{
				return;
			}

			if (!info.HasValue)
			{
				return;
			}

			GUIContent nameLabel = new GUIContent(info.name, info.path);
			GUIContent refreshLabel = new GUIContent("Refresh", "Refresh the lock status of this file");
			GUIContent lockLabel = new GUIContent("Lock", "Lock this file");
			GUIContent unlockLabel = new GUIContent("Unlock", "Unlock this file");
			GUIContent lockedLabel = new GUIContent("Locked", "This file is locked");
			GUIContent ownerLabel = new GUIContent(info.owner, info.lockedAt);

			using (new HorizontalScope())
			{
				Indent();

				if (GUILayout.Button(nameLabel, label))
				{
					Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(info.path);
				}

				GUILayout.FlexibleSpace();

				if (info.locked)
				{
					GUILayout.Label(lockedLabel, new GUIStyle(label) { normal = { textColor = Color.red } });
					GUILayout.FlexibleSpace();

					if (info.LockedByMe)
					{
						if (GUILayout.Button(unlockLabel))
						{
							AssetLockManager.Instance.UnlockFile(info);
						}
					}
					else
					{
						GUILayout.Label(ownerLabel);
						GUILayout.FlexibleSpace();
					}
				}
				else
				{
					if (GUILayout.Button(lockLabel))
					{
						AssetLockManager.Instance.LockFile(info);
					}
				}

				if (GUILayout.Button(refreshLabel))
				{
					AssetLockManager.Instance.RefreshFile(info);
				}
			}
		}
	}
}