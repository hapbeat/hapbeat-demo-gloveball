using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Two-arm, button-free jump. Detection is head-relative so virtual lift cannot trigger itself.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class VolleyArmJump : MonoBehaviour
    {
        public VolleyFloorTracking Floor;
        public VolleyTrackedHand Left, Right;
        [Min(.1f)] public float JumpHeight=.75f;
        [Min(.4f)] public float Duration=1f;
        [Min(.1f)] public float Cooldown=.5f;
        [Tooltip("Both wrists must first be this far below the eyes.")]
        public float LoweredDistance=.35f;
        public float RequiredRise=.18f;
        public float MinimumUpwardSpeed=.8f;
        public float GestureWindow=.8f;
        [Tooltip("Beginner Block: the opponent sequence schedules jumps; raised hands never trigger another jump.")]
        public bool AutomaticJump;
        [Tooltip("No jump at all (RECEIVE and TOSS: a pass's arm swing must never launch one), whatever AutomaticJump says.")]
        public bool Suppressed;
        [Header("Block: relaxed height-only gesture")]
        public bool UseHeightThreshold;
        [Tooltip("Both wrists below this eye-relative height prepare the next jump.")]
        public float RearmHeightFromEyes=-.25f;
        [Tooltip("Both wrists above this eye-relative height jump once. No speed or time requirement.")]
        public float JumpHeightFromEyes=-.05f;
        [Min(0f)] public float TrackingGraceSeconds=1.5f;
        [Tooltip("Height threshold: a hand lost this long ago (s) still counts at its last tracked height (raised hands often leave the cameras' view).")]
        [Min(0f)] public float HandHoldSeconds=.4f;
        float _lastLeftY, _lastRightY, _leftSeen=-1f, _rightSeen=-1f;
        static bool Seen(VolleyTrackedHand hand)=>hand.Ready && !hand.IsEstimated && hand.Source!="brief-loss";
        public bool Airborne { get; private set; }
        /// <summary>A jump took off (the player's or an automatic one) / came down at its end (not a reset).</summary>
        public event System.Action TookOff, Landed;
        public int Jumps { get; private set; }
        public float Lift { get; private set; }
        /// <summary>0..1 through the current jump; 0 when grounded.</summary>
        public float Progress=>Airborne ? Mathf.Clamp01(_elapsed/Mathf.Max(.1f,Duration)) : 0f;
        float _elapsed, _cooldown, _gestureAge, _baseLeft, _baseRight, _previousLeft, _previousRight;
        bool _sampled, _armed;
        float _trackingGap;

        void Update()
        {
            // Use tracking-space coordinates, never the elevated world-space eye/hand positions.
            var space=Floor.Origin.CameraFloorOffsetObject.transform;
            float head=space.InverseTransformPoint(Floor.Origin.Camera.transform.position).y;
            float leftY=space.InverseTransformPoint(Left.transform.position).y-head,rightY=space.InverseTransformPoint(Right.transform.position).y-head;
            bool left=Seen(Left),right=Seen(Right);
            if(UseHeightThreshold)
            {
                // A raised hand that left the cameras' view a moment ago still counts where it was last seen.
                float now=Time.unscaledTime;
                if(left){_lastLeftY=leftY;_leftSeen=now;}else if(_leftSeen>=0f && now-_leftSeen<=HandHoldSeconds){leftY=_lastLeftY;left=true;}
                if(right){_lastRightY=rightY;_rightSeen=now;}else if(_rightSeen>=0f && now-_rightSeen<=HandHoldSeconds){rightY=_lastRightY;right=true;}
            }
            Tick(Time.deltaTime,left&&right,leftY,rightY,GameInputGate.IsBlocked,head,Time.unscaledDeltaTime);
            Floor.SetVirtualLift(Lift);
            LogJumpState(head);
        }
        int _loggedJumps;float _blockedLog;
        /// <summary>The log says why hands raised above the jump height did not jump (every 2 s at most), and each jump.</summary>
        void LogJumpState(float head)
        {
            if(Jumps!=_loggedJumps){_loggedJumps=Jumps;Debug.Log($"[VolleyJump] jump {Jumps} auto={AutomaticJump}");}
            var space=Floor.Origin.CameraFloorOffsetObject.transform;
            float l=space.InverseTransformPoint(Left.transform.position).y-head,r=space.InverseTransformPoint(Right.transform.position).y-head;
            bool up=l>=JumpHeightFromEyes && r>=JumpHeightFromEyes;
            _blockedLog-=Time.unscaledDeltaTime;
            if(!up || Airborne || _blockedLog>0f || GameInputGate.IsBlocked || Left.Source=="lost" && Right.Source=="lost")return;
            _blockedLog=2f;
            Debug.Log($"[VolleyJump] hands up, no jump: paused={GameInputGate.IsBlocked} dt={Time.deltaTime:F3} scale={Time.timeScale:F2} auto={AutomaticJump} suppressed={Suppressed} cooldown={_cooldown:F2} armed={_armed} " +
                $"L ready={Left.Ready} est={Left.IsEstimated} src={Left.Source} y={l:F2} / R ready={Right.Ready} est={Right.IsEstimated} src={Right.Source} y={r:F2}");
        }
        public static float HeightAt(float time,float duration,float height)
        {
            float t=Mathf.Clamp01(time/Mathf.Max(.1f,duration));
            return 4f*height*t*(1f-t);
        }
        public void Tick(float dt,bool tracked,float leftY,float rightY,bool paused,float headY=0f,float trackingDt=-1f)
        {
            if(paused){if(!AutomaticJump)ResetJump();return;}
            if(dt<=0f)
            {
                if(!tracked && trackingDt>0f){_trackingGap+=trackingDt;if(_trackingGap>TrackingGraceSeconds){_sampled=false;_armed=false;}}
                return; // Tracking pause freezes an airborne jump, rather than cancelling it.
            }
            if(Airborne)
            {
                _elapsed+=Mathf.Max(0,dt);Lift=HeightAt(_elapsed,Duration,JumpHeight);
                if(_elapsed>=Duration){Airborne=false;Lift=0;_cooldown=Cooldown;Landed?.Invoke();}
                _sampled=false;_armed=false;return;
            }
            _cooldown=Mathf.Max(0,_cooldown-dt);
            if(AutomaticJump || Suppressed){_armed=false;_sampled=false;return;}
            if(!tracked)
            {
                _trackingGap+=dt;
                if(_trackingGap>TrackingGraceSeconds){_sampled=false;_armed=false;}
                return;
            }
            if(UseHeightThreshold)
            {
                _trackingGap=0;
                if(_cooldown>0)return;
                if(leftY<=RearmHeightFromEyes && rightY<=RearmHeightFromEyes)_armed=true;
                if(_armed && leftY>=JumpHeightFromEyes && rightY>=JumpHeightFromEyes)
                {Airborne=true;_elapsed=0;Lift=0;Jumps++;_armed=false;_sampled=false;TookOff?.Invoke();}
                return;
            }
            if(dt>.12f){_sampled=false;_armed=false;_trackingGap=0;return;}
            // Infer intent only on a real reacquired pose. Never invent a jump during occlusion.
            float sampleSeconds=dt+Mathf.Min(_trackingGap,.2f);
            _trackingGap=0;
            // Absolute tracking-space wrist speed rejects a head crouch with stationary hands.
            float lv=_sampled?(leftY+headY-_previousLeft)/sampleSeconds:0;
            float rv=_sampled?(rightY+headY-_previousRight)/sampleSeconds:0;
            _previousLeft=leftY+headY;_previousRight=rightY+headY;_sampled=true;
            if(_cooldown>0)return;
            if(!_armed && leftY<-LoweredDistance && rightY<-LoweredDistance)
            {_armed=true;_baseLeft=leftY;_baseRight=rightY;_gestureAge=0;}
            if(!_armed)return;
            _gestureAge+=dt;
            if(_gestureAge>GestureWindow){_armed=false;return;}
            if(leftY-_baseLeft>=RequiredRise && rightY-_baseRight>=RequiredRise
                && lv>=MinimumUpwardSpeed && rv>=MinimumUpwardSpeed)
            {Airborne=true;_elapsed=0;Lift=0;Jumps++;_armed=false;_sampled=false;TookOff?.Invoke();}
        }
        public void ResetJump()
        {
            Airborne=false;Lift=0;_elapsed=0;_sampled=false;_armed=false;_cooldown=Cooldown;_trackingGap=0;
            if(Floor!=null)Floor.SetVirtualLift(0);
        }
        public bool TryStartAutomaticJump()
        {
            if(!AutomaticJump || Suppressed || Airborne || _cooldown>0 || GameInputGate.IsBlocked)return false;
            Airborne=true;_elapsed=0;Lift=0;Jumps++;_armed=false;_sampled=false;
            TookOff?.Invoke();
            return true;
        }
        void OnDisable()=>ResetJump();
    }
}
