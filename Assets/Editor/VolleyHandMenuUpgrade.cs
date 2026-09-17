using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GloveBallDemo.Runtime;

public static class VolleyHandMenuUpgrade
{
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save manual edits first.");
        var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        var drill=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
        var floor=UnityEngine.Object.FindFirstObjectByType<VolleyFloorTracking>();
        var menu=drill.GetComponent<VolleyHandMenu>();if(menu==null)menu=drill.gameObject.AddComponent<VolleyHandMenu>();
        menu.Drill=drill;menu.Floor=floor;floor.StandingEyeHeight=1.6f;floor.HeightCorrection=0f;
        EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Save failed");
        typeof(UnityEditor.AI.NavMeshVisualizationSettings).GetProperty("showNavMesh",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null,false);
    }
}
