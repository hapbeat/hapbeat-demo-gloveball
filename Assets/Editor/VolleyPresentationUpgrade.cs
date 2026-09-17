using System;
using System.Linq;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VolleyPresentationUpgrade
{
    const string Art="Assets/GloveBallDemo/Art/BallFeeder/";
    [MenuItem("GloveBall Demo/Volley/Apply Feeder Skin and Plain Court")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save manual changes first.");
        var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        var objects=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Transform>(true)).ToArray();
        var drill=objects.Select(t=>t.GetComponent<VolleyDrillController>()).First(x=>x!=null);
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"BallFeeder.fbx");
        if(model==null) throw new InvalidOperationException("Feeder model not imported.");
        foreach(var launcher in drill.FeedLaunchers)
        {
            var before=launcher.MuzzlePosition;
            foreach(var renderer in launcher.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.name=="Body" || renderer.name=="Barrel") renderer.enabled=false;
            if(launcher.transform.Find("Ball Feeder Skin")==null)
            {
                var skin=UnityEngine.Object.Instantiate(model,launcher.transform);
                skin.name="Ball Feeder Skin"; skin.transform.localPosition=Vector3.zero;
                // Keep the FBX's axis/unit conversion on this visual only. No new collider or muzzle.
                foreach(var renderer in skin.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>MaterialFor(source.name)).ToArray();
            }
            var localDirection=launcher.transform.InverseTransformDirection(before-launcher.transform.position).normalized;
            launcher.transform.Find("Ball Feeder Skin").localRotation=
                Quaternion.LookRotation(localDirection,Vector3.up)*model.transform.localRotation;
            if(Vector3.Distance(before,launcher.MuzzlePosition)>.00001f) throw new InvalidOperationException("Muzzle moved.");
        }
        var court=objects.Single(t=>t.name=="court").GetComponent<MeshRenderer>();
        court.sharedMaterial=MakeMaterial("ReceiveCourtBlue",new Color(.035f,.13f,.65f));
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Save failed.");
        Debug.Log("[VolleyPresentation] Visual-only feeder skins and plain blue court applied; muzzle transforms and court lines preserved.");
    }
    static Material MaterialFor(string name)
    {
        if(name.StartsWith("FeederBlue")) return MakeMaterial("FeederBlue",new Color(.06f,.23f,.38f));
        if(name.StartsWith("FeederRubber")) return MakeMaterial("FeederRubber",new Color(.025f,.03f,.04f));
        if(name.StartsWith("FeederMetal")) return MakeMaterial("FeederMetal",new Color(.65f,.7f,.74f));
        if(name.StartsWith("FeederAccent")) return MakeMaterial("FeederAccent",new Color(.98f,.62f,.06f));
        throw new InvalidOperationException("Unexpected feeder material: "+name);
    }
    static Material MakeMaterial(string name,Color colour)
    {
        var path=Art+name+".mat"; var result=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(result!=null) return result;
        result=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        result.SetColor("_BaseColor",colour); result.SetFloat("_Smoothness",.18f);
        AssetDatabase.CreateAsset(result,path); return result;
    }
}
