using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GloveBallDemo.Runtime;

public static class VolleyOpponentPrototypeBuilder
{
    const string ScenePath="Assets/GloveBallDemo/Scenes/VolleyOpponentPrototype-codex.unity";
    static Transform Part(string name,Transform parent,PrimitiveType shape,Color colour)
    {
        var go=GameObject.CreatePrimitive(shape);go.name=name;go.transform.SetParent(parent,false);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        const string folder="Assets/GloveBallDemo/Art/OpponentPrototype";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/GloveBallDemo/Art","OpponentPrototype");
        var path=folder+"/Colour_"+ColorUtility.ToHtmlStringRGBA(colour)+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",colour);AssetDatabase.CreateAsset(material,path);}
        go.GetComponent<Renderer>().sharedMaterial=material; return go.transform;
    }
    public static void Create()
    {
        if(File.Exists(ScenePath))throw new InvalidOperationException("Prototype exists; do not regenerate.");
        if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save/stop first.");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=new GameObject("Opponent motion PROTOTYPE");var actor=root.AddComponent<VolleyOpponentPrototype>();
        root.transform.position=new Vector3(0,0,1.4f);root.transform.rotation=Quaternion.Euler(0,180,0);
        Color skin=new Color(.8f,.83f,.85f),shirt=new Color(.8f,.16f,.07f),shorts=new Color(.05f,.07f,.1f);
        actor.Torso=Part("Jersey",root.transform,PrimitiveType.Cube,shirt);
        actor.Head=Part("Head",root.transform,PrimitiveType.Sphere,skin);actor.Head.localScale=Vector3.one*.25f;
        actor.LeftUpperArm=Part("Left upper arm",root.transform,PrimitiveType.Capsule,skin);actor.LeftForearm=Part("Left forearm",root.transform,PrimitiveType.Capsule,skin);
        actor.RightUpperArm=Part("Right upper arm",root.transform,PrimitiveType.Capsule,skin);actor.RightForearm=Part("Right forearm",root.transform,PrimitiveType.Capsule,skin);
        actor.LeftThigh=Part("Left thigh",root.transform,PrimitiveType.Capsule,shorts);actor.LeftShin=Part("Left shin",root.transform,PrimitiveType.Capsule,skin);
        actor.RightThigh=Part("Right thigh",root.transform,PrimitiveType.Capsule,shorts);actor.RightShin=Part("Right shin",root.transform,PrimitiveType.Capsule,skin);
        actor.PreviewBall=Part("Preview ball",null,PrimitiveType.Sphere,Color.white);actor.PreviewBall.localScale=Vector3.one*.22f;
        actor.Target=new GameObject("Receive point 0.65m").transform;actor.Target.position=new Vector3(0,.65f,-4f);
        var floor=Part("Preview floor",null,PrimitiveType.Cube,new Color(.22f,.62f,.76f));floor.position=new Vector3(0,-.05f,0);floor.localScale=new Vector3(12,.1f,14);
        var netAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Art/VolleyballNet/VolleyballNet.fbx");
        var net=UnityEngine.Object.Instantiate(netAsset); net.name="Reference net 2.43m";
        foreach(var r in net.GetComponentsInChildren<Renderer>())
        {
            var ms=r.sharedMaterials;
            for(int i=0;i<ms.Length;i++)ms[i]=AssetDatabase.LoadAssetAtPath<Material>("Assets/GloveBallDemo/Art/VolleyballNet/"+ms[i].name+".mat");r.sharedMaterials=ms;
        }
        var camera=new GameObject("Preview camera").AddComponent<Camera>();camera.transform.position=new Vector3(5,3.4f,-6);camera.transform.LookAt(new Vector3(0,1.5f,0));camera.nearClipPlane=.05f;
        var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var label=new GameObject("Prototype state").AddComponent<TextMesh>();label.transform.position=new Vector3(-2,3,0);label.characterSize=.09f;label.fontSize=50;label.anchor=TextAnchor.MiddleCenter;
        label.transform.rotation=Quaternion.LookRotation(label.transform.position-camera.transform.position);actor.Status=label;
        actor.Pose(.4f);
        EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new InvalidOperationException("Save failed");
    }
}
