using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetLock.Editor.UI
{
	[CustomEditor(typeof(Object), true)]
	public class AssetEditorInject : UnityEditor.Editor
	{
		private LockInfo m_info;

		private LockInfo Info
		{
			get => m_info;
			set
			{
				m_info = value;
				if (m_status == null)
				{
					return;
				}

				if (value.locked)
				{
					m_status.text = "locked";
					m_status.style.color = Color.red;
					m_owner.text = $"by {(value.LockedByMe ? "me" : value.owner)}";
					m_lockButton.SetEnabled(value.LockedByMe);
					m_lockButton.text = value.LockedByMe ? "Unlock" : "Locked";
				}
				else
				{
					m_status.text = "unlocked";
					m_status.style.color = Color.green;
					m_owner.text = "";
					m_lockButton.SetEnabled(true);
					m_lockButton.text = "Lock";
				}
			}
		}
		private Label m_status;
		private Label m_owner;
		private Button m_lockButton;
		
		public override VisualElement CreateInspectorGUI()
		{
			var container = new VisualElement();

			if (ShouldShowLockInfo())
			{ 
				container.Add(CreateLockInfoElement());
			}
			
			InspectorElement.FillDefaultInspector(container, serializedObject, this);

			return container;
		}
		
		private bool ShouldShowLockInfo()
		{
			if (!AssetLockSettings.MasterEnable)
			{
				return false;
			}

			var path = AssetDatabase.GetAssetPath(target);

			if (!AssetLockManager.Instance.TryGetLockInfoByPath(AssetLockUtility.NormalizePath(path), out var info))
			{
				return false;
			}

			m_info = info;
			return true;

		}
		
		private VisualElement CreateLockInfoElement()
		{
			var container = new VisualElement();
			container.style.flexDirection = FlexDirection.Row;
			container.style.alignContent = Align.Center;
			container.style.alignItems = Align.Center;

			var label = new Label("This asset is");
			m_status = new Label();
			m_owner = new Label();
			m_lockButton = new Button(LockOrUnlock);
			
			container.Add(label);
			container.Add(m_status);
			container.Add(m_owner);
			container.Add(m_lockButton);

			return container;
		}

		private async void LockOrUnlock()
		{
			m_lockButton.SetEnabled(false);
			m_lockButton.text = "Processing...";
			var info = Info;
			info.locked = !info.locked;
			await AssetLockManager.Instance.SetLockAsync(info);
			Info = info;
		}
	}
}