using GameKit.Core.Foundation.Events;
using UnityEditor;
using UnityEngine;

namespace GameKit.EditorTools.Core
{
    /// <summary>Lists every event bus used so far, with its listener and raise counts.</summary>
    public class EventBusMonitorWindow : EditorWindow
    {
        private Vector2 scroll;
        private string filter = string.Empty;

        [MenuItem("Tools/GameKit/Event Bus Monitor")]
        private static void Open()
        {
            GetWindow<EventBusMonitorWindow>("Event Bus");
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                filter = EditorGUILayout.TextField(filter, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("Clear all", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    EventBusRegistry.ClearAll();
                }
            }

            var entries = EventBusRegistry.Entries;
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("No event bus used yet. Buses appear the first time an event type is subscribed or raised.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Event", EditorStyles.boldLabel);
                GUILayout.Label("Listeners", EditorStyles.boldLabel, GUILayout.Width(70));
                GUILayout.Label("Raised", EditorStyles.boldLabel, GUILayout.Width(60));
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var name = entry.EventType.Name;
                if (filter.Length > 0 && name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent(name, entry.EventType.FullName));
                    GUILayout.Label(entry.ListenerCount.ToString(), GUILayout.Width(70));
                    GUILayout.Label(entry.RaiseCount.ToString(), GUILayout.Width(60));
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
