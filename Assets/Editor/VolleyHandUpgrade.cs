using System;
using System.Collections.Generic;
using System.Linq;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;

public static class VolleyHandUpgrade
{
    const string Art = "Assets/GloveBallDemo/Art/UnityGhostHands/";

    [MenuItem("GloveBall Demo/Volley/Apply Net and Joined Receive")]
    public static void ApplyNetAndJoined()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save manual edits first.");
        var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        var drill=All<VolleyDrillController>(scene).Single();
        var marker=All<Transform>(scene).FirstOrDefault(t=>t.name=="BallSpawner");
        var netPosition=marker!=null ? marker.position : Vector3.zero;
        netPosition.y=0;
        if(marker!=null) marker.gameObject.SetActive(false); // Remove visual from experience; retain recoverable authored hierarchy.
        var net=All<Transform>(scene).FirstOrDefault(t=>t.name=="Volley Net");
        if(net==null)
        {
            const string path="Assets/GloveBallDemo/Art/VolleyballNet/";
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path+"VolleyballNet.fbx");
            if(model==null) throw new InvalidOperationException("Import authored net FBX first.");
            net=new GameObject("Volley Net").transform; net.position=netPosition;
            var visual=UnityEngine.Object.Instantiate(model,net).transform;
            visual.name="Net Mesh"; visual.localPosition=Vector3.zero;
            var colours=new Dictionary<string,Color>{{"NetCord",new Color(.025f,.035f,.05f)},{"NetTape",new Color(.94f,.95f,.96f)},{"NetPost",new Color(.04f,.22f,.48f)}};
            foreach(var renderer in net.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(old=>
                {
                    var key=colours.Keys.First(k=>old.name.StartsWith(k,StringComparison.Ordinal));
                    var asset=path+key+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(asset);
                    if(material==null)
                    {
                        material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        material.SetColor("_BaseColor",colours[key]);
                        material.SetFloat("_Smoothness",.15f); AssetDatabase.CreateAsset(material,asset);
                    }
                    return material;
                }).ToArray();
            }
            var barrier=net.gameObject.AddComponent<BoxCollider>();
            barrier.center=new Vector3(0,1.93f,0); barrier.size=new Vector3(9.5f,1,.04f);
        }
        // Normalize the initial authored installation if the FBX was used as the physics root.
        if(net.GetComponent<MeshRenderer>()!=null)
        {
            var mesh=net; mesh.name="Net Mesh";
            UnityEngine.Object.DestroyImmediate(mesh.GetComponent<BoxCollider>());
            net=new GameObject("Volley Net").transform; net.position=netPosition;
            mesh.SetParent(net,true);
            var barrier=net.gameObject.AddComponent<BoxCollider>();
            barrier.center=new Vector3(0,1.93f,0); barrier.size=new Vector3(9.5f,1,.04f);
        }
        var layout=new SerializedObject(drill.Targets);
        layout.FindProperty("_minX").floatValue=netPosition.x-3;
        layout.FindProperty("_maxX").floatValue=netPosition.x+3;
        layout.FindProperty("_minZ").floatValue=netPosition.z+.2f;
        layout.FindProperty("_maxZ").floatValue=netPosition.z+.6f;
        layout.FindProperty("_minHeight").floatValue=2.95f;
        layout.FindProperty("_maxHeight").floatValue=3.65f;
        layout.ApplyModifiedPropertiesWithoutUndo();
        drill.Targets.BeginWave(0,1,1);
        drill.VerticalSpread=.2f;
        if(drill.JoinedHands==null)
        {
            var joined=new GameObject("Volley Joined Hands").AddComponent<VolleyJoinedHands>();
            joined.Left=drill.Left; joined.Right=drill.Right;
            joined.Volume=joined.gameObject.AddComponent<BoxCollider>();
            joined.Volume.isTrigger=true; joined.Volume.size=new Vector3(.32f,.10f,.24f); joined.Volume.enabled=false;
            drill.JoinedHands=joined;
        }
        if(!All<VolleyTrackingWarning>(scene).Any())
        {
            var warning=new GameObject("Volley Tracking Warning",typeof(RectTransform),typeof(Canvas),typeof(VolleyTrackingWarning));
            warning.transform.SetParent(drill.Head,false); warning.transform.localPosition=new Vector3(0,0,1.4f);
            warning.transform.localScale=Vector3.one*.0015f;
            warning.GetComponent<RectTransform>().sizeDelta=new Vector2(760,160);
            var canvas=warning.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.sortingOrder=100;
            var background=warning.AddComponent<UnityEngine.UI.Image>(); background.color=new Color(.06f,.025f,.01f,.9f); background.raycastTarget=false;
            var label=new GameObject("Message",typeof(RectTransform),typeof(UnityEngine.UI.Text)); label.transform.SetParent(warning.transform,false);
            var rect=label.GetComponent<RectTransform>(); rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=Vector2.zero; rect.offsetMax=Vector2.zero;
            var text=label.GetComponent<UnityEngine.UI.Text>(); text.font=drill.StatusText.font; text.fontSize=40;
            text.text="HANDS NOT TRACKED\nShow at least one hand"; text.alignment=TextAnchor.MiddleCenter; text.color=new Color(1,.8f,.2f); text.raycastTarget=false;
            var state=warning.GetComponent<VolleyTrackingWarning>(); state.Left=drill.Left; state.Right=drill.Right;
            state.Graphics=new UnityEngine.UI.Graphic[]{background,text}; background.enabled=false; text.enabled=false;
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        Debug.Log("[VolleyUpgrade] Net, random above-net target, head warning and joined hands applied.");
    }

    [MenuItem("GloveBall Demo/Volley/Apply Wrist Frame and Receive Layout")]
    public static void ApplyWristAndFeed()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i=0;i<SceneManager.sceneCount;i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save manual scene edits first.");
        foreach (var name in new[] { "VolleyReceive-codex", "VolleySpike-codex" })
        {
            var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/"+name+".unity");
            foreach(var hand in All<VolleyTrackedHand>(scene))
            {
                // Wrist is now the origin. The volume covers the extended hand and stays rigid when making a fist.
                hand.ContactVolume.size=new Vector3(.12f,.07f,.20f);
                hand.ContactVolume.center=new Vector3(0,0,.08f);
            }
            var drill=All<VolleyDrillController>(scene).Single();
            if(drill.Drill==VolleyDrill.Receive)
            {
                drill.FeedLaunchers=All<BallLauncher>(scene).OrderBy(x=>x.name).ToArray();
                if(drill.FeedLaunchers.Length!=3) throw new InvalidOperationException("Expected three existing launchers.");
                foreach(var launcher in drill.FeedLaunchers)
                {
                    launcher.enabled=false; // One owner for serving; do not revive the old game's launch loop.
                    launcher.gameObject.SetActive(true);
                }
                drill.FlightSeconds=1.5f; // Existing launchers are farther away than the former floating feed point.
                var layout=new SerializedObject(drill.Targets);
                layout.FindProperty("_player").objectReferenceValue=drill.Head;
                foreach(var field in new[]{"_minX","_maxX"}) layout.FindProperty(field).floatValue=0;
                foreach(var field in new[]{"_minZ","_maxZ"}) layout.FindProperty(field).floatValue=1;
                foreach(var field in new[]{"_minHeight","_maxHeight"}) layout.FindProperty(field).floatValue=3.5f;
                layout.ApplyModifiedPropertiesWithoutUndo();
                // Show the same single target in edit mode; runtime generations reuse this fixed position.
                for(int i=0;i<drill.Panels.Length;i++)
                {
                    drill.Panels[i].gameObject.SetActive(i==0);
                    if(i==0) drill.Panels[i].transform.SetPositionAndRotation(new Vector3(0,3.5f,1),
                        Quaternion.LookRotation(drill.Head.position-new Vector3(0,3.5f,1),Vector3.up));
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        }
        EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        Debug.Log("[VolleyUpgrade] Wrist-fixed boxes and receive launcher/target layout applied.");
    }

    [MenuItem("GloveBall Demo/Volley/Apply Ghost Hands and Thick Contact")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i=0;i<SceneManager.sceneCount;i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save manual scene edits first.");
        foreach (var name in new[] { "VolleyReceive-codex", "VolleySpike-codex" })
        {
            var path = "Assets/GloveBallDemo/Scenes/" + name + ".unity";
            var scene = EditorSceneManager.OpenScene(path);
            var hands = All<VolleyTrackedHand>(scene);
            foreach (var hand in hands)
            {
                if (hand.ContactVolume == null)
                {
                    hand.ContactVolume = hand.gameObject.AddComponent<BoxCollider>();
                    hand.ContactVolume.isTrigger = true; // Custom continuous sweep owns the bounce, not Unity's duplicate response.
                    hand.ContactVolume.size = new Vector3(.15f,.09f,.20f);
                    hand.ContactVolume.center = new Vector3(hand.Side == GloveSide.Left ? .015f : -.015f,0,-.015f);
                }
                if (hand.Visual != null && hand.Visual.GetComponent<VolleyGhostHand>() != null) continue;
                if (hand.Visual != null) hand.Visual.gameObject.SetActive(false); // Preserve old proxy, hidden.
                InstallGhostHand(hand, new[] {
                    AssetDatabase.LoadAssetAtPath<Material>(Art+"Materials/Unity_Hand_Medium.mat"),
                    AssetDatabase.LoadAssetAtPath<Material>(Art+"Materials/DepthOnly.mat") },
                    XRHandTrackingEvents.UpdateTypes.Dynamic | XRHandTrackingEvents.UpdateTypes.BeforeRender);
                EditorUtility.SetDirty(hand);
            }
            var drill=All<VolleyDrillController>(scene).Single();
            if (drill.Drill == VolleyDrill.Receive) drill.ContactHeightFromHead = -.15f;
            foreach (var pool in All<BallPool>(scene))
            {
                var data=new SerializedObject(pool);
                var kinds=data.FindProperty("_launchBallKinds");
                kinds.arraySize=4; var selected=new[]{0,1,2,4};
                for(int i=0;i<4;i++) kinds.GetArrayElementAtIndex(i).intValue=selected[i];
                data.FindProperty("_verboseLog").boolValue=true;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
        Debug.Log("[VolleyUpgrade] Existing scenes updated in place; original Demo scene untouched.");
    }
    public static readonly string[] VolleyScenes = { "VolleyReceive-codex", "VolleySpike-codex", "VolleyJumpSpike-codex", "VolleyBlock-codex" };

    /// <summary>
    /// Replaces mesh copies baked into the Volley scenes with a <see cref="VolleyHandModelResolver"/> root that
    /// instantiates the hand at runtime (private XR Hands model when linked, public placeholder otherwise).
    /// Idempotent: resolver roots are only refreshed. Materials, handedness and update types are carried over.
    /// </summary>
    [MenuItem("GloveBall Demo/Volley/Install Hand Model Resolver (all Volley scenes)")]
    public static void InstallHandModelResolver()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i=0;i<SceneManager.sceneCount;i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save manual scene edits first.");
        foreach (var name in VolleyScenes)
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/" + name + ".unity");
            foreach (var hand in All<VolleyTrackedHand>(scene))
            {
                var old = hand.Visual != null ? hand.Visual.GetComponent<VolleyGhostHand>() : null;
                if (old == null) throw new InvalidOperationException(name + ": " + hand.name + " has no ghost hand to migrate.");
                var resolver = old.GetComponent<VolleyHandModelResolver>();
                if (resolver != null) { ConfigureResolver(resolver, hand.Side, resolver.Materials); continue; }
                var events = old.GetComponent<XRHandTrackingEvents>();
                var materials = old.Mesh != null ? old.Mesh.sharedMaterials : new Material[0];
                var oldRoot = old.gameObject;
                var owned = new HashSet<UnityEngine.Object>(oldRoot.GetComponentsInChildren<Component>(true).Concat<UnityEngine.Object>(oldRoot.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject)));
                var outside = All<Component>(scene).Where(c => c != null && !owned.Contains(c) && c != hand && c != oldRoot.transform.parent)
                    .Where(c => { var p = new SerializedObject(c).GetIterator(); while (p.Next(true)) if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null && owned.Contains(p.objectReferenceValue)) return true; return false; })
                    .Select(c => c.GetType().Name + " on " + c.name).ToArray();
                if (outside.Length != 0) throw new InvalidOperationException(name + ": other components reference " + oldRoot.name + ": " + string.Join(", ", outside));
                var extra = oldRoot.GetComponentsInChildren<MonoBehaviour>(true).Where(b => !(b is XRHandTrackingEvents || b is XRHandSkeletonDriver || b is VolleyGhostHand)).Select(b => b == null ? "missing script" : b.GetType().Name + " on " + b.name).ToArray();
                if (extra.Length != 0) throw new InvalidOperationException(name + ": " + oldRoot.name + " carries extra components: " + string.Join(", ", extra));
                var sibling = oldRoot.transform.GetSiblingIndex();
                bool active = oldRoot.activeSelf;
                var ghost = InstallGhostHand(hand, materials, events != null ? events.updateType : XRHandTrackingEvents.UpdateTypes.Dynamic | XRHandTrackingEvents.UpdateTypes.BeforeRender);
                ghost.transform.SetSiblingIndex(sibling);
                ghost.gameObject.SetActive(active);
                UnityEngine.Object.DestroyImmediate(oldRoot);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        }
        Debug.Log("[VolleyUpgrade] Hand model resolver installed in " + string.Join(", ", VolleyScenes) + ".");
    }

    public const string PrivateHandResourceFolder = "HapbeatPrivate/UnityHands/";
    public const string FallbackHandFolder = "Assets/GloveBallDemo/Art/FallbackHands/";

    /// <summary>Scene side of a ghost hand: tracking events, a disabled skeleton driver, the visual driver and the model resolver. No mesh.</summary>
    static VolleyGhostHand InstallGhostHand(VolleyTrackedHand hand, Material[] materials, XRHandTrackingEvents.UpdateTypes updateType)
    {
        var visual = new GameObject("Volley " + hand.Side + " Ghost Hand");
        visual.transform.SetParent(hand.TrackingSpace, false);
        var events = visual.AddComponent<XRHandTrackingEvents>();
        events.handedness = hand.Side == GloveSide.Left ? Handedness.Left : Handedness.Right;
        events.updateType = updateType;
        var skeleton = visual.AddComponent<XRHandSkeletonDriver>();
        skeleton.jointTransformReferences = new List<JointToTransformReference>();
        skeleton.handTrackingEvents = events;
        skeleton.enabled = false; // Joints are wired by the resolver; VolleyGhostHand enables it while hands are tracked.
        var driver = visual.AddComponent<VolleyGhostHand>(); driver.Hand = hand; driver.Skeleton = skeleton;
        var resolver = visual.AddComponent<VolleyHandModelResolver>();
        resolver.Ghost = driver; resolver.Skeleton = skeleton;
        ConfigureResolver(resolver, hand.Side, materials);
        hand.Visual = visual.transform;
        EditorUtility.SetDirty(hand);
        return driver;
    }

    static void ConfigureResolver(VolleyHandModelResolver resolver, GloveSide side, Material[] materials)
    {
        var model = side == GloveSide.Left ? "LeftHand" : "RightHand";
        resolver.PrivateResourcePath = PrivateHandResourceFolder + model;
        resolver.FallbackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FallbackHandFolder + model + "Placeholder.prefab");
        if (resolver.FallbackPrefab == null) throw new InvalidOperationException("Build the placeholder hands first (GloveBall Demo/Volley/Build Placeholder Hands).");
        if (materials == null || materials.Length != 2 || materials.Any(m => m == null)) throw new InvalidOperationException("Ghost hand needs its hand and depth-only materials.");
        resolver.Materials = materials;
        EditorUtility.SetDirty(resolver);
        // Placeholder root until the resolver points the driver at the model's wrist. XR Hands compares rootTransform
        // in OnAfterDeserialize on the loading thread; an unassigned (editor fake-null) reference throws there.
        if (resolver.Skeleton != null && resolver.Skeleton.rootTransform == null)
        {
            resolver.Skeleton.rootTransform = resolver.transform;
            EditorUtility.SetDirty(resolver.Skeleton);
        }
    }

    static T[] All<T>(Scene s) where T:Component => s.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<T>(true)).ToArray();
}
