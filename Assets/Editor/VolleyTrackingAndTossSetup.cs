using System;
using System.Linq;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Focused, non-destructive update of existing volley scenes. Never regenerates a scene.</summary>
public static class VolleyTrackingAndTossSetup
{
    public static void ApplyFeedback()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save manual edits first.");
        var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity");
        var aerial=UnityEngine.Object.FindFirstObjectByType<VolleyAerialSequence>();
        aerial.Jump.UseHeightThreshold=true;
        aerial.Jump.RearmHeightFromEyes=-.25f;
        aerial.Jump.JumpHeightFromEyes=-.05f;
        aerial.TossLauncher.GetComponent<VolleyFeederAim>().ShotClip=
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/UltimateGloveBall/Sound/ball_launch_bland.wav");
        const string spikePath="Assets/GloveBallDemo/Audio/BallImpacts/VolleyballImpact.wav";
        aerial.SpikeClip=AssetDatabase.LoadAssetAtPath<AudioClip>(spikePath);
        if(aerial.SpikeClip==null)throw new InvalidOperationException("Missing spike audio");
        var importer=(AudioImporter)AssetImporter.GetAtPath(spikePath);
        var settings=importer.defaultSampleSettings;settings.preloadAudioData=true;
        importer.defaultSampleSettings=settings;importer.SaveAndReimport();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Save failed");
    }
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save manual edits first.");
        foreach(var name in new[]{"VolleyReceive-codex","VolleyBlock-codex"})
        {
            var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/"+name+".unity");
            var drill=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
            drill.TrackingGraceSeconds=1.5f;
            drill.Left.VisualHoldSeconds=drill.Right.VisualHoldSeconds=1.5f;
            var warning=UnityEngine.Object.FindFirstObjectByType<VolleyTrackingWarning>();
            warning.Drill=drill;
            var rect=(RectTransform)warning.transform;
            rect.anchoredPosition=Vector2.zero;rect.localScale=Vector3.one*.0015f;
            foreach(var graphic in warning.Graphics)graphic.enabled=false;
            if(drill.Aerial!=null)
            {
                var aerial=drill.Aerial;
                aerial.Jump.TrackingGraceSeconds=1.5f;
                aerial.TossLauncher=drill.FeedLaunchers[0];
                var launcher=aerial.TossLauncher;
                launcher.gameObject.SetActive(true);
                launcher.enabled=false; // Only VolleyDrillController schedules balls.
                var position=aerial.Opponent.transform.position;
                launcher.transform.position=new Vector3(position.x-3f,launcher.transform.position.y,position.z+.7f);
                launcher.GetComponent<VolleyFeederAim>().AimForShot(aerial.Opponent.ReleasePosition,aerial.WindupSeconds);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Save failed: "+name);
        }
        EditorBuildSettings.scenes=EditorBuildSettings.scenes
            .Select(s=>s.path.EndsWith("VolleyJumpSpike-codex.unity") ? new EditorBuildSettingsScene(s.path,false) : s).ToArray();
        EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
    }
}
