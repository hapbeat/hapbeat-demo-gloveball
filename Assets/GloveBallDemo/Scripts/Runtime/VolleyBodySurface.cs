using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Torso remains solid for incoming AND returned balls; no miss penalty or lives.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class VolleyBodySurface : MonoBehaviour
    {
        public Transform Head;
        public VolleyDrillController Drill;
        public float Height = 1.05f;
        public float Radius = .22f;
        private Rigidbody _body;
        private float _lastHit = -100f;
        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var capsule = GetComponent<CapsuleCollider>();
            capsule.height = Height; capsule.radius = Radius; capsule.direction = 1; capsule.isTrigger = false;
        }
        private void FixedUpdate()
        {
            _body.MovePosition(Head.position - Vector3.up * (Height * .5f + .15f));
        }
        private void OnCollisionEnter(Collision collision)
        {
            var ball = collision.collider.GetComponentInParent<Ball>();
            if (ball == null || (ball.State != BallState.Incoming && ball.State != BallState.Thrown) || Time.time - _lastHit < .2f) return;
            _lastHit = Time.time;
            Drill.RegisterBodyHit(ball, collision.GetContact(0).point);
        }
    }
}
