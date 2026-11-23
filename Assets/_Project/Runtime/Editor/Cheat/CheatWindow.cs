using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
#if !DISABLE_CHEATS
using GameKit.Cheat;
#endif

namespace GameKit.EditorTools.Cheat
{
    /// <summary>
    /// Cheats in their own Editor window (toolbar "Cheats" / Tools/GameKit/Cheats/Cheat Window), so nothing covers the
    /// Game view while switching levels. Lists the same entries as the in-game panel (<c>CheatMenu.Current</c>), only in
    /// Play mode. The in-game panel stays for devices (pause, then tap "Paused" 3 times).
    /// </summary>
    public sealed class CheatWindow : EditorWindow
    {
        public const string MenuPath = "Tools/GameKit/Cheats/Cheat Window";

        private Vector2 scroll;
        private readonly Dictionary<string, bool> folded = new Dictionary<string, bool>();
        private readonly Dictionary<object, int> typedNumbers = new Dictionary<object, int>();

        /// <summary>Unity's Inspector window type (internal), to dock tool windows beside it.</summary>
        public static readonly Type InspectorType = Type.GetType("UnityEditor.InspectorWindow,UnityEditor");

        [MenuItem(MenuPath, false, 0)]
        public static void Open()
        {
            // A tab next to the Inspector, so it never takes the Inspector's place.
            var window = GetWindow<CheatWindow>("Cheats", true, InspectorType);
            window.minSize = new Vector2(260f, 200f);
            window.Show();
        }

        // Values change while the game runs: redraw ~10 times a second.
        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
#if DISABLE_CHEATS
            EditorGUILayout.HelpBox("Cheats are disabled (DISABLE_CHEATS). Enable them in Tools/GameKit/Cheats.", MessageType.Info);
#else
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to use the cheats.", MessageType.Info);
                return;
            }
            CheatMenu menu = CheatMenu.Current;
            if (menu == null)
            {
                EditorGUILayout.HelpBox("No cheats in this Play session (no GameBootstrap in the scene?).", MessageType.Warning);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            string category = null;
            bool open = true;
            foreach (CheatMenu.Item item in Ordered(menu))
            {
                string itemCategory = string.IsNullOrEmpty(item.Category) ? "Misc" : item.Category;
                if (itemCategory != category)
                {
                    category = itemCategory;
                    EditorGUILayout.Space(4f);
                    folded.TryGetValue(category, out bool closed);
                    open = EditorGUILayout.BeginFoldoutHeaderGroup(!closed, category);
                    folded[category] = !open;
                    EditorGUILayout.EndFoldoutHeaderGroup();
                }
                if (open)
                {
                    DrawItem(item);
                }
            }
            EditorGUILayout.EndScrollView();
#endif
        }

#if !DISABLE_CHEATS
        /// <summary>Items grouped by category, categories in the order they first appear (like the in-game panel).</summary>
        private static IEnumerable<CheatMenu.Item> Ordered(CheatMenu menu)
        {
            var order = new List<string>();
            var groups = new Dictionary<string, List<CheatMenu.Item>>();
            foreach (CheatMenu.Item item in menu.Items)
            {
                string category = string.IsNullOrEmpty(item.Category) ? "Misc" : item.Category;
                if (!groups.TryGetValue(category, out List<CheatMenu.Item> list))
                {
                    list = new List<CheatMenu.Item>();
                    groups[category] = list;
                    order.Add(category);
                }
                list.Add(item);
            }
            foreach (string category in order)
            {
                foreach (CheatMenu.Item item in groups[category])
                {
                    yield return item;
                }
            }
        }

        private void DrawItem(CheatMenu.Item item)
        {
            switch (item.Kind)
            {
                case CheatMenu.Kind.Button:
                    if (GUILayout.Button(item.Label, GUILayout.Height(24f)))
                    {
                        Run(item.Click);
                    }
                    break;

                case CheatMenu.Kind.Toggle:
                {
                    bool value = Get(item.GetBool);
                    bool next = EditorGUILayout.Toggle(item.Label, value);
                    if (next != value)
                    {
                        Run(() => item.SetBool(next));
                    }
                    break;
                }

                case CheatMenu.Kind.Stepper:
                {
                    float value = Get(item.GetFloat);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(item.Label);
                    if (GUILayout.Button("-", GUILayout.Width(28f)))
                    {
                        Step(item, value, -1f);
                    }
                    GUILayout.Label(value.ToString(item.Format, CultureInfo.InvariantCulture), EditorStyles.centeredGreyMiniLabel,
                        GUILayout.Width(48f));
                    if (GUILayout.Button("+", GUILayout.Width(28f)))
                    {
                        Step(item, value, 1f);
                    }
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                case CheatMenu.Kind.Number:
                {
                    int current = (int)Get(item.GetFloat);
                    if (!typedNumbers.TryGetValue(item, out int typed))
                    {
                        typed = current; // Follows the game until something is typed.
                    }
                    EditorGUILayout.BeginHorizontal();
                    int edited = EditorGUILayout.IntField(item.Label, typed);
                    if (edited != typed)
                    {
                        typedNumbers[item] = edited;
                        typed = edited;
                    }
                    if (GUILayout.Button("Go", GUILayout.Width(40f)))
                    {
                        int value = Mathf.Clamp(typed, (int)item.Min, (int)item.Max);
                        Run(() => item.SetFloat(value));
                        typedNumbers.Remove(item); // Show the game's value again.
                    }
                    EditorGUILayout.EndHorizontal();
                    break;
                }
            }
        }

        private static void Step(CheatMenu.Item item, float value, float direction)
        {
            float next = value + direction * item.Step;
            if (item.Step > 0f)
            {
                next = Mathf.Round(next / item.Step) * item.Step;
            }
            next = Mathf.Clamp(next, item.Min, item.Max);
            Run(() => item.SetFloat(next));
        }

        private static T Get<T>(Func<T> getter)
        {
            try
            {
                return getter != null ? getter() : default;
            }
            catch (Exception)
            {
                return default; // Game not ready yet (e.g. between levels).
            }
        }

        private static void Run(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
#endif
    }
}
