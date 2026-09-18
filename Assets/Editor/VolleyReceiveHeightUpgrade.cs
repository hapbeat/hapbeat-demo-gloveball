using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using GloveBallDemo.Runtime;

public static class VolleyReceiveHeightUpgrade
{
    public static void RetuneContactOnly()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save changes first.");
        var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        var drill=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
        drill.ReceiveContactHeight=.9f;drill.ReceiveHeightSpread=.1f;drill.ContactForwardDistance=0f;
        // Only feed tuning: preserve floor correction, hand volumes, targets and all manual scene edits.
        foreach(var launcher in drill.FeedLaunchers)
        {
            var aim=launcher.GetComponent<VolleyFeederAim>();var destination=drill.GetContactCentre();
            for(int i=0;i<8;i++)aim.AimForShot(destination,drill.GetFlightSeconds(launcher.MuzzlePosition,destination),Time.fixedDeltaTime);
        }
        EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Save failed");
    }
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save changes first.");
        var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        var drill=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
        drill.ReceiveContactHeight=.9f; drill.ReceiveHeightSpread=.1f; drill.ReceiveMinimumFlightSeconds=.95f;
        drill.ReceiveNet=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BoxCollider>()).Single(c=>c.name=="Volley Net");
        var origin=UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
        var p=origin.CameraFloorOffsetObject.transform.localPosition;p.y=0f;origin.CameraFloorOffsetObject.transform.localPosition=p;
        // CameraYOffset remains the 1.6m fallback for Device mode; it is not added in Floor mode.
        var tracking=origin.GetComponent<VolleyFloorTracking>();
        if(tracking==null)tracking=origin.gameObject.AddComponent<VolleyFloorTracking>(); tracking.Origin=origin;
        foreach(var launcher in drill.FeedLaunchers)
        {
            var aim=launcher.GetComponent<VolleyFeederAim>();
            var destination=drill.GetContactCentre();
            for(int i=0;i<8;i++)aim.AimForShot(destination,drill.GetFlightSeconds(launcher.MuzzlePosition,destination));
        }
        EditorSceneManager.MarkSceneDirty(scene); if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Save failed");
        Debug.Log("[VolleyReceive] Floor-relative low contacts and late XR floor initialization applied. Spike untouched.");
    }
}
