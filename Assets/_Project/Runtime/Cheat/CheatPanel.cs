#if !DISABLE_CHEATS
using System;
using System.Collections.Generic;
using System.Globalization;
using GameKit.Core.Foundation.Logging;
using GameKit.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameKit.Cheat
{
    /// <summary>
    /// Debug cheat panel, built in code at runtime (no prefab, never in a scene, nothing on screen until opened):
    /// a scrolling list of the <see cref="CheatMenu"/> entries, grouped by category. Opened from the Editor toolbar
    /// "Cheats" / Tools/GameKit/Cheats/Toggle Cheat Panel, or on a device by tapping the "Paused" title 3 times
    /// (<see cref="CheatTapTrigger"/>). Own Canvas on top of everything (1080x1920 reference like the game UI, box inside
    /// the safe area); while open it covers the screen, so game taps do not go through. Works with Time.timeScale = 0.
    /// Debug UI only, so its buttons skip the ButtonPress / click-sound rule of the game UI.
    /// </summary>
    public sealed class CheatPanel : MonoBehaviour
    {
        private const string Tag = "Cheat";
        private const int FontSize = 40;
        private const float RowHeight = 112f;

        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color BoxColor = new Color(0.09f, 0.1f, 0.13f, 0.97f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.45f, 0.85f, 1f);
        private static readonly Color DangerColor = new Color(0.8f, 0.3f, 0.3f, 1f);
        private static readonly Color OnColor = new Color(0.2f, 0.65f, 0.35f, 1f);
        private static readonly Color OffColor = new Color(0.38f, 0.38f, 0.42f, 1f);
        private static readonly Color FieldColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color CategoryColor = new Color(1f, 0.8f, 0.3f, 1f);

        private readonly List<Action> refreshers = new List<Action>();
        private CheatMenu menu;
        private GameObject window;

        public static CheatPanel Instance { get; private set; }

        public bool IsOpen => window != null && window.activeSelf;

        /// <summary>Builds the panel for <paramref name="cheats"/> (replaces an existing one).</summary>
        public static CheatPanel Create(CheatMenu cheats)
        {
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
            }
            var go = new GameObject("[Cheats]");
            DontDestroyOnLoad(go);
            var panel = go.AddComponent<CheatPanel>();
            panel.menu = cheats;
            panel.Build();
            Instance = panel;
            return panel;
        }

        public void Open()
        {
            RefreshAll();
            window.SetActive(true);
        }

        public void Close()
        {
            window.SetActive(false);
        }

        public void ToggleOpen()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ---- Build ----------------------------------------------------------------------------------------------

        private void Build()
        {
            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                DontDestroyOnLoad(eventSystem);
            }

            gameObject.layer = LayerMask.NameToLayer("UI");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // The dim layer covers the whole screen (catches every tap); only the box keeps out of the notch.
            RectTransform windowRect = NewRect("Window", transform);
            window = windowRect.gameObject;
            window.SetActive(false); // Built inactive: input fields wake up only once fully set up.
            Stretch(windowRect);
            window.AddComponent<Image>().color = DimColor;

            RectTransform safe = NewRect("SafeArea", windowRect);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();

            RectTransform box = NewRect("Box", safe);
            Stretch(box, 24f, 24f, 24f, 24f);
            box.gameObject.AddComponent<Image>().color = BoxColor;

            RectTransform header = NewRect("Header", box);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 130f);
            TextMeshProUGUI title = MakeText(header, "Cheats", 52, TextAlignmentOptions.MidlineLeft);
            Stretch(title.rectTransform, 32f, 260f, 0f, 0f);
            title.fontStyle = FontStyles.Bold;
            Button close = MakeButton(header, "Close", DangerColor, Close);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(220f, 92f);
            closeRect.anchoredPosition = new Vector2(-20f, 0f);

            RectTransform content = BuildScroll(box);
            foreach (KeyValuePair<string, List<CheatMenu.Item>> group in Grouped())
            {
                AddCategory(content, group.Key);
                foreach (CheatMenu.Item item in group.Value)
                {
                    AddItem(content, item);
                }
            }
        }

        /// <summary>Scroll view under the header; returns the content that rows go into.</summary>
        private static RectTransform BuildScroll(RectTransform box)
        {
            RectTransform scroll = NewRect("Scroll", box);
            Stretch(scroll, 0f, 0f, 130f, 0f);
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = NewRect("Viewport", scroll);
            Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = Color.clear; // Drag target for scrolling.
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 8, 40);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 40f;
            return content;
        }

        private IEnumerable<KeyValuePair<string, List<CheatMenu.Item>>> Grouped()
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
                yield return new KeyValuePair<string, List<CheatMenu.Item>>(category, groups[category]);
            }
        }

        private static void AddCategory(RectTransform content, string category)
        {
            TextMeshProUGUI text = MakeText(content, category, 44, TextAlignmentOptions.BottomLeft);
            text.color = CategoryColor;
            text.fontStyle = FontStyles.Bold;
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
        }

        private void AddItem(RectTransform content, CheatMenu.Item item)
        {
            if (item.Kind == CheatMenu.Kind.Button)
            {
                Button button = MakeButton(content, item.Label, ButtonColor, () =>
                {
                    if (item.ClosePanel)
                    {
                        Close();
                    }
                    Run(item.Click);
                    RefreshAll();
                });
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
                return;
            }

            RectTransform row = NewRect("Row " + item.Label, content);
            row.gameObject.AddComponent<Image>().color = RowColor;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(28, 12, 10, 10);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            TextMeshProUGUI label = MakeText(row, item.Label, FontSize, TextAlignmentOptions.MidlineLeft);
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            switch (item.Kind)
            {
                case CheatMenu.Kind.Toggle:
                    AddToggle(row, item);
                    break;
                case CheatMenu.Kind.Stepper:
                    AddStepper(row, item);
                    break;
                case CheatMenu.Kind.Number:
                    AddNumber(row, item);
                    break;
            }
        }

        private void AddToggle(RectTransform row, CheatMenu.Item item)
        {
            Button button = null;
            button = MakeButton(row, "", OffColor, () =>
            {
                Run(() => item.SetBool(!item.GetBool()));
                RefreshAll();
            }, FontSize, 200f);
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            var image = (Image)button.targetGraphic;
            refreshers.Add(() =>
            {
                bool on = item.GetBool();
                text.text = on ? "ON" : "OFF";
                image.color = on ? OnColor : OffColor;
            });
        }

        private void AddStepper(RectTransform row, CheatMenu.Item item)
        {
            MakeButton(row, "-", ButtonColor, () => Step(item, -1f), 52, 110f);
            TextMeshProUGUI value = MakeText(row, "", FontSize, TextAlignmentOptions.Center);
            var valueLayout = value.gameObject.AddComponent<LayoutElement>();
            valueLayout.preferredWidth = valueLayout.minWidth = 170f;
            MakeButton(row, "+", ButtonColor, () => Step(item, 1f), 52, 110f);
            refreshers.Add(() => value.text = item.GetFloat().ToString(item.Format, CultureInfo.InvariantCulture));
        }

        private void Step(CheatMenu.Item item, float direction)
        {
            Run(() =>
            {
                float next = item.GetFloat() + direction * item.Step;
                if (item.Step > 0f)
                {
                    next = Mathf.Round(next / item.Step) * item.Step; // No 0.30000004 after a few presses.
                }
                item.SetFloat(Mathf.Clamp(next, item.Min, item.Max));
            });
            RefreshAll();
        }

        private void AddNumber(RectTransform row, CheatMenu.Item item)
        {
            RectTransform fieldRect = NewRect("Input", row);
            var fieldImage = fieldRect.gameObject.AddComponent<Image>();
            fieldImage.color = FieldColor;
            var fieldLayout = fieldRect.gameObject.AddComponent<LayoutElement>();
            fieldLayout.preferredWidth = fieldLayout.minWidth = 220f;

            RectTransform area = NewRect("Text Area", fieldRect);
            Stretch(area, 16f, 16f, 6f, 6f);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = MakeText(area, "", FontSize, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            var field = fieldRect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.targetGraphic = fieldImage;
            field.fontAsset = text.font;
            field.pointSize = FontSize;
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
            field.characterLimit = 6;

            MakeButton(row, "Go", ButtonColor, () =>
            {
                if (int.TryParse(field.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    Run(() => item.SetFloat(Mathf.Clamp(value, item.Min, item.Max)));
                }
                RefreshAll();
            }, FontSize, 150f);

            refreshers.Add(() =>
            {
                if (!field.isFocused)
                {
                    field.text = ((int)item.GetFloat()).ToString(CultureInfo.InvariantCulture);
                }
            });
        }

        private void RefreshAll()
        {
            foreach (Action refresh in refreshers)
            {
                Run(refresh);
            }
        }

        /// <summary>A cheat that throws only logs: the panel keeps working.</summary>
        private void Run(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Log.Exception(e, this);
                Log.Warning(Tag, "Cheat failed, see the exception above.");
            }
        }

        // ---- UI helpers -----------------------------------------------------------------------------------------

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static TextMeshProUGUI MakeText(Transform parent, string value, float size, TextAlignmentOptions alignment)
        {
            RectTransform rect = NewRect("Text", parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static Button MakeButton(Transform parent, string label, Color color, Action click, float fontSize = FontSize,
            float width = -1f)
        {
            RectTransform rect = NewRect("Button " + label, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => click());

            TextMeshProUGUI text = MakeText(rect, label, fontSize, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform, 8f, 8f, 0f, 0f);

            if (width > 0f)
            {
                var layout = rect.gameObject.AddComponent<LayoutElement>();
                layout.preferredWidth = layout.minWidth = width;
            }
            return button;
        }
    }
}
#endif
