using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SettingsManagement;
using UnityEngine;
using static UnityEditor.EditorGUILayout;
using static UnityEngine.GUILayout;

namespace AssetLock.Editor.UI
{
	/// <summary>
	/// Small utility class for GUI elements.
	/// </summary>
	internal static class ALGUI
	{
		private static HashSet<string> s_keywords = null;

		public static bool MatchSearchGroups(string searchContext, string label)
		{
			if (s_keywords != null)
			{
				foreach (var keyword in label.Split(' '))
				{
					s_keywords.Add(keyword);
				}
			}

			if (searchContext == null)
			{
				return true;
			}

			var context = searchContext.Trim();

			if (string.IsNullOrEmpty(context))
			{
				return true;
			}

			var split = context.Split(' ');

			return split.Any(
				x => !string.IsNullOrEmpty(x) && label.IndexOf(x, StringComparison.InvariantCultureIgnoreCase) > -1
			);
		}

		public static void SearchableLabel(GUIContent label, string searchContext)
		{
			if (!MatchSearchGroups(searchContext, label.text))
			{
				return;
			}

			GUILayout.Label(label);
		}

		public static void SearchableLabel(
			GUIContent label,
			GUIStyle style,
			string searchContext,
			params GUILayoutOption[] options
		)
		{
			if (!MatchSearchGroups(searchContext, label.text))
			{
				return;
			}

			GUILayout.Label(label, style, options);
		}

		public static bool SearchableButton(GUIContent label, string searchContext)
		{
			if (!MatchSearchGroups(searchContext, label.text))
			{
				return false;
			}

			return GUILayout.Button(label);
		}

		public static bool SearchableButton(
			GUIContent label,
			GUIStyle style,
			string searchContext,
			params GUILayoutOption[] options
		)
		{
			if (!MatchSearchGroups(searchContext, label.text))
			{
				return false;
			}

			return GUILayout.Button(label, style, options);
		}

		public static void SearchableButton(
			GUIContent label,
			GUIStyle style,
			string ctx,
			Action action,
			params GUILayoutOption[] options
		)
		{
			if (MatchSearchGroups(ctx, label.text))
			{
				if (GUILayout.Button(label, style, options))
				{
					action();
				}
			}
		}

		public static void SearchableButton(GUIContent label, string ctx, Action action)
		{
			SearchableButton(label, EditorStyles.miniButton, ctx, action);
		}

		public static void BeginSearchableGroup(GUIContent label, string ctx)
		{
			if (MatchSearchGroups(ctx, label.text))
			{
				EditorGUILayout.BeginVertical();
				Label(label, EditorStyles.boldLabel);
				EditorGUI.indentLevel++;
			}
		}

		public static void EndSearchableGroup(GUIContent label, string ctx)
		{
			if (MatchSearchGroups(ctx, label.text))
			{
				EditorGUI.indentLevel--;
				EditorGUILayout.EndVertical();
			}
		}

		public static void SearchableToggle(GUIContent label, UserSetting<bool> setting, string ctx)
		{
			if (MatchSearchGroups(ctx, label.text))
			{
				EditorGUI.BeginChangeCheck();

				using (new EditorGUILayout.HorizontalScope())
				{
					Indent();
					Label(label);
					FlexibleSpace();
					setting.value = EditorGUILayout.Toggle(setting.value);
				}

				if (EditorGUI.EndChangeCheck())
				{
					setting.ApplyModifiedProperties();
				}
			}
		}

		public static void LinkButton(GUIContent label, string url)
		{
			if (GUILayout.Button(label, EditorStyles.linkLabel))
			{
				Application.OpenURL(url);
			}
		}

		public static void SearchableNumericField<T>(GUIContent label, UserSetting<T> setting, string ctx)
			where T : unmanaged
		{
			if (MatchSearchGroups(ctx, label.text))
			{
				EditorGUI.BeginChangeCheck();

				setting.value = setting.value switch
				{
					int i => (T)(object)IntField(label, i),
					float f => (T)(object)FloatField(label, f),
					long l => (T)(object)LongField(label, l),
					double d => (T)(object)DoubleField(label, d),
					_ => throw new ArgumentOutOfRangeException()
				};

				if (EditorGUI.EndChangeCheck())
				{
					setting.ApplyModifiedProperties();
				}
			}
		}
		
		public static void SearchableStringField(GUIContent label, UserSetting<string> setting, string ctx)
		{
			if (MatchSearchGroups(ctx, label.text))
			{
				EditorGUI.BeginChangeCheck();

				setting.value = EditorGUILayout.TextField(label, setting.value);

				if (EditorGUI.EndChangeCheck())
				{
					setting.ApplyModifiedProperties();
				}
			}
		}

		public static void SearchableFilePicker(
			GUIContent content,
			UserSetting<string> setting,
			string ctx,
			string directory,
			string filekind,
			GUIContent downloadLabel = default,
			string downloadUrl = null
		)
		{
			if (MatchSearchGroups(ctx, content.text))
			{
				EditorGUI.BeginChangeCheck();

				using (new EditorGUILayout.HorizontalScope())
				{
					Indent();
					Label(content);
					setting.value = EditorGUILayout.TextField(setting.value, ExpandWidth(true));

					EditorGUI.indentLevel++;

					if (Button("...", Width(20)))
					{
						var path = EditorUtility.OpenFilePanel("Select File", directory, filekind);

						if (!string.IsNullOrEmpty(path))
						{
							setting.value = path;
						}
					}

					if (!string.IsNullOrEmpty(downloadUrl))
					{
						LinkButton(downloadLabel, downloadUrl);
					}

					EditorGUI.indentLevel--;

					if (EditorGUI.EndChangeCheck())
					{
						setting.ApplyModifiedProperties();
					}
				}
			}
		}

		public static void SearchableStringList(GUIContent content, UserSetting<List<string>> setting, string ctx)
		{
			if (MatchSearchGroups(ctx, content.text))
			{
				EditorGUI.BeginChangeCheck();
				var list = setting.value;

				for (var i = 0; i < list.Count; i++)
				{
					using (new EditorGUILayout.HorizontalScope())
					{
						list[i] = EditorGUILayout.TextField(list[i], GUILayout.ExpandWidth(true));

						if (GUILayout.Button(new GUIContent("X", $"Remove '{list[i]}'"), GUILayout.Width(20)))
						{
							list.RemoveAt(i);
						}
					}
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					FlexibleSpace();

					if (GUILayout.Button("Add", GUILayout.Width(50)))
					{
						list.Add("");
					}
				}

				if (EditorGUI.EndChangeCheck())
				{
					setting.ApplyModifiedProperties();
				}
			}
		}

		public static void Indent()
		{
			GUILayout.Space(EditorGUI.indentLevel * 15);
		}

		public static void LabelAndToggleButton(GUIContent label, UserSetting<bool> toggle)
		{
			GUIContent buttonLabel = toggle.value ? new GUIContent("ON", $"Click to disable") : new GUIContent("OFF", $"Click to enable");
			GUIStyle buttonStyle = toggle.value ? new GUIStyle(EditorStyles.miniButton) { normal = { textColor = Color.green } } : new GUIStyle(EditorStyles.miniButton) { normal = { textColor = Color.red } };
			
			using (new EditorGUILayout.HorizontalScope())
			{
				Indent();
				Label(label);
				FlexibleSpace();

				if (Button(buttonLabel, buttonStyle))
				{
					toggle.SetValue(!toggle.value);
				}
			}
		}
	}
}