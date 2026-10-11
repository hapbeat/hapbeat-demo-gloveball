using System;
using System.IO;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Role=GloveBallDemo.Runtime.VolleyOpponentPrototype.MotionRole;

/// <summary>Offscreen renders of the player model in its key poses (needs a GPU: run without -nographics). Never saves a scene.</summary>
public static class VolleyPlayerModelPreview
{
    const string BlockScene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";

    public static void CapturePoses()
    {
        var outDir=Arg("-gbOut",Path.GetFullPath("tools/artifacts/volley-player"));
        Directory.CreateDirectory(outDir);
        var court=EditorSceneManager.OpenScene(BlockScene,OpenSceneMode.Single);
        var aerial=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>().Aerial;
        CaptureCourt(aerial,Path.Combine(outDir,"court.png"));

        var stage=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(stage);
        var poses=new (string name,Role role,float phase,bool ally)[]{
            ("ready",Role.Spike,0f,false),("spike",Role.Spike,.52f,false),("set",Role.Set,VolleyOpponentPrototype.SetContactPhase,true),
            ("block",Role.Block,VolleyOpponentPrototype.BlockPeakPhase,false),("dig",Role.Dig,1f,true)};
        for(int i=0;i<poses.Length;i++)
        {
            var actor=UnityEngine.Object.Instantiate(aerial.Opponent);actor.name=poses[i].name;
            actor.transform.SetPositionAndRotation(new Vector3((i-2)*1.25f,0f,0f),Quaternion.identity);
            actor.Animate=false;actor.PreviewOnly=false;actor.Role=poses[i].role;actor.JumpHeight=.65f;
            actor.OverrideKit=true;
            actor.JerseyColour=poses[i].ally?aerial.AllyJersey:aerial.OpponentJersey;
            actor.ShortsColour=poses[i].ally?aerial.AllyShorts:aerial.OpponentShorts;
            actor.Pose(poses[i].phase);actor.ApplyKit();
        }
        EditorSceneManager.CloseScene(court,true);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.62f,.68f,.78f);RenderSettings.ambientEquatorColor=new Color(.5f,.5f,.5f);RenderSettings.ambientGroundColor=new Color(.28f,.26f,.24f);
        var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.Soft;
        light.transform.rotation=Quaternion.Euler(42f,-35f,0f);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.localScale=Vector3.one*2f;
        var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.75f,.55f,.35f));
        floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
        var camera=new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.82f,.86f,.9f);camera.fieldOfView=30f;camera.nearClipPlane=.05f;
        Shoot(camera,new Vector3(2.2f,1.6f,8.5f),new Vector3(0f,1.25f,0f),Path.Combine(outDir,"poses-front.png"));
        Shoot(camera,new Vector3(-2.2f,1.6f,-8.5f),new Vector3(0f,1.25f,0f),Path.Combine(outDir,"poses-back.png"));
        Shoot(camera,new Vector3(-1.9f,1.75f,2.1f),new Vector3(-2.5f,1.45f,0f),Path.Combine(outDir,"ready-closeup.png"));
        Shoot(camera,new Vector3(0.6f,1.9f,2.2f),new Vector3(0f,1.75f,0f),Path.Combine(outDir,"set-closeup.png"));
        UnityEngine.Object.DestroyImmediate(floorMaterial);
    }

    /// <summary>The real court from the player's position, everyone in their saved pose.</summary>
    static void CaptureCourt(VolleyAerialSequence aerial,string file)
    {
        var drill=aerial.Drill;
        var camera=new GameObject("Court preview camera").AddComponent<Camera>();
        var forward=Vector3.ProjectOnPlane(drill.CourtFrame.forward,Vector3.up).normalized;
        camera.fieldOfView=70f;camera.nearClipPlane=.05f;
        var eye=new Vector3(drill.Head.position.x,drill.CourtFrame.position.y+1.65f,drill.Head.position.z);
        Shoot(camera,eye,eye+forward*6f-Vector3.up*.3f,file);
        UnityEngine.Object.DestroyImmediate(camera.gameObject);
    }

    static void Shoot(Camera camera,Vector3 from,Vector3 at,string file)
    {
        const int width=1600,height=900;
        camera.transform.position=from;camera.transform.LookAt(at);
        var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){antiAliasing=4};
        camera.targetTexture=rt;
        DynamicGI.UpdateEnvironment();camera.Render();camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=rt;
        var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
        RenderTexture.active=previous;camera.targetTexture=null;
        File.WriteAllBytes(file,texture.EncodeToPNG());
        Debug.Log("[VolleyPlayerModel] wrote "+file);
        UnityEngine.Object.DestroyImmediate(texture);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    }
    static string Arg(string name,string fallback)
    {
        var args=Environment.GetCommandLineArgs();
        for(int i=0;i<args.Length-1;i++)if(args[i]==name)return args[i+1];
        return fallback;
    }
}
