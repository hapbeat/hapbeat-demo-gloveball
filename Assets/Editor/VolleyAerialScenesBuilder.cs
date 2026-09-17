using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GloveBallDemo.Runtime;

public static class VolleyAerialScenesBuilder
{
    const string Folder="Assets/GloveBallDemo/Scenes/";
    public static void Create()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before creating scenes.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save manual edits first.");
        if(File.Exists(Folder+"VolleyJumpSpike-codex.unity") || File.Exists(Folder+"VolleyBlock-codex.unity"))
            throw new InvalidOperationException("Aerial scenes already exist; do not regenerate.");
        CreateOne(false);CreateOne(true);
        EditorSceneManager.OpenScene(Folder+"VolleyJumpSpike-codex.unity");
    }
    static void Float(SerializedObject so,string name,float value)=>so.FindProperty(name).floatValue=value;
    static void CreateOne(bool block)
    {
        string path=Folder+(block?"VolleyBlock-codex.unity":"VolleyJumpSpike-codex.unity");
        if(!AssetDatabase.CopyAsset(Folder+"VolleyReceive-codex.unity",path))throw new InvalidOperationException("Copy failed");
        var scene=EditorSceneManager.OpenScene(path);
        var drill=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
        var floor=UnityEngine.Object.FindFirstObjectByType<VolleyFloorTracking>();
        // Only the new scene's origin is moved; the court/net stay put, with zero lateral locomotion.
        var p=floor.Origin.transform.position;p.x=drill.ReceiveNet.transform.position.x;p.z=drill.ReceiveNet.transform.position.z-(block?1.0f:1.4f);
        floor.Origin.transform.position=p;
        var jump=floor.gameObject.AddComponent<VolleyArmJump>();jump.Floor=floor;jump.Left=drill.Left;jump.Right=drill.Right;
        jump.JumpHeight=block?.85f:.75f;jump.Duration=1f;
        drill.Drill=block?VolleyDrill.Block:VolleyDrill.Spike;
        drill.StatusText.text=block?"BLOCK - swing both arms up, block the opponent":"JUMP SPIKE - swing both arms up, strike with the right hand";
        drill.ScoreText.text=block?"BLOCK":"JUMP SPIKE";
        var aerial=drill.gameObject.AddComponent<VolleyAerialSequence>();aerial.Drill=drill;aerial.Jump=jump;drill.Aerial=aerial;
        drill.ReadySeconds=1f;drill.ServeInterval=1f;drill.MaximumBallAge=4f;drill.ContactForwardDistance=block?.45f:.55f;
        drill.ReceiveMinimumFlightSeconds=.5f;drill.ReceiveNetClearance=.18f;
        // Separate, fixed wrist boxes are needed for overhead right-hand strikes and two-hand blocks.
        if(drill.JoinedHands!=null){drill.JoinedHands.Volume.gameObject.SetActive(false);drill.JoinedHands.enabled=false;drill.JoinedHands=null;}
        if(block)
        {
            foreach(var launcher in drill.FeedLaunchers)launcher.gameObject.SetActive(false);
            foreach(var panel in drill.Panels)panel.gameObject.SetActive(false);
            var preview=EditorSceneManager.OpenScene(Folder+"VolleyOpponentPrototype-codex.unity",OpenSceneMode.Additive);
            var source=preview.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<VolleyOpponentPrototype>()).Single();
            var actor=UnityEngine.Object.Instantiate(source);SceneManager.MoveGameObjectToScene(actor.gameObject,scene);
            actor.name="Block opponent";actor.Animate=false;actor.PreviewOnly=false;actor.PreviewBall=null;actor.Target=null;actor.Status=null;
            actor.JumpHeight=.95f;actor.Variant=VolleyOpponentPrototype.MotionVariant.A_Readable;
            actor.transform.SetPositionAndRotation(drill.ReceiveNet.transform.position+Vector3.forward*1.1f,Quaternion.Euler(0,180,0));
            actor.PreviewPhase=0;actor.Pose(0);aerial.Opponent=actor;
            EditorSceneManager.CloseScene(preview,true);SceneManager.SetActiveScene(scene);
            var head=new GameObject("Block head contact",typeof(SphereCollider),typeof(Rigidbody),typeof(VolleyHeadSurface));
            // Use the existing torso's collision layer so the Ball collision matrix also permits head hits.
            var body=UnityEngine.Object.FindFirstObjectByType<VolleyBodySurface>();head.layer=body.gameObject.layer;
            head.GetComponent<SphereCollider>().radius=.14f;head.GetComponent<VolleyHeadSurface>().Drill=drill;
            head.transform.position=drill.Head.position;
        }
        else
        {
            aerial.TossLauncher=drill.FeedLaunchers[0];
            for(int i=0;i<drill.FeedLaunchers.Length;i++)drill.FeedLaunchers[i].gameObject.SetActive(i==0);
            aerial.TossLauncher.transform.position=new Vector3(p.x-2.2f,aerial.TossLauncher.transform.position.y,p.z-.2f);
            aerial.TossLauncher.GetComponent<VolleyFeederAim>().AimForShot(p+new Vector3(0,floor.StandingEyeHeight+jump.JumpHeight+aerial.SpikeReachAboveEye,drill.ContactForwardDistance),aerial.TossFlightSeconds);
            var so=new SerializedObject(drill.Targets);
            Float(so,"_minX",-2.5f);Float(so,"_maxX",2.5f);Float(so,"_minZ",2.5f);Float(so,"_maxZ",6f);
            Float(so,"_minHeight",.45f);Float(so,"_maxHeight",.75f);
            so.FindProperty("_player").objectReferenceValue=drill.Head;so.ApplyModifiedPropertiesWithoutUndo();
            so.FindProperty("_faceUp").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();
            drill.Targets.BeginWave(0,1,1);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Save failed");
    }
}
