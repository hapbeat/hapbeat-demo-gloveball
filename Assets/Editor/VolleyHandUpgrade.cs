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
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Models/" + (hand.Side == GloveSide.Left ? "LeftHand" : "RightHand") + ".fbx");
                if (model == null) throw new InvalidOperationException("Hand model missing.");
                var visual = UnityEngine.Object.Instantiate(model, hand.TrackingSpace);
                visual.name = "Volley " + hand.Side + " Ghost Hand";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                var mesh = visual.GetComponentInChildren<SkinnedMeshRenderer>();
                mesh.sharedMaterials = new[] {
                    AssetDatabase.LoadAssetAtPath<Material>(Art+"Materials/Unity_Hand_Medium.mat"),
                    AssetDatabase.LoadAssetAtPath<Material>(Art+"Materials/DepthOnly.mat") };
                mesh.updateWhenOffscreen = true;
                var events = visual.AddComponent<XRHandTrackingEvents>();
                events.handedness = hand.Side == GloveSide.Left ? Handedness.Left : Handedness.Right;
                events.updateType = XRHandTrackingEvents.UpdateTypes.Dynamic | XRHandTrackingEvents.UpdateTypes.BeforeRender;
                var skeleton = visual.AddComponent<XRHandSkeletonDriver>();
                skeleton.jointTransformReferences = new List<JointToTransformReference>();
                skeleton.handTrackingEvents = events;
                skeleton.rootTransform = visual.GetComponentsInChildren<Transform>().First(x => x.name.IndexOf("wrist", StringComparison.OrdinalIgnoreCase) >= 0);
                var missing = new List<string>(); skeleton.FindJointsFromRoot(missing);
                if (missing.Count != 0) throw new InvalidOperationException("Unmapped joints: " + string.Join(",", missing));
                skeleton.InitializeFromSerializedReferences();
                var driver = visual.AddComponent<VolleyGhostHand>(); driver.Hand=hand; driver.Skeleton=skeleton; driver.Mesh=mesh;
                hand.Visual=visual.transform;
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
    static T[] All<T>(Scene s) where T:Component => s.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<T>(true)).ToArray();
}
