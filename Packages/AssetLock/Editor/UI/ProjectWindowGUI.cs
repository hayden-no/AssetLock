using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEngine;
using static AssetLock.Editor.AssetLockSettings;

namespace AssetLock.Editor.UI
{
	internal static class ProjectWindowGUI
	{
		private static readonly GUIContent s_lockIcon = EditorGUIUtility.IconContent("P4_LockedLocal@2x");
		private static readonly GUIContent s_unlockIcon = EditorGUIUtility.IconContent("P4_LockedRemote@2x");

		public static void DrawOnProjectWindowGUI(string guid, Rect selectionRect)
		{
			if (Application.isPlaying || Event.current.type != EventType.Repaint || !MasterEnable || string
                .IsNullOrWhiteSpace(guid))
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
		private static void LockSelected()
		{
			Object target = Selection.activeObject;

			if (!target)
			{
				return;
			}

			string path = AssetDatabase.GetAssetPath(target);
			AssetLockManager.Instance.LockFile(path);
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
			var reference = FileReference.FromPath(path);

			if (AssetLockManager.Instance.Repo.TryGetValue(reference, out var lockInfo))
			{
				return lockInfo is { HasValue: true, locked: false };
			}

			return false;
		}

		[MenuItem("Assets/AssetLock/Unlock Asset")]
		private static void UnlockSelected()
		{
			Object target = Selection.activeObject;

			if (!target)
			{
				return;
			}

			string path = AssetDatabase.GetAssetPath(target);
			AssetLockManager.Instance.UnlockFile(path);
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
			var reference = FileReference.FromPath(path);

			if (AssetLockManager.Instance.Repo.TryGetValue(reference, out var lockInfo))
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