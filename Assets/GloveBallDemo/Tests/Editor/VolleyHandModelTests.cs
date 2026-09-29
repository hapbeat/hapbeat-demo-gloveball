using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;

namespace GloveBallDemo.Tests
{
    /// <summary>Hand meshes resolve at runtime: private XR Hands model when linked, public placeholder otherwise.</summary>
    public class VolleyHandModelTests
    {
        const string Fallback="Assets/GloveBallDemo/Art/FallbackHands/";
        const string PrivateFolder="HapbeatPrivate/UnityHands/";
        // GUIDs of the Unity XR Hands sample FBX files, which must never be baked into a public scene.
        static readonly string[] XrHandsModelGuids={"bf7151579c38e2a44be94ba8773876c1","56186ccf27ad7864681108ed88349071"};
        static readonly string[] Scenes={"VolleyReceive-codex","VolleySpike-codex","VolleyJumpSpike-codex","VolleyBlock-codex"};

        static VolleyHandModelResolver MakeResolver(Scene scene,string side,string privatePath)
        {
            var go=new GameObject(side+" resolver test"); SceneManager.MoveGameObjectToScene(go,scene);
            var events=go.AddComponent<XRHandTrackingEvents>();
            var skeleton=go.AddComponent<XRHandSkeletonDriver>(); skeleton.enabled=false;
            skeleton.jointTransformReferences=new List<JointToTransformReference>(); skeleton.handTrackingEvents=events;
            var ghost=go.AddComponent<VolleyGhostHand>(); ghost.Skeleton=skeleton;
            var resolver=go.AddComponent<VolleyHandModelResolver>();
            resolver.Ghost=ghost; resolver.Skeleton=skeleton; resolver.PrivateResourcePath=privatePath;
            resolver.FallbackPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Fallback+side+"Placeholder.prefab");
            return resolver;
        }
        static void AssertAllJointsMapped(VolleyHandModelResolver resolver)
        {
            var refs=resolver.Skeleton.jointTransformReferences;
            Assert.That(refs.Count,Is.EqualTo(XRHandJointID.EndMarker.ToIndex()),"All 26 XR hand joints map.");
            Assert.That(refs.All(r=>r.jointTransform!=null && r.jointTransform.IsChildOf(resolver.Model.transform)),Is.True);
            Assert.That(refs.Select(r=>r.xrHandJointID).Distinct().Count(),Is.EqualTo(refs.Count));
            Assert.That(resolver.Skeleton.rootTransform.name,Does.EndWith("Wrist"));
            Assert.That(resolver.Ghost.Mesh,Is.SameAs(resolver.Mesh));
            Assert.That(resolver.Mesh.bones.Length,Is.GreaterThanOrEqualTo(26));
            Assert.That(resolver.Mesh.sharedMesh,Is.Not.Null);
        }

        [TestCase("LeftHand")]
        [TestCase("RightHand")]
        public void WithoutPrivateAssetsThePlaceholderIsUsedAndAllJointsMap(string side)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var resolver=MakeResolver(scene,side,PrivateFolder+"NotLinked/"+side);
                Assert.That(resolver.Resolve(),Is.True);
                Assert.That(resolver.Source,Is.EqualTo(VolleyHandModelResolver.ModelSource.Fallback));
                AssertAllJointsMapped(resolver);
                var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/GloveBallDemo/Art/UnityGhostHands/Materials/DepthOnly.mat");
                resolver.Materials=null; Assert.That(resolver.Resolve(),Is.True,"Second call keeps the existing model.");
                Assert.That(resolver.Mesh.sharedMaterials.Length,Is.EqualTo(2));
                Assert.That(resolver.Mesh.sharedMaterials[1],Is.EqualTo(material));
                Assert.That(resolver.transform.childCount,Is.EqualTo(1));
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }

        [TestCase("LeftHand")]
        [TestCase("RightHand")]
        public void PlaceholderIsHandSizedAndPublicSafe(string side)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Fallback+side+"Placeholder.prefab");
            Assert.That(prefab,Is.Not.Null);
            var mesh=prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            Assert.That(AssetDatabase.GetAssetPath(mesh),Does.StartWith(Fallback),"Hapbeat-authored mesh, not a third-party model.");
            var size=mesh.bounds.size;
            Assert.That(size.z,Is.InRange(.17f,.24f),"Wrist to fingertip.");
            Assert.That(size.x,Is.InRange(.08f,.16f),"Palm width including the thumb.");
            var dependencies=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab),true);
            Assert.That(dependencies.Any(d=>XrHandsModelGuids.Contains(AssetDatabase.AssetPathToGUID(d))),Is.False);
        }

        [TestCase("LeftHand")]
        [TestCase("RightHand")]
        public void WithPrivateAssetsThePrivateModelIsUsed(string side)
        {
            if(VolleyHandModelResolver.LoadPrivateModel(PrivateFolder+side)==null)
                Assert.Ignore("Private hand assets are not linked (public clone); the placeholder is used instead.");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var resolver=MakeResolver(scene,side,PrivateFolder+side);
                Assert.That(resolver.Resolve(),Is.True);
                Assert.That(resolver.Source,Is.EqualTo(VolleyHandModelResolver.ModelSource.Private));
                AssertAllJointsMapped(resolver);
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }

        [Test]
        public void VolleyScenesCarryNoHandMeshAndNoMissingReferences()
        {
            foreach(var name in Scenes)
            {
                var path="Assets/GloveBallDemo/Scenes/"+name+".unity";
                var text=File.ReadAllText(path);
                foreach(var guid in XrHandsModelGuids)Assert.That(text,Does.Not.Contain(guid),name+" must not reference the XR Hands sample meshes.");
                var scene=EditorSceneManager.OpenScene(path); // Single: the resolved models are discarded with the next open.
                var objects=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).ToArray();
                Assert.That(objects.Sum(o=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(o)),Is.Zero,name+": missing scripts");
                Assert.That(objects.Where(o=>PrefabUtility.IsPartOfPrefabInstance(o) && PrefabUtility.IsPrefabAssetMissing(o)).Select(o=>o.name),Is.Empty,name+": missing prefabs");
                var missing=new List<string>();
                // Scene-authored objects only: the hand rig is never a prefab instance.
                foreach(var component in objects.SelectMany(o=>o.GetComponents<Component>()).Where(c=>c!=null && !PrefabUtility.IsPartOfPrefabInstance(c)))
                {
                    var property=new SerializedObject(component).GetIterator();
                    while(property.Next(true))
                        if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue==null && property.objectReferenceInstanceIDValue!=0)
                            missing.Add(component.GetType().Name+" on "+component.name+"."+property.propertyPath);
                }
                Assert.That(missing,Is.Empty,name+": missing object references");
                var resolvers=objects.SelectMany(o=>o.GetComponents<VolleyHandModelResolver>()).ToArray();
                Assert.That(resolvers.Length,Is.EqualTo(2),name);
                foreach(var resolver in resolvers)
                {
                    Assert.That(resolver.transform.childCount,Is.Zero,"No baked hand mesh in the scene.");
                    Assert.That(resolver.FallbackPrefab,Is.Not.Null);
                    Assert.That(resolver.Materials,Has.Length.EqualTo(2));
                    Assert.That(resolver.Skeleton.enabled,Is.False,"Skeleton driver waits for the model.");
                    Assert.That(resolver.Skeleton.rootTransform,Is.EqualTo(resolver.transform),"Assigned placeholder root; XR Hands cannot deserialize an unassigned one off the main thread.");
                    var expected=VolleyHandModelResolver.LoadPrivateModel(resolver.PrivateResourcePath)!=null
                        ? VolleyHandModelResolver.ModelSource.Private : VolleyHandModelResolver.ModelSource.Fallback;
                    Assert.That(resolver.Resolve(),Is.True);
                    Assert.That(resolver.Source,Is.EqualTo(expected),name+" "+resolver.name);
                    AssertAllJointsMapped(resolver);
                    Assert.That(resolver.Mesh.sharedMaterials,Is.EqualTo(resolver.Materials));
                    TestContext.WriteLine(name+" "+resolver.name+": "+resolver.Source);
                }
            }
        }
    }
}
