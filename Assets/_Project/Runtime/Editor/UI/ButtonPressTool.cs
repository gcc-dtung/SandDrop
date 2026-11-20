using System.Collections.Generic;
using GameKit.UI.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameKit.EditorTools.UI
{
    /// <summary>Adds <see cref="ButtonPress"/> to every Button that has neither ButtonPress nor ButtonPressIgnore.</summary>
    public static class ButtonPressTool
    {
        [MenuItem("Tools/GameKit/UI/Add ButtonPress To Buttons")]
        private static void AddToOpenSceneOrPrefab()
        {
            var roots = new List<GameObject>();
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                roots.Add(prefabStage.prefabContentsRoot);
            }
            else
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
                }
            }

            int added = 0;
            foreach (var root in roots)
            {
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                {
                    if (button.GetComponent<ButtonPress>() != null || button.GetComponent<ButtonPressIgnore>() != null)
                    {
                        continue;
                    }
                    Undo.AddComponent<ButtonPress>(button.gameObject);
                    added++;
                }
            }

            if (added > 0)
            {
                if (prefabStage != null) EditorSceneManager.MarkSceneDirty(prefabStage.scene);
                else EditorSceneManager.MarkAllScenesDirty();
            }

            var where = prefabStage != null ? prefabStage.prefabContentsRoot.name : "open scenes";
            Debug.Log($"[ButtonPress] Added to {added} button(s) in {where}.");
        }
    }
}
