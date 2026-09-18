using System;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Focused configuration for the block-and-spike rally. It only edits VolleyBlock-codex; it never rebuilds a scene.</summary>
public static class VolleyRallySetup
{
    const string ScenePath="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";

    [MenuItem("Hapbeat/Volley/Apply block + spike rally")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before applying the rally setup.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save manual edits first.");
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        var drill=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
        if(drill==null || drill.Aerial==null || drill.ReceiveNet==null)throw new InvalidOperationException("VolleyBlock scene is missing its drill, aerial sequence, or net.");
        var aerial=drill.Aerial;
        if(drill.FeedLaunchers==null || drill.FeedLaunchers.Length<2)throw new InvalidOperationException("Rally requires two existing feed launchers.");
        var forward=Vector3.ProjectOnPlane(drill.CourtFrame.forward,Vector3.up).normalized;
        var right=Vector3.Cross(Vector3.up,forward);
        var net=drill.ReceiveNet.transform.position;

        aerial.RallyEnabled=true;
        aerial.TossLauncher=drill.FeedLaunchers[0];
        aerial.TossLauncher.gameObject.SetActive(true);
        aerial.TossLauncher.enabled=false;
        aerial.AllyTossLauncher=drill.FeedLaunchers[1];
        aerial.AllyTossLauncher.gameObject.SetActive(true);
        aerial.AllyTossLauncher.enabled=false;
        aerial.AllyTossLauncher.transform.SetPositionAndRotation(net-forward*3.4f+right*2.25f,Quaternion.LookRotation(forward,Vector3.up));

        var ally=UnityEngine.Object.FindFirstObjectByType<VolleyOpponentPrototype>(FindObjectsInactive.Include);
        foreach(var candidate in UnityEngine.Object.FindObjectsByType<VolleyOpponentPrototype>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(candidate.gameObject.name=="Rally friendly setter"){ally=candidate;break;}
        if(ally==null || ally==aerial.Opponent)
        {
            if(aerial.Opponent==null)throw new InvalidOperationException("Block opponent is missing.");
            ally=UnityEngine.Object.Instantiate(aerial.Opponent);
            ally.name="Rally friendly setter";
        }
        ally.Animate=false;ally.PreviewOnly=false;ally.PreviewBall=null;ally.Target=null;ally.Status=null;
        ally.Role=VolleyOpponentPrototype.MotionRole.Set;
        ally.Variant=VolleyOpponentPrototype.MotionVariant.A_Readable;
        ally.transform.SetPositionAndRotation(net-forward*2.15f+right*1.35f,Quaternion.LookRotation(forward,Vector3.up));
        ally.PreviewPhase=0f;ally.Pose(0f);
        aerial.Ally=ally;

        drill.MaximumBallAge=4.5f;
        ConfigureFloorTargets(drill,net);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Failed to save VolleyBlock-codex.");
    }

    static void ConfigureFloorTargets(VolleyDrillController drill,Vector3 net)
    {
        var targets=drill.Targets;
        targets.RandomizeOnStart=true;
        targets.AlternateHorizontalThirds=true;
        targets.ZoneEdgeInset=.16f;
        var serialized=new SerializedObject(targets);
        Set(serialized,"_minX",net.x-2.8f);Set(serialized,"_maxX",net.x+2.8f);
        Set(serialized,"_minZ",net.z+1.65f);Set(serialized,"_maxZ",net.z+5.4f);
        Set(serialized,"_minHeight",.08f);Set(serialized,"_maxHeight",.14f);
        serialized.FindProperty("_minimumSpacing").floatValue=1.5f;
        serialized.FindProperty("_faceUp").boolValue=true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Set(SerializedObject serialized,string name,float value)
    {
        var property=serialized.FindProperty(name);
        if(property==null)throw new InvalidOperationException("Target placement property is missing: "+name);
        property.floatValue=value;
    }
}
