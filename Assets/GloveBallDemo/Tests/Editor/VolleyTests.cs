using GloveBallDemo.Core;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;

namespace GloveBallDemo.Tests
{
    public class VolleyTests
    {
        [Test]
        public void HighSpeedHandBallCrossingIsNotMissed()
        {
            Assert.That(VolleyMath.SweptContact(new Vector3(0,0,1), new Vector3(0,0,-1),
                Vector3.zero, Vector3.zero, .2f, out float t), Is.True);
            Assert.That(t, Is.EqualTo(.4f).Within(.001f));
            Assert.That(VolleyMath.SweptContact(new Vector3(2,0,1), new Vector3(2,0,-1),
                Vector3.zero, Vector3.zero, .2f, out _), Is.False);
        }
        [Test]
        public void HandSweepAlsoHitsStationaryBall()
        {
            Assert.That(VolleyMath.SweptContact(Vector3.zero, Vector3.zero,
                Vector3.left, Vector3.right, .2f, out _), Is.True);
        }
        [Test]
        public void ReceiveFaceAndSwingControlReturnWithoutTargetAssist()
        {
            var incoming = new Vector3(0,-3,-5);
            var normal = new Vector3(0,1,1).normalized;
            var received = VolleyMath.ReturnVelocity(incoming, Vector3.zero, normal, 1,1,20);
            Assert.That(received.y, Is.GreaterThan(0));
            Assert.That(received.z, Is.GreaterThan(0));
            var flat = VolleyMath.ReturnVelocity(incoming, Vector3.zero, Vector3.up,1,1,20);
            Assert.That(flat.z, Is.LessThan(0), "Badly angled receive may travel toward torso; never auto-correct away");
            var spike = VolleyMath.ReturnVelocity(Vector3.down*4, new Vector3(0,-3,5),Vector3.forward, .9f,1.5f,14);
            Assert.That(spike.z, Is.GreaterThan(0));
            Assert.That(spike.y, Is.LessThan(0));
            Assert.That(spike.magnitude, Is.LessThanOrEqualTo(14.001f));
        }
        [Test]
        public void FeedArrivesAtSpecifiedHeightAndTime()
        {
            var origin = new Vector3(0,2.2f,1);
            var contact = new Vector3(.2f,1.2f,-4);
            float t = .85f;
            var v = VolleyMath.ServeVelocity(origin,contact,t,Physics.gravity);
            Assert.That(Vector3.Distance(origin + v*t + .5f*Physics.gravity*t*t, contact), Is.LessThan(.0001f));
        }
        [Test]
        public void VolleyDeflectionNeverGrabsAndHeldBallsCannotBeHit()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("volley ball test"); SceneManager.MoveGameObjectToScene(go,scene);
                var ball = go.AddComponent<Ball>();
                ball.BindPool(null); // EditMode does not invoke Awake; use the normal pool initialization seam.
                ball.LaunchIncoming(Vector3.up,Vector3.back);
                Assert.That(ball.Deflect(Vector3.forward*4),Is.True);
                Assert.That(ball.State,Is.EqualTo(BallState.Thrown));
                Assert.That(ball.transform.parent,Is.Null);
                Assert.That(ball.Body.isKinematic,Is.False);
                var anchor = new GameObject("anchor"); SceneManager.MoveGameObjectToScene(anchor,scene);
                ball.TryGrab(anchor.transform);
                Assert.That(ball.Deflect(Vector3.forward),Is.False);
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        [TestCase("VolleyReceive-codex")]
        [TestCase("VolleySpike-codex")]
        public void DrillCopyHasNoActiveGrabButtonsAndHasBothTrackingHands(string name)
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/"+name+".unity",OpenSceneMode.Additive);
            try
            {
                var components=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
                Assert.That(components.OfType<DemoGameController>().Count(),Is.Zero);
                Assert.That(components.OfType<GloveController>().Count(c=>c.isActiveAndEnabled),Is.Zero);
                Assert.That(components.OfType<QuestMenuController>().Count(c=>c.isActiveAndEnabled),Is.Zero);
                var drill=components.OfType<VolleyDrillController>().Single();
                Assert.That(drill.Left.Side,Is.EqualTo(GloveSide.Left));
                Assert.That(drill.Right.Side,Is.EqualTo(GloveSide.Right));
                Assert.That(drill.Left.TrackingSpace,Is.EqualTo(drill.Head.parent));
                Assert.That(drill.Panels.Length,Is.GreaterThanOrEqualTo(3));
                Assert.That(components.OfType<VolleyBodySurface>().Single().Drill,Is.EqualTo(drill));
            }
            finally {EditorSceneManager.CloseScene(scene,true);}
        }
    }
}
