using System.Collections.Generic;
using GameKit.Gameplay.Level;
using GameKit.Gameplay.Voxel;
using UnityEditor;
using UnityEngine;

namespace GameKit.EditorTools.Gameplay
{
    /// <summary>
    /// The sand blocks are drawn by code (no GameObject per block), so the Scene view cannot select them. This window
    /// does instead: in Play mode, click the model in the Scene view to pick the block there and see its index, name,
    /// colour, layers (parent / children), state and whether it can be tapped; the picked block gets a box in the
    /// Scene view. Tools/GameKit/Sand/Block Picker.
    /// </summary>
    public sealed class BlockPickerWindow : EditorWindow
    {
        private static readonly Color HighlightColor = new Color(1f, 0.85f, 0.1f, 1f);

        private bool picking = true;
        private int selected = -1;
        /// <summary>Block that owns the clicked cell when it is a hidden layer drawn as part of <see cref="selected"/>.</summary>
        private int hiddenUnderClick = -1;
        private Vector2 scroll;

        [MenuItem("Tools/GameKit/Sand/Block Picker")]
        public static void Open()
        {
            // A tab next to the Inspector, so it never takes the Inspector's place.
            var window = GetWindow<BlockPickerWindow>("Block Picker", true, Cheat.CheatWindow.InspectorType);
            window.minSize = new Vector2(260f, 220f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.RepaintAll();
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying && selected >= 0)
            {
                Repaint(); // State changes while playing (tapped, draining, revealed).
            }
        }

        private static SandModelRenderer Model => EditorApplication.isPlaying ? FindObjectOfType<SandModelRenderer>() : null;

        private static LevelController Controller => EditorApplication.isPlaying ? FindObjectOfType<LevelController>() : null;

        private void OnGUI()
        {
            picking = EditorGUILayout.ToggleLeft(new GUIContent("Pick in Scene view (left click)",
                "While on, a left click on the sand model in the Scene view picks a block instead of selecting objects."), picking);
            if (picking)
            {
                SceneView.RepaintAll();
            }

            SandModelRenderer model = Model;
            if (model == null || model.Grid == null)
            {
                EditorGUILayout.HelpBox("Enter Play mode, then click the sand model in the Scene view.", MessageType.Info);
                return;
            }
            if (selected < 0 || selected >= model.BlockCount)
            {
                EditorGUILayout.HelpBox("Click a block of the sand model in the Scene view.", MessageType.Info);
                return;
            }

            LevelController controller = Controller;
            SandLevelData level = controller != null ? controller.CurrentLevel : null;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawBlock(model, controller, level);
            EditorGUILayout.EndScrollView();
        }

        private void DrawBlock(SandModelRenderer model, LevelController controller, SandLevelData level)
        {
            int b = selected;
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Block " + b, EditorStyles.boldLabel);
            if (level != null && b < level.blocks.Count)
            {
                EditorGUILayout.SelectableLabel(level.blocks[b].name, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField("Colour index", level.blocks[b].colorIndex.ToString());
            }
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ColorField("Colour", model.GetBlockColor(b));
            }

            string state = model.IsCollected(b) ? "Collected (" + model.GetBlockPhase(b) + ")"
                : model.IsHiddenLayer(b) ? "Hidden layer (its outer block is still there)"
                : "On the model";
            EditorGUILayout.LabelField("State", state);
            if (controller != null && controller.Board != null && b < controller.Board.BlockCount)
            {
                EditorGUILayout.LabelField("Can tap", controller.Board.CanTap(b) ? "Yes" : "No");
            }
            EditorGUILayout.LabelField("Grains", model.GetBlockCellCount(b).ToString());
            if (hiddenUnderClick >= 0)
            {
                EditorGUILayout.HelpBox($"The clicked grain belongs to hidden layer {hiddenUnderClick}, drawn as part of this block.",
                    MessageType.None);
            }

            if (level == null || b >= level.blocks.Count)
            {
                return;
            }
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
            int parent = level.blocks[b].parent;
            if (parent >= 0)
            {
                BlockButton("Outer (parent)", parent, level);
            }
            else
            {
                EditorGUILayout.LabelField("Outer (parent)", "none (top level)");
            }
            var children = new List<int>();
            for (int i = 0; i < level.blocks.Count; i++)
            {
                if (level.blocks[i].parent == b)
                {
                    children.Add(i);
                }
            }
            if (children.Count == 0)
            {
                EditorGUILayout.LabelField("Inner (children)", "none");
            }
            foreach (int child in children)
            {
                BlockButton("Inner (child)", child, level);
            }
        }

        private void BlockButton(string label, int block, SandLevelData level)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            if (GUILayout.Button($"{block}: {level.blocks[block].name}", EditorStyles.miniButton))
            {
                selected = block;
                hiddenUnderClick = -1;
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void OnSceneGui(SceneView view)
        {
            SandModelRenderer model = Model;
            if (model == null || model.Grid == null)
            {
                return;
            }

            Event e = Event.current;
            if (picking)
            {
                int id = GUIUtility.GetControlID(FocusType.Passive);
                if (e.type == EventType.Layout)
                {
                    HandleUtility.AddDefaultControl(id); // Clicks pick blocks instead of selecting objects.
                }
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    Pick(model, HandleUtility.GUIPointToWorldRay(e.mousePosition));
                    e.Use();
                }
            }

            if (e.type == EventType.Repaint && selected >= 0 && model.TryGetBlockBounds(selected, out Bounds bounds))
            {
                Matrix4x4 previous = Handles.matrix;
                Handles.matrix = model.transform.localToWorldMatrix;
                Handles.color = HighlightColor;
                Handles.DrawWireCube(bounds.center, bounds.size);
                Handles.matrix = previous;
                Handles.Label(model.transform.TransformPoint(bounds.center + Vector3.up * bounds.extents.y), "Block " + selected,
                    EditorStyles.whiteBoldLabel);
            }
        }

        private void Pick(SandModelRenderer model, Ray ray)
        {
            int cell = model.RaycastCell(ray);
            if (cell < 0)
            {
                selected = -1;
                hiddenUnderClick = -1;
            }
            else
            {
                int owner = model.Grid[cell];
                selected = model.VisibleBlock(owner);
                hiddenUnderClick = owner != selected ? owner : -1;
            }
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
