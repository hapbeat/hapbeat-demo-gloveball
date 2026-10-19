using System.Collections.Generic;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Feed choreography only; pooled balls and contacts remain owned by VolleyDrillController.</summary>
    public sealed partial class VolleyAerialSequence : MonoBehaviour
    {
        public enum RallyPhase { Disabled, Idle, TurnChange, OpponentReceive, OpponentSet, OpponentSpike, AllyReceive, PlayerSpike, Recovery, MatchOver, MatchPlay, PlayerServe, PlayerReceive, PlayerSet }
        public enum RallyTurn { Block, Spike }
        public enum RallyMode { Alternate, SpikeOnly, BlockOnly, Match, Set, Pass }
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
        [Tooltip("Demo Session: keep the final score instead of restarting after MatchEndSeconds.")]
        public bool HoldAtMatchEnd;
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
        [Header("SPIKE + BLOCK: the rally carries on")]
        [Tooltip("SPIKE + BLOCK: a dug ball carries the rally on instead of ending the point. A comfortable dig and they set and attack (you block); a diving dig only gets it back over (your setter, left or right, sets you).")]
        public bool FlowRally=true;
        [Tooltip("The glide between the block spot and the spike stance during a flowing rally (real seconds, behind the comfort vignette).")]
        [Min(.1f)] public float FlowGlideSeconds=.45f;
        float _dropSeconds=1f, _turnGlide=-1f;
        Vector3 _glideStart, _glideEnd;
        float _allySide=1f;
        bool Flowing=>FlowRally && Mode==RallyMode.Alternate;
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
        [Tooltip("The player's eyes from the net at the block spot (the Block Reposition Point); arms reach about this far forward.")]
        [Min(.2f)] public float BlockStandDistance=.5f;
        [Header("Player spike and block: where the ball goes")]
        [Tooltip("Pull of a spike from the swing's own direction toward a shot that clears the net and comes down sharply in their court (0 = the swing only).")]
        [Range(0f,1f)] public float SpikeAimAssist=.6f;
        [Tooltip("Landing depth past the net for the steepest swing (60° down) and for a flat one: a steeper swing lands shorter.")]
        public Vector2 SpikeLandingDepth=new Vector2(1.5f,7.5f);
        [Range(0f,60f)] public float SpikeMaximumYaw=35f;
        [Tooltip("Pull of a palm block toward a rebound that clears the net into their court (0 = the hands' physics only). Fingertips never get it.")]
        [Range(0f,1f)] public float BlockAssist=.8f;
        [Range(0f,45f)] public float BlockMaximumYaw=20f;
        public Vector2 BlockLandingDepth=new Vector2(.8f,3f);
        public float BlockerStandbyDistance=3.2f;
        [Tooltip("Blocker airtime; the arms stay up over the net for most of it.")]
        [Min(.4f)] public float BlockerJumpSeconds=1.3f;
        [Tooltip("Blockers reach their highest point this long after the set reaches the player (when the spike crosses the net).")]
        public float BlockerReactionSeconds=.05f;
        [Min(0f)] public float BlockerTimingJitter=.05f;
        [Tooltip("One blocker reads the set and lines up within this distance of the contact point; the others are random.")]
        [Min(0f)] public float ReadingBlockerRange=.5f;
        [Min(.5f)] public float BlockerShuffleSpeed=3.2f;
        [Header("Opponent team: six on court")]
        [Tooltip("Back-row defenders. They dig any ball they can reach: the longer the ball is in the air after the last touch, the more ground they cover.")]
        public VolleyOpponentPrototype[] Defenders=new VolleyOpponentPrototype[0];
        public float DefenderDepth=6f;
        public float DefenderSpacing=2.8f;
        [Min(0f)] public float DefenderReactionSeconds=.3f;
        [Min(.1f)] public float DefenderSpeed=4.5f;
        [Tooltip("Horizontal reach of a digging defender around its feet.")]
        [Min(.1f)] public float DigReach=.8f;
        [Tooltip("Height at which a defender plays the ball.")]
        [Min(.2f)] public float DigHeight=.8f;
        [Tooltip("The middle defender covers short balls (tips, soft shots behind the block) from this depth; the others defend deep.")]
        [Min(1f)] public float CoverDepth=3.3f;
        [Tooltip("Chance to dig a reachable ball: easy at or below the first speed, hard at or above the second (m/s at the dig).")]
        public Vector2 DigSpeedRange=new Vector2(8f,20f);
        [Range(0f,1f)] public float DigEasyChance=.95f;
        [Range(0f,1f)] public float DigHardChance=.3f;
        [Tooltip("The short cover only plays balls slower than this (m/s): a hard spike past the block is not its ball.")]
        [Min(1f)] public float CoverMaxSpeed=11f;
        [Tooltip("A defender that cannot get there can still dive this much further (m), at this share of the dig chance.")]
        [Min(0f)] public float DiveReach=.9f;
        [Range(0f,1f)] public float DiveChance=.45f;
        [Tooltip("Opponent setter feet in the block turn: x = court right, y = court forward (positive is the opponent side).")]
        public Vector2 OpponentSetterOffset=new Vector2(-1.4f,.4f);
        [Tooltip("Idle middle blocker position along the net in the block turn.")]
        public float MiddleBlockerOffset=1.6f;
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
        [Tooltip("Jump-spike scene only: toss lateral spread.")]
        [Min(0)] public float HorizontalSpread=.85f;
        [Header("Opponent attack tell (block turn)")]
        [Tooltip("The attacker turns its body up to this many degrees toward the shot at the start of the attack, so the direction can be read.")]
        [Range(0f,30f)] public float AttackBodyYaw=15f;
        [Tooltip("A player spike off their block keeps this share of its rebound speed.")]
        [Range(.1f,1f)] public float BlockerReboundScale=.5f;
        [Tooltip("Their hitter's spot along the net moves this far either way every attack (m).")]
        [Min(0f)] public float AttackerShift=1.2f;
        [Tooltip("The shot leaves within this many degrees of the body direction.")]
        [Range(0f,20f)] public float AttackShotDeviation=5f;
        [Tooltip("Shots never go further to the side than this at the player's block: always reachable.")]
        [Min(0f)] public float BlockLateralLimit=.7f;
        [Min(0)] public float HeightSpread=.15f;
        public float SpikeReachAboveEye=.50f;
        [Tooltip("Block shot height above the eyes at the top of the jump (± HeightSpread); keep within arm reach.")]
        public float BlockReachAboveEye=.45f;
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
        /// <summary>
        /// SPIKE / BLOCK turns (not the match engine): a rep ended. (block turn, success): success is a spike or block that
        /// went into their court - a point, or dug by them.
        /// </summary>
        public event System.Action<bool,bool> TurnRepResolved;
        public event System.Action<bool> MatchEnded;
        public bool PlayerTouched=>_playerTouched;
        /// <summary>
        /// SPIKE: where the player should look while the ball comes to our setter and is set (the ball, else the setter),
        /// for the gaze arrow. False outside that part of the spike turn.
        /// </summary>
        public bool TryGetSpikeGaze(out Vector3 point)
        {
            point=default;
            if(!RallyActive || MatchEngine)return false;
            if(Phase!=RallyPhase.AllyReceive && !(Phase==RallyPhase.PlayerSpike && !_playerTouched))return false;
            var ball=_preparedBall!=null ? _preparedBall : Drill!=null ? Drill.ActiveBall : null;
            if(ball!=null && ball.gameObject.activeInHierarchy){point=ball.transform.position;return true;}
            if(Ally!=null && Ally.gameObject.activeInHierarchy){point=Ally.transform.position+Vector3.up*1.8f;return true;}
            return false;
        }
        public bool BlockerTouched=>_blockerTouched;
        public bool AwaitingPlayerTouch=>CurrentPlayerTouch!=PlayerTouch.None && !_playerTouched;
        public int OutcomeSerial { get; private set; }
        /// <summary>Seconds after the set leaves the setter's hands when the automatic spike jump starts.</summary>
        public float SpikeJumpTime { get; private set; }
        public float[] BlockerJumpTimes=>_blockerJumpAt;
        public bool[] ActiveBlockers=>_blockerActive;
        bool RallyActive => RallyEnabled && Drill!=null && Drill.Drill==VolleyDrill.Block;
        public bool IsOpponentSpike => RallyActive && Phase==RallyPhase.OpponentSpike;
        public bool IsPlayerSpike => RallyActive && Phase==RallyPhase.PlayerSpike;
        float _windup=-1, _follow=-1, _sinceRelease=-1;
        float _rallyTimer, _allyLateral, _opponentFollow=-1, _blockerClock=-1;
        int _attemptsInTurn;
        RallyTurn _pendingTurn;
        bool _layoutApplied, _spikeJumpIssued, _playerTouched, _homeCaptured, _blockersFresh;
        bool _blockerTouched, _handLostNear, _handReadyNear, _silentBlink, _havePhysicsSample;
        Vector3 _lastBallPosition, _previousBallPosition, _previousBallVelocity;
        Vector3 SpikeStance=>-Forward*SpikeStandBackDistance;
        /// <summary>Stance the spike approach glides from: fixed in the turn modes, the current embodiment in a match.</summary>
        Vector3 ApproachBase=>MatchEngine ? _approachBase : SpikeStance;
        Vector3 _opponentHome; Quaternion _opponentHomeRotation;
        float[] _blockerJumpAt=new float[0];
        bool[] _blockerActive=new bool[0];
        Vector3[] _blockerTargets=new Vector3[0];
        public bool AttackStarted=>_windup>=0f;
        Ball _preparedBall;
        Vector3 _tossStart, _tossEnd;
        float _attackYaw;
        /// <summary>Body yaw (degrees) the attacker shows for the current attack.</summary>
        public float AttackYaw=>_attackYaw;
        VolleyOpponentPrototype _followSetter;
        float _setterFollow=-1f, _sinceTouch;
        int _digger=-1, _digRolledFor=-1;
        bool _digSuccess, _digMissShown, _netCrossLogged, _digDive, _crossedUnder;
        float[] _diveClock;Vector3[] _diveAxis;
        int CoverIndex=>Defenders!=null && Defenders.Length>=3 ? (Defenders.Length-1)/2 : -1;
        /// <summary>A defender throws itself at the ball: it tips over toward it from its feet, stays down, gets up.</summary>
        void StartDive(int index,Vector3 ball)
        {
            if(Defenders==null || index<0 || index>=Defenders.Length)return;
            if(_diveClock==null || _diveClock.Length!=Defenders.Length){_diveClock=new float[Defenders.Length];_diveAxis=new Vector3[Defenders.Length];for(int i=0;i<_diveClock.Length;i++)_diveClock[i]=-1f;}
            if(_diveClock[index]>=0f)return;
            var toward=Vector3.ProjectOnPlane(ball-Defenders[index].transform.position,Vector3.up);
            if(toward.sqrMagnitude<1e-4f)toward=-Forward;
            _diveAxis[index]=Vector3.Cross(Vector3.up,toward.normalized);
            _diveClock[index]=0f;
        }
        /// <summary>Tilt (degrees) of a diving defender at <paramref name="t"/> seconds: down in 0.2 s, 0.6 s on the floor, up in 0.4 s.</summary>
        static float DiveAngle(float t)=>t<.2f ? 75f*t/.2f : t<.8f ? 75f : t<1.2f ? 75f*(1f-(t-.8f)/.4f) : 0f;
        Vector3 _digTarget;
        Vector3[] _defenderHome=new Vector3[0];
        /// <summary>Defender index committed to the current ball, or -1.</summary>
        public int Digger=>_digger;
        /// <summary>Velocity the drill applies to the ball when SampleBall reports a dig.</summary>
        public Vector3 DigVelocity { get; private set; }
        /// <summary>Blockers[0] sets for the opponent attacker in the block turn.</summary>
        VolleyOpponentPrototype OpponentSetter=>Blockers!=null && Blockers.Length>0 ? Blockers[0] : null;

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
            if(Drill.Drill==VolleyDrill.Block)
            {
                // Tell: the attacker squares up toward the shot for the whole wind-up.
                CaptureHome();
                _attackYaw=Random.Range(-AttackBodyYaw,AttackBodyYaw);
                Opponent.transform.rotation=_opponentHomeRotation*Quaternion.Euler(0f,_attackYaw,0f);
            }
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
        /// <summary>Back-row defender: no colliders; digs are scripted from the predicted flight.</summary>
        public static VolleyOpponentPrototype CreateDefender(VolleyOpponentPrototype template,int index,Color jersey,Color shorts)
        {
            var defender=Instantiate(template);defender.name="Opponent defender "+(index+1);
            ConfigureActor(defender,VolleyOpponentPrototype.MotionRole.Dig,jersey,shorts);
            return defender;
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
            // The opponent setter now feeds the attacker; the side feeder is not part of the rally.
            if(TossLauncher!=null){TossLauncher.enabled=false;TossLauncher.gameObject.SetActive(false);}
            if(Opponent==null)return;
            if(Ally==null)Ally=CreateAlly(Opponent,AllyJersey,AllyShorts);
            if(Teammates==null || Teammates.Length==0)
            {
                Teammates=new VolleyOpponentPrototype[5];
                for(int i=0;i<Teammates.Length;i++)Teammates[i]=CreateTeammate(Opponent,i,AllyJersey,AllyShorts);
            }
            if(Blockers==null || Blockers.Length==0)
            {
                Blockers=new VolleyOpponentPrototype[3];
                for(int i=0;i<Blockers.Length;i++)Blockers[i]=CreateBlocker(Opponent,i,this,OpponentJersey,OpponentShorts);
            }
            foreach(var blocker in Blockers)if(blocker!=null && blocker.TryGetComponent<VolleyBlockerContact>(out var contact))contact.Rally=this;
            if(Defenders==null || Defenders.Length==0)
            {
                Defenders=new VolleyOpponentPrototype[3];
                for(int i=0;i<Defenders.Length;i++)Defenders[i]=CreateDefender(Opponent,i,OpponentJersey,OpponentShorts);
            }
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
            int defenders=Defenders!=null ? Defenders.Length : 0;
            _defenderHome=new Vector3[defenders];
            for(int i=0;i<defenders;i++)
            {
                // Perimeter defence: the middle one plays short behind the block, the others deep in the corners.
                bool cover=defenders>=3 && i==(defenders-1)/2;
                _defenderHome[i]=NetCentre+Forward*(cover ? CoverDepth : DefenderDepth)+Right*(cover ? 0f : (i-(defenders-1)*.5f)*DefenderSpacing);
            }
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
            Ally.transform.position=NetCentre+Right*(AllyNetOffset.x*_allySide)+Forward*AllyNetOffset.y;
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
            TickFlash(dt);
            CaptureHome();
            if(Phase==RallyPhase.Disabled)Phase=RallyPhase.Idle;
            if(MatchEngine)return TickMatch(dt,ballInPlay,out start,out destination,out seconds);
            TickTurnGlide(dt);
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
                case RallyPhase.OpponentReceive:
                    TickOpponentReceive(dt);
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
                    if(!HoldAtMatchEnd && _rallyTimer>=MatchEndSeconds)ResetMatch();
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
            // Absolute at every turn change (the view is blanked): the head goes to the block spot or the spike stance,
            // whatever an alignment or the player's own steps did since.
            if(Jump!=null && Jump.Floor!=null)Jump.Floor.PlaceStance(turn==RallyTurn.Spike ? SpikeStance : Vector3.zero);
            if(Jump!=null)
            {
                Jump.JumpHeight=turn==RallyTurn.Spike ? SpikeJumpHeight : BlockJumpHeight;
                Jump.Duration=turn==RallyTurn.Spike ? SpikeJumpSeconds : BlockJumpSeconds;
            }
            // Six opponents always. Spike turn: three blockers + three back-row defenders.
            // Block turn: attacker, setter (blocker 1), idle middle (blocker 2) + the same three defenders.
            bool spikeTurn=turn==RallyTurn.Spike;
            if(Teammates!=null)foreach(var mate in Teammates)if(mate!=null)mate.gameObject.SetActive(false); // match-only players
            if(Opponent!=null)
            {
                Opponent.gameObject.SetActive(!spikeTurn);
                Opponent.transform.SetPositionAndRotation(_opponentHome,_opponentHomeRotation);
                _opponentFollow=-1f;Act(Opponent,0f);
            }
            if(Ally!=null)Ally.gameObject.SetActive(spikeTurn);
            // Our setter stands on the player's right or left, a different side now and then.
            if(Flowing)_allySide=Random.value<.5f ? -1f : 1f;
            PlaceAlly();Act(Ally,0f);_followSetter=null;_setterFollow=-1f;
            var facing=Quaternion.LookRotation(-Forward,Vector3.up);
            if(Defenders!=null)for(int i=0;i<Defenders.Length;i++)
            {
                if(Defenders[i]==null)continue;
                Defenders[i].gameObject.SetActive(true);
                Defenders[i].transform.SetPositionAndRotation(_defenderHome[i],facing);Act(Defenders[i],0f);
            }
            _digger=-1;
            if(Blockers==null)return;
            for(int i=0;i<Blockers.Length;i++)
            {
                _blockerActive[i]=false;_blockerTargets[i]=BlockerStandby(i);
                if(Blockers[i]==null)continue;
                var blocker=Blockers[i];
                blocker.Role=VolleyOpponentPrototype.MotionRole.Block;
                if(!spikeTurn && i==0){blocker.Role=VolleyOpponentPrototype.MotionRole.Set;_blockerTargets[i]=NetCentre+Right*OpponentSetterOffset.x+Forward*OpponentSetterOffset.y;}
                if(!spikeTurn && i==1)_blockerTargets[i]=NetCentre+Right*MiddleBlockerOffset+Forward*BlockerNetDistance;
                blocker.gameObject.SetActive(spikeTurn || i<2);
                SetSolid(blocker,false);
                blocker.transform.SetPositionAndRotation(_blockerTargets[i],facing);
                Act(blocker,0f);
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
                if(!BeginOpponentReceive())return;
                Phase=RallyPhase.OpponentReceive;
                Cue="OPPONENT RECEIVING — WATCH THE ATTACKER";
            }
            else
            {
                if(!BeginAllyReceive())return;
                // Their short cover stands off to one side behind the block, a different side each time.
                if(CoverIndex>=0 && _defenderHome!=null && CoverIndex<_defenderHome.Length)
                    _defenderHome[CoverIndex]=NetCentre+Forward*CoverDepth+Right*((Random.value<.5f ? -1f : 1f)*Random.Range(1.2f,2.4f));
                Phase=RallyPhase.AllyReceive;
                Cue="TEAMMATE SETTING — GET READY TO SPIKE";
            }
            _attemptsInTurn++;
        }

        /// <summary>Block turn: the pass drops onto the opponent setter, who sets the attacker (no feeder).</summary>
        bool BeginOpponentReceive(Vector3? from=null)
        {
            if(_preparedBall!=null)return false;
            var setter=OpponentSetter;
            if(setter==null || Opponent==null || Drill==null || Drill.Pool==null)return false;
            // Tell: the attacker squares up toward the shot from the start of the play.
            // The hitter attacks from a different spot along the net every time; its body still points at the shot
            // (toward the player's block, plus the random turn the player reads).
            Opponent.transform.position=_opponentHome+Right*Random.Range(-AttackerShift,AttackerShift);
            Opponent.transform.rotation=_opponentHomeRotation;
            var hand=Opponent.ReleasePosition;var eye=GroundedEye+Forward*Drill.ContactForwardDistance;
            var line=Vector3.ProjectOnPlane(eye-hand,Vector3.up);
            float aim=line.sqrMagnitude>.01f ? Vector3.SignedAngle(-Forward,line,Vector3.up) : 0f;
            _attackYaw=aim+Random.Range(-AttackBodyYaw,AttackBodyYaw);
            Opponent.transform.rotation=_opponentHomeRotation*Quaternion.Euler(0f,_attackYaw,0f);
            var toward=Vector3.ProjectOnPlane(Opponent.ReleasePosition-setter.transform.position,Vector3.up);
            if(toward.sqrMagnitude>.001f)setter.transform.rotation=Quaternion.LookRotation(toward,Vector3.up);
            _tossEnd=setter.ReleasePosition;
            _tossStart=from ?? _tossEnd+Vector3.up*AllyDropHeight+Forward*AllyDropBehind;
            _dropSeconds=from.HasValue ? Mathf.Max(AllyDropSeconds,Vector3.Distance(_tossStart,_tossEnd)/5f) : AllyDropSeconds;
            if(!StageBall(_tossStart))return false;
            _rallyTimer=0f;_setterFollow=-1f;_windup=-1f;
            Act(setter,0f);
            return true;
        }
        void TickOpponentReceive(float dt)
        {
            var setter=OpponentSetter;
            if(_preparedBall==null || setter==null){Phase=RallyPhase.Idle;return;}
            _rallyTimer+=dt;
            Act(setter,Mathf.Clamp01(_rallyTimer/_dropSeconds)*VolleyOpponentPrototype.SetContactPhase);
            var position=TossPosition(_tossStart,_tossEnd,_rallyTimer,_dropSeconds);
            _preparedBall.transform.position=position;_preparedBall.Body.position=position;
            if(_rallyTimer<_dropSeconds)return;
            // The set now travels from the setter's hands to the attacker's hitting hand during the wind-up.
            _tossStart=_tossEnd;_tossEnd=Opponent.ReleasePosition;
            _followSetter=setter;_setterFollow=0f;
            Phase=RallyPhase.OpponentSet;
            Cue="OPPONENT SETTING — READ THE ATTACKER";
        }

        bool BeginAllyReceive(Vector3? from=null)
        {
            if(_preparedBall!=null)return false;
            if(Ally==null || Drill==null || Drill.Pool==null)return false;
            FaceAlly();
            _tossEnd=Ally.ReleasePosition;
            _tossStart=from ?? _tossEnd+Vector3.up*AllyDropHeight-Forward*AllyDropBehind;
            // A ball coming back over the net flies a high arc to the setter (a free ball), not a drop from above.
            _dropSeconds=from.HasValue ? Mathf.Max(1.4f,Vector3.Distance(_tossStart,_tossEnd)/5f) : AllyDropSeconds;
            if(!StageBall(_tossStart))return false;
            _rallyTimer=0f;_setterFollow=-1f;
            Act(Ally,0f);
            if(!_blockersFresh)PlanSpike(); // Later attempts shuffle visibly during the drop.
            _blockersFresh=false;
            return true;
        }

        /// <summary>Picks where the set goes and places the blockers at random along the net around it, each with its own reach.</summary>
        void PlanSpike(float? contactLateral=null)
        {
            if(contactLateral==null)_allyLateral=Random.Range(-AllySetLateralSpread,AllySetLateralSpread);
            if(Blockers==null || Blockers.Length==0)return;
            int wanted=Mathf.Clamp(Random.Range(Mathf.Min(MinBlockers,MaxBlockers),Mathf.Max(MinBlockers,MaxBlockers)+1),0,Blockers.Length);
            float contact=contactLateral ?? Vector3.Dot(GroundedEye-NetCentre,Right)+_allyLateral;
            float limit=Drill.ReceiveNet!=null ? Mathf.Max(.5f,Drill.ReceiveNet.bounds.extents.x-.5f) : CourtHalfWidth;
            var placed=new List<float>();
            if(wanted>0)placed.Add(Mathf.Clamp(contact+Random.Range(-ReadingBlockerRange,ReadingBlockerRange),-limit,limit));
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
            Act(Ally,Mathf.Clamp01(_rallyTimer/_dropSeconds)*VolleyOpponentPrototype.SetContactPhase);
            var position=TossPosition(_tossStart,_tossEnd,_rallyTimer,_dropSeconds);
            _preparedBall.transform.position=position;_preparedBall.Body.position=position;
            if(_rallyTimer<_dropSeconds)return false;
            start=_tossEnd;
            destination=SpikeContactPoint(_allyLateral);
            seconds=AllyFlightSeconds;
            // Peak of the jump meets the arriving set; jumping at the release would land before the ball arrives.
            SpikeJumpTime=Mathf.Max(0f,AllyFlightSeconds-Jump.Duration*.5f+SpikeJumpPeakOffset);
            for(int i=0;i<_blockerJumpAt.Length;i++)
                _blockerJumpAt[i]=AllyFlightSeconds+BlockerReactionSeconds+Random.Range(-BlockerTimingJitter,BlockerTimingJitter)
                    -VolleyOpponentPrototype.BlockPeakPhase*BlockerJumpSeconds;
            _blockerClock=0f;_followSetter=Ally;_setterFollow=0f;
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
            var target=ApproachBase+Forward*glide;
            // Never glide back within a jump (the stance only moves forward here).
            if(Vector3.Dot(target-Jump.Floor.StanceOffset,Forward)>0f)Jump.Floor.SetStanceOffset(target);
        }

        void BeginBallInPlay(Vector3 start)
        {
            _lastBallPosition=start;_havePhysicsSample=false;
            _playerTouched=_blockerTouched=_handLostNear=_handReadyNear=false;
            _digger=-1;_digRolledFor=-1;_digSuccess=false;_digMissShown=false;_netCrossLogged=false;_digDive=false;_crossedUnder=false;_sinceTouch=0f;
        }
        /// <summary>
        /// Physics-rate landing check. A fast ball can bounce between two samples without ever being seen near the floor,
        /// so a downward-to-upward velocity flip near the floor also counts, and the touchdown point is extrapolated.
        /// </summary>
        /// <returns>True when a defender dug the ball; the drill then applies <see cref="DigVelocity"/>.</returns>
        public bool SampleBall(Vector3 position,Vector3 velocity,float radius,float step,float damping=0f)
        {
            if(MatchEngine)return SampleMatch(position,velocity,radius,step,damping);
            if(Phase!=RallyPhase.OpponentSpike && Phase!=RallyPhase.PlayerSpike)return false;
            if(_playerTouched)
            {
                _sinceTouch+=step;
                if(Phase==RallyPhase.PlayerSpike)LogNetCrossing(position);
                // A ball that crosses to their side below the tape went under or through the net: it does not count.
                if(_havePhysicsSample && Vector3.Dot(_previousBallPosition-NetCentre,Forward)<0f && Vector3.Dot(position-NetCentre,Forward)>=0f
                    && Mathf.Lerp(_previousBallPosition.y,position.y,.5f)<(Drill.ReceiveNet!=null ? Drill.ReceiveNet.bounds.max.y : 2.43f)-.05f)
                    _crossedUnder=true;
                PlanDig(position,velocity,damping);
                if(TryDig(position))return true;
            }
            if(DetectLanding(position,velocity,radius,step,out var landing)){_lastBallPosition=landing;Resolve(landing);}
            return false;
        }
        /// <summary>Tuning log: where the player's spike crossed the net and how far the nearest jumping blocker's hands were.</summary>
        void LogNetCrossing(Vector3 position)
        {
            if(_netCrossLogged || !_havePhysicsSample || Blockers==null)return;
            if(Vector3.Dot(_previousBallPosition-NetCentre,Forward)>=0f || Vector3.Dot(position-NetCentre,Forward)<0f)return;
            _netCrossLogged=true;
            float lateral=Vector3.Dot(position-NetCentre,Right),nearest=float.MaxValue,hands=0f;
            for(int i=0;i<Blockers.Length;i++)
            {
                if(Blockers[i]==null || !_blockerActive[i])continue;
                float gap=Mathf.Abs(Vector3.Dot(Blockers[i].transform.position-NetCentre,Right)-lateral);
                if(gap<nearest){nearest=gap;hands=Mathf.Max(Blockers[i].LeftHand!=null?Blockers[i].LeftHand.position.y:0f,Blockers[i].RightHand!=null?Blockers[i].RightHand.position.y:0f);}
            }
            Debug.Log($"[VolleySpike] net crossing height={position.y:F2} lateral={lateral:F2} nearestBlocker={nearest:F2}m hands={hands:F2} blocked={_blockerTouched}");
        }
        /// <summary>First floor contact, including a bounce that happened between two samples (touchdown extrapolated).</summary>
        bool DetectLanding(Vector3 position,Vector3 velocity,float radius,float step,out Vector3 landing)
        {
            float floor=Drill.CourtFrame.position.y;
            landing=position;
            bool touching=position.y-floor<=radius+.03f;
            bool bounced=_havePhysicsSample && _previousBallVelocity.y<-.5f && velocity.y>-.1f && position.y-floor<radius+.45f;
            if(touching || bounced)
            {
                if(_havePhysicsSample && _previousBallVelocity.y<-.01f)
                {
                    float t=Mathf.Clamp((_previousBallPosition.y-floor-radius)/-_previousBallVelocity.y,0f,step*1.5f);
                    landing=_previousBallPosition+_previousBallVelocity*t;
                }
                landing.y=floor;
                return true;
            }
            _previousBallPosition=position;_previousBallVelocity=velocity;_havePhysicsSample=true;_lastBallPosition=position;
            return false;
        }

        /// <summary>
        /// Predicts where the ball will come down to dig height in the opponent court and commits the defender who can get
        /// there in time (reaction, then running). Re-run every physics step, so blocker deflections update the plan.
        /// </summary>
        void PlanDig(Vector3 position,Vector3 velocity,float damping)
        {
            _digger=-1;
            if(Defenders==null || Defenders.Length==0)return;
            float floor=Drill.CourtFrame.position.y;
            var p=position;var v=velocity;float t=0f;const float dt=.02f;
            bool reached=false;
            for(int i=0;i<200;i++)
            {
                if(v.y<0f && p.y-floor<=DigHeight){reached=true;break;}
                v+=Physics.gravity*dt;v*=Mathf.Clamp01(1f-damping*dt);p+=v*dt;t+=dt;
            }
            if(!reached)return;
            float arriving=v.magnitude;
            var local=p-NetCentre;
            float depth=Vector3.Dot(local,Forward),side=Vector3.Dot(local,Right);
            // Defend only balls coming down inside the opponent court; out balls are left alone.
            if(depth<=0f || depth>CourtDepth || Mathf.Abs(side)>CourtHalfWidth)return;
            float moving=t-Mathf.Max(0f,DefenderReactionSeconds-_sinceTouch);
            float best=float.MaxValue;bool dive=false;
            for(int i=0;i<Defenders.Length;i++)
            {
                if(Defenders[i]==null || !Defenders[i].gameObject.activeSelf)continue;
                var offset=Vector3.ProjectOnPlane(p-Defenders[i].transform.position,Vector3.up);
                // A hard ball the short cover would have to move for is not its ball; one straight at it, it plays.
                if(i==CoverIndex && arriving>CoverMaxSpeed && offset.magnitude>DigReach+DiveReach)continue;
                // Before it could react (a hard spike) it still plays a ball at its body, or dives for one just beside it.
                float ready=Mathf.Max(0f,moving);
                float need=Mathf.Max(0f,offset.magnitude-DigReach)/DefenderSpeed;
                // Out of reach on foot: only a dive (the last metre, no time to step).
                bool diving=need>ready && Mathf.Max(0f,offset.magnitude-DigReach-DiveReach)/DefenderSpeed<=ready;
                if(need>ready && !diving)continue;
                float cost=diving ? need+10f : need; // a defender who can get there on foot goes first
                if(cost<best){best=cost;_digger=i;dive=diving;_digTarget=new Vector3(p.x,floor,p.z);}
            }
            _digDive=dive;
            // One roll per defender and ball: a hard-driven ball is often shanked even when reached, a soft one rarely.
            if(_digger>=0 && _digger!=_digRolledFor)
            {
                _digRolledFor=_digger;
                _digSuccess=Random.value<Mathf.Lerp(DigEasyChance,DigHardChance,Mathf.InverseLerp(DigSpeedRange.x,DigSpeedRange.y,arriving))*(_digDive ? DiveChance : 1f);
            }
        }
        bool TryDig(Vector3 position)
        {
            if(_digger<0)return false;
            var defender=Defenders[_digger];
            float height=position.y-Drill.CourtFrame.position.y;
            if(height>DigHeight+.6f || height<.15f)return false;
            float reach=Vector3.ProjectOnPlane(position-defender.transform.position,Vector3.up).magnitude;
            if(reach>DigReach+(_digDive ? DiveReach : 0f))return false;
            if(_digDive || reach>DigReach*.8f)StartDive(_digger,position);
            if(!_digSuccess)
            {
                // Got there but could not control it: the dig motion, and the ball plays on to the floor.
                if(!_digMissShown){_digMissShown=true;Act(defender,.8f);}
                return false;
            }
            // Pop the ball up toward the opponent's own front court, as a real dig would.
            DigVelocity=Vector3.up*5.5f-Forward*Mathf.Clamp(Vector3.Dot(position-NetCentre,Forward)*.35f,0f,2.5f);
            Act(defender,1f);
            PlaySpikeAudio(position);
            if(Flowing){ContinueRally(position,!_digDive);return false;}
            Award(false,Phase==RallyPhase.OpponentSpike ? "BLOCK DUG" : "DUG",position);
            return true;
        }
        /// <summary>
        /// SPIKE + BLOCK, their defender dug the player's spike or block: no point, the play goes on from the dig. Dug
        /// comfortably, they set and attack - the player glides to the block spot and blocks. Dug diving, it only gets back
        /// over - our setter (left or right) takes it and sets the player, who glides back to the spike stance.
        /// </summary>
        void ContinueRally(Vector3 from,bool comfortable)
        {
            Drill.RetireRallyBall(); // a scripted ball carries on from the same spot
            var turn=comfortable ? RallyTurn.Block : RallyTurn.Spike;
            StartFlowTurn(turn);
            if(turn==RallyTurn.Block)
            {
                if(!BeginOpponentReceive(from)){Phase=RallyPhase.Idle;return;}
                Phase=RallyPhase.OpponentReceive;
                Cue="THEY DUG IT — THEY ATTACK: BLOCK!";
            }
            else
            {
                if(!BeginAllyReceive(from)){Phase=RallyPhase.Idle;return;}
                Phase=RallyPhase.AllyReceive;
                Cue="SAVED, BUT ONLY BACK OVER — YOUR SETTER SETS YOU";
            }
        }
        /// <summary>The turn changes inside the rally: no blank, the stance glides there behind the comfort vignette.</summary>
        void StartFlowTurn(RallyTurn turn)
        {
            bool spikeTurn=turn==RallyTurn.Spike;
            CurrentTurn=turn;TurnStarted=true;_attemptsInTurn=1;
            if(Jump!=null)
            {
                Jump.ResetJump();
                Jump.JumpHeight=spikeTurn ? SpikeJumpHeight : BlockJumpHeight;
                Jump.Duration=spikeTurn ? SpikeJumpSeconds : BlockJumpSeconds;
            }
            if(Jump!=null && Jump.Floor!=null)
            {
                _glideStart=Jump.Floor.StanceOffset;_glideEnd=spikeTurn ? SpikeStance : Vector3.zero;_turnGlide=0f;
            }
            if(Opponent!=null){Opponent.gameObject.SetActive(!spikeTurn);_opponentFollow=-1f;Act(Opponent,0f);}
            if(Ally!=null)
            {
                if(spikeTurn && !Ally.gameObject.activeSelf){_allySide=Random.value<.5f ? -1f : 1f;PlaceAlly();}
                Ally.gameObject.SetActive(spikeTurn);Act(Ally,0f);
            }
            _followSetter=null;_setterFollow=-1f;_digger=-1;
            // Their front row walks to the new jobs (no teleport): three blockers, or setter + middle for their attack.
            if(Blockers!=null)for(int i=0;i<Blockers.Length;i++)
            {
                if(Blockers[i]==null)continue;
                var blocker=Blockers[i];
                _blockerActive[i]=false;_blockerTargets[i]=BlockerStandby(i);
                blocker.Role=VolleyOpponentPrototype.MotionRole.Block;
                if(!spikeTurn && i==0){blocker.Role=VolleyOpponentPrototype.MotionRole.Set;_blockerTargets[i]=NetCentre+Right*OpponentSetterOffset.x+Forward*OpponentSetterOffset.y;}
                if(!spikeTurn && i==1)_blockerTargets[i]=NetCentre+Right*MiddleBlockerOffset+Forward*BlockerNetDistance;
                blocker.gameObject.SetActive(spikeTurn || i<2);
                SetSolid(blocker,false);Act(blocker,0f);
            }
            _blockersFresh=false;
            TurnAnnounced?.Invoke(turn);
        }
        void TickTurnGlide(float dt)
        {
            if(_turnGlide<0f)
            {
                if(!MatchEngine && VignetteAlpha>0f)VignetteAlpha=Mathf.MoveTowards(VignetteAlpha,0f,dt/VignetteOutSeconds);
                return;
            }
            _turnGlide+=dt;
            VignetteAlpha=Mathf.MoveTowards(VignetteAlpha,1f,dt/VignetteInSeconds);
            float u=Mathf.Clamp01((_turnGlide-VignetteInSeconds)/FlowGlideSeconds);
            if(Jump!=null && Jump.Floor!=null)Jump.Floor.SetStanceOffset(Vector3.Lerp(_glideStart,_glideEnd,Mathf.SmoothStep(0f,1f,u)));
            if(u<1f)return;
            _turnGlide=-1f;
            // End exactly on the spot (absolute), whatever the player's own steps did meanwhile.
            if(Jump!=null && Jump.Floor!=null)Jump.Floor.PlaceStance(_glideEnd);
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
            if(_playerTouched && opponentCourt && _crossedUnder){Award(false,"UNDER THE NET",position);return;}
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
            bool blockTurn=Phase==RallyPhase.OpponentSpike; // SetOutcome moves on to Recovery
            if(player)PlayerScore++;else OpponentScore++;
            if(!_playerTouched && (_handLostNear || _handReadyNear))
                Debug.Log("[VolleyRally] untouched "+Phase+": hand near ball ready="+_handReadyNear+" lost="+_handLostNear);
            SetOutcome(text,player);
            PointScored?.Invoke(player,position);
            if(!MatchEngine)TurnRepResolved?.Invoke(blockTurn,player || text=="DUG" || text=="BLOCK DUG");
        }
        /// <summary>Drill reports a hand passing near the untouched ball, with whether it could collide.</summary>
        public void NoteHandNearBall(bool collidable)
        {
            if(!AwaitingPlayerTouch)return;
            if(collidable)_handReadyNear=true;else _handLostNear=true;
        }
        /// <summary>Demo Session retry: a new match from 0 - 0 in the current mode.</summary>
        public void RestartMatch()
        {
            SetMode(Mode);
            ResetMatch();
        }
        void ResetMatch()
        {
            PlayerScore=OpponentScore=0;TurnStarted=false;_attemptsInTurn=0;_playerSlot=-1;
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
            if(_setterFollow>=0f && _followSetter!=null)
            {
                _setterFollow+=dt;
                Act(_followSetter,Mathf.Lerp(VolleyOpponentPrototype.SetContactPhase,1f,Mathf.Clamp01(_setterFollow/.8f)));
                if(_setterFollow>=.8f)_setterFollow=-1f;
            }
            if(Phase==RallyPhase.TurnChange)return;
            TickDefenders(dt);
            if(Blockers==null)return;
            if(_blockerClock>=0f)_blockerClock+=dt;
            for(int i=0;i<Blockers.Length;i++)
            {
                var blocker=Blockers[i];
                if(blocker==null)continue;
                blocker.transform.position=Vector3.MoveTowards(blocker.transform.position,_blockerTargets[i],BlockerShuffleSpeed*dt);
                if(blocker.Role!=VolleyOpponentPrototype.MotionRole.Block)continue; // the block-turn setter is posed by the set
                float phase=_blockerActive[i] && _blockerClock>=0f ? Mathf.Clamp01((_blockerClock-_blockerJumpAt[i])/BlockerJumpSeconds) : 0f;
                Act(blocker,phase>=1f ? 0f : phase);
            }
        }

        /// <summary>The committed defender runs (after its reaction time) to the predicted dig point; the others drift home.</summary>
        void TickDefenders(float dt)
        {
            if(Defenders==null)return;
            for(int i=0;i<Defenders.Length;i++)
            {
                var defender=Defenders[i];
                if(defender==null || !defender.gameObject.activeSelf)continue;
                bool digging=i==_digger && (Phase==RallyPhase.OpponentSpike || Phase==RallyPhase.PlayerSpike);
                var target=digging ? _digTarget : _defenderHome[i];
                if(!digging || _sinceTouch>=DefenderReactionSeconds)
                    defender.transform.position=Vector3.MoveTowards(defender.transform.position,target,DefenderSpeed*dt);
                if(digging)
                {
                    float distance=Vector3.ProjectOnPlane(target-defender.transform.position,Vector3.up).magnitude;
                    Act(defender,Mathf.Clamp01(1.2f-distance/2f));
                }
                else if(defender.PreviewPhase>0f)Act(defender,Mathf.MoveTowards(defender.PreviewPhase,0f,dt*2f));
                if(_diveClock!=null && i<_diveClock.Length && _diveClock[i]>=0f)
                {
                    _diveClock[i]+=dt;
                    float angle=DiveAngle(_diveClock[i]);
                    defender.transform.rotation=Quaternion.AngleAxis(angle,_diveAxis[i])*Quaternion.LookRotation(-Forward,Vector3.up);
                    if(_diveClock[i]>=1.2f){_diveClock[i]=-1f;defender.transform.rotation=Quaternion.LookRotation(-Forward,Vector3.up);}
                }
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
            var start=GroundedEye-Vector3.ProjectOnPlane(Jump.Floor.StanceOffset-ApproachBase,Vector3.up);
            var point=start+Forward*(SpikeContactForward+SpikeApproachDistance)+Right*lateral;
            point.y=GroundedEye.y+Jump.JumpHeight+SpikeReachAboveEye;
            return point;
        }

        /// <summary>The player's hand deflected the rally ball (block or spike). The point is decided where it lands.</summary>
        public void RegisterPlayerTouch()
        {
            if(CurrentPlayerTouch==PlayerTouch.None)return;
            _playerTouched=true;_sinceTouch=0f;
            if(MatchEngine){OnMatchPlayerTouch();return;}
            Cue=IsPlayerSpike ? "SPIKE!" : "TOUCHED — IS IT GOING BACK?";
        }
        /// <summary>Hand zone of the player's spike or block touch (0 wrist .. 1 fingertips); fingertips only graze the ball.</summary>
        public float LastTouchZone { get; private set; }
        public void NoteTouchZone(float zone,bool fingertips,bool spike)
        {
            LastTouchZone=zone;
            Cue=fingertips ? (spike ? "FINGERTIPS — ONLY A TIP" : "FINGERTIPS — IT SLIPS THROUGH")
                : (Cue+"  (PALM — SOLID)");
        }
        /// <summary>Called by a solid opposing blocker. The ball rebounds physically; its landing decides the point.</summary>
        public bool RegisterSpikeBlocked(Ball ball,Vector3 point)
        {
            if(!IsPlayerSpike || !_playerTouched || _blockerTouched || ball==null || ball!=Drill.ActiveBall)return false;
            _blockerTouched=true;
            // Their hands absorb much of it: the physics bounce off the kinematic colliders came off far too lively.
            ball.Body.linearVelocity*=BlockerReboundScale;
            // After a block the attacking side has three touches again; the blocking side touched it last.
            _lastTouchTeam=Team.Them;_touches[0]=0;
            PlaySpikeAudio(point);
            Cue="BLOCKER TOUCH!";
            return true;
        }
        /// <summary>The ball vanished (target flash, timeout) before touching the floor: decide from its last position.</summary>
        public void RegisterBallUnavailable()
        {
            if(MatchEngine)
            {
                if(Phase==RallyPhase.OpponentSpike || Phase==RallyPhase.PlayerSpike || Phase==RallyPhase.PlayerServe
                    || Phase==RallyPhase.PlayerReceive || Phase==RallyPhase.PlayerSet)ResolveMatch(_lastBallPosition);
                return;
            }
            if(Phase==RallyPhase.OpponentSpike || Phase==RallyPhase.PlayerSpike)Resolve(_lastBallPosition);
        }

        /// <summary>Lateral offset (from the player's eyes) where a shot along the attacker's body direction ± deviation crosses the block plane.</summary>
        public float AttackLateral(Vector3 release)
        {
            float angle=_attackYaw+Random.Range(-AttackShotDeviation,AttackShotDeviation);
            var direction=Quaternion.AngleAxis(angle,Vector3.up)*-Forward;
            var eye=GroundedEye;
            float along=Vector3.Dot(release-(eye+Forward*Drill.ContactForwardDistance),Forward);
            float lateral=Vector3.Dot(release-eye,Right)+Vector3.Dot(direction,Right)/Mathf.Max(.2f,Vector3.Dot(direction,-Forward))*along;
            return Mathf.Clamp(lateral,-BlockLateralLimit,BlockLateralLimit);
        }
        public Vector3 GroundedEye=>Drill.Head.position-Vector3.up*Jump.Floor.VirtualLift;
        [Header("Player spike: a high contact is a good shot")]
        [Tooltip("Weight of the contact height in a spike's quality (the rest is the swing speed): fast swings lose hand tracking, so a high contact is what makes a good spike.")]
        [Range(0f,1f)] public float SpikeHeightWeight=.65f;
        [Tooltip("Quality 0 at this far below the top of the reach (m), 1 within 0.1 m of it.")]
        [Min(.1f)] public float SpikeHeightWindow=.8f;
        [Tooltip("Swing speed (m/s) that counts as a full swing.")]
        [Min(1f)] public float SpikeFullSwing=7f;
        /// <summary>Quality (0..1) of the player's last spike: mostly how high it was met.</summary>
        public float LastSpikeQuality { get; private set; }
        /// <summary>Spike speed between the drill's minimum and maximum, from contact height and swing.</summary>
        public float SpikeSpeed(Vector3 ball,float swing)
        {
            float top=GroundedEye.y+Jump.JumpHeight+SpikeReachAboveEye;
            float height=Mathf.InverseLerp(top-SpikeHeightWindow,top-.1f,ball.y);
            float arm=Mathf.InverseLerp(Drill.SpikeSwingSpeed,SpikeFullSwing,swing);
            LastSpikeQuality=Mathf.Clamp01(SpikeHeightWeight*height+(1f-SpikeHeightWeight)*arm);
            return Mathf.Lerp(Drill.SpikeMinimumSpeed,Drill.SpikeMaximumSpeed,LastSpikeQuality);
        }
        /// <summary>The forearms are drawn and solid while the player blocks (block turn, or a block in the match).</summary>
        public bool ShowsForearms=>RallyActive && (CurrentPlayerTouch==PlayerTouch.Block || (!MatchEngine && Mode!=RallyMode.SpikeOnly && CurrentTurn==RallyTurn.Block && TurnStarted));
        float _flashAge=-1f;
        /// <summary>A short flash where a fingertip (or the side of the hand) grazed the ball.</summary>
        public void ShowTouchFlash(Vector3 at)
        {
            PlaceTarget(4,at,.09f,new Color(1f,.9f,.4f,.85f));
            _flashAge=0f;
        }
        void TickFlash(float dt)
        {
            if(_flashAge<0f)return;
            _flashAge+=dt;
            float t=_flashAge/.35f;
            if(t>=1f){HideTarget(4);_flashAge=-1f;return;}
            var target=_targets[4];
            if(target==null)return;
            target.transform.localScale=Vector3.one*(.18f+.25f*t);
            if(_targetBlock==null)_targetBlock=new MaterialPropertyBlock();
            target.GetPropertyBlock(_targetBlock);_targetBlock.SetColor("_BaseColor",new Color(1f,.9f,.4f,.85f*(1f-t)));target.SetPropertyBlock(_targetBlock);
        }

        /// <summary>
        /// The player's spike: the swing decides speed, side and how steep it is (a steeper swing lands shorter); the
        /// direction is pulled by <see cref="SpikeAimAssist"/> toward a flight that clears the tape and comes down sharply
        /// inside their court, so a downward swing never floats.
        /// </summary>
        public Vector3 SpikeShot(Vector3 ball,Vector3 swing,float speed)
        {
            var forward=Forward;
            var flat=Vector3.ProjectOnPlane(swing,Vector3.up);
            if(flat.sqrMagnitude<1e-4f || Vector3.Dot(flat.normalized,forward)<.2f)flat=forward;
            float yaw=Mathf.Clamp(Vector3.SignedAngle(forward,flat.normalized,Vector3.up),-SpikeMaximumYaw,SpikeMaximumYaw);
            var direction=Quaternion.AngleAxis(yaw,Vector3.up)*forward;
            float pitch=Mathf.Atan2(swing.y,Vector3.ProjectOnPlane(swing,Vector3.up).magnitude)*Mathf.Rad2Deg;
            float depth=Mathf.Lerp(SpikeLandingDepth.x,SpikeLandingDepth.y,Mathf.InverseLerp(-60f,10f,pitch));
            // A high, clean contact can be hit sharper.
            depth=Mathf.Max(SpikeLandingDepth.x,depth-1.5f*LastSpikeQuality);
            float aimAssist=Mathf.Lerp(SpikeAimAssist,Mathf.Min(1f,SpikeAimAssist+.25f),LastSpikeQuality);
            float toNet=Mathf.Max(0f,Vector3.Dot(NetCentre-ball,forward));
            var target=ball+direction*((toNet+depth)/Mathf.Max(.3f,Vector3.Dot(direction,forward)));
            target=NetCentre+Right*Mathf.Clamp(Vector3.Dot(target-NetCentre,Right),-CourtHalfWidth+.3f,CourtHalfWidth-.3f)
                +forward*Mathf.Clamp(Vector3.Dot(target-NetCentre,forward),.5f,CourtDepth-.3f);
            target.y=Drill.CourtFrame.position.y;
            float netTop=Drill.ReceiveNet!=null ? Drill.ReceiveNet.bounds.max.y : 2.43f;
            var aimed=GloveBallDemo.Core.VolleyMath.FlightOver(ball,target,NetCentre,forward,netTop+.15f,speed,Physics.gravity);
            var raw=GloveBallDemo.Core.VolleyMath.SpikeVelocity(swing,1f,speed,speed,Drill.SpikeMaximumRise);
            // A solid spike stays in: when the swing's own share would send it into the net or past the end line,
            // the aim takes over more.
            foreach(float assist in new[]{aimAssist,.8f,1f})
            {
                var velocity=Vector3.Slerp(raw,aimed,Mathf.Max(assist,aimAssist));
                float lands=LandingDepth(ball,velocity);
                if(ClearsNet(ball,velocity,netTop+.05f) && lands>.3f && lands<CourtDepth)return velocity;
            }
            return aimed;
        }
        /// <summary>How far past the net a ball from <paramref name="start"/> lands (no drag).</summary>
        float LandingDepth(Vector3 start,Vector3 velocity)
        {
            float g=Mathf.Abs(Physics.gravity.y),h=Mathf.Max(0f,start.y-Drill.CourtFrame.position.y);
            float t=(velocity.y+Mathf.Sqrt(velocity.y*velocity.y+2f*g*h))/g;
            return Vector3.Dot(start+Vector3.ProjectOnPlane(velocity,Vector3.up)*t-NetCentre,Forward);
        }
        /// <summary>
        /// A palm block: the hands' physical rebound pulled by <see cref="BlockAssist"/> toward a flight that clears the
        /// tape into their court (the block spot is half a metre off the net, so a plain rebound often dropped on our side).
        /// The rebound's sideways and forward speed still choose where it lands.
        /// </summary>
        public Vector3 BlockShot(Vector3 ball,Vector3 physical)
        {
            var forward=Forward;
            float depth=Mathf.Lerp(BlockLandingDepth.x,BlockLandingDepth.y,Mathf.InverseLerp(0f,8f,Vector3.Dot(physical,forward)));
            // Sideways only as far as the rebound itself turns, never more than BlockMaximumYaw off straight back.
            float yaw=Mathf.Clamp(Mathf.Atan2(Vector3.Dot(physical,Right),Mathf.Max(.5f,Vector3.Dot(physical,forward)))*Mathf.Rad2Deg,-BlockMaximumYaw,BlockMaximumYaw);
            float toNet=Mathf.Max(0f,Vector3.Dot(NetCentre-ball,forward));
            float lateral=Mathf.Clamp(Vector3.Dot(ball-NetCentre,Right)+Mathf.Tan(yaw*Mathf.Deg2Rad)*(toNet+depth),-CourtHalfWidth+.3f,CourtHalfWidth-.3f);
            var target=NetCentre+Right*lateral+forward*depth;target.y=Drill.CourtFrame.position.y;
            float netTop=Drill.ReceiveNet!=null ? Drill.ReceiveNet.bounds.max.y : 2.43f;
            var aimed=GloveBallDemo.Core.VolleyMath.FlightOver(ball,target,NetCentre,forward,netTop+.1f,Mathf.Clamp(physical.magnitude,5f,12f),Physics.gravity);
            var velocity=Vector3.Lerp(physical,aimed,BlockAssist);
            // The hands' own share must not turn it more than that either.
            var flat=Vector3.ProjectOnPlane(velocity,Vector3.up);
            float turned=Vector3.SignedAngle(forward,flat,Vector3.up);
            if(flat.sqrMagnitude>1e-4f && Mathf.Abs(turned)>BlockMaximumYaw)
                velocity=Quaternion.AngleAxis(Mathf.Clamp(turned,-BlockMaximumYaw,BlockMaximumYaw),Vector3.up)*forward*flat.magnitude+Vector3.up*velocity.y;
            return ClearsNet(ball,velocity,netTop+.05f) ? velocity : aimed;
        }
        bool ClearsNet(Vector3 start,Vector3 velocity,float height)
        {
            float toward=Vector3.Dot(velocity,Forward),distance=Vector3.Dot(NetCentre-start,Forward);
            if(distance<=0f)return true;
            if(toward<=.01f)return false;
            float t=distance/toward;
            return start.y+velocity.y*t+.5f*Physics.gravity.y*t*t>=height;
        }
        public float SolveBlockShot(Vector3 start,ref Vector3 destination)
        {
            // A spike is a fast descending shot, not the receive feeder's net-clearing lob.
            float seconds=Mathf.Clamp(Vector3.ProjectOnPlane(destination-start,Vector3.up).magnitude/BlockSpeed,.08f,.4f);
            float g=Mathf.Abs(Physics.gravity.y);
            float fraction=Drill.ReceiveNet!=null ? (Drill.ReceiveNet.bounds.center.z-start.z)/(destination.z-start.z) : 0f;
            float minimum=float.NegativeInfinity;
            if(fraction>0f && fraction<1f)
            {
                float safe=Drill.ReceiveNet.bounds.max.y+.2f; // ball radius plus margin: the attack must never clip the tape
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
            if(block)
            {
                Opponent.Pose(.52f);start=Opponent.ReleasePosition;
                destination=Destination(AttackLateral(start),Random.Range(-HeightSpread,HeightSpread),LastWasFaceShot);
                seconds=SolveBlockShot(start,ref destination);
                PlaySpikeAudio(start);
                _follow=0;
            }
            else
            {
                destination=Destination(Random.Range(-HorizontalSpread,HorizontalSpread),Random.Range(-HeightSpread,HeightSpread),false);
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
            if(MatchEngine)CancelMatch();
            _windup=-1;
            var ball=TakePreparedBall();
            if(ball!=null && Drill!=null && Drill.Pool!=null)Drill.Pool.Return(ball,"cancelled volley toss");
            if(_follow<0 && Opponent!=null)Opponent.PreviewPhase=0;
            if(RallyActive)
            {
                // A layout already applied behind a blink stays; only the in-progress attempt is dropped.
                Phase=RallyPhase.Idle;_rallyTimer=0f;FadeAlpha=0f;_spikeJumpIssued=false;_setterFollow=-1f;_digger=-1;
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
            CancelMatch();_embodied=null;_pendingShow=null;_playerSlot=-1;_roleTouch=PlayerTouch.None;_serving=Team.Us;
            Mode=mode;TurnStarted=false;_attemptsInTurn=0;PlayerScore=OpponentScore=0;
            Jump.ResetJump();
            Drill.ResetCurrentAttempt();
        }
        void PlaySpikeAudio(Vector3 position)
        {
            if(SpikeClip==null)return;
            SpikeAudioRequested?.Invoke(SpikeClip,position);
            if(!Application.isPlaying)return; // EditMode verification observes requests without PC audio.
            // NPC touches sound like the ball in play (its own impact clip), from where they happen: a 3D source fades with
            // distance like any other sound. The spike clip only when the ball has no clip of its own.
            var ball=_preparedBall!=null ? _preparedBall : Drill!=null ? Drill.ActiveBall : null;
            var clip=SpikeClip;float volume=SpikeVolume;
            if(ball!=null && ball.VariantIndex>=0 && HapticEventRelay.TryGetClip(ball.ImpactEvent(DemoHapticEvent.BodyCollide),out var own,out var ownVolume))
            {clip=own;volume=ownVolume;}
            if(_spikeAudio==null)
            {
                var source=new GameObject("Opponent spike audio");source.transform.SetParent(transform,false);
                _spikeAudio=source.AddComponent<AudioSource>();_spikeAudio.playOnAwake=false;
                _spikeAudio.spatialBlend=1f;_spikeAudio.minDistance=2f;_spikeAudio.maxDistance=25f;_spikeAudio.dopplerLevel=0;
            }
            _spikeAudio.transform.position=position;
            _spikeAudio.PlayOneShot(clip,volume);
        }
        void OnDisable()=>CancelFeed();
    }
}
