using System.Collections.Generic;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Role=GloveBallDemo.Runtime.VolleyOpponentPrototype.MotionRole;

namespace GloveBallDemo.Tests
{
    /// <summary>The low-poly player body (VolleyPlayerModelBuilder) changes only meshes: contact points and the pose contract stay put.</summary>
    public class VolleyPlayerModelTests
    {
        const string Scene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
        const string PlayerShader="GloveBall/VolleyPlayer";
        static void Near(Vector3 actual,Vector3 expected,string what)=>Assert.That(Vector3.Distance(actual,expected),Is.LessThan(1e-4f),$"{what}: {actual} vs {expected}");

        [Test]
        public void ContactPointsMatchTheMannequinValues()
        {
            var go=new GameObject("contact test");
            try
            {
                var actor=go.AddComponent<VolleyOpponentPrototype>();actor.JumpHeight=.65f;
                actor.Role=Role.Spike;Near(actor.LocalRelease,new Vector3(.25f,2.684871f,.22f),"spike release");
                actor.Role=Role.Set;Near(actor.LocalRelease,new Vector3(0f,1.82f,.3f),"set contact");
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void EveryVolleyActorWearsThePlayerModel()
        {
            EditorSceneManager.OpenScene(Scene);
            var actors=Object.FindObjectsByType<VolleyOpponentPrototype>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            Assert.That(actors.Length,Is.GreaterThan(1));
            var jerseys=new HashSet<Color32>();var block=new MaterialPropertyBlock();
            foreach(var actor in actors)
            {
                foreach(var part in new[]{actor.Torso,actor.Head,actor.LeftUpperArm,actor.LeftForearm,actor.RightUpperArm,actor.RightForearm,
                    actor.LeftThigh,actor.LeftShin,actor.RightThigh,actor.RightShin,actor.LeftHandModel,actor.RightHandModel,actor.LeftShoe,actor.RightShoe})
                {
                    Assert.That(part,Is.Not.Null,actor.name+" is missing a body part");
                    var renderer=part.GetComponent<MeshRenderer>();
                    Assert.That(renderer.sharedMaterial.shader.name,Is.EqualTo(PlayerShader),part.name);
                    Assert.That(part.GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.GreaterThan(48),part.name+" is still a primitive");
                }
                Assert.That(actor.Torso.Find("Jersey number"),Is.Not.Null,actor.name+" has no jersey number");
                Assert.That(actor.Head.localScale,Is.EqualTo(Vector3.one*.25f),"blocker head colliders are sized for the 0.25 head");

                if(actor.Role==Role.Block)
                {
                    actor.Pose(VolleyOpponentPrototype.BlockPeakPhase);
                    Near(actor.RightHandModel.localPosition,new Vector3(.16f,2.14f+actor.JumpHeight,.42f),actor.name+" right hand at the block peak");
                    Near(actor.LeftHandModel.localPosition,new Vector3(-.16f,2.14f+actor.JumpHeight,.42f),actor.name+" left hand at the block peak");
                    if(actor.RightHand!=null)Near(actor.RightHand.localPosition,actor.RightHandModel.localPosition,actor.name+" palm collider follows the hand");
                }
                if(actor.Role==Role.Set)Near(actor.LocalRelease,new Vector3(0f,1.82f,.3f),actor.name+" set contact");

                if(!actor.OverrideKit)continue;
                actor.ApplyKit();
                actor.Torso.GetComponent<Renderer>().GetPropertyBlock(block);
                var tinted=block.GetColor("_JerseyColor");
                Assert.That(tinted==actor.JerseyColour || tinted==actor.JerseyColour.linear,Is.True,actor.name+" jersey tint");
                jerseys.Add(actor.JerseyColour);
            }
            Assert.That(jerseys.Count,Is.GreaterThanOrEqualTo(2),"the two teams keep distinct jerseys");
        }
    }
}
