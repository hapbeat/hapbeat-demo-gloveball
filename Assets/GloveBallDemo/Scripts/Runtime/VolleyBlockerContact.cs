using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Solid opposing blocker. The ball rebounds physically; the rally scores a player spike touching it as blocked.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VolleyBlockerContact : MonoBehaviour
    {
        public VolleyAerialSequence Rally;
        void Awake()=>Configure();
        public void Configure()
        {
            var body=GetComponent<Rigidbody>();
            body.isKinematic=true;body.useGravity=false;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
        }
        void OnCollisionEnter(Collision hit)
        {
            var ball=hit.collider.GetComponentInParent<Ball>();
            if(ball!=null && Rally!=null)Rally.RegisterSpikeBlocked(ball,hit.GetContact(0).point);
        }
    }
}
