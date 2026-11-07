using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssetLock.Editor.Data;
using AssetLock.Editor.Manager;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static AssetLock.Editor.AssetLockSettings;
using static AssetLock.Editor.AssetLockUtility;

namespace AssetLock.Editor.UI
{
	/// <summary>UI Toolkit browser for the current AssetLock repository.</summary>
	public sealed class LockBrowserToolkit : EditorWindow
	{
		private sealed class Row
		{
			internal FileReference File;
			internal LockInfo Info;
		}

		private readonly List<Row> m_rows = new();
		private ListView m_list;
		private Label m_status;
		private Label m_counts;
		private Label m_username;
		private Label m_empty;
		private Label m_message;
		private ToolbarSearchField m_search;
		private ToolbarMenu m_filter;
		private Toggle m_enabled;
		private Toggle m_autoLock;
		private Toggle m_warnOnly;
		private Toggle m_force;
		private VisualElement m_actions;
		private VisualElement m_generalActions;
		private readonly List<Button> m_lockActions = new();
		private Button m_reboot;
		private IVisualElementScheduledItem m_poll;
		private IVisualElementScheduledItem m_statusPoll;
		private string m_filterValue = "All";
		private int m_revision = int.MinValue;
		private bool m_busy;

		internal static void Open()
		{
			var window = GetWindow<LockBrowserToolkit>();
			window.titleContent = EditorGUIUtility.TrTextContentWithIcon(
				Constants.BROWSER_TITLE,
				Constants.BROWSER_ICON
			);
			window.minSize = new Vector2(430, 300);
			window.Show();
		}

		public void CreateGUI()
		{
			m_poll?.Pause();
			m_statusPoll?.Pause();
			var root = rootVisualElement;
			root.Clear();
			root.AddToClassList("asset-lock-browser");
			root.AddToClassList(EditorGUIUtility.isProSkin ? "dark" : "light");
			var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.haydenno.assetlock/Editor/UI/LockBrowserToolkit.uss"
			);
			if (stylesheet != null) root.styleSheets.Add(stylesheet);

			var header = new VisualElement();
			header.AddToClassList("header");
			var heading = new VisualElement();
			heading.AddToClassList("heading");
			heading.Add(new Label("ASSET LOCK") { name = "browser-title" });
			var subtitle = new Label("Manage Git LFS locks across your project");
			subtitle.AddToClassList("subtitle");
			heading.Add(subtitle);
			header.Add(heading);
			m_status = new Label { name = "refresh-status" };
			m_status.AddToClassList("status-chip");
			header.Add(m_status);
			root.Add(header);

			var settings = new VisualElement();
			settings.AddToClassList("settings-row");
			m_enabled = AddToggle(settings, "Asset Lock", MasterEnable, value => MasterEnable.SetValue(value));
			m_autoLock = AddToggle(settings, "Auto lock", AutoLock, value => AutoLock.SetValue(value));
			m_warnOnly = AddToggle(settings, "Warnings only", WarnOnly, value => WarnOnly.SetValue(value));
			m_warnOnly.tooltip = "Allow operations even when an asset is not locked by you.";
			root.Add(settings);

			var summary = new VisualElement();
			summary.AddToClassList("summary");
			var summaryTitleContainer = new VisualElement { style = { flexDirection = FlexDirection.Row } };
			summaryTitleContainer.AddToClassList("section-label");
			var summaryTitle = new Label("LOCK OVERVIEW");
			summaryTitleContainer.Add(summaryTitle);
			summaryTitleContainer.Add(new VisualElement { style = { flexGrow = 1 } });
			var userSummaryTitle = new Label("IDENTITY");
			summaryTitleContainer.Add(userSummaryTitle);
			summary.Add(summaryTitleContainer);
			var summaryValuesContainer = new VisualElement { style = { flexDirection = FlexDirection.Row } };
			m_counts = new Label();
			m_counts.AddToClassList("counts");
			summaryValuesContainer.Add(m_counts);
			summaryValuesContainer.Add(new VisualElement() { style = { flexGrow = 1 } });
			m_username = new Label();
			m_username.AddToClassList("username");
			summaryValuesContainer.Add(m_username);
			summary.Add(summaryValuesContainer);
			root.Add(summary);

			var actionRow = new VisualElement();
			actionRow.AddToClassList("action-row");
			m_actions = new VisualElement();
			m_actions.AddToClassList("action-row");
			m_actions.Add(ActionButton("Refresh", () => RunAsync(() => AssetLockManager.Instance.RefreshAsync())));
			m_lockActions.Clear();
			m_lockActions.Add(ActionButton("Scan assets", () => RunSync(() => AssetLockManager.Instance.ParseAll())));
			m_lockActions.Add(ActionButton("Unlock mine", () => RunSync(() => AssetLockManager.Instance.UnlockAll())));
			m_lockActions.Add(ActionButton("Lock selected", () => RunSelected(true)));
			m_lockActions.Add(ActionButton("Unlock selected", () => RunSelected(false)));
			foreach (var button in m_lockActions) m_actions.Add(button);
			actionRow.Add(m_actions);
			actionRow.Add(new VisualElement() { style = { width = 10 } });
			m_generalActions = new VisualElement();
			m_generalActions.AddToClassList("action-row");
			m_reboot = ActionButton("Reboot", () => RunSync(AssetLockManager.Reboot));
			m_generalActions.Add(m_reboot);
			actionRow.Add(m_generalActions);
			root.Add(actionRow);

			var toolbar = new Toolbar();
			toolbar.AddToClassList("search-toolbar");
			m_search = new ToolbarSearchField { name = "asset-search" };
			m_search.tooltip =
				"Search names, paths, and owners. Use * to match any text; wildcard patterns match the whole field.";
			m_search.style.flexGrow = 1;
			m_search.RegisterValueChangedCallback(_ => RefreshView(true));
			toolbar.Add(m_search);
			m_filter = new ToolbarMenu { text = "All", name = "status-filter" };
			m_filter.style.width = 95;

			foreach (string value in new[] { "All", "Mine", "Others", "Unlocked" })
			{
				string selected = value;
				m_filter.menu.AppendAction(
					selected,
					_ =>
					{
						m_filterValue = selected;
						m_filter.text = selected;
						RefreshView(true);
					}
				);
			}

			toolbar.Add(m_filter);
			m_force = new ToolbarToggle
			{
				text = "Force",
				tooltip =
					"Show force lock and unlock actions; these may require repository administrator permissions."
			};
			m_force.style.width = 75;
			m_force.style.flexGrow = 0;
			m_force.RegisterValueChangedCallback(_ => RefreshView(true));
			toolbar.Add(m_force);
			root.Add(toolbar);

			var columnHeader = new VisualElement();
			columnHeader.AddToClassList("column-header");
			var assetHeader = new Label("ASSET / PATH");
			assetHeader.AddToClassList("asset-column");
			columnHeader.Add(assetHeader);
			var stateHeader = new Label("LOCK STATUS");
			stateHeader.AddToClassList("state-column");
			columnHeader.Add(stateHeader);
			columnHeader.Add(new Label("ACTIONS") { style = { width = 145 } });
			root.Add(columnHeader);

			m_list = new ListView { name = "lock-list", fixedItemHeight = 48, selectionType = SelectionType.Multiple };
			m_list.AddToClassList("lock-list");
			m_list.itemsSource = m_rows;
			m_list.makeItem = MakeRow;
			m_list.bindItem = BindRow;
			root.Add(m_list);
			m_empty = new Label { name = "empty-state" };
			m_empty.AddToClassList("empty-state");
			root.Add(m_empty);
			m_message = new Label();
			m_message.AddToClassList("message");
			root.Add(m_message);
			m_poll = root.schedule.Execute(() => RefreshView(false)).Every(1000);
			m_statusPoll = root.schedule.Execute(UpdateStatus).Every(250);
			RefreshView(true);
		}

		private void OnDisable()
		{
			m_poll?.Pause();
			m_statusPoll?.Pause();
		}

		private Toggle AddToggle(VisualElement parent, string text, bool initial, Action<bool> changed)
		{
			var toggle = new Toggle(text) { value = initial };
			toggle.RegisterValueChangedCallback(e =>
				{
					changed(e.newValue);
					RefreshView(true);
				}
			);
			parent.Add(toggle);

			return toggle;
		}

		private static Button ActionButton(string text, Action action)
		{
			var button = new Button(action) { text = text };
			button.AddToClassList("action-button");

			return button;
		}

		private VisualElement MakeRow()
		{
			var row = new VisualElement();
			row.AddToClassList("asset-row");
			var asset = new VisualElement();
			asset.AddToClassList("asset-column");
			var name = new Button(() =>
				{
					if (row.userData is Row data)
					{
						var path = data.File.UnityPath.Replace('\\', '/');
						Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(path);
						if (Selection.activeObject != null) EditorGUIUtility.PingObject(Selection.activeObject);
					}
				}
			) { name = "asset-name" };
			name.AddToClassList("asset-name");
			asset.Add(name);
			var pathLabel = new Label { name = "asset-path" };
			pathLabel.AddToClassList("asset-path");
			asset.Add(pathLabel);
			row.Add(asset);
			var status = new Label { name = "asset-status" };
			status.AddToClassList("state-column");
			row.Add(status);
			var buttons = new VisualElement();
			buttons.AddToClassList("row-actions");
			var lockButton = new Button(() => RunRowAction(row, true)) { name = "lock" };
			var unlockButton = new Button(() => RunRowAction(row, false)) { name = "unlock" };
			var refreshButton = new Button(() =>
				{
					if (row.userData is Row data) RunAsync(data.File.RefreshLockAsync);
				}
			) { name = "refresh", text = "↻", tooltip = "Refresh lock status" };
			buttons.Add(lockButton);
			buttons.Add(unlockButton);
			buttons.Add(refreshButton);
			row.Add(buttons);

			return row;
		}

		private void BindRow(VisualElement element, int index)
		{
			var row = m_rows[index];
			element.userData = row;
			var name = element.Q<Button>("asset-name");
			name.text = row.File.NameWithExtension;
			name.tooltip = row.File.UnityPath;
			element.Q<Label>("asset-path").text = row.File.UnityPath.Replace('\\', '/');
			var status = element.Q<Label>("asset-status");
			status.text = !row.Info.locked ? "Unlocked" :
				row.Info.LockedByMe ? "Locked by you" : $"Locked by {row.Info.owner ?? "unknown"}";
			status.tooltip = row.Info.lockedAt ?? string.Empty;
			status.EnableInClassList("owned", row.Info.LockedByMe);
			status.EnableInClassList("other", row.Info.locked && !row.Info.LockedByMe);
			var lockButton = element.Q<Button>("lock");
			var unlockButton = element.Q<Button>("unlock");
			lockButton.text = m_force.value ? "Force lock" : "Lock";
			unlockButton.text = m_force.value ? "Force unlock" : "Unlock";
			lockButton.style.display = !row.Info.locked || m_force.value ? DisplayStyle.Flex : DisplayStyle.None;
			unlockButton.style.display = row.Info.locked && (row.Info.LockedByMe || m_force.value)
				? DisplayStyle.Flex
				: DisplayStyle.None;
			element.Q<VisualElement>(className: "row-actions")
				.SetEnabled(!m_busy && AssetLockManager.Instance?.AutomaticRefreshPaused != true);
		}

		private void RunRowAction(VisualElement element, bool lockFile)
		{
			if (!(element.userData is Row row)) return;

			bool force = m_force.value;
			RunAsync(() => lockFile ? row.File.LockFileAsync(force) : row.File.UnlockFileAsync(force));
		}

		private void RunSelected(bool lockFiles)
		{
			bool force = m_force.value;
			var selected = m_list.selectedIndices.Where(i => i >= 0 && i < m_rows.Count)
				.Select(i => m_rows[i])
				.ToArray();
			var eligible = selected.Where(row =>
					lockFiles ? !row.Info.locked || force : row.Info.locked && (row.Info.LockedByMe || force)
				)
				.ToArray();

			if (eligible.Length == 0)
			{
				m_message.text = "Select assets that can be " + (lockFiles ? "locked." : "unlocked.");

				return;
			}

			RunAsync(() => Task.WhenAll(
					eligible.Select(row => lockFiles ? row.File.LockFileAsync(force) : row.File.UnlockFileAsync(force))
				)
			);
		}

		private void RunSync(Action action)
		{
			try
			{
				action();
				m_message.text = string.Empty;
			}
			catch (Exception error)
			{
				m_message.text = error.Message;
				Debug.LogException(error);
			}

			RefreshView(true);
		}

		private async void RunAsync(Func<Task> action)
		{
			if (m_busy) return;

			m_busy = true;
			RefreshView(true);

			try
			{
				await action();
				m_message.text = string.Empty;
			}
			catch (Exception error)
			{
				m_message.text = error.Message;
				Debug.LogException(error);
			}
			finally
			{
				m_busy = false;
				if (this != null) RefreshView(true);
			}
		}

		private void RefreshView(bool force)
		{
			if (m_list == null) return;

			var manager = AssetLockManager.Instance;
			bool ready = MasterEnable && manager?.IsReady == true;
			bool paused = ready && manager.AutomaticRefreshPaused;
			bool loading = ready && !paused && (manager.HasPendingCommands || manager.IsRefreshPending);
			var entries = ready ? manager.Repo.ToArray() : Array.Empty<KeyValuePair<FileReference, LockInfo>>();
			int revision = ready ? (paused ? 19 : loading ? 23 : 17) : manager?.IsInitializing == true ? 29 : 0;
			UpdateStatus();

			unchecked
			{
				foreach (var entry in entries.OrderBy(e => e.Key.AbsolutePath, StringComparer.OrdinalIgnoreCase))
				{
					revision = revision * 31 + entry.Key.GetHashCode();
					revision = revision * 31 + (entry.Value.locked ? 1 : 0);
					revision = revision * 31 + (entry.Value.owner?.GetHashCode() ?? 0);
					revision = revision * 31 + (entry.Value.lockId?.GetHashCode() ?? 0);
					revision = revision * 31 + (entry.Value.lockedAt?.GetHashCode() ?? 0);
				}
			}

			if (!force &&
				revision == m_revision &&
				m_enabled.value == MasterEnable.value &&
				m_autoLock.value == AutoLock.value &&
				m_warnOnly.value == WarnOnly.value)
				return;

			m_revision = revision;
			m_enabled.SetValueWithoutNotify(MasterEnable);
			m_autoLock.SetValueWithoutNotify(AutoLock);
			m_warnOnly.SetValueWithoutNotify(WarnOnly);
			m_actions.SetEnabled(ready && !m_busy);
			foreach (var button in m_lockActions) button.SetEnabled(!paused);
			m_reboot.SetEnabled(!m_busy);
			m_enabled.SetEnabled(!m_busy);
			const string pausedMessage =
				"Automatic refresh paused. Check Git credentials, then select Refresh to retry.";
			if (paused)
				m_message.text = pausedMessage;
			else if (m_message.text == pausedMessage) m_message.text = string.Empty;
			int mine = entries.Count(e => e.Value.LockedByMe);
			int others = entries.Count(e => e.Value.locked && !e.Value.LockedByMe);
			m_counts.text =
				$"{entries.Length} tracked     ·     {mine} yours     ·     {others} others     ·     {entries.Length - mine - others} unlocked";
			m_username.text = AssetLockManager.Instance.User;
			string search = m_search.value?.Trim() ?? string.Empty;
			m_rows.Clear();

			foreach (var entry in entries.OrderBy(e => e.Key.UnityPath, StringComparer.OrdinalIgnoreCase))
			{
				var info = entry.Value;

				if (m_filterValue == "Mine" && !info.LockedByMe ||
					m_filterValue == "Others" && (!info.locked || info.LockedByMe) ||
					m_filterValue == "Unlocked" && info.locked)
					continue;
				if (search.Length > 0 &&
					!ALGUI.MatchesBrowserSearch(entry.Key.NameWithExtension, search) &&
					!ALGUI.MatchesBrowserSearch(entry.Key.UnityPath.Replace('\\', '/'), search.Replace('\\', '/')) &&
					!ALGUI.MatchesBrowserSearch(info.owner, search))
					continue;

				m_rows.Add(new Row { File = entry.Key, Info = info });
			}

			m_list.Rebuild();
			m_list.style.display = m_rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			m_empty.style.display = m_rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			m_empty.text = !MasterEnable
				? "Asset Lock is disabled. Enable it above to get started."
				:
				manager?.IsInitializing == true
					? "Initializing Asset Lock..."
					:
					!ready
						? "Asset Lock is not ready. Configure it in Preferences, then reboot."
						:
						entries.Length == 0
							?
							(loading
								? "Loading tracked assets and lock status..."
								: "No tracked assets yet. Scan assets to get started.")
							: "No assets match your search or filter.";
		}

		private void UpdateStatus()
		{
			if (m_status == null) return;

			var manager = AssetLockManager.Instance;
			string status = !MasterEnable ? "Disabled" :
				manager?.IsInitializing == true ? "Initializing" :
				manager?.IsReady != true ? "Setup required" :
				manager.AutomaticRefreshPaused ? "Refresh paused" :
				manager.HasPendingCommands ? "Updating assets" :
				manager.IsRefreshPending ? "Refreshing locks" :
				m_busy ? "Working" : "Ready";
			m_status.text = status;
			m_status.EnableInClassList("ready", status == "Ready");
		}
	}
}