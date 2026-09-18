using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace GloveBallDemo.Tests
{
    public class BallFeelTests
    {
        [Test]
        public void FoamImpactHasPromptAudibleOnset()
        {
            var clip = Resources.Load<BallFeelSettings>("BallFeelSettings").Find(BallKind.Foam).ImpactClip;
            clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            Assert.That(clip.GetData(data, 0), Is.True);
            float peak = 0f;
            foreach (float sample in data) peak = Mathf.Max(peak, Mathf.Abs(sample));
            Assert.That(peak, Is.GreaterThan(0f));
            int onset = System.Array.FindIndex(data, sample => Mathf.Abs(sample) > peak * .1f);
            Assert.That(onset / (float)(clip.channels * clip.frequency), Is.LessThan(.03f));
        }
        [TestCase(.95f, 1.05f, 0f, .95f)]
        [TestCase(.95f, 1.05f, 1f, 1.05f)]
        [TestCase(1.05f, .95f, .5f, 1f)]
        [TestCase(1f, 1f, .3f, 1f)]
        public void ImpactVariationRespectsRange(float a, float b, float sample, float expected)
        {
            Assert.That(BallFeel.SampleRange(new Vector2(a,b), sample, .1f, 3f),
                Is.EqualTo(expected).Within(.0001f));
        }
        [Test]
        public void EveryKindHasUniqueAudioAndPhysicsAndFoamIsRough()
        {
            var settings = Resources.Load<BallFeelSettings>("BallFeelSettings");
            Assert.That(settings, Is.Not.Null);
            var clips = new HashSet<AudioClip>();
            for (int i=0;i<5;i++)
            {
                var feel=settings.Find((BallKind)i);
                Assert.That(feel, Is.Not.Null);
                Assert.That(feel.ImpactClip, Is.Not.Null);
                Assert.That(clips.Add(feel.ImpactClip), Is.True);
                // Supplied plastic click is only 34ms long.
                Assert.That(feel.ImpactClip.length, Is.InRange(.01f,3f));
                Assert.That(feel.BounceMaterial, Is.Not.Null);
            }
            Assert.That(settings.Find(BallKind.Foam).AirResistance, Is.GreaterThan(settings.Find(BallKind.Basketball).AirResistance));
            var material=UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/GloveBallDemo/Art/Balls/Foam_0.mat");
            Assert.That(material.GetFloat("_Smoothness"),Is.LessThan(.1f));
            Assert.That(material.GetTexture("_BaseMap"),Is.Not.Null);
        }

        [TestCase(.02f)] [TestCase(.08f)] [TestCase(.65f)] [TestCase(.04f)] [TestCase(.4f)]
        public void DampedLaunchStillArrivesAtAimPoint(float damping)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                Assert.That(scene.GetPhysicsScene(),Is.Not.EqualTo(Physics.defaultPhysicsScene),"Never simulate the user's scene");
                var go=new GameObject("isolated test ball");
                SceneManager.MoveGameObjectToScene(go,scene);
                var body=go.AddComponent<Rigidbody>();
                body.linearDamping=damping;
                var origin=new Vector3(0,1.5f,0);
                var target=new Vector3(0,1.1f,8);
                var planned=new Vector3(0,4f,10f);
                body.position=origin;
                body.linearVelocity=BallLauncher.CompensateAirResistance(origin,target,planned,damping);
                int steps=Mathf.RoundToInt(.8f/Time.fixedDeltaTime);
                for(int i=0;i<steps;i++) scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
                Assert.That(Vector3.Distance(body.position,target),Is.LessThan(.08f));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
