using UnityEngine;
using GloveBallDemo.Core;

namespace GloveBallDemo.Runtime
{
    /// <summary>Unit-scale pivot: local +Z is the outlet direction. The stand is not a child.</summary>
    public sealed class VolleyFeederAim : MonoBehaviour
    {
        public Transform Pivot;
        public Transform Muzzle;
        [Min(1f)] public float DegreesPerSecond = 120f;

        public Quaternion SolveRotation(Vector3 destination, float seconds, Vector3 gravity, float physicsStep = 0f)
        {
            var rotation = Pivot.rotation;
            var offset = Pivot.InverseTransformPoint(Muzzle.position);
            // The muzzle moves with pitch. Solve that dependency rather than aiming from the stand.
            for (int i = 0; i < 12; i++)
            {
                var origin = Pivot.position + rotation * offset;
                var velocity = VolleyMath.ServeVelocity(origin, destination, seconds, gravity) - .5f * gravity * physicsStep;
                rotation = Quaternion.LookRotation(velocity, Vector3.up);
            }
            return rotation;
        }

        public void Track(Vector3 destination, float seconds, float deltaTime)
        {
            Pivot.rotation = Quaternion.RotateTowards(Pivot.rotation,
                SolveRotation(destination, seconds, Physics.gravity), DegreesPerSecond * deltaTime);
        }

        public Vector3 AimForShot(Vector3 destination, float seconds, float physicsStep = 0f)
        {
            Pivot.rotation = SolveRotation(destination, seconds, Physics.gravity, physicsStep);
            // PhysX advances velocity before position (semi-implicit Euler).
            return VolleyMath.ServeVelocity(Muzzle.position, destination, seconds, Physics.gravity) - .5f * Physics.gravity * physicsStep;
        }
    }
}
