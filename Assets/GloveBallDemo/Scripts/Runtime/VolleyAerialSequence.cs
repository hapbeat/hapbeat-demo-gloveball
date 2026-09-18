using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Feed choreography only; pooled balls and contacts remain owned by VolleyDrillController.</summary>
    public sealed class VolleyAerialSequence : MonoBehaviour
    {
        public enum RallyPhase { Disabled, Idle, OpponentSet, OpponentSpike, AllyDelay, AllySet, PlayerSpike, Recovery }
        public VolleyDrillController Drill;
        public VolleyArmJump Jump;
        public VolleyOpponentPrototype Opponent;
        public BallLauncher TossLauncher;
        [Header("Block + spike rally")]
        [Tooltip("Runs opponent spike -> player block -> friendly set -> player spike. The player remains at a fixed court position.")]
        public bool RallyEnabled=true;
        public VolleyOpponentPrototype Ally;
        public BallLauncher AllyTossLauncher;
        [Min(.1f)] public float AllyRecoverySeconds=.55f;
        [Min(.35f)] public float AllySetSeconds=.9f;
        [Min(.3f)] public float AllyFlightSeconds=.72f;
        [Min(.1f)] public float RallyResetSeconds=1.05f;
        [Min(0f)] public float AllySetLateralSpread=.55f;
        [Header("Opponent contact audio (no haptics)")]
        public AudioClip SpikeClip;
        [Range(0f,1f)] public float SpikeVolume=.85f;
        public event System.Action<AudioClip,Vector3> SpikeAudioRequested;
        AudioSource _spikeAudio;
        [Min(.5f)] public float WindupSeconds=1.2f;
        [Min(.5f)] public float TossFlightSeconds=1.15f;
        [Min(1f)] public float BlockSpeed=9f;
        [Range(0,1)] public float FaceShotChance=.25f;
        [Min(0)] public float HorizontalSpread=.24f;
        [Min(0)] public float HeightSpread=.18f;
        public float SpikeReachAboveEye=.50f;
        public float BlockReachAboveEye=.55f;
        [Header("Beginner automatic jump")]
        [Min(0f)] public float AutoJumpLeadSeconds=.32f;
        [Range(0f,.1f)] public float AutoJumpTimingJitter=.03f;
        float _autoJumpAt;
        bool _autoJumpIssued;
        public float ScheduledJumpTime=>_autoJumpAt;
        public bool BeginnerBlock=>Drill.Drill==VolleyDrill.Block && Jump.AutomaticJump;
        public bool LastWasFaceShot { get; private set; }
        public string Cue { get; private set; }="GET READY";
        public RallyPhase Phase { get; private set; }=RallyPhase.Disabled;
        bool RallyActive => RallyEnabled && Drill!=null && Drill.Drill==VolleyDrill.Block;
        public bool IsOpponentSpike => RallyActive && Phase==RallyPhase.OpponentSpike;
        public bool IsPlayerSpike => RallyActive && Phase==RallyPhase.PlayerSpike;
        float _windup=-1, _follow=-1, _sinceRelease=-1;
        float _rallyTimer, _allyJumpAt, _allyLateral;
        bool _allyJumpIssued;
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

        /// <summary>Creates only runtime presentation objects when an older block scene has not yet been saved with the rally wiring.</summary>
        public void EnsureRallyWiring()
        {
            if(!RallyActive || !Application.isPlaying)return;
            var forward=Vector3.ProjectOnPlane(Drill.CourtFrame.forward,Vector3.up).normalized;
            var right=Vector3.Cross(Vector3.up,forward);
            var net=Drill.ReceiveNet!=null ? Drill.ReceiveNet.transform.position : Drill.CourtFrame.position;
            if(TossLauncher==null && Drill.FeedLaunchers!=null && Drill.FeedLaunchers.Length>0)TossLauncher=Drill.FeedLaunchers[0];
            if(TossLauncher!=null){TossLauncher.gameObject.SetActive(true);TossLauncher.enabled=false;}
            if(Ally==null && Opponent!=null)
            {
                Ally=Instantiate(Opponent);
                Ally.name="Rally friendly setter (runtime)";
                Ally.Animate=false;Ally.PreviewOnly=false;Ally.PreviewBall=null;Ally.Target=null;Ally.Status=null;
                Ally.Role=VolleyOpponentPrototype.MotionRole.Set;
                Ally.transform.SetPositionAndRotation(net-forward*2.15f+right*1.35f,Quaternion.LookRotation(forward,Vector3.up));
                Ally.Pose(0f);
            }
            if(AllyTossLauncher==null && Drill.FeedLaunchers!=null && Drill.FeedLaunchers.Length>1)AllyTossLauncher=Drill.FeedLaunchers[1];
            if(AllyTossLauncher!=null)
            {
                AllyTossLauncher.gameObject.SetActive(true);AllyTossLauncher.enabled=false;
                AllyTossLauncher.transform.SetPositionAndRotation(net-forward*3.4f+right*2.25f,Quaternion.LookRotation(forward,Vector3.up));
            }
        }

        /// <summary>Advances the fixed-position rally and emits a physical ball flight when a setter releases it.</summary>
        public bool TickRally(float dt,out Vector3 start,out Vector3 destination,out float seconds)
        {
            start=destination=Vector3.zero; seconds=0f;
            if(!RallyActive || dt<=0f)return false;
            if(Phase==RallyPhase.Disabled)Phase=RallyPhase.Idle;
            switch(Phase)
            {
                case RallyPhase.Idle:
                    if(!BeginToss())return false;
                    Phase=RallyPhase.OpponentSet;
                    Cue="OPPONENT SETTING — PREPARE TO BLOCK";
                    return false;
                case RallyPhase.OpponentSet:
                    if(!TickFeed(dt,out start,out destination,out seconds))return false;
                    Phase=RallyPhase.OpponentSpike;
                    Cue="BLOCK THE SPIKE";
                    return true;
                case RallyPhase.AllyDelay:
                    _rallyTimer+=dt;
                    if(_rallyTimer<AllyRecoverySeconds)return false;
                    if(!BeginAllySet())return false;
                    Phase=RallyPhase.AllySet;
                    Cue="TEAMMATE SETTING — GET READY TO SPIKE";
                    return false;
                case RallyPhase.AllySet:
                    return TickAllySet(dt,out start,out destination,out seconds);
                case RallyPhase.Recovery:
                    _rallyTimer+=dt;
                    if(_rallyTimer>=RallyResetSeconds)
                    {
                        Phase=RallyPhase.Idle;
                        Cue="NEXT ATTACK — GET READY";
                    }
                    return false;
            }
            return false;
        }

        bool BeginAllySet()
        {
            if(_preparedBall!=null)return false;
            if(Ally==null || Drill==null || Drill.Pool==null)return false;
            _preparedBall=Drill.Pool.Take();
            if(_preparedBall==null)return false;
            _tossStart=AllyTossLauncher!=null ? AllyTossLauncher.MuzzlePosition : Ally.transform.position+Vector3.back;
            _tossEnd=Ally.ReleasePosition;
            _preparedBall.Body.isKinematic=true;
            _preparedBall.Body.detectCollisions=false;
            _preparedBall.transform.SetPositionAndRotation(_tossStart,Quaternion.identity);
            _preparedBall.Body.position=_tossStart;
            _preparedBall.gameObject.SetActive(true);
            _rallyTimer=0f;_allyJumpIssued=false;
            _allyLateral=Random.Range(-AllySetLateralSpread,AllySetLateralSpread);
            float lead=AutoJumpLeadSeconds+Random.Range(-AutoJumpTimingJitter,AutoJumpTimingJitter);
            _allyJumpAt=Mathf.Max(0f,AllySetSeconds-Mathf.Clamp(lead,0f,Jump.Duration*.75f));
            Ally.Pose(0f);
            if(AllyTossLauncher!=null)
            {
                var aim=AllyTossLauncher.GetComponent<VolleyFeederAim>();
                if(aim!=null){aim.AimForShot(_tossEnd,AllySetSeconds);aim.PlayShotFeedback();}
            }
            return true;
        }

        bool TickAllySet(float dt,out Vector3 start,out Vector3 destination,out float seconds)
        {
            start=destination=Vector3.zero;seconds=0f;
            if(_preparedBall==null){Phase=RallyPhase.Recovery;_rallyTimer=0f;return false;}
            _rallyTimer+=dt;
            float phase=Mathf.Clamp01(_rallyTimer/AllySetSeconds)*.62f;
            Ally.Pose(phase);
            var position=TossPosition(_tossStart,_tossEnd,_rallyTimer,AllySetSeconds);
            _preparedBall.transform.position=position;_preparedBall.Body.position=position;
            if(BeginnerBlock && !_allyJumpIssued && _rallyTimer>=_allyJumpAt)
                _allyJumpIssued=Jump.TryStartAutomaticJump();
            if(_rallyTimer<AllySetSeconds)return false;
            start=Ally.ReleasePosition;
            destination=PlayerSpikeDestination(_allyLateral);
            seconds=AllyFlightSeconds;
            Phase=RallyPhase.PlayerSpike;
            Cue="SPIKE THE SET TOWARD THE FLOOR TARGET";
            return true;
        }

        Vector3 PlayerSpikeDestination(float lateral)
        {
            var forward=Vector3.ProjectOnPlane(Drill.CourtFrame.forward,Vector3.up).normalized;
            var right=Vector3.Cross(Vector3.up,forward);
            var point=GroundedEye+forward*Drill.ContactForwardDistance+right*lateral;
            point.y=GroundedEye.y+Jump.JumpHeight+SpikeReachAboveEye;
            return point;
        }

        public bool RegisterOpponentBlock()
        {
            if(!IsOpponentSpike)return false;
            Phase=RallyPhase.AllyDelay;_rallyTimer=0f;_follow=-1f;
            if(Opponent!=null)Opponent.Pose(.78f);
            Cue="GREAT BLOCK — TEAMMATE WILL SET";
            return true;
        }
        public void RegisterPlayerSpike()
        {
            if(IsPlayerSpike)Cue="FOLLOW THROUGH — HIT THE FLOOR TARGET";
        }
        public void RegisterTargetHit()
        {
            if(!IsPlayerSpike)return;
            Phase=RallyPhase.Recovery;_rallyTimer=0f;Cue="POINT — RESETTING THE RALLY";
        }
        public void RegisterBallUnavailable()
        {
            if(Phase!=RallyPhase.OpponentSpike && Phase!=RallyPhase.PlayerSpike)return;
            Phase=RallyPhase.Recovery;_rallyTimer=0f;Cue="RESETTING THE RALLY";
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
            if(dt<=0f)return false;
            if(_windup<0)
            {
                _windup=0;_follow=-1;_sinceRelease=-1;_autoJumpIssued=false;
                float lead=AutoJumpLeadSeconds+Random.Range(-AutoJumpTimingJitter,AutoJumpTimingJitter);
                _autoJumpAt=Mathf.Max(0,WindupSeconds-Mathf.Clamp(lead,0,Jump.Duration*.75f));
            }
            _windup+=dt;
            if(BeginnerBlock && !_autoJumpIssued && _windup>=_autoJumpAt)
                _autoJumpIssued=Jump.TryStartAutomaticJump();
            if(_preparedBall!=null)
            {
                var position=TossPosition(_tossStart,_tossEnd,_windup,WindupSeconds);
                _preparedBall.transform.position=position;
                _preparedBall.Body.position=position;
            }
            bool block=Drill.Drill==VolleyDrill.Block;
            Cue=block ? BeginnerBlock ? "AUTO JUMP — REACH UP AND BLOCK THE BALL" : "ADVANCED: HANDS AT CHEST — RAISE BOTH HANDS TO JUMP"
                : "GET READY — HANDS LOW, WAIT FOR THE TOSS";
            if(block)Opponent.PreviewPhase=Mathf.Min(.52f,_windup/WindupSeconds*.52f);
            if(_windup<WindupSeconds)return false;
            LastWasFaceShot=block&&Random.value<FaceShotChance;
            destination=Destination(Random.Range(-HorizontalSpread,HorizontalSpread),Random.Range(-HeightSpread,HeightSpread),LastWasFaceShot);
            if(block)
            {
                Opponent.Pose(.52f);start=Opponent.ReleasePosition;
                seconds=SolveBlockShot(start,ref destination);
                PlaySpikeAudio(start);
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
            if(RallyActive)
            {
                if(Phase==RallyPhase.OpponentSpike && _follow>=0 && Opponent!=null)
                {
                    _follow+=Time.deltaTime;
                    Opponent.PreviewPhase=Mathf.Lerp(.52f,1f,Mathf.Clamp01(_follow/.9f));
                }
                return;
            }
            if(_sinceRelease>=0)
            {
                _sinceRelease+=Time.deltaTime;
                Cue=Drill.Drill==VolleyDrill.Block ? "RAISE BOTH HANDS — BLOCK IN FRONT"
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
            if(RallyActive)
            {
                Phase=RallyPhase.Idle;_rallyTimer=0f;_allyJumpIssued=false;
                if(Ally!=null)Ally.Pose(0f);
            }
        }
        public void SetBeginnerBlock(bool enabled)
        {
            if(Drill.Drill!=VolleyDrill.Block)return;
            Jump.AutomaticJump=enabled;
            Jump.ResetJump();
            Drill.ResetCurrentAttempt();
        }
        void PlaySpikeAudio(Vector3 position)
        {
            if(SpikeClip==null)return;
            SpikeAudioRequested?.Invoke(SpikeClip,position);
            if(!Application.isPlaying)return; // EditMode verification observes requests without PC audio.
            if(_spikeAudio==null)
            {
                var source=new GameObject("Opponent spike audio");source.transform.SetParent(transform,false);
                _spikeAudio=source.AddComponent<AudioSource>();_spikeAudio.playOnAwake=false;
                _spikeAudio.spatialBlend=1f;_spikeAudio.minDistance=2f;_spikeAudio.maxDistance=25f;_spikeAudio.dopplerLevel=0;
            }
            _spikeAudio.transform.position=position;
            _spikeAudio.PlayOneShot(SpikeClip,SpikeVolume);
        }
        void OnDisable()=>CancelFeed();
    }
}
