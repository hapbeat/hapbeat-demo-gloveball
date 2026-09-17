using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>One rigid receive surface while both tracked hands are close. Never auto-aims.</summary>
    public sealed class VolleyJoinedHands : MonoBehaviour
    {
        public VolleyTrackedHand Left;
        public VolleyTrackedHand Right;
        public BoxCollider Volume;
        [Min(.01f)] public float JoinDistance=.18f;
        [Min(.01f)] public float SeparateDistance=.24f;
        [Min(.001f)] public float SmoothingSeconds=.06f;
        public bool Joined { get; private set; }
        public Vector3 Velocity { get; private set; }
        public Vector3 PreviousPosition { get; private set; }
        public Quaternion PreviousRotation { get; private set; }

        public static bool ShouldJoin(bool previouslyJoined,bool leftReady,bool rightReady,float distance,float join,float separate)
            => leftReady && rightReady && distance <= (previouslyJoined ? Mathf.Max(join,separate) : join);

        public void Sample(float dt)
        {
            var l=Left.ContactVolume.transform.TransformPoint(Left.ContactVolume.center);
            var r=Right.ContactVolume.transform.TransformPoint(Right.ContactVolume.center);
            bool wasJoined=Joined;
            Joined=ShouldJoin(Joined,Left.Ready,Right.Ready,Vector3.Distance(l,r),JoinDistance,SeparateDistance);
            Volume.enabled=Joined;
            if(!Joined) { Velocity=Vector3.zero; return; }
            var position=(l+r)*.5f;
            var rotation=Quaternion.Slerp(Left.transform.rotation,Right.transform.rotation,.5f);
            PreviousPosition=transform.position; PreviousRotation=transform.rotation;
            float alpha=1f-Mathf.Exp(-dt/Mathf.Max(.001f,SmoothingSeconds));
            if(!wasJoined)
            {
                transform.SetPositionAndRotation(position,rotation);
                PreviousPosition=position; PreviousRotation=rotation;
                Velocity=(Left.Velocity+Right.Velocity)*.5f;
            }
            else
            {
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position,position,alpha),Quaternion.Slerp(transform.rotation,rotation,alpha));
                Velocity=Vector3.Lerp(Velocity,(Left.Velocity+Right.Velocity)*.5f,alpha);
            }
        }
        private void OnDrawGizmosSelected()
        {
            if(Volume==null) return;
            Gizmos.color=Joined ? Color.yellow : Color.gray;
            Gizmos.matrix=Volume.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Volume.center,Volume.size);
            Gizmos.matrix=Matrix4x4.identity;
        }
    }
}
