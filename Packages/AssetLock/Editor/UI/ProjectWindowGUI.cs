using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.UI
{
	internal static class ProjectWindowGUI
	{
		private static readonly GUIContent lockIcon = EditorGUIUtility.IconContent("P4_LockedLocal@2x");
		private static readonly GUIContent unlockIcon = EditorGUIUtility.IconContent("P4_LockedRemote@2x");

		public static void DrawOnProjectWindowGUI(string guid, Rect selectionrect)
		{
			if (Application.isPlaying || Event.current.type != EventType.Repaint || !MasterEnable)
			{
				return;
			}

			if (!AssetLockManager.Instance.TryGetLockInfoByGuid(guid, out var lockInfo))
			{
				return;
			}

			if (IsListView(selectionrect) && !IsListSubItem(selectionrect))
			{
				DrawListView(selectionrect, lockInfo);
			}
			else if (!IsListView(selectionrect))
			{
				DrawIconView(selectionrect, lockInfo);
			}
		}

		private static void DrawIconView(Rect selectionrect, LockInfo lockInfo)
		{
			if (!lockInfo.HasValue)
			{
				return;
			}

			GUIContent icon = lockInfo.locked ? lockIcon : unlockIcon;
			float min = Mathf.Min(selectionrect.width, selectionrect.height);
			Vector2 iconSize = new Vector2(min / 2, min / 2);
			Rect iconRect = new Rect(selectionrect.x, selectionrect.y, iconSize.x, iconSize.y);
			GUI.DrawTexture(iconRect, icon.image, ScaleMode.ScaleToFit);
		}

		private static void DrawListView(Rect selectionrect, LockInfo lockInfo)
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
				selectionrect.x + selectionrect.width - contentSize.x - selectionrect.width / 10,
				selectionrect.y + (selectionrect.height - contentSize.y) / 2,
				contentSize.x,
				contentSize.y
			);
			GUI.Label(contentRect, content, EditorStyles.label);
		}

		private static bool IsListView(Rect itemView)
		{
			if (itemView.height < 40)
			{
				return true;
			}

			return false;
		}

		private static bool IsListSubItem(Rect itemView)
		{
			if (itemView.x > 15)
			{
				return true;
			}

			return false;
		}

		[MenuItem("Assets/AssetLock/Lock Asset")]
		private static async void LockSelected()
		{
			Object target = Selection.activeObject;

			if (!target)
			{
				return;
			}

			string path = AssetDatabase.GetAssetPath(target);

			if (AssetLockManager.Instance.TryGetLockInfoByGuid(path, out var lockInfo))
			{
				var info = lockInfo;
				info.locked = true;
				await AssetLockManager.Instance.SetLockAsync(info);
			}
		}

		[MenuItem("Assets/AssetLock/Lock Asset", true)]
		private static bool CanLockSelected()
		{
			Object target = Selection.activeObject;

			if (!target)
			{
				return false;
			}

			string path = AssetDatabase.GetAssetPath(target);

			if (AssetLockManager.Instance.TryGetLockInfoByGuid(path, out var lockInfo))
			{
				return lockInfo is { HasValue: true, locked: false };
			}

			return false;
		}

		[MenuItem("Assets/AssetLock/Unlock Asset")]
		private static async void UnlockSelected()
		{
			Object target = Selection.activeObject;

			if (!target)
			{
				return;
			}

			string path = AssetDatabase.GetAssetPath(target);

			if (AssetLockManager.Instance.TryGetLockInfoByPath(path, out var lockInfo))
			{
				var info = lockInfo;
				info.locked = false;
				await AssetLockManager.Instance.SetLockAsync(info);
			}
		}

		[MenuItem("Assets/AssetLock/Unlock Asset", true)]
		private static bool CanUnlockSelected()
		{
			Object target = Selection.activeObject;

			if (!target)
			{
				return false;
			}

			string path = AssetDatabase.GetAssetPath(target);

			if (AssetLockManager.Instance.TryGetLockInfoByPath(path, out var lockInfo))
			{
				return lockInfo is { HasValue: true, locked: true };
			}

			return false;
		}

		[MenuItem("Assets/AssetLock/Is Binary Asset")]
		private static void IsBinaryAsset()
		{
			Object target = Selection.activeObject;
			
			if (!target)
			{
				return;
			}
			
			string path = AssetDatabase.GetAssetPath(target);
			
			AssetLockUtility.Logging.LogFormat("IsBinaryAsset: {0}", AssetLockUtility.ShouldTrack(path));
		}
	}
}