using System.Collections.Generic;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Feed choreography only; pooled balls and contacts remain owned by VolleyDrillController.</summary>
    public sealed class VolleyAerialSequence : MonoBehaviour
    {
        public enum RallyPhase { Disabled, Idle, TurnChange, OpponentSet, OpponentSpike, AllyReceive, PlayerSpike, Recovery, MatchOver }
        public enum RallyTurn { Block, Spike }
        public enum RallyMode { Alternate, SpikeOnly, BlockOnly }
        public VolleyDrillController Drill;
        public VolleyArmJump Jump;
        public VolleyOpponentPrototype Opponent;
        public BallLauncher TossLauncher;
        [Header("Block + spike rally")]
        [Tooltip("Explicit turns: the opponent attacks (player blocks) or a teammate sets (player spikes).")]
        public bool RallyEnabled=true;
        public RallyMode Mode=RallyMode.Alternate;
        public RallyTurn FirstTurn=RallyTurn.Spike;
        [Tooltip("Alternate mode: attempts before the other turn starts.")]
        [Min(1)] public int AttemptsPerTurn=1;
        [Tooltip("Turn changes blank the view briefly; the stance and players move only while it is dark.")]
        [Min(.05f)] public float BlinkOutSeconds=.2f;
        [Min(0f)] public float BlinkHoldSeconds=.15f;
        [Min(.05f)] public float BlinkInSeconds=.3f;
        [Tooltip("Pause after the view returns so the turn banner and chime register before play starts.")]
        [Min(0f)] public float TurnLeadSeconds=1.1f;
        [Tooltip("Time after a point is decided before the next attempt, so the landing ball and marker stay visible.")]
        [Min(.1f)] public float RallyResetSeconds=2f;
        [Header("Match")]
        [Tooltip("First side to this many points wins; the match then restarts automatically.")]
        [Min(1)] public int MatchPoints=7;
        [Min(1f)] public float MatchEndSeconds=5f;
        [Header("Jump per turn (virtual lift)")]
        [Min(.1f)] public float BlockJumpHeight=.85f;
        [Min(.4f)] public float BlockJumpSeconds=1f;
        [Tooltip("Higher and longer so the player strikes down from above the blockers.")]
        [Min(.1f)] public float SpikeJumpHeight=1.3f;
        [Min(.4f)] public float SpikeJumpSeconds=1.15f;
        [Header("Spike turn: friendly setter")]
        public VolleyOpponentPrototype Ally;
        [Tooltip("Spike stance distance behind the block stance (away from the net).")]
        [Min(0f)] public float SpikeStandBackDistance=1.2f;
        [Tooltip("Setter feet from the net centre: x = court right, y = court forward (negative is the player's side).")]
        public Vector2 AllyNetOffset=new Vector2(1.4f,-.35f);
        [Tooltip("The pass drops onto the setter from this height above its hands.")]
        [Min(.5f)] public float AllyDropHeight=3.4f;
        [Tooltip("The pass comes from this far behind the setter (toward the player's back court).")]
        [Min(0f)] public float AllyDropBehind=.8f;
        [Min(.4f)] public float AllyDropSeconds=1f;
        [Tooltip("Set flight from the setter's hands to the spike contact point.")]
        [Min(.4f)] public float AllyFlightSeconds=1.4f;
        [Min(0f)] public float AllySetLateralSpread=.35f;
        [Tooltip("Spike contact point in front of the grounded eyes (added to the approach distance).")]
        public float SpikeContactForward=.3f;
        [Tooltip("Approach: the player glides this far toward the net while rising, so the contact is close to the net and a downward spike clears it. 0 = vertical jump only.")]
        [Min(0f)] public float SpikeApproachDistance=.7f;
        [Tooltip("Blank-and-return used to bring the player back after an approach jump before the next spike.")]
        [Min(0f)] public float ResetBlinkLeadSeconds=.3f;
        [Tooltip("Automatic spike jump peaks this many seconds after the set reaches the contact point (negative = earlier).")]
        public float SpikeJumpPeakOffset=0f;
        [Header("Spike turn: opposing blockers")]
        public VolleyOpponentPrototype[] Blockers=new VolleyOpponentPrototype[0];
        [Range(0,3)] public int MinBlockers=3;
        [Range(0,3)] public int MaxBlockers=3;
        [Tooltip("Each blocker is placed at random within this lateral distance of the spike contact point.")]
        [Min(.5f)] public float BlockerLateralRange=2f;
        [Tooltip("Minimum gap between blocker centres (they may still form a wall).")]
        [Min(.4f)] public float BlockerMinSpacing=.8f;
        [Tooltip("Per-attempt random jump height for each blocker; reach varies from just above the net to high blocks.")]
        [Min(.1f)] public float BlockerJumpMin=.8f;
        [Min(.1f)] public float BlockerJumpMax=1.1f;
        public float BlockerNetDistance=.35f;
        public float BlockerStandbyDistance=3.2f;
        [Min(.4f)] public float BlockerJumpSeconds=.9f;
        [Tooltip("Blockers reach their highest point this long after the set reaches the player.")]
        public float BlockerReactionSeconds=.1f;
        [Min(0f)] public float BlockerTimingJitter=.08f;
        [Min(.5f)] public float BlockerShuffleSpeed=3.2f;
        [Header("Spike landing")]
        public float CourtHalfWidth=4.5f;
        public float CourtDepth=9f;
        [Tooltip("Half the painted line width plus a little ball footprint; landing on the line is in.")]
        [Min(0f)] public float LineTolerance=.06f;
        [Header("Team kits")]
        public Color AllyJersey=new Color(.12f,.38f,.92f);
        public Color AllyShorts=new Color(.92f,.93f,.95f);
        public Color OpponentJersey=new Color(.8f,.16f,.07f);
        public Color OpponentShorts=new Color(.05f,.07f,.1f);
        [Header("Opponent contact audio (no haptics)")]
        public AudioClip SpikeClip;
        [Range(0f,1f)] public float SpikeVolume=.85f;
        public event System.Action<AudioClip,Vector3> SpikeAudioRequested;
        AudioSource _spikeAudio;
        [Min(.5f)] public float WindupSeconds=1.2f;
        [Min(.5f)] public float TossFlightSeconds=1.15f;
        [Min(1f)] public float BlockSpeed=9f;
        [Range(0,1)] public float FaceShotChance=.25f;
        [Tooltip("Opponent spike lateral spread at the player's block (either side).")]
        [Min(0)] public float HorizontalSpread=.85f;
        [Tooltip("Share of opponent spikes aimed wide (at least WideShotMinimum to the side) instead of at the player's front.")]
        [Range(0,1)] public float WideShotChance=.7f;
        [Min(0)] public float WideShotMinimum=.35f;
        [Min(0)] public float HeightSpread=.3f;
        public float SpikeReachAboveEye=.50f;
        public float BlockReachAboveEye=.55f;
        [Header("Beginner automatic jump")]
        [Min(0f)] public float AutoJumpLeadSeconds=.32f;
        [Range(0f,.1f)] public float AutoJumpTimingJitter=.03f;
        float _autoJumpAt;
        bool _autoJumpIssued;
        public float ScheduledJumpTime=>_autoJumpAt;
        /// <summary>Automatic jumps for both rally turns; ADVANCED uses the hand-height gesture instead.</summary>
        public bool BeginnerBlock=>Drill.Drill==VolleyDrill.Block && Jump.AutomaticJump;
        public bool LastWasFaceShot { get; private set; }
        public string Cue { get; private set; }="GET READY";
        public RallyPhase Phase { get; private set; }=RallyPhase.Disabled;
        public RallyTurn CurrentTurn { get; private set; }
        public bool TurnStarted { get; private set; }
        /// <summary>0 = clear view, 1 = fully blanked by the turn change.</summary>
        public float FadeAlpha { get; private set; }
        public event System.Action<RallyTurn> TurnAnnounced;
        public int PlayerScore { get; private set; }
        public int OpponentScore { get; private set; }
        public bool PlayerWonMatch { get; private set; }
        public string Outcome { get; private set; }="";
        /// <summary>True when the last decided point went to the player's team.</summary>
        public bool OutcomeGood { get; private set; }
        /// <summary>Point decided: winner (true = player) and where the ball ended.</summary>
        public event System.Action<bool,Vector3> PointScored;
        public event System.Action<bool> MatchEnded;
        public bool PlayerTouched=>_playerTouched;
        public bool BlockerTouched=>_blockerTouched;
        public bool AwaitingPlayerTouch=>(IsOpponentSpike || IsPlayerSpike) && !_playerTouched;
        public int OutcomeSerial { get; private set; }
        /// <summary>Seconds after the set leaves the setter's hands when the automatic spike jump starts.</summary>
        public float SpikeJumpTime { get; private set; }
        public float[] BlockerJumpTimes=>_blockerJumpAt;
        public bool[] ActiveBlockers=>_blockerActive;
        bool RallyActive => RallyEnabled && Drill!=null && Drill.Drill==VolleyDrill.Block;
        public bool IsOpponentSpike => RallyActive && Phase==RallyPhase.OpponentSpike;
        public bool IsPlayerSpike => RallyActive && Phase==RallyPhase.PlayerSpike;
        float _windup=-1, _follow=-1, _sinceRelease=-1;
        float _rallyTimer, _allyLateral, _opponentFollow=-1, _allyFollow=-1, _blockerClock=-1;
        int _attemptsInTurn;
        RallyTurn _pendingTurn;
        bool _layoutApplied, _spikeJumpIssued, _playerTouched, _homeCaptured, _blockersFresh;
        bool _blockerTouched, _handLostNear, _handReadyNear, _silentBlink, _havePhysicsSample;
        Vector3 _lastBallPosition, _previousBallPosition, _previousBallVelocity;
        Vector3 SpikeStance=>-Forward*SpikeStandBackDistance;
        Vector3 _opponentHome; Quaternion _opponentHomeRotation;
        float[] _blockerJumpAt=new float[0];
        bool[] _blockerActive=new bool[0];
        Vector3[] _blockerTargets=new Vector3[0];
        public bool AttackStarted=>_windup>=0f;
        Ball _preparedBall;
        Vector3 _tossStart, _tossEnd;

        Vector3 Forward=>Vector3.ProjectOnPlane(Drill.CourtFrame.forward,Vector3.up).normalized;
        Vector3 Right=>Vector3.Cross(Vector3.up,Forward);
        Vector3 NetCentre
        {
            get
            {
                var net=Drill.ReceiveNet!=null ? Drill.ReceiveNet.transform.position : Drill.CourtFrame.position;
                net.y=Drill.CourtFrame.position.y;return net;
            }
        }

        public static Vector3 TossPosition(Vector3 start,Vector3 end,float elapsed,float duration)
        {
            float t=Mathf.Clamp01(elapsed/Mathf.Max(.01f,duration));
            return Vector3.Lerp(start,end,t)-.5f*Physics.gravity*duration*duration*t*(1f-t);
        }
        bool StageBall(Vector3 position)
        {
            _preparedBall=Drill.Pool.Take();
            if(_preparedBall==null)return false;
            _preparedBall.Body.isKinematic=true;
            _preparedBall.Body.detectCollisions=false;
            _preparedBall.transform.SetPositionAndRotation(position,Quaternion.identity);
            _preparedBall.Body.position=position;
            _preparedBall.gameObject.SetActive(true);
            return true;
        }
        public bool BeginToss()
        {
            if(_preparedBall!=null)return true;
            if(TossLauncher==null || Opponent==null)return false;
            _tossEnd=Opponent.ReleasePosition;
            var aim=TossLauncher.GetComponent<VolleyFeederAim>();
            if(aim!=null)aim.AimForShot(_tossEnd,WindupSeconds);
            _tossStart=TossLauncher.MuzzlePosition;
            if(!StageBall(_tossStart))return false;
            if(aim!=null)aim.PlayShotFeedback();
            return true;
        }
        public Ball TakePreparedBall()
        {
            var ball=_preparedBall;_preparedBall=null;
            if(ball!=null)ball.Body.detectCollisions=true;
            return ball;
        }

        static void Act(VolleyOpponentPrototype actor,float phase)
        {
            if(actor==null)return;
            actor.PreviewPhase=phase;actor.Pose(phase);
        }
        public static void ConfigureActor(VolleyOpponentPrototype actor,VolleyOpponentPrototype.MotionRole role,Color jersey,Color shorts)
        {
            actor.Animate=false;actor.PreviewOnly=false;actor.PreviewBall=null;actor.Target=null;actor.Status=null;
            actor.Role=role;actor.Variant=VolleyOpponentPrototype.MotionVariant.A_Readable;
            actor.OverrideKit=true;actor.JerseyColour=jersey;actor.ShortsColour=shorts;actor.ApplyKit();
            Act(actor,0f);
        }
        /// <summary>Friendly setter built from the opponent mannequin; shared by the editor setup and the runtime fallback.</summary>
        public static VolleyOpponentPrototype CreateAlly(VolleyOpponentPrototype template,Color jersey,Color shorts)
        {
            var ally=Instantiate(template);ally.name="Rally friendly setter";
            ConfigureActor(ally,VolleyOpponentPrototype.MotionRole.Set,jersey,shorts);
            return ally;
        }
        /// <summary>Solid opposing blocker: kinematic body, limb/torso/head colliders and palm spheres that follow the pose.</summary>
        public static VolleyOpponentPrototype CreateBlocker(VolleyOpponentPrototype template,int index,VolleyAerialSequence rally,Color jersey,Color shorts)
        {
            var blocker=Instantiate(template);blocker.name="Opponent blocker "+(index+1);
            blocker.JumpHeight=.7f;
            foreach(var limb in new[]{blocker.LeftUpperArm,blocker.LeftForearm,blocker.RightUpperArm,blocker.RightForearm})
                if(limb.GetComponent<Collider>()==null)limb.gameObject.AddComponent<CapsuleCollider>();
            if(blocker.Torso.GetComponent<Collider>()==null)blocker.Torso.gameObject.AddComponent<BoxCollider>();
            if(blocker.Head.GetComponent<Collider>()==null)blocker.Head.gameObject.AddComponent<SphereCollider>();
            blocker.LeftHand=Palm("Left palm",blocker.transform);blocker.RightHand=Palm("Right palm",blocker.transform);
            if(blocker.GetComponent<Rigidbody>()==null)blocker.gameObject.AddComponent<Rigidbody>();
            var contact=blocker.GetComponent<VolleyBlockerContact>();
            if(contact==null)contact=blocker.gameObject.AddComponent<VolleyBlockerContact>();
            contact.Rally=rally;contact.Configure();
            ConfigureActor(blocker,VolleyOpponentPrototype.MotionRole.Block,jersey,shorts);
            SetSolid(blocker,false);
            return blocker;
        }
        static Transform Palm(string name,Transform parent)
        {
            var existing=parent.Find(name);
            if(existing!=null)return existing;
            var palm=new GameObject(name,typeof(SphereCollider)).transform;palm.SetParent(parent,false);
            palm.GetComponent<SphereCollider>().radius=.12f;
            return palm;
        }
        static void SetSolid(VolleyOpponentPrototype actor,bool solid)
        {
            foreach(var collider in actor.GetComponentsInChildren<Collider>(true))collider.enabled=solid;
        }

        /// <summary>Creates only runtime presentation objects when the block scene has not been saved with the rally cast.</summary>
        public void EnsureRallyWiring()
        {
            if(!RallyActive || !Application.isPlaying)return;
            if(TossLauncher==null && Drill.FeedLaunchers!=null && Drill.FeedLaunchers.Length>0)TossLauncher=Drill.FeedLaunchers[0];
            if(TossLauncher!=null){TossLauncher.gameObject.SetActive(true);TossLauncher.enabled=false;}
            if(Opponent==null)return;
            if(Ally==null)Ally=CreateAlly(Opponent,AllyJersey,AllyShorts);
            if(Blockers==null || Blockers.Length==0)
            {
                Blockers=new VolleyOpponentPrototype[3];
                for(int i=0;i<Blockers.Length;i++)Blockers[i]=CreateBlocker(Opponent,i,this,OpponentJersey,OpponentShorts);
            }
            foreach(var blocker in Blockers)if(blocker!=null && blocker.TryGetComponent<VolleyBlockerContact>(out var contact))contact.Rally=this;
            if(GetComponent<VolleyRallyPresenter>()==null)gameObject.AddComponent<VolleyRallyPresenter>().Rally=this;
        }

        void CaptureHome()
        {
            if(_homeCaptured)return;
            _homeCaptured=true;
            if(Opponent!=null){_opponentHome=Opponent.transform.position;_opponentHomeRotation=Opponent.transform.rotation;}
            int count=Blockers!=null ? Blockers.Length : 0;
            _blockerJumpAt=new float[count];_blockerActive=new bool[count];_blockerTargets=new Vector3[count];
            for(int i=0;i<count;i++)_blockerTargets[i]=BlockerStandby(i);
            PlaceAlly();
        }
        Vector3 BlockerStandby(int index)
        {
            int count=Blockers!=null ? Blockers.Length : 1;
            return NetCentre+Forward*BlockerStandbyDistance+Right*((index-(count-1)*.5f)*1.8f);
        }
        void PlaceAlly()
        {
            if(Ally==null)return;
            Ally.transform.position=NetCentre+Right*AllyNetOffset.x+Forward*AllyNetOffset.y;
            FaceAlly();
        }
        void FaceAlly()
        {
            if(Ally==null)return;
            var toward=Vector3.ProjectOnPlane(SpikeContactPoint(0f)-Ally.transform.position,Vector3.up);
            if(toward.sqrMagnitude>.001f)Ally.transform.rotation=Quaternion.LookRotation(toward,Vector3.up);
        }

        RallyTurn NextTurn()
        {
            if(Mode==RallyMode.SpikeOnly)return RallyTurn.Spike;
            if(Mode==RallyMode.BlockOnly)return RallyTurn.Block;
            if(!TurnStarted)return FirstTurn;
            if(_attemptsInTurn<AttemptsPerTurn)return CurrentTurn;
            return CurrentTurn==RallyTurn.Block ? RallyTurn.Spike : RallyTurn.Block;
        }

        /// <summary>Advances the fixed-position rally and emits a physical ball flight when an attacker or setter releases it.</summary>
        public bool TickRally(float dt,bool ballInPlay,out Vector3 start,out Vector3 destination,out float seconds)
        {
            start=destination=Vector3.zero; seconds=0f;
            if(!RallyActive || dt<=0f)return false;
            CaptureHome();
            if(Phase==RallyPhase.Disabled)Phase=RallyPhase.Idle;
            TickCast(dt);
            switch(Phase)
            {
                case RallyPhase.Idle:
                    if(ballInPlay)return false;
                    var next=NextTurn();
                    if(!TurnStarted || next!=CurrentTurn){BeginTurnChange(next,false);return false;}
                    if(CurrentTurn==RallyTurn.Spike && Jump!=null && Jump.Floor!=null
                        && (Jump.Floor.StanceOffset-SpikeStance).sqrMagnitude>.0001f)
                    {BeginTurnChange(CurrentTurn,true);return false;} // back to the approach start, unannounced
                    StartAttempt();
                    return false;
                case RallyPhase.TurnChange:
                    TickTurnChange(dt);
                    return false;
                case RallyPhase.OpponentSet:
                    if(!TickFeed(dt,out start,out destination,out seconds))return false;
                    Phase=RallyPhase.OpponentSpike;_opponentFollow=0f;
                    BeginBallInPlay(start);
                    Cue="BLOCK THE SPIKE";
                    return true;
                case RallyPhase.OpponentSpike:
                    TickLanding();
                    return false;
                case RallyPhase.AllyReceive:
                    return TickAllyReceive(dt,out start,out destination,out seconds);
                case RallyPhase.PlayerSpike:
                    TickPlayerSpike(dt);
                    return false;
                case RallyPhase.Recovery:
                    _rallyTimer+=dt;
                    if(_rallyTimer>=RallyResetSeconds)
                    {
                        Drill.RetireRallyBall();
                        if(PlayerScore>=MatchPoints || OpponentScore>=MatchPoints)
                        {
                            PlayerWonMatch=PlayerScore>OpponentScore;
                            Phase=RallyPhase.MatchOver;_rallyTimer=0f;
                            Cue=(PlayerWonMatch ? "YOU WIN " : "YOU LOSE ")+PlayerScore+" - "+OpponentScore;
                            MatchEnded?.Invoke(PlayerWonMatch);
                        }
                        else Phase=RallyPhase.Idle;
                    }
                    return false;
                case RallyPhase.MatchOver:
                    _rallyTimer+=dt;
                    if(_rallyTimer>=MatchEndSeconds)ResetMatch();
                    return false;
            }
            return false;
        }

        void BeginTurnChange(RallyTurn turn,bool silent)
        {
            _pendingTurn=turn;_layoutApplied=false;_rallyTimer=0f;_silentBlink=silent;
            Phase=RallyPhase.TurnChange;
            if(!silent)Cue=turn==RallyTurn.Spike ? "SPIKE TURN" : "BLOCK TURN";
        }
        void TickTurnChange(float dt)
        {
            _rallyTimer+=dt;
            float t=_rallyTimer;
            FadeAlpha=t<BlinkOutSeconds ? t/BlinkOutSeconds
                : t<BlinkOutSeconds+BlinkHoldSeconds ? 1f
                : Mathf.Clamp01(1f-(t-BlinkOutSeconds-BlinkHoldSeconds)/BlinkInSeconds);
            if(!_layoutApplied && t>=BlinkOutSeconds)
            {
                _layoutApplied=true;
                ApplyTurnLayout(_pendingTurn);
                if(!_silentBlink)
                {
                    CurrentTurn=_pendingTurn;TurnStarted=true;_attemptsInTurn=0;
                    Cue=CurrentTurn==RallyTurn.Spike ? "SPIKE TURN — YOUR TEAM ATTACKS" : "BLOCK TURN — STOP THE OPPONENT";
                    TurnAnnounced?.Invoke(CurrentTurn);
                }
            }
            if(t>=BlinkOutSeconds+BlinkHoldSeconds+BlinkInSeconds+(_silentBlink ? ResetBlinkLeadSeconds : TurnLeadSeconds)){FadeAlpha=0f;Phase=RallyPhase.Idle;}
        }

        /// <summary>Runs only while the view is blanked: moves the player stance and repositions both teams without visible sliding.</summary>
        void ApplyTurnLayout(RallyTurn turn)
        {
            if(Jump!=null && Jump.Floor!=null)Jump.Floor.SetStanceOffset(turn==RallyTurn.Spike ? SpikeStance : Vector3.zero);
            if(Jump!=null)
            {
                Jump.JumpHeight=turn==RallyTurn.Spike ? SpikeJumpHeight : BlockJumpHeight;
                Jump.Duration=turn==RallyTurn.Spike ? SpikeJumpSeconds : BlockJumpSeconds;
            }
            if(Opponent!=null)
            {
                // The attacker waits in the back row while its team blocks.
                if(turn==RallyTurn.Block)Opponent.transform.SetPositionAndRotation(_opponentHome,_opponentHomeRotation);
                else Opponent.transform.SetPositionAndRotation(NetCentre+Forward*(BlockerStandbyDistance+1.4f)-Right*3f,_opponentHomeRotation);
                _opponentFollow=-1f;Act(Opponent,0f);
            }
            // Block turn: only the attacker is shown; the setter and idle blockers would just stand in the way.
            bool spikeTurn=turn==RallyTurn.Spike;
            if(Ally!=null)Ally.gameObject.SetActive(spikeTurn);
            PlaceAlly();Act(Ally,0f);_allyFollow=-1f;
            if(Blockers==null)return;
            for(int i=0;i<Blockers.Length;i++)
            {
                _blockerActive[i]=false;_blockerTargets[i]=BlockerStandby(i);
                if(Blockers[i]==null)continue;
                Blockers[i].gameObject.SetActive(spikeTurn);
                SetSolid(Blockers[i],false);
                Blockers[i].transform.SetPositionAndRotation(_blockerTargets[i],Quaternion.LookRotation(-Forward,Vector3.up));
                Act(Blockers[i],0f);
            }
            _blockerClock=-1f;
            if(turn==RallyTurn.Spike)
            {
                PlanSpike();_blockersFresh=true; // The first attempt keeps this hidden placement.
                for(int i=0;i<Blockers.Length;i++)if(Blockers[i]!=null)Blockers[i].transform.position=_blockerTargets[i];
            }
        }

        void StartAttempt()
        {
            if(CurrentTurn==RallyTurn.Block)
            {
                if(!BeginToss())return;
                Phase=RallyPhase.OpponentSet;
                Cue="OPPONENT SETTING — PREPARE TO BLOCK";
            }
            else
            {
                if(!BeginAllyReceive())return;
                Phase=RallyPhase.AllyReceive;
                Cue="TEAMMATE SETTING — GET READY TO SPIKE";
            }
            _attemptsInTurn++;
        }

        bool BeginAllyReceive()
        {
            if(_preparedBall!=null)return false;
            if(Ally==null || Drill==null || Drill.Pool==null)return false;
            FaceAlly();
            _tossEnd=Ally.ReleasePosition;
            _tossStart=_tossEnd+Vector3.up*AllyDropHeight-Forward*AllyDropBehind;
            if(!StageBall(_tossStart))return false;
            _rallyTimer=0f;_allyFollow=-1f;
            Act(Ally,0f);
            if(!_blockersFresh)PlanSpike(); // Later attempts shuffle visibly during the drop.
            _blockersFresh=false;
            return true;
        }

        /// <summary>Picks where the set goes and places the blockers at random along the net around it, each with its own reach.</summary>
        void PlanSpike()
        {
            _allyLateral=Random.Range(-AllySetLateralSpread,AllySetLateralSpread);
            if(Blockers==null || Blockers.Length==0)return;
            int wanted=Mathf.Clamp(Random.Range(Mathf.Min(MinBlockers,MaxBlockers),Mathf.Max(MinBlockers,MaxBlockers)+1),0,Blockers.Length);
            float contact=Vector3.Dot(GroundedEye-NetCentre,Right)+_allyLateral;
            float limit=Drill.ReceiveNet!=null ? Mathf.Max(.5f,Drill.ReceiveNet.bounds.extents.x-.5f) : CourtHalfWidth;
            var placed=new List<float>();
            for(int attempt=0;attempt<300 && placed.Count<wanted;attempt++)
            {
                float x=Mathf.Clamp(contact+Random.Range(-BlockerLateralRange,BlockerLateralRange),-limit,limit);
                bool clear=true;foreach(var other in placed)if(Mathf.Abs(other-x)<BlockerMinSpacing){clear=false;break;}
                if(clear)placed.Add(x);
            }
            placed.Sort();
            for(int i=0;i<Blockers.Length;i++)
            {
                bool active=i<placed.Count;
                _blockerActive[i]=active;
                _blockerTargets[i]=active ? NetCentre+Right*placed[i]+Forward*BlockerNetDistance : BlockerStandby(i);
                if(Blockers[i]==null)continue;
                SetSolid(Blockers[i],active);
                Blockers[i].JumpHeight=Random.Range(Mathf.Min(BlockerJumpMin,BlockerJumpMax),Mathf.Max(BlockerJumpMin,BlockerJumpMax));
            }
            _blockerClock=-1f;
        }

        bool TickAllyReceive(float dt,out Vector3 start,out Vector3 destination,out float seconds)
        {
            start=destination=Vector3.zero;seconds=0f;
            if(_preparedBall==null){Phase=RallyPhase.Idle;return false;}
            _rallyTimer+=dt;
            Act(Ally,Mathf.Clamp01(_rallyTimer/AllyDropSeconds)*VolleyOpponentPrototype.SetContactPhase);
            var position=TossPosition(_tossStart,_tossEnd,_rallyTimer,AllyDropSeconds);
            _preparedBall.transform.position=position;_preparedBall.Body.position=position;
            if(_rallyTimer<AllyDropSeconds)return false;
            start=_tossEnd;
            destination=SpikeContactPoint(_allyLateral);
            seconds=AllyFlightSeconds;
            // Peak of the jump meets the arriving set; jumping at the release would land before the ball arrives.
            SpikeJumpTime=Mathf.Max(0f,AllyFlightSeconds-Jump.Duration*.5f+SpikeJumpPeakOffset);
            for(int i=0;i<_blockerJumpAt.Length;i++)
                _blockerJumpAt[i]=AllyFlightSeconds+BlockerReactionSeconds+Random.Range(-BlockerTimingJitter,BlockerTimingJitter)
                    -VolleyOpponentPrototype.BlockPeakPhase*BlockerJumpSeconds;
            _blockerClock=0f;_allyFollow=0f;
            _rallyTimer=0f;_spikeJumpIssued=false;
            BeginBallInPlay(start);
            Phase=RallyPhase.PlayerSpike;
            Cue=BeginnerBlock ? "AUTO JUMP — SPIKE PAST THE BLOCKERS" : "JUMP: RAISE BOTH HANDS — SPIKE PAST THE BLOCKERS";
            return true;
        }

        void TickPlayerSpike(float dt)
        {
            _rallyTimer+=dt;
            if(BeginnerBlock && !_spikeJumpIssued && _rallyTimer>=SpikeJumpTime)
                _spikeJumpIssued=Jump.TryStartAutomaticJump();
            TickApproach();
            TickLanding();
        }
        /// <summary>Glides toward the net during the rising half of a spike-turn jump; the player stays there until the next reset blink.</summary>
        void TickApproach()
        {
            if(CurrentTurn!=RallyTurn.Spike || Jump==null || Jump.Floor==null || !Jump.Airborne || SpikeApproachDistance<=0f)return;
            float glide=SpikeApproachDistance*Mathf.SmoothStep(0f,1f,Mathf.Clamp01(Jump.Progress/.5f));
            var target=SpikeStance+Forward*glide;
            // Never glide back within a jump (the stance only moves forward here).
            if(Vector3.Dot(target-Jump.Floor.StanceOffset,Forward)>0f)Jump.Floor.SetStanceOffset(target);
        }

        void BeginBallInPlay(Vector3 start)
        {
            _lastBallPosition=start;_havePhysicsSample=false;
            _playerTouched=_blockerTouched=_handLostNear=_handReadyNear=false;
        }
        /// <summary>
        /// Physics-rate landing check. A fast ball can bounce between two samples without ever being seen near the floor,
        /// so a downward-to-upward velocity flip near the floor also counts, and the touchdown point is extrapolated.
        /// </summary>
        public void SampleBall(Vector3 position,Vector3 velocity,float radius,float step)
        {
            if(Phase!=RallyPhase.OpponentSpike && Phase!=RallyPhase.PlayerSpike)return;
            float floor=Drill.CourtFrame.position.y;
            bool touching=position.y-floor<=radius+.03f;
            bool bounced=_havePhysicsSample && _previousBallVelocity.y<-.5f && velocity.y>-.1f && position.y-floor<radius+.45f;
            if(touching || bounced)
            {
                var landing=position;
                if(_havePhysicsSample && _previousBallVelocity.y<-.01f)
                {
                    float t=Mathf.Clamp((_previousBallPosition.y-floor-radius)/-_previousBallVelocity.y,0f,step*1.5f);
                    landing=_previousBallPosition+_previousBallVelocity*t;
                }
                landing.y=floor;
                _lastBallPosition=landing;
                Resolve(landing);
                return;
            }
            _previousBallPosition=position;_previousBallVelocity=velocity;_havePhysicsSample=true;_lastBallPosition=position;
        }
        /// <summary>The first floor contact decides the point; the ball itself keeps bouncing until the reset.</summary>
        void TickLanding()
        {
            var ball=Drill.ActiveBall;
            if(ball==null)return;
            _lastBallPosition=ball.transform.position;
            if(_lastBallPosition.y<=Drill.CourtFrame.position.y+.16f)Resolve(_lastBallPosition);
        }
        /// <summary>
        /// Awards the point from where the ball ended and who touched it last.
        /// Block turn: a touched ball must reach the opponent court. Spike turn: a blocker touch turns an out ball into a block-out.
        /// </summary>
        void Resolve(Vector3 position)
        {
            var local=position-NetCentre;
            float depth=Vector3.Dot(local,Forward), side=Vector3.Dot(local,Right);
            // Painted lines are part of the court: a ball touching the line is in.
            bool inBounds=Mathf.Abs(side)<=CourtHalfWidth+LineTolerance && Mathf.Abs(depth)<=CourtDepth+LineTolerance;
            bool opponentCourt=inBounds && depth>0f;
            if(Phase==RallyPhase.OpponentSpike)
            {
                if(!inBounds)Award(!_playerTouched,_playerTouched ? "BLOCK OUT" : "OUT",position);
                else if(opponentCourt)Award(true,_playerTouched ? "BLOCK POINT!" : "POINT!",position);
                else Award(false,_playerTouched ? "BLOCK FELL ON YOUR SIDE" : "NO BLOCK"+MissHint(),position);
                return;
            }
            if(!_playerTouched)Award(false,"MISSED"+MissHint(),position);
            else if(!inBounds)Award(_blockerTouched,_blockerTouched ? "BLOCK OUT!" : "OUT",position);
            else if(opponentCourt)Award(true,_blockerTouched ? "OFF THE BLOCK!" : "POINT!",position);
            else Award(false,_blockerTouched ? "BLOCKED" : "NET",position);
        }
        string MissHint()=>_handLostNear && !_handReadyNear ? " (HAND LOST)" : "";
        void Award(bool player,string text,Vector3 position)
        {
            if(player)PlayerScore++;else OpponentScore++;
            if(!_playerTouched && (_handLostNear || _handReadyNear))
                Debug.Log("[VolleyRally] untouched "+Phase+": hand near ball ready="+_handReadyNear+" lost="+_handLostNear);
            SetOutcome(text,player);
            PointScored?.Invoke(player,position);
        }
        /// <summary>Drill reports a hand passing near the untouched ball, with whether it could collide.</summary>
        public void NoteHandNearBall(bool collidable)
        {
            if(!AwaitingPlayerTouch)return;
            if(collidable)_handReadyNear=true;else _handLostNear=true;
        }
        void ResetMatch()
        {
            PlayerScore=OpponentScore=0;TurnStarted=false;_attemptsInTurn=0;
            Phase=RallyPhase.Idle;_rallyTimer=0f;
        }

        /// <summary>Advances follow-through, blocker footwork and blocker jumps; runs in every phase so motions finish during recovery.</summary>
        void TickCast(float dt)
        {
            if(_opponentFollow>=0f && Opponent!=null)
            {
                _opponentFollow+=dt;
                Act(Opponent,Mathf.Lerp(.52f,1f,Mathf.Clamp01(_opponentFollow/.9f)));
                if(_opponentFollow>=.9f)_opponentFollow=-1f;
            }
            if(_allyFollow>=0f && Ally!=null)
            {
                _allyFollow+=dt;
                Act(Ally,Mathf.Lerp(VolleyOpponentPrototype.SetContactPhase,1f,Mathf.Clamp01(_allyFollow/.8f)));
                if(_allyFollow>=.8f)_allyFollow=-1f;
            }
            if(Blockers==null || Phase==RallyPhase.TurnChange)return;
            if(_blockerClock>=0f)_blockerClock+=dt;
            for(int i=0;i<Blockers.Length;i++)
            {
                var blocker=Blockers[i];
                if(blocker==null)continue;
                blocker.transform.position=Vector3.MoveTowards(blocker.transform.position,_blockerTargets[i],BlockerShuffleSpeed*dt);
                float phase=_blockerActive[i] && _blockerClock>=0f ? Mathf.Clamp01((_blockerClock-_blockerJumpAt[i])/BlockerJumpSeconds) : 0f;
                Act(blocker,phase>=1f ? 0f : phase);
            }
        }

        void SetOutcome(string text,bool good)
        {
            Outcome=text;OutcomeGood=good;OutcomeSerial++;
            Phase=RallyPhase.Recovery;_rallyTimer=0f;
            Cue=text;
        }

        Vector3 SpikeContactPoint(float lateral)
        {
            // Measured from the approach start: the player reaches it after gliding SpikeApproachDistance.
            var start=GroundedEye-Vector3.ProjectOnPlane(Jump.Floor.StanceOffset-SpikeStance,Vector3.up);
            var point=start+Forward*(SpikeContactForward+SpikeApproachDistance)+Right*lateral;
            point.y=GroundedEye.y+Jump.JumpHeight+SpikeReachAboveEye;
            return point;
        }

        /// <summary>The player's hand deflected the rally ball (block or spike). The point is decided where it lands.</summary>
        public void RegisterPlayerTouch()
        {
            if(!IsOpponentSpike && !IsPlayerSpike)return;
            _playerTouched=true;
            Cue=IsPlayerSpike ? "SPIKE!" : "TOUCHED — IS IT GOING BACK?";
        }
        /// <summary>Called by a solid opposing blocker. The ball rebounds physically; its landing decides the point.</summary>
        public bool RegisterSpikeBlocked(Ball ball,Vector3 point)
        {
            if(!IsPlayerSpike || !_playerTouched || _blockerTouched || ball==null || ball!=Drill.ActiveBall)return false;
            _blockerTouched=true;
            PlaySpikeAudio(point);
            Cue="BLOCKER TOUCH!";
            return true;
        }
        /// <summary>The ball vanished (target flash, timeout) before touching the floor: decide from its last position.</summary>
        public void RegisterBallUnavailable()
        {
            if(Phase==RallyPhase.OpponentSpike || Phase==RallyPhase.PlayerSpike)Resolve(_lastBallPosition);
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
            float lateral=Random.value<WideShotChance
                ? (Random.value<.5f ? -1f : 1f)*Random.Range(Mathf.Min(WideShotMinimum,HorizontalSpread),HorizontalSpread)
                : Random.Range(-WideShotMinimum,WideShotMinimum);
            destination=Destination(LastWasFaceShot ? 0f : lateral,Random.Range(-HeightSpread,HeightSpread),LastWasFaceShot);
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
            if(GameInputGate.IsBlocked || RallyActive)return; // Rally animation is advanced by TickRally.
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
                // A layout already applied behind a blink stays; only the in-progress attempt is dropped.
                Phase=RallyPhase.Idle;_rallyTimer=0f;FadeAlpha=0f;_spikeJumpIssued=false;_allyFollow=-1f;
                Act(Ally,0f);
            }
        }
        public void SetBeginnerBlock(bool enabled)
        {
            if(Drill.Drill!=VolleyDrill.Block)return;
            Jump.AutomaticJump=enabled;
            Jump.ResetJump();
            Drill.ResetCurrentAttempt();
        }
        /// <summary>Changing the mode restarts with a blinked turn change so the new first turn is announced.</summary>
        public void SetMode(RallyMode mode)
        {
            Mode=mode;TurnStarted=false;_attemptsInTurn=0;PlayerScore=OpponentScore=0;
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
