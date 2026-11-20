using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityToolbarExtender;

internal static class ToolbarStyles
{
    public static readonly GUIStyle commandButtonStyle;

    static ToolbarStyles()
    {
        commandButtonStyle = new GUIStyle("Command")
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            imagePosition = ImagePosition.ImageAbove,
            fontStyle = FontStyle.Normal,
            fixedWidth = 100f
        };
    }
}

[InitializeOnLoad]
public class SceneSwitchButtonConfig
{
    // Scene paths shown on the right side of the toolbar, refreshed when the project changes.
    private static List<string> scenePaths = new List<string>();

    static SceneSwitchButtonConfig()
    {
        RefreshScenes();
        EditorApplication.projectChanged += RefreshScenes;

        ToolbarExtender.LeftToolbarGUI.Add(OnLeftToolbarGUI);
        ToolbarExtender.RightToolbarGUI.Add(OnRightToolbarGUI);
    }

    private static void RefreshScenes()
    {
        scenePaths.Clear();
        if (!AssetDatabase.IsValidFolder(UISupport.ScenesFolder))
        {
            return;
        }

        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { UISupport.ScenesFolder }))
        {
            scenePaths.Add(AssetDatabase.GUIDToAssetPath(guid));
        }
        scenePaths.Sort();
    }

    private static void OnLeftToolbarGUI()
    {
        GUILayout.FlexibleSpace();

        foreach (var tool in UISupport.Tools)
        {
            if (GUILayout.Button(new GUIContent(tool.Label, "Open " + tool.MenuPath), ToolbarStyles.commandButtonStyle))
            {
                if (!EditorApplication.ExecuteMenuItem(tool.MenuPath))
                {
                    Debug.LogWarning("Toolbar: menu item not found: " + tool.MenuPath);
                }
            }
        }
    }

    private static void OnRightToolbarGUI()
    {
        foreach (var path in scenePaths)
        {
            var sceneName = Path.GetFileNameWithoutExtension(path);
            if (GUILayout.Button(new GUIContent(sceneName, "Open " + path), ToolbarStyles.commandButtonStyle))
            {
                SceneHelper.OpenScene(path);
            }
        }
        GUILayout.FlexibleSpace();
    }
}

internal static class SceneHelper
{
    private static string sceneToOpen;

    public static void OpenScene(string scenePath)
    {
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
        }

        sceneToOpen = scenePath;
        EditorApplication.update += OnUpdate;
    }

    private static void OnUpdate()
    {
        if (sceneToOpen == null ||
            EditorApplication.isPlaying || EditorApplication.isPaused ||
            EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        EditorApplication.update -= OnUpdate;

        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            if (File.Exists(sceneToOpen))
            {
                EditorSceneManager.OpenScene(sceneToOpen);
            }
            else
            {
                Debug.LogWarning("Toolbar: couldn't find scene file " + sceneToOpen);
            }
        }

        sceneToOpen = null;
    }
}
