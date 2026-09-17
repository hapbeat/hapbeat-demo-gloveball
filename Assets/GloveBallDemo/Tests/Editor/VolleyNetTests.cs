using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using GloveBallDemo.Runtime;
using System.Linq;

namespace GloveBallDemo.Tests
{
    public class VolleyNetTests
    {
        [TestCase(1f)] [TestCase(-1f)]
        public void NetReturnsSlowlyOnEitherSide(float side)
        {
            var normal=Vector3.forward*side;
            var incoming=new Vector3(2,1,-6*side);
            var outgoing=VolleyNetResponse.DampedReturn(incoming,normal,.08f,.25f);
            Assert.That(Vector3.Dot(outgoing,normal),Is.EqualTo(.48f).Within(.001f));
            Assert.That(outgoing.x,Is.EqualTo(.5f).Within(.001f));
            Assert.That(outgoing.magnitude,Is.LessThan(incoming.magnitude*.15f));
        }

        [Test]
        public void NetDeformsLocallyRestoresAndLeavesSharedAssetUnchanged()
        {
            var scene=EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var response=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<VolleyNetResponse>()).Single();
            var original=response.Visual.sharedMesh;
            var rest=original.vertices;
            var collider=response.GetComponent<BoxCollider>(); var size=collider.size;
            Assert.That(original.isReadable,Is.True);
            Assert.That(collider.isTrigger,Is.False);
            Assert.That(collider.sharedMaterial.bounciness,Is.Zero);
            typeof(VolleyNetResponse).GetMethod("Start",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(response,null);
            response.AddDent(response.transform.TransformPoint(new Vector3(0,1.93f,0)),response.transform.forward*6f);
            response.Animate(.08f);
            var deformed=response.Visual.sharedMesh.vertices;
            int moved=0;
            for(int i=0;i<rest.Length;i++)
            {
                var p=response.transform.InverseTransformPoint(response.Visual.transform.TransformPoint(rest[i]));
                if((rest[i]-deformed[i]).sqrMagnitude>1e-12f)moved++;
                if(Mathf.Abs(p.x)>4.75f) Assert.That(deformed[i],Is.EqualTo(rest[i]),"Post/cable must remain fixed");
            }
            Assert.That(moved,Is.GreaterThan(100));
            Assert.That(original.vertices,Is.EqualTo(rest));
            Assert.That(collider.size,Is.EqualTo(size));
            response.Animate(3f);
            Assert.That(response.Visual.sharedMesh.vertices,Is.EqualTo(rest));
            typeof(VolleyNetResponse).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(response,null);
        }
    }
}
