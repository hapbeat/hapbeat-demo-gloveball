using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Small physical head proxy for block misses; uses the existing body impact event family.</summary>
    [RequireComponent(typeof(Rigidbody),typeof(SphereCollider))]
    public sealed class VolleyHeadSurface : MonoBehaviour
    {
        public VolleyDrillController Drill;
        Rigidbody _body;
        float _lastHit=-100f;
        void Awake()
        {
            _body=GetComponent<Rigidbody>();_body.isKinematic=true;_body.useGravity=false;
            _body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
        }
        void FixedUpdate()=>_body.MovePosition(Drill.Head.position);
        void OnCollisionEnter(Collision hit)
        {
            var ball=hit.collider.GetComponentInParent<Ball>();
            if(ball==null || (ball.State!=BallState.Incoming && ball.State!=BallState.Thrown) || Time.time-_lastHit<.2f)return;
            _lastHit=Time.time;Drill.RegisterBodyHit(ball,hit.GetContact(0).point);
        }
    }
}
