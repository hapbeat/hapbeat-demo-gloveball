using System;
using System.Linq;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Focused configuration for the spike/block turn rally. It only edits VolleyBlock-codex; it never rebuilds a scene.</summary>
public static class VolleyRallySetup
{
    const string ScenePath="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
    const string AllyName="Rally friendly setter";
    const string BlockerPrefix="Opponent blocker ";

    [MenuItem("Hapbeat/Volley/Apply spike + block turn rally")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before applying the rally setup.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save manual edits first.");
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        var drill=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
        if(drill==null || drill.Aerial==null || drill.ReceiveNet==null)throw new InvalidOperationException("VolleyBlock scene is missing its drill, aerial sequence, or net.");
        var aerial=drill.Aerial;
        if(aerial.Opponent==null)throw new InvalidOperationException("Block opponent is missing.");
        if(drill.FeedLaunchers==null || drill.FeedLaunchers.Length<1)throw new InvalidOperationException("Rally requires the opponent toss launcher.");

        aerial.RallyEnabled=true;
        aerial.TossLauncher=drill.FeedLaunchers[0];
        aerial.TossLauncher.gameObject.SetActive(true);
        aerial.TossLauncher.enabled=false;
        // The friendly pass now drops from above; the former second feeder stays hidden.
        for(int i=1;i<drill.FeedLaunchers.Length;i++)drill.FeedLaunchers[i].gameObject.SetActive(false);

        var opponent=aerial.Opponent;
        opponent.OverrideKit=true;opponent.JerseyColour=aerial.OpponentJersey;opponent.ShortsColour=aerial.OpponentShorts;opponent.ApplyKit();

        var actors=UnityEngine.Object.FindObjectsByType<VolleyOpponentPrototype>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        // Idempotent: existing cast members keep their scene identity and are only reconfigured.
        var ally=actors.FirstOrDefault(a=>a.gameObject.name==AllyName);
        if(ally==null)ally=VolleyAerialSequence.CreateAlly(opponent,aerial.AllyJersey,aerial.AllyShorts);
        else VolleyAerialSequence.ConfigureActor(ally,VolleyOpponentPrototype.MotionRole.Set,aerial.AllyJersey,aerial.AllyShorts);
        var forward=Vector3.ProjectOnPlane(drill.CourtFrame.forward,Vector3.up).normalized;
        var right=Vector3.Cross(Vector3.up,forward);
        var net=drill.ReceiveNet.transform.position;net.y=drill.CourtFrame.position.y;
        ally.transform.SetPositionAndRotation(net+right*aerial.AllyNetOffset.x+forward*aerial.AllyNetOffset.y,Quaternion.LookRotation(-right,Vector3.up));
        aerial.Ally=ally;

        aerial.Blockers=new VolleyOpponentPrototype[3];
        for(int i=0;i<aerial.Blockers.Length;i++)
        {
            var blocker=actors.FirstOrDefault(a=>a.gameObject.name==BlockerPrefix+(i+1));
            if(blocker==null)blocker=VolleyAerialSequence.CreateBlocker(opponent,i,aerial,aerial.OpponentJersey,aerial.OpponentShorts);
            else
            {
                VolleyAerialSequence.ConfigureActor(blocker,VolleyOpponentPrototype.MotionRole.Block,aerial.OpponentJersey,aerial.OpponentShorts);
                blocker.GetComponent<VolleyBlockerContact>().Rally=aerial;
            }
            blocker.transform.SetPositionAndRotation(net+forward*aerial.BlockerStandbyDistance+right*((i-1)*1.8f),Quaternion.LookRotation(-forward,Vector3.up));
            aerial.Blockers[i]=blocker;
        }

        foreach(var blocker in aerial.Blockers)blocker.JumpHeight=.7f;
        // Serialized scene values win over code defaults, so the tuned rally values are written explicitly.
        aerial.AllyFlightSeconds=1.4f;       // higher set
        aerial.RallyResetSeconds=2f;         // landing ball and marker stay visible
        aerial.SpikeJumpHeight=1.3f;aerial.SpikeJumpSeconds=1.15f;
        aerial.BlockJumpHeight=.85f;aerial.BlockJumpSeconds=1f;
        aerial.MatchPoints=7;
        // Fast overhead swings exceed 8 m/s in tracking space; the old limit flagged them as tracking jumps.
        drill.Left.MaximumTrackedSpeed=drill.Right.MaximumTrackedSpeed=15f;
        drill.Left.MaximumHandSpeed=drill.Right.MaximumHandSpeed=20f;
        drill.MaximumReturnSpeed=18f;        // room for a hard downward spike
        drill.MaximumBallAge=6f;
        ConfigureFloorTargets(drill,net);
        EditorUtility.SetDirty(aerial);
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
