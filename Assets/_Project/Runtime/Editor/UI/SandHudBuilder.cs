using System.IO;
using GameKit.App;
using GameKit.Gameplay.Level;
using GameKit.Gameplay.UI;
using GameKit.UI.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameKit.EditorTools.UI
{
    /// <summary>
    /// Builds the in-level HUD in Main (Canvas_HUD/SafeArea/LevelHud): pause button, level title, level progress row,
    /// zoom slider and recenter button, using rounded "chunky" sprites generated here (white, tinted per Image).
    /// Re-run to rebuild after changing the layout below.
    /// </summary>
    public static class SandHudBuilder
    {
        private const string SpriteFolder = "Assets/_Project/Source/Art/UI/Common";
        private const string RoundedPath = SpriteFolder + "/UI_Rounded.png";
        private const string CirclePath = SpriteFolder + "/UI_Circle.png";
        private const int Lip = 14;

        private static readonly Color Blue = new Color(0.22f, 0.56f, 1f);
        private static readonly Color Ink = new Color(0.2f, 0.25f, 0.45f);

        [MenuItem("Tools/GameKit/Sand/Build HUD")]
        public static void Build()
        {
            Sprite rounded = MakeSprite(RoundedPath, 128, 128, 40, new Vector4(44, 44 + Lip, 44, 44));
            Sprite circle = MakeSprite(CirclePath, 128, 128, 64, Vector4.zero);

            var bootstrap = Object.FindObjectOfType<GameBootstrap>(true);
            Transform safe = FindSafeArea();
            if (bootstrap == null || safe == null)
            {
                Debug.LogError("[SandHudBuilder] Open the Main scene (needs GameBootstrap and Canvas_HUD/SafeArea).");
                return;
            }

            Transform old = safe.Find("LevelHud");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            RectTransform root = Rect("LevelHud", safe);
            Stretch(root);
            var hud = root.gameObject.AddComponent<LevelHud>();

            // Pause (top-left).
            RectTransform pause = Rect("PauseButton", root);
            Place(pause, new Vector2(0f, 1f), new Vector2(36f, -36f), new Vector2(130f, 130f));
            Button pauseButton = ButtonOn(pause, rounded, Blue);
            for (int i = 0; i < 2; i++)
            {
                RectTransform bar = Rect($"Bar_{i}", pause);
                Place(bar, new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -17f : 17f, 6f), new Vector2(22f, 54f));
                ImageOn(bar, rounded, Color.white, false);
            }

            // Title.
            RectTransform title = Rect("LevelText", root);
            Place(title, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(600f, 90f));
            TMP_Text titleText = Text(title, "Level 1", 64, Ink);

            // Progress row.
            RectTransform row = Rect("LevelProgress", root);
            Place(row, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(560f, 100f));
            RectTransform line = Rect("Line", row);
            Place(line, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430f, 12f));
            ImageOn(line, rounded, new Color(0.7f, 0.75f, 0.92f), false);
            var cells = new Image[5];
            var labels = new TMP_Text[5];
            for (int i = 0; i < 5; i++)
            {
                RectTransform cell = Rect($"Cell_{i}", row);
                Place(cell, new Vector2(0.5f, 0.5f), new Vector2((i - 2) * 106f, 0f), new Vector2(74f, 74f));
                cells[i] = ImageOn(cell, rounded, Color.white, false);
                RectTransform label = Rect("Label", cell);
                Stretch(label);
                label.offsetMin = new Vector2(0f, Lip * 0.5f);
                labels[i] = Text(label, (i + 1).ToString(), 34, Ink);
            }

            // Recenter (left).
            RectTransform recenter = Rect("RecenterButton", root);
            Place(recenter, new Vector2(0f, 0.5f), new Vector2(36f, -40f), new Vector2(110f, 110f));
            Button recenterButton = ButtonOn(recenter, circle, Color.white);
            RectTransform ring = Rect("Ring", recenter);
            Place(ring, new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(56f, 56f));
            ImageOn(ring, circle, Blue, false);
            RectTransform hole = Rect("Hole", recenter);
            Place(hole, new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(34f, 34f));
            ImageOn(hole, circle, Color.white, false);
            RectTransform dot = Rect("Dot", recenter);
            Place(dot, new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(16f, 16f));
            ImageOn(dot, circle, Blue, false);

            // Zoom slider (left, below recenter).
            Slider zoom = BuildSlider(root, rounded, circle);

            var so = new SerializedObject(hud);
            so.FindProperty("levelText").objectReferenceValue = titleText;
            so.FindProperty("pauseButton").objectReferenceValue = pauseButton;
            SerializedProperty cellList = so.FindProperty("progressCells");
            SerializedProperty labelList = so.FindProperty("progressLabels");
            cellList.arraySize = 5;
            labelList.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                cellList.GetArrayElementAtIndex(i).objectReferenceValue = cells[i];
                labelList.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
            }
            so.FindProperty("zoomSlider").objectReferenceValue = zoom;
            so.FindProperty("recenterButton").objectReferenceValue = recenterButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            var bootSo = new SerializedObject(bootstrap);
            bootSo.FindProperty("levelHud").objectReferenceValue = hud;
            bootSo.ApplyModifiedPropertiesWithoutUndo();

            // Make room for the HUD: trays and waiting row a bit lower.
            var controller = Object.FindObjectOfType<LevelController>(true);
            if (controller != null)
            {
                var cso = new SerializedObject(controller);
                cso.FindProperty("trayRowY").floatValue = 2.02f;
                cso.FindProperty("waitingRowY").floatValue = 1.24f;
                cso.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            EditorSceneManager.SaveScene(bootstrap.gameObject.scene);
            Debug.Log("[SandHudBuilder] HUD built.");
        }

        private static Slider BuildSlider(RectTransform parent, Sprite rounded, Sprite circle)
        {
            RectTransform area = Rect("ZoomSlider", parent);
            Place(area, new Vector2(0f, 0.5f), new Vector2(61f, -150f), new Vector2(60f, 420f));
            area.pivot = new Vector2(0f, 1f);

            RectTransform plus = Rect("Plus", area);
            Place(plus, new Vector2(0.5f, 1f), new Vector2(0f, 10f), new Vector2(60f, 60f));
            Text(plus, "+", 56, Blue);
            RectTransform minus = Rect("Minus", area);
            Place(minus, new Vector2(0.5f, 0f), new Vector2(0f, -8f), new Vector2(60f, 60f));
            minus.pivot = new Vector2(0.5f, 1f);
            Text(minus, "-", 64, Blue);

            RectTransform track = Rect("Track", area);
            Place(track, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(30f, 300f));
            ImageOn(track, rounded, new Color(1f, 1f, 1f, 0.85f), false);

            RectTransform slideArea = Rect("HandleArea", area);
            Place(slideArea, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(30f, 250f));
            RectTransform handle = Rect("Handle", slideArea);
            handle.sizeDelta = new Vector2(70f, 70f);
            Image handleImage = ImageOn(handle, circle, Blue, true);

            var slider = area.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.BottomToTop;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.minValue = 0.7f;
            slider.maxValue = 1.5f;
            slider.value = 1f;
            return slider;
        }

        private static Transform FindSafeArea()
        {
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "SafeArea" && t.parent != null && t.parent.name == "Canvas_HUD")
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static Image ImageOn(RectTransform rt, Sprite sprite, Color color, bool raycast)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static Button ButtonOn(RectTransform rt, Sprite sprite, Color color)
        {
            Image image = ImageOn(rt, sprite, color, true);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            rt.gameObject.AddComponent<ButtonPress>();
            return button;
        }

        private static TMP_Text Text(RectTransform rt, string value, float size, Color color)
        {
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = value;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>
        /// White rounded rectangle (radius = half size gives a circle) with a darker bottom lip for the "chunky"
        /// look; tinted per Image. Saved as a sprite (9-sliced when a border is given).
        /// </summary>
        private static Sprite MakeSprite(string path, int width, int height, float radius, Vector4 border)
        {
            Directory.CreateDirectory(SpriteFolder);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            Vector2 half = new Vector2(width, height) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Signed distance to the rounded rect (shape), and to the same rect raised by the lip (face).
                    float shape = RoundedDistance(new Vector2(x + 0.5f, y + 0.5f) - half, half, radius);
                    float face = RoundedDistance(new Vector2(x + 0.5f, y + 0.5f - Lip * 0.5f) - half, half - new Vector2(0f, Lip * 0.5f), radius);
                    float alpha = Mathf.Clamp01(0.5f - shape);
                    float isFace = Mathf.Clamp01(0.5f - face);
                    float shade = Mathf.Lerp(0.72f, 1f, isFace);
                    pixels[y * width + x] = new Color(shade, shade, shade, alpha);
                }
            }
            tex.SetPixels32(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static float RoundedDistance(Vector2 p, Vector2 half, float radius)
        {
            radius = Mathf.Min(radius, Mathf.Min(half.x, half.y));
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (half - Vector2.one * radius);
            Vector2 outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }
    }
}
