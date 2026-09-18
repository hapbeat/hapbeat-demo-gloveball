using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Feed choreography only; pooled balls and contacts remain owned by VolleyDrillController.</summary>
    public sealed class VolleyAerialSequence : MonoBehaviour
    {
        public VolleyDrillController Drill;
        public VolleyArmJump Jump;
        public VolleyOpponentPrototype Opponent;
        public BallLauncher TossLauncher;
        [Min(.5f)] public float WindupSeconds=1.2f;
        [Min(.5f)] public float TossFlightSeconds=1.15f;
        [Min(1f)] public float BlockSpeed=9f;
        [Range(0,1)] public float FaceShotChance=.25f;
        [Min(0)] public float HorizontalSpread=.24f;
        [Min(0)] public float HeightSpread=.18f;
        public float SpikeReachAboveEye=.50f;
        public float BlockReachAboveEye=.55f;
        public bool LastWasFaceShot { get; private set; }
        public string Cue { get; private set; }="LOWER BOTH HANDS, THEN SWING UP TO JUMP";
        float _windup=-1, _follow=-1, _sinceRelease=-1;
        public bool AttackStarted=>_windup>=0f;
        Ball _preparedBall;
        Vector3 _tossStart, _tossEnd;

        public static Vector3 TossPosition(Vector3 start,Vector3 end,float elapsed,float duration)
        {
            float t=Mathf.Clamp01(elapsed/Mathf.Max(.01f,duration));
            return Vector3.Lerp(start,end,t)-.5f*Physics.gravity*duration*duration*t*(1f-t);
        }
        public bool BeginToss()
        {
            if(_preparedBall!=null)return true;
            if(TossLauncher==null || Opponent==null)return false;
            _preparedBall=Drill.Pool.Take();
            if(_preparedBall==null)return false;
            _tossEnd=Opponent.ReleasePosition;
            var aim=TossLauncher.GetComponent<VolleyFeederAim>();
            if(aim!=null)aim.AimForShot(_tossEnd,WindupSeconds);
            _tossStart=TossLauncher.MuzzlePosition;
            _preparedBall.Body.isKinematic=true;
            _preparedBall.Body.detectCollisions=false;
            _preparedBall.transform.SetPositionAndRotation(_tossStart,Quaternion.identity);
            _preparedBall.Body.position=_tossStart;
            _preparedBall.gameObject.SetActive(true);
            if(aim!=null)aim.PlayShotFeedback();
            return true;
        }
        public Ball TakePreparedBall()
        {
            var ball=_preparedBall;_preparedBall=null;
            if(ball!=null)ball.Body.detectCollisions=true;
            return ball;
        }

        public Vector3 GroundedEye=>Drill.Head.position-Vector3.up*Jump.Floor.VirtualLift;
        public float SolveBlockShot(Vector3 start,ref Vector3 destination)
        {
            // A spike is a fast descending shot, not the receive feeder's net-clearing lob.
            float seconds=Mathf.Clamp(Vector3.ProjectOnPlane(destination-start,Vector3.up).magnitude/BlockSpeed,.08f,.4f);
            float g=Mathf.Abs(Physics.gravity.y);
            float fraction=Drill.ReceiveNet!=null ? (Drill.ReceiveNet.bounds.center.z-start.z)/(destination.z-start.z) : 0f;
            float minimum=float.NegativeInfinity;
            if(fraction>0f && fraction<1f)
            {
                float safe=Drill.ReceiveNet.bounds.max.y+.12f;
                seconds=Mathf.Min(seconds,Mathf.Sqrt(Mathf.Max(.001f,2f*(start.y-safe)/g))*.9f/fraction);
                minimum=start.y+(safe-start.y-.5f*g*seconds*seconds*fraction*(1f-fraction))/fraction;
            }
            float maximum=start.y-.5f*g*seconds*seconds;
            destination.y=Mathf.Clamp(destination.y,Mathf.Min(minimum,maximum),maximum);
            return seconds;
        }
        public Vector3 Destination(float horizontal,float vertical,bool faceShot)
        {
            var forward=Vector3.ProjectOnPlane(Drill.CourtFrame.forward,Vector3.up).normalized;
            var right=Vector3.Cross(Vector3.up,forward);
            var result=GroundedEye+forward*Drill.ContactForwardDistance+right*horizontal;
            result.y+=Drill.Drill==VolleyDrill.Spike ? Jump.JumpHeight+SpikeReachAboveEye
                : faceShot ? Jump.Floor.VirtualLift : Jump.JumpHeight+BlockReachAboveEye;
            result.y+=vertical;return result;
        }
        public bool TickFeed(float dt,out Vector3 start,out Vector3 destination,out float seconds)
        {
            start=destination=Vector3.zero;seconds=0;
            if(_windup<0){_windup=0;_follow=-1;_sinceRelease=-1;}
            _windup+=dt;
            if(_preparedBall!=null)
            {
                var position=TossPosition(_tossStart,_tossEnd,_windup,WindupSeconds);
                _preparedBall.transform.position=position;
                _preparedBall.Body.position=position;
            }
            bool block=Drill.Drill==VolleyDrill.Block;
            Cue=block ? "WATCH OPPONENT — PREPARE BOTH HANDS LOW" : "GET READY — HANDS LOW, WAIT FOR THE TOSS";
            if(block)Opponent.PreviewPhase=Mathf.Min(.52f,_windup/WindupSeconds*.52f);
            if(_windup<WindupSeconds)return false;
            LastWasFaceShot=block&&Random.value<FaceShotChance;
            destination=Destination(Random.Range(-HorizontalSpread,HorizontalSpread),Random.Range(-HeightSpread,HeightSpread),LastWasFaceShot);
            if(block)
            {
                Opponent.Pose(.52f);start=Opponent.ReleasePosition;
                seconds=SolveBlockShot(start,ref destination);
                _follow=0;
            }
            else
            {
                seconds=TossFlightSeconds;
                var aim=TossLauncher.GetComponent<VolleyFeederAim>();
                aim.AimForShot(destination,seconds);start=TossLauncher.MuzzlePosition;
            }
            _windup=-1;_sinceRelease=0;return true;
        }
        void Update()
        {
            if(GameInputGate.IsBlocked)return;
            if(_sinceRelease>=0)
            {
                _sinceRelease+=Time.deltaTime;
                Cue=Drill.Drill==VolleyDrill.Block ? "JUMP — SWING ARMS UP, BLOCK IN FRONT"
                    : _sinceRelease<TossFlightSeconds-.65f ? "WATCH THE TOSS"
                    : _sinceRelease<TossFlightSeconds+.2f ? "JUMP — SWING BOTH ARMS UP, THEN SPIKE WITH RIGHT HAND"
                    : "LAND — LOWER BOTH HANDS FOR THE NEXT TOSS";
            }
            if(_follow>=0 && Opponent!=null)
            {
                _follow+=Time.deltaTime;
                Opponent.PreviewPhase=Mathf.Lerp(.52f,1f,Mathf.Clamp01(_follow/.9f));
            }
        }
        public void CancelFeed()
        {
            _windup=-1;
            var ball=TakePreparedBall();
            if(ball!=null && Drill!=null && Drill.Pool!=null)Drill.Pool.Return(ball,"cancelled volley toss");
            if(_follow<0 && Opponent!=null)Opponent.PreviewPhase=0;
        }
        void OnDisable()=>CancelFeed();
    }
}
