using System.IO;
using GameKit.Gameplay.Level;
using GameKit.Gameplay.Visuals;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameKit.EditorTools.Gameplay
{
    /// <summary>
    /// Builds the tray area art from generated meshes: Tray prefab (rounded tray + 3 glass jars + "+ Unlock" mark)
    /// and WaitingSlot prefab (plate + jar), then puts them in the Main scene as the LevelController templates.
    /// Re-run after changing <see cref="ProceduralMesh"/> shapes.
    /// </summary>
    public static class SandTrayBuilder
    {
        private const string MeshFolder = "Assets/_Project/Source/Art/InGame/Meshes";
        private const string MaterialFolder = "Assets/_Project/Source/Effect/Materials";
        private const string PrefabFolder = "Assets/_Project/Prefabs/InGame/Sand";
        private const float TrayTilt = -72f;
        private const float JarScale = 1.25f;

        /// <summary>Jars in a triangle: two at the back, one in front (tray local space).</summary>
        private static readonly Vector3[] JarPositions =
        {
            new Vector3(-0.17f, 0.07f, 0.14f), new Vector3(0.17f, 0.07f, 0.14f), new Vector3(0f, 0.07f, -0.17f)
        };

        [MenuItem("Tools/GameKit/Sand/Build Tray Prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(PrefabFolder);

            Mesh jar = SaveMesh(ProceduralMesh.Jar());
            Mesh sand = SaveMesh(ProceduralMesh.JarSand());
            Mesh lid = SaveMesh(ProceduralMesh.JarLid());
            Mesh plate = SaveMesh(ProceduralMesh.Plate());
            Mesh trayBase = SaveMesh(ProceduralMesh.TrayBase());
            Mesh trayFloor = SaveMesh(ProceduralMesh.TrayFloor());
            Mesh badge = SaveMesh(ProceduralMesh.Badge());

            Material surface = SandLevelTools.CreateSurfaceMaterial();
            Material glass = MaterialAsset("Glass", "GameKit/Glass", null);
            Material green = MaterialAsset("SandBadge", "GameKit/SandSurface", new Color(0.36f, 0.78f, 0.38f));
            Material white = MaterialAsset("SandWhite", "GameKit/SandSurface", Color.white);

            GameObject trayPrefab = BuildTray(trayBase, trayFloor, jar, sand, lid, badge, surface, glass, green, white);
            GameObject slotPrefab = BuildWaitingSlot(plate, jar, sand, lid, surface, glass);
            PlaceInScene(trayPrefab, slotPrefab);
            Debug.Log("[SandTrayBuilder] Tray and WaitingSlot prefabs built and placed in the scene.");
        }

        private static GameObject BuildTray(Mesh trayBase, Mesh trayFloor, Mesh jar, Mesh sand, Mesh lid, Mesh badge,
            Material surface, Material glass, Material green, Material white)
        {
            var root = new GameObject("Tray");
            try
            {
                var collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.84f, 0.5f, 0.8f);
                collider.center = new Vector3(0f, 0.18f, 0f);

                Renderer body = Part(root.transform, "Body", trayBase, surface, Vector3.zero);
                Renderer floor = Part(root.transform, "Floor", trayFloor, surface, new Vector3(0f, 0.055f, 0f));

                var jars = new JarView[3];
                for (int i = 0; i < 3; i++)
                {
                    jars[i] = Jar(root.transform, $"Jar_{i}", JarPositions[i], jar, sand, lid, surface, glass);
                    jars[i].transform.localScale = Vector3.one * JarScale;
                }

                var lockMark = new GameObject("LockMark");
                lockMark.transform.SetParent(root.transform, false);
                Transform badgeRoot = new GameObject("BadgeRoot").transform;
                badgeRoot.SetParent(lockMark.transform, false);
                badgeRoot.localPosition = new Vector3(0f, 0f, 0.13f);
                badgeRoot.localScale = Vector3.one * 1.5f;
                Part(badgeRoot, "Badge", badge, green, new Vector3(0f, 0.045f, 0f));
                Box(badgeRoot, "Plus_H", white, new Vector3(0f, 0.09f, 0f), new Vector3(0.1f, 0.012f, 0.026f));
                Box(badgeRoot, "Plus_V", white, new Vector3(0f, 0.09f, 0f), new Vector3(0.026f, 0.012f, 0.1f));
                Label(lockMark.transform, "Unlock", new Vector3(0f, 0.075f, -0.19f));

                var view = root.AddComponent<TrayView>();
                var so = new SerializedObject(view);
                so.FindProperty("body").objectReferenceValue = body;
                so.FindProperty("floor").objectReferenceValue = floor;
                SerializedProperty jarList = so.FindProperty("jars");
                jarList.arraySize = 3;
                for (int i = 0; i < 3; i++)
                {
                    jarList.GetArrayElementAtIndex(i).objectReferenceValue = jars[i];
                }
                so.FindProperty("lockMark").objectReferenceValue = lockMark;
                so.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/Tray.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildWaitingSlot(Mesh plate, Mesh jar, Mesh sand, Mesh lid, Material surface, Material glass)
        {
            var root = new GameObject("WaitingSlot");
            try
            {
                Renderer plateRenderer = Part(root.transform, "Plate", plate, surface, Vector3.zero);
                JarView jarView = Jar(root.transform, "Jar", new Vector3(0f, 0.026f, 0f), jar, sand, lid, surface, glass);
                jarView.transform.localScale = Vector3.one * JarScale;
                jarView.gameObject.SetActive(false);

                var view = root.AddComponent<WaitingSlotView>();
                var so = new SerializedObject(view);
                so.FindProperty("plate").objectReferenceValue = plateRenderer;
                so.FindProperty("jar").objectReferenceValue = jarView;
                so.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/WaitingSlot.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static JarView Jar(Transform parent, string name, Vector3 position, Mesh jar, Mesh sand, Mesh lid,
            Material surface, Material glass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            Renderer sandRenderer = Part(go.transform, "Sand", sand, surface, Vector3.zero);
            Part(go.transform, "Glass", jar, glass, Vector3.zero);
            Renderer lidRenderer = Part(go.transform, "Lid", lid, surface, Vector3.zero);
            lidRenderer.gameObject.SetActive(false);

            var view = go.AddComponent<JarView>();
            var so = new SerializedObject(view);
            so.FindProperty("sand").objectReferenceValue = sandRenderer.transform;
            so.FindProperty("sandRenderer").objectReferenceValue = sandRenderer;
            so.FindProperty("lid").objectReferenceValue = lidRenderer.transform;
            so.FindProperty("lidRenderer").objectReferenceValue = lidRenderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        private static Renderer Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static void Box(Transform parent, string name, Material material, Vector3 position, Vector3 scale)
        {
            Renderer r = Part(parent, name, Resources.GetBuiltinResource<Mesh>("Cube.fbx"), material, position);
            r.transform.localScale = scale;
        }

        private static void Label(Transform parent, string text, Vector3 position)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 1.5f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            // No outline: setting it in edit mode makes TMP create a material copy inside the prefab.
            tmp.color = new Color(0.22f, 0.26f, 0.33f);
            tmp.rectTransform.sizeDelta = new Vector2(0.7f, 0.16f);
        }

        private static Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{MeshFolder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material MaterialAsset(string name, string shaderName, Color? color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(shaderName);
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            if (color.HasValue)
            {
                material.SetColor("_Color", color.Value);
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        private static void PlaceInScene(GameObject trayPrefab, GameObject slotPrefab)
        {
            var controller = Object.FindObjectOfType<LevelController>(true);
            if (controller == null)
            {
                Debug.LogWarning("[SandTrayBuilder] No LevelController in the open scene; prefabs built only.");
                return;
            }

            var so = new SerializedObject(controller);
            var rows = so.FindProperty("rowsRoot").objectReferenceValue as Transform;
            if (rows == null)
            {
                Debug.LogWarning("[SandTrayBuilder] LevelController has no Rows root.");
                return;
            }

            ReplaceTemplate(so, "trayTemplate");
            ReplaceTemplate(so, "waitingTemplate");

            var tray = (GameObject)PrefabUtility.InstantiatePrefab(trayPrefab, rows);
            tray.name = "TrayTemplate";
            tray.transform.localEulerAngles = new Vector3(TrayTilt, 0f, 0f);
            tray.transform.localScale = Vector3.one;
            tray.SetActive(false);

            var slot = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, rows);
            slot.name = "WaitingSlotTemplate";
            slot.transform.localEulerAngles = new Vector3(TrayTilt, 0f, 0f);
            slot.SetActive(false);

            so.FindProperty("trayTemplate").objectReferenceValue = tray.GetComponent<TrayView>();
            so.FindProperty("waitingTemplate").objectReferenceValue = slot.GetComponent<WaitingSlotView>();
            so.FindProperty("traySpacing").floatValue = 0.86f;
            so.FindProperty("trayRowY").floatValue = 2.35f;
            so.FindProperty("waitingRowY").floatValue = 1.5f;
            so.FindProperty("waitingSpacing").floatValue = 0.58f;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveScene(controller.gameObject.scene);
        }

        private static void ReplaceTemplate(SerializedObject so, string field)
        {
            var old = so.FindProperty(field).objectReferenceValue as Component;
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }
        }
    }
}
