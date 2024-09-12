using System.IO;
using AssetLock.Editor.Data;
using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;
using static AssetLock.Editor.AssetLockUtility;

namespace AssetLock.Editor.UI
{
	/// <summary>
	/// Handles the GUI for the Project Window, including context menus.
	/// </summary>
	internal static class ProjectWindowGUI
	{
		private static readonly GUIContent s_lockIcon = EditorGUIUtility.IconContent("P4_LockedLocal@2x");
		private static readonly GUIContent s_unlockIcon = EditorGUIUtility.IconContent("P4_LockedRemote@2x");

		public static void DrawOnProjectWindowGUI(string guid, Rect selectionRect)
		{
			if (Application.isPlaying ||
				Event.current.type != EventType.Repaint ||
				!MasterEnable ||
				string.IsNullOrWhiteSpace(guid))
			{
				return;
			}

			var reference = FileReference.FromGuid(guid);

			if (!AssetLockManager.Instance.Repo.TryGetValue(reference, out var lockInfo))
			{
				return;
			}

			if (IsListView(selectionRect) && !IsListSubItem(selectionRect))
			{
				DrawListView(selectionRect, lockInfo);
			}
			else if (!IsListView(selectionRect))
			{
				DrawIconView(selectionRect, lockInfo);
			}
		}

		private static void DrawIconView(Rect selectionRect, LockInfo lockInfo)
		{
			if (!lockInfo.HasValue)
			{
				return;
			}

			GUIContent icon = lockInfo.locked ? s_lockIcon : s_unlockIcon;
			float min = Mathf.Min(selectionRect.width, selectionRect.height);
			Vector2 iconSize = new Vector2(min / 2, min / 2);
			Rect iconRect = new Rect(selectionRect.x, selectionRect.y, iconSize.x, iconSize.y);
			GUI.DrawTexture(iconRect, icon.image, ScaleMode.ScaleToFit);
		}

		private static void DrawListView(Rect selectionRect, LockInfo lockInfo)
		{
			if (!lockInfo.HasValue)
			{
				return;
			}

			GUIContent content = new GUIContent(
				$"{(lockInfo.locked ? "🔒 Locked" : "🔓 Unlocked")}{(lockInfo.owner != null ? $" by {lockInfo.owner}" : "")}"
			);
			Vector2 contentSize = EditorStyles.label.CalcSize(content);
			Rect contentRect = new Rect(
				selectionRect.x + selectionRect.width - contentSize.x - selectionRect.width / 10,
				selectionRect.y + (selectionRect.height - contentSize.y) / 2,
				contentSize.x,
				contentSize.y
			);
			GUI.Label(contentRect, content, EditorStyles.label);
		}

		private static bool IsListView(Rect itemView)
		{
			return itemView.height < 40;
		}

		private static bool IsListSubItem(Rect itemView)
		{
			return itemView.x > 15;
		}

		private static bool Get(out string path)
		{
			Object target = Selection.activeObject;

			if (!target)
			{
				path = default;

				return false;
			}

			path = AssetDatabase.GetAssetPath(target);

			return true;
		}

		private static bool Get(out FileReference reference)
		{
			Object target = Selection.activeObject;

			if (!target)
			{
				reference = default;

				return false;
			}

			string path = AssetDatabase.GetAssetPath(target);
			reference = FileReference.FromPath(path);

			return true;
		}

		private static bool Get(out FileReference reference, out LockInfo info)
		{
			if (!Get(out reference))
			{
				info = default;

				return false;
			}

			return AssetLockManager.Instance.Repo.TryGetValue(reference, out info);
		}

		[MenuItem("Assets/AssetLock/Lock Asset", priority = Constants.CONTEXT_MENU_BASE_PRIORITY)]
		private static void LockSelected()
		{
			if (!Get(out FileReference path))
			{
				return;
			}

			AssetLockManager.Instance.LockFile(path);
		}

		[MenuItem("Assets/AssetLock/Lock Asset", true)]
		private static bool CanLockSelected()
		{
			if (!Get(out FileReference reference, out LockInfo info))
			{
				return false;
			}

			return info is { HasValue: true, locked: false };
		}

		[MenuItem("Assets/AssetLock/Unlock Asset", priority = Constants.CONTEXT_MENU_BASE_PRIORITY)]
		private static void UnlockSelected()
		{
			if (!Get(out FileReference path))
			{
				return;
			}

			AssetLockManager.Instance.UnlockFile(path);
		}

		[MenuItem("Assets/AssetLock/Unlock Asset", true)]
		private static bool CanUnlockSelected()
		{
			if (!Get(out FileReference reference, out LockInfo info))
			{
				return false;
			}

			return info is { HasValue: true, LockedByMe: true };
		}

		[MenuItem("Assets/AssetLock/Debug/Is Binary Asset", priority = Constants.CONTEXT_MENU_BASE_PRIORITY + 
            Constants.CONTEXT_MENU_SEPARATOR * 2)]
		private static void IsBinaryAsset()
		{
			if (!Get(out FileReference reference))
			{
				return;
			}

			Logging.LogFormat(
				"Is {0} Binary Asset: {1}",
				reference.Name,
				IsBinary(reference)
			);
		}

		[MenuItem("Assets/AssetLock/Debug/Print Info", priority = Constants.CONTEXT_MENU_BASE_PRIORITY + 
			Constants.CONTEXT_MENU_SEPARATOR * 2)]
		private static void PrintInfo()
		{
			if (Get(out FileReference reference, out LockInfo info))
			{
				Logging.Log(info.ToString());
			}
			else if (reference.Exists)
			{
				Logging.Log(reference.ToString());
			}
			else
			{
				Logging.Log("No file selected.");
			}
		}

		[MenuItem("Assets/AssetLock/Force Lock", priority = Constants.CONTEXT_MENU_BASE_PRIORITY + Constants.CONTEXT_MENU_SEPARATOR)]
		private static void ForceLock()
		{
			if (!Get(out FileReference reference))
			{
				return;
			}

			AssetLockManager.Instance.LockFile(reference, true);
		}
		
		[MenuItem("Assets/AssetLock/Force Lock", true)]
		private static bool CanForceLock()
		{
			if (!Get(out FileReference reference, out LockInfo info))
			{
				return false;
			}

			return info is { HasValue: true, locked: false };
		}

		[MenuItem("Assets/AssetLock/Force Unlock", priority = Constants.CONTEXT_MENU_BASE_PRIORITY + Constants.CONTEXT_MENU_SEPARATOR)]
		private static void ForceUnlock()
		{
			if (!Get(out FileReference reference))
			{
				return;
			}

			AssetLockManager.Instance.UnlockFile(reference, true);
		}
		
		[MenuItem("Assets/AssetLock/Force Unlock", true)]
		private static bool CanForceUnlock()
		{
			return Get(out FileReference reference, out LockInfo info);
		}
	}
}