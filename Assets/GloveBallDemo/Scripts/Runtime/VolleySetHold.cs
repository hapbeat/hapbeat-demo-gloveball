using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// A set (toss) is not a bounce: the ball is caught on the fingers, rides the hands down and up, and leaves them
    /// with the push. Each physics step feeds the hands' centre and velocity; the hold ends when the hands have pushed
    /// up faster than <see cref="ReleaseSpeed"/> and start to slow down (the end of the push), or after
    /// <see cref="MaxSeconds"/>. The ball leaves with the fastest upward hand velocity of the push.
    /// </summary>
    public sealed class VolleySetHold
    {
        public float MaxSeconds=.35f;
        public float ReleaseSpeed=.8f;
        /// <summary>The push has peaked once the upward speed falls below this share of its peak.</summary>
        public float PeakDrop=.85f;
        public bool Holding { get; private set; }
        public float Seconds { get; private set; }
        /// <summary>Lowest upward hand speed during the hold (negative: how far the hands gave way under the ball).</summary>
        public float Give { get; private set; }
        public Vector3 Peak { get; private set; }
        Vector3 _offset;

        public void Begin(Vector3 ball,Vector3 hands)
        {
            Holding=true;Seconds=0f;Give=0f;Peak=Vector3.zero;
            _offset=ball-hands;
            if(_offset.y<.06f)_offset.y=.06f; // the ball sits on the fingers, never inside the hands
        }
        /// <summary>Advances the hold; returns true when the ball leaves, with its release velocity.</summary>
        public bool Step(Vector3 hands,Vector3 handVelocity,float dt,out Vector3 ballPosition,out Vector3 release)
        {
            ballPosition=hands+_offset;release=default;
            if(!Holding)return false;
            Seconds+=dt;
            Give=Mathf.Min(Give,handVelocity.y);
            if(handVelocity.y>Peak.y)Peak=handVelocity;
            bool pushed=Peak.y>=ReleaseSpeed && handVelocity.y<Peak.y*PeakDrop;
            if(!pushed && Seconds<MaxSeconds)return false;
            Holding=false;
            release=pushed ? Peak : handVelocity;
            return true;
        }
        public void Cancel()=>Holding=false;
    }
}
