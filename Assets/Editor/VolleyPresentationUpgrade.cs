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
    public static void ApplySoftNet()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save manual changes first.");
        var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        const string netArt="Assets/GloveBallDemo/Art/VolleyballNet/";
        var importer=(ModelImporter)AssetImporter.GetAtPath(netArt+"VolleyballNetDeformable.fbx");
        if(!importer.isReadable) { importer.isReadable=true; importer.SaveAndReimport(); }
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(netArt+"VolleyballNetDeformable.fbx");
        var net=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Volley Net");
        var visual=net.GetComponentInChildren<MeshFilter>();
        Undo.RecordObject(visual,"Use deformable net mesh");
        visual.sharedMesh=model.GetComponentInChildren<MeshFilter>().sharedMesh;
        var sourceMaterials=model.GetComponentInChildren<MeshRenderer>().sharedMaterials;
        visual.GetComponent<MeshRenderer>().sharedMaterials=sourceMaterials.Select(m=>AssetDatabase.LoadAssetAtPath<Material>(netArt+m.name+".mat")).ToArray();
        if(visual.GetComponent<MeshRenderer>().sharedMaterials.Any(m=>m==null)) throw new InvalidOperationException("Missing net material.");
        var response=net.GetComponent<VolleyNetResponse>();
        if(response==null) response=Undo.AddComponent<VolleyNetResponse>(net.gameObject);
        response.Visual=visual;
        const string physicsPath=netArt+"NetLowBounce.physicMaterial";
        var material=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsPath);
        if(material==null)
        {
            material=new PhysicsMaterial("NetLowBounce") { bounciness=0f, bounceCombine=PhysicsMaterialCombine.Minimum,
                staticFriction=.4f,dynamicFriction=.4f };
            AssetDatabase.CreateAsset(material,physicsPath);
        }
        net.GetComponent<BoxCollider>().sharedMaterial=material;
        var floor=AssetDatabase.LoadAssetAtPath<Material>(Art+"ReceiveCourtBlue.mat");
        Undo.RecordObject(floor,"Light cyan court"); floor.SetColor("_BaseColor",new Color(.22f,.62f,.76f,1f));
        EditorUtility.SetDirty(floor); EditorUtility.SetDirty(response);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Save failed.");
        Debug.Log("[VolleyPresentation] Soft net and light cyan floor applied in place.");
    }
    public static void ApplyArticulation()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save manual changes first.");
        var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        var drill=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<VolleyDrillController>(true)).Single();
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"BallFeederArticulated.fbx");
        if(model==null) throw new InvalidOperationException("Missing articulated feeder.");
        foreach(var launcher in drill.FeedLaunchers)
        {
            var existing=launcher.GetComponent<VolleyFeederAim>();
            if(existing!=null)
            {
                if(existing.Muzzle.parent!=existing.Pivot) SetMuzzle(launcher,existing);
                continue;
            }
            var serialized=new SerializedObject(launcher);
            var muzzle=(Transform)serialized.FindProperty("_muzzle").objectReferenceValue;
            var direction=(muzzle.position-launcher.transform.position).normalized;
            var pivot=new GameObject("Feeder Aim Pivot").transform;
            pivot.SetParent(launcher.transform,false); pivot.rotation=Quaternion.LookRotation(direction,Vector3.up);
            var skin=UnityEngine.Object.Instantiate(model,launcher.transform);
            skin.name="Articulated Feeder Skin"; skin.transform.localPosition=Vector3.zero;
            skin.transform.rotation=pivot.rotation*model.transform.localRotation;
            foreach(var renderer in skin.GetComponentsInChildren<MeshRenderer>())
                renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>MaterialFor(source.name)).ToArray();
            var head=skin.GetComponentsInChildren<Transform>().Single(t=>t.name=="FeederHead");
            head.SetParent(pivot,true);
            var aim=launcher.gameObject.AddComponent<VolleyFeederAim>(); aim.Pivot=pivot;
            SetMuzzle(launcher,aim);
            var old=launcher.transform.Find("Ball Feeder Skin");
            if(old!=null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var target=drill.Head.position+Vector3.ProjectOnPlane(drill.CourtFrame.forward,Vector3.up).normalized*drill.ContactForwardDistance+Vector3.up*drill.ContactHeightFromHead;
            aim.AimForShot(target,drill.FlightSeconds);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Save failed.");
        Debug.Log("[VolleyPresentation] Articulated emitters applied without rebuilding scene.");
    }
    static void SetMuzzle(BallLauncher launcher,VolleyFeederAim aim)
    {
        // Leave the inherited prefab child intact; override only the launcher's reference.
        var muzzle=new GameObject("Articulated Muzzle").transform;
        muzzle.SetParent(aim.Pivot,false); muzzle.localPosition=new Vector3(0,0,.6f);
        aim.Muzzle=muzzle;
        var so=new SerializedObject(launcher);
        so.FindProperty("_muzzle").objectReferenceValue=muzzle;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(aim);
    }
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
            if(launcher.GetComponent<VolleyFeederAim>()!=null) continue;
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
        // Old obstacle silhouettes also exist in baked lighting. Unlit keeps this requested plain floor uniform.
        court.sharedMaterial.shader=Shader.Find("Universal Render Pipeline/Unlit");
        EditorUtility.SetDirty(court.sharedMaterial);
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
