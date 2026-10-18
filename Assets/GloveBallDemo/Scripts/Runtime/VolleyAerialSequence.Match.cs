using System.Collections.Generic;
using GloveBallDemo.Core;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Continuous 6 v 6 rally (<see cref="RallyMode.Match"/>). NPC-to-NPC play follows scripted ballistic segments;
    /// every touch the involvement level gives to the player is physical. The player is one fixed blue player (that
    /// mannequin is hidden) who rotates one position clockwise on every side-out. The view never blanks: when the
    /// player has to get somewhere (attack approach, block, set, the next rotation spot) the stance glides there in
    /// slow motion behind a narrow comfort vignette. After any physical touch the ball is predicted every physics step
    /// and the team whose side it comes down on digs, sets or loses the point.
    /// </summary>
    public sealed partial class VolleyAerialSequence
    {
        public enum Team { Us, Them }
        public enum PlayerTouch { None, Spike, Block, Receive, Serve, Set }
        public enum Involvement { SpikeBlock, AddReceiveServe, AddSet }

        [Header("Match: 6 v 6 continuous rally")]
        [Tooltip("Touches the player makes. 1: spike + block. 2: adds serve and serve receive. 3: adds the set (assisted).")]
        public Involvement PlayerInvolvement=Involvement.SpikeBlock;
        [Tooltip("Blue teammates besides the setter (Ally): middle, outside, back left, back middle, back right (server).")]
        public VolleyOpponentPrototype[] Teammates=new VolleyOpponentPrototype[0];
        [Tooltip("Forearm platform, enabled only while the player receives a serve.")]
        public VolleyJoinedHands ReceiveVolume;
        [Tooltip("Game speed while the player glides to a new spot. The rally keeps moving, only slower.")]
        [Range(.1f,1f)] public float SlowMotionScale=.35f;
        [Tooltip("Glide speed (m/s, real time) and its duration limits.")]
        [Min(.5f)] public float GlideSpeed=4f;
        [Min(.05f)] public float GlideMinSeconds=.25f;
        [Min(.1f)] public float GlideMaxSeconds=1.2f;
        [Tooltip("Comfort vignette: closes before the glide starts and opens after it ends (real seconds).")]
        [Min(.02f)] public float VignetteInSeconds=.12f;
        [Min(.02f)] public float VignetteOutSeconds=.3f;
        [Tooltip("Rotation slot the player starts each match in (0 front right, 1 front middle, 2 front left, 3 back left, 4 back middle, 5 back right / server).")]
        [Range(0,5)] public int PlayerStartSlot=2;
        [Tooltip("How far a front-row player shifts along the net to block (m).")]
        [Min(0f)] public float PlayerBlockShift=2.5f;
        [Tooltip("Take-off distance from the net for a back-row attack (the approach glide then carries the contact forward).")]
        [Min(3f)] public float BackRowAttackDepth=4f;
        [Tooltip("Level 3: after this long the player's set is taken over by the teammate it was pushed to.")]
        [Min(.05f)] public float SetHandOffSeconds=.25f;
        [Min(0f)] public float ServeDelaySeconds=1f;
        [Min(.4f)] public float PassSeconds=1.15f;
        [Min(.6f)] public float ServeSeconds=1.5f;
        [Min(5f)] public float NpcAttackSpeed=13f;
        [Min(1f)] public float NpcRunSpeed=4.5f;
        [Tooltip("Random offset of each opponent from its formation spot every rally (ours move much less).")]
        [Min(0f)] public float FormationJitter=.6f;
        [Header("Match: opponent defence (weaker than ours)")]
        [Min(0f)] public float TheirDigReaction=.45f;
        [Min(.1f)] public float TheirDigSpeed=3.2f;
        [Min(.1f)] public float TheirDigReach=.6f;
        [Range(0f,1f)] public float TheirDigSuccess=.7f;
        [Range(0f,1f)] public float TheirReceiveSuccess=.9f;
        [Header("Match: our defence")]
        [Min(0f)] public float OurDigReaction=.2f;
        [Min(.1f)] public float OurDigSpeed=5f;
        [Min(.1f)] public float OurDigReach=1f;
        [Range(0f,1f)] public float OurDigSuccess=.95f;
        [Range(0f,1f)] public float OurReceiveSuccess=.95f;
        [Tooltip("A setter chasing a pass.")]
        [Min(.1f)] public float SetterSpeed=5.5f;
        [Min(.1f)] public float SetterReach=1.2f;
        [Tooltip("An NPC setter plays a loose ball at this height.")]
        [Min(.5f)] public float SetHeight=2.1f;
        [Header("Match: NPC block against an NPC attack")]
        [Range(0f,1f)] public float NpcBlockStuff=.12f;
        [Range(0f,1f)] public float NpcBlockTouch=.25f;
        [Header("Match: player serve")]
        [Min(0f)] public float ServePower=1.5f;
        [Min(1f)] public float ServeMinimumSpeed=9f;
        [Min(1f)] public float ServeMaximumSpeed=18f;
        [Range(-1f,1f)] public float ServeMaximumRise=.35f;

        enum Anim { Ready, Dig, Set, Spike, Block }
        sealed class MatchActor
        {
            public VolleyOpponentPrototype A;
            public Team Team;
            public int Slot;
            public Vector3 Home, Target;
            public Quaternion Facing;
            public float Speed, Delay;
            public Anim Anim;
            public float Clock=-1f, Phase, JumpAt;
        }
        // (lateral toward the team's own right, depth from the net). 0 setter, 1 middle, 2 outside, 3-5 back row; 5 serves.
        static readonly Vector2[] SlotBase={new Vector2(2f,1f),new Vector2(0f,1.3f),new Vector2(-2.4f,1.4f),new Vector2(-2.6f,6f),new Vector2(0f,6.8f),new Vector2(2.6f,6f)};
        readonly List<MatchActor> _cast=new List<MatchActor>();
        readonly int[] _touches=new int[2];
        MatchActor _embodied, _pendingShow, _icActor, _plannedAttacker;
        Team _serving=Team.Us, _lastTouchTeam, _icTeam;
        PlayerTouch _roleTouch=PlayerTouch.None, _embodyTouch, _launchTouch;
        RallyPhase _launchPhase;
        bool _embodying, _slowActive, _restoreScale, _launchPending, _icSuccess, _playerServes;
        int _playerSlot=-1;
        float _embodyClock, _glideSeconds, _serveTimer=-1f, _serveWait, _segTime=-1f, _segDuration, _icHeight, _icReach, _icReaction, _icSpeed;
        float _flightTop=float.MinValue;
        Vector3 _icRootOffset;
        /// <summary>Height of a setter's hands (the set pose's forehead window) above the floor.</summary>
        float SetterHandsHeight=>FloorY+SetWindow.y*(Ally!=null ? Ally.transform.lossyScale.y : 1f);
        /// <summary>The set pose's ball window in actor space (<see cref="VolleyOpponentPrototype.LocalRelease"/> for the Set role).</summary>
        static readonly Vector3 SetWindow=new Vector3(0f,1.82f,.30f);
        /// <summary>Where a front-row player's hands meet a pass when they set from their spot, facing the net.</summary>
        Vector3 SetWindowAt(MatchActor actor)
        {
            var root=actor.Home;root.y=FloorY;
            return root+TeamFacing(actor.Team)*Vector3.Scale(actor.A.transform.lossyScale,SetWindow);
        }
        Vector3 _glideFrom, _glideDelta, _segFrom, _segTo, _icPoint, _approachBase;
        System.Action _segArrive, _afterEmbody, _onLaunched;
        System.Action<float> _segProgress;
        System.Func<(Vector3 from,Vector3 to,float seconds)> _launchCompute;

        /// <summary>Touch the player is expected to make now (None while NPCs play).</summary>
        public PlayerTouch AwaitedTouch { get; private set; }
        /// <summary>Kind of touch the player's current embodiment exists for.</summary>
        public PlayerTouch RoleTouch=>_roleTouch;
        public Team Serving=>_serving;
        public Team LastTouchTeam=>_lastTouchTeam;
        public int Touches(Team team)=>_touches[(int)team];
        public bool Embodying=>_embodying;
        public VolleyOpponentPrototype Embodied=>_embodied!=null ? _embodied.A : null;
        /// <summary>Comfort vignette strength (0 open, 1 narrow); drawn head-locked by the presenter.</summary>
        public float VignetteAlpha { get; private set; }
        /// <summary>The player's rotation slot (see <see cref="PlayerStartSlot"/>).</summary>
        public int PlayerSlot=>Station ? StationSlot : _playerSlot<0 ? PlayerStartSlot : _playerSlot;
        public bool PlayerFrontRow=>PlayerSlot<=2;
        static readonly string[] SlotNames={"FRONT RIGHT","FRONT MIDDLE","FRONT LEFT","BACK LEFT","BACK MIDDLE","BACK RIGHT"};
        public string PositionName=>SlotNames[PlayerSlot];
        MatchActor Me=>ActorAt(Team.Us,PlayerSlot);
        public PlayerTouch CurrentPlayerTouch=>MatchEngine ? AwaitedTouch
            : IsPlayerSpike ? PlayerTouch.Spike : IsOpponentSpike ? PlayerTouch.Block : PlayerTouch.None;
        public string BannerTitle
        {
            get
            {
                if(!MatchEngine)return CurrentTurn==RallyTurn.Spike ? "SPIKE TURN" : "BLOCK TURN";
                switch(_roleTouch)
                {
                    case PlayerTouch.Spike: return "ATTACK";
                    case PlayerTouch.Block: return "BLOCK";
                    case PlayerTouch.Receive: return "RECEIVE";
                    case PlayerTouch.Serve: return "SERVE";
                    case PlayerTouch.Set: return "SET";
                }
                return "MATCH";
            }
        }
        public string ModeLabel=>Mode==RallyMode.Set ? "SET STATION  /  FIRST TO "+MatchPoints
            : Mode==RallyMode.Pass ? "PASS STATION  /  FIRST TO "+MatchPoints
            : Mode==RallyMode.Match ? "LEVEL "+((int)PlayerInvolvement+1)+"  /  YOU: "+PositionName
            : "FIRST TO "+MatchPoints+"  /  "+(Mode==RallyMode.Alternate ? "ALTERNATING" : Mode==RallyMode.SpikeOnly ? "SPIKE ONLY" : "BLOCK ONLY");

        public static VolleyOpponentPrototype CreateTeammate(VolleyOpponentPrototype template,int index,Color jersey,Color shorts)
        {
            var mate=Instantiate(template);mate.name="Rally teammate "+(index+1);
            ConfigureActor(mate,VolleyOpponentPrototype.MotionRole.Dig,jersey,shorts);
            return mate;
        }
        /// <summary>Menu: which touches the player makes. Restarts the match so the new level applies from a serve.</summary>
        public void SetInvolvement(Involvement level)
        {
            PlayerInvolvement=level;
            SetMode(Mode);
        }

        bool Involved(PlayerTouch touch)
        {
            if(Station)return touch==(Mode==RallyMode.Set ? PlayerTouch.Set : PlayerTouch.Receive);
            switch(touch)
            {
                case PlayerTouch.Spike: case PlayerTouch.Block: return true;
                case PlayerTouch.Receive: case PlayerTouch.Serve: return PlayerInvolvement>=Involvement.AddReceiveServe;
                case PlayerTouch.Set: return PlayerInvolvement>=Involvement.AddSet;
            }
            return false;
        }
        static Team Other(Team team)=>team==Team.Us ? Team.Them : Team.Us;
        static Vector3 Flat(Vector3 v)=>new Vector3(v.x,0f,v.z);
        float FloorY=>Drill.CourtFrame.position.y;
        float NetTop=>Drill.ReceiveNet!=null ? Drill.ReceiveNet.bounds.max.y : 2.43f;
        Vector3 TeamForward(Team team)=>team==Team.Us ? Forward : -Forward;
        Vector3 TeamRight(Team team)=>team==Team.Us ? Right : -Right;
        Quaternion TeamFacing(Team team)=>Quaternion.LookRotation(TeamForward(team),Vector3.up);
        Vector3 TeamPoint(Team team,float lateral,float depth)=>NetCentre+TeamRight(team)*lateral-TeamForward(team)*depth;
        Team SideOf(Vector3 position)=>Vector3.Dot(position-NetCentre,Forward)>0f ? Team.Them : Team.Us;
        float Lateral(Vector3 position)=>Vector3.Dot(position-NetCentre,Right);
        MatchActor ActorAt(Team team,int slot)
        {
            foreach(var actor in _cast)if(actor.Team==team && actor.Slot==slot)return actor;
            return null;
        }
        MatchActor Find(VolleyOpponentPrototype prototype)
        {
            foreach(var actor in _cast)if(actor.A==prototype)return actor;
            return null;
        }
        bool InsideCourt(Vector3 position,float margin)
        {
            var local=position-NetCentre;
            return Mathf.Abs(Vector3.Dot(local,Right))<=CourtHalfWidth+margin && Mathf.Abs(Vector3.Dot(local,Forward))<=CourtDepth+margin;
        }
        Vector3 ClampToCourt(Vector3 position,Team team)
        {
            float lateral=Mathf.Clamp(Vector3.Dot(position-NetCentre,TeamRight(team)),-CourtHalfWidth+.4f,CourtHalfWidth-.4f);
            float depth=Mathf.Clamp(-Vector3.Dot(position-NetCentre,TeamForward(team)),.8f,CourtDepth-.4f);
            return TeamPoint(team,lateral,depth);
        }

        void EnsureMatchCast()
        {
            if(_cast.Count>0)return;
            void Add(VolleyOpponentPrototype actor,Team team,int slot)
            {
                if(actor==null)return;
                _cast.Add(new MatchActor{A=actor,Team=team,Slot=slot,Home=actor.transform.position,Target=actor.transform.position,Facing=TeamFacing(team),Speed=NpcRunSpeed});
            }
            Add(Ally,Team.Us,0);
            if(Teammates!=null)for(int i=0;i<Teammates.Length && i<5;i++)Add(Teammates[i],Team.Us,i+1);
            if(Blockers!=null)for(int i=0;i<Blockers.Length && i<3;i++)Add(Blockers[i],Team.Them,i);
            // Defenders are ordered by world x; the opponent's own right is world -x, so Defenders[0] serves.
            if(Defenders!=null)for(int i=0;i<Defenders.Length && i<3;i++)Add(Defenders[i],Team.Them,5-i);
        }

        bool TickMatch(float dt,bool ballInPlay,out Vector3 start,out Vector3 destination,out float seconds)
        {
            start=destination=Vector3.zero;seconds=0f;
            EnsureMatchCast();
            if(_restoreScale && !_slowActive && Time.timeScale>0f){Time.timeScale=1f;_restoreScale=false;}
            float real=_slowActive && Time.timeScale>0f ? dt/Time.timeScale : dt;
            TickEmbody(real);
            TickActors(dt);
            switch(Phase)
            {
                case RallyPhase.Idle:
                    if(!ballInPlay && !_embodying)StartRally();
                    return false;
                case RallyPhase.Recovery:
                    _rallyTimer+=dt;
                    if(_rallyTimer>=RallyResetSeconds)EndRally();
                    return false;
                case RallyPhase.MatchOver:
                    _rallyTimer+=dt;
                    if(!HoldAtMatchEnd && _rallyTimer>=MatchEndSeconds)ResetMatch();
                    return false;
                case RallyPhase.PlayerSpike:
                    _rallyTimer+=dt;
                    if(BeginnerBlock && !_spikeJumpIssued && _rallyTimer>=SpikeJumpTime)_spikeJumpIssued=Jump.TryStartAutomaticJump();
                    TickApproach();
                    break;
            }
            if(_serveTimer>=0f && !_embodying)
            {
                _serveTimer=Mathf.Max(0f,_serveTimer-dt);
                // The others walk to their rotation spots between rallies; serve once they are there.
                if(_serveTimer<=0f && (FormationSettled() || (_serveWait+=dt)>3f)){_serveTimer=-1f;BeginServe();}
            }
            TickSegment(dt);
            // A launch toward the player waits until the view is back, so its target uses the new stance.
            if(_launchPending && !_embodying && !ballInPlay && _preparedBall!=null)
            {
                _launchPending=false;
                var launch=_launchCompute();
                start=launch.from;destination=launch.to;seconds=launch.seconds;
                Phase=_launchPhase;AwaitedTouch=_launchTouch;
                BeginBallInPlay(start);_rallyTimer=0f;
                var after=_onLaunched;_onLaunched=null;after?.Invoke();
                return true;
            }
            return false;
        }

        // ---------- rally lifecycle ----------
        void StartRally()
        {
            _touches[0]=_touches[1]=0;_lastTouchTeam=_serving;
            ClearIntercept();_segTime=-1f;_launchPending=false;AwaitedTouch=PlayerTouch.None;_flightTop=float.MinValue;
            _plannedAttacker=null;_playerServes=false;
            if(Opponent!=null)Opponent.gameObject.SetActive(false);
            if(TossLauncher!=null && Application.isPlaying)TossLauncher.gameObject.SetActive(false);
            RandomizeFormation();
            if(Station){StartStationRep();return;}
            var server=Server(_serving);
            var me=Me;
            if(server==null || me==null)return;
            server.Home=server.Target=TeamPoint(_serving,SlotBase[server.Slot].x,CourtDepth+.7f);
            // The player waits at their own rotation spot; the touch only labels what comes first.
            PlayerTouch touch;
            if(server==me){touch=PlayerTouch.Serve;_playerServes=true;}
            else if(_serving==Team.Them && Involved(PlayerTouch.Receive))touch=PlayerTouch.Receive;
            else if(_serving==Team.Us && PlayerFrontRow)touch=PlayerTouch.Block;
            else touch=PlayerTouch.Spike;
            EmbodyRequest(me.Home,touch,true);
            Phase=RallyPhase.MatchPlay;_serveTimer=ServeDelaySeconds;_serveWait=0f;
            Cue=(_serving==Team.Us ? "YOUR TEAM SERVES" : "OPPONENT SERVE")+"  /  YOU: "+PositionName;
        }
        /// <summary>The back-right player serves; at level 1 a player in that spot hands the serve to the back middle.</summary>
        MatchActor Server(Team team)
        {
            var server=ActorAt(team,5);
            if(team==Team.Us && server!=null && server==Me && !Involved(PlayerTouch.Serve))server=ActorAt(team,4);
            return server;
        }
        /// <summary>Side-out: the player moves one spot clockwise (front right -> back right -> back middle -> ... -> front middle).</summary>
        void RotatePlayer(){_playerSlot=(PlayerSlot+5)%6;}
        bool FormationSettled()
        {
            foreach(var actor in _cast)
                if(actor!=_embodied && actor.A.gameObject.activeSelf && Flat(actor.Target-actor.A.transform.position).magnitude>.3f)return false;
            return true;
        }
        void EndRally()
        {
            Drill.RetireRallyBall();
            ReturnScriptedBall();
            if(PlayerScore>=MatchPoints || OpponentScore>=MatchPoints)
            {
                PlayerWonMatch=PlayerScore>OpponentScore;
                Phase=RallyPhase.MatchOver;_rallyTimer=0f;
                Cue=(PlayerWonMatch ? "YOU WIN " : "YOU LOSE ")+PlayerScore+" - "+OpponentScore;
                MatchEnded?.Invoke(PlayerWonMatch);
            }
            else Phase=RallyPhase.Idle;
        }
        void CancelMatch()
        {
            ReturnScriptedBall();
            _launchPending=false;_serveTimer=-1f;_segArrive=null;_segProgress=null;_afterEmbody=null;
            ClearIntercept();DisarmTheirBlock();EnableReceiveVolume(false);
            _embodying=false;FadeAlpha=0f;VignetteAlpha=0f;SetSlowMotion(false);ShowPending();ShowRings(false);HideTarget(3);
            AwaitedTouch=PlayerTouch.None;
        }
        void RandomizeFormation()
        {
            foreach(var actor in _cast)
            {
                var slot=SlotBase[actor.Slot];
                float jitter=actor.Team==Team.Them ? FormationJitter : .2f;
                actor.Home=TeamPoint(actor.Team,slot.x+Random.Range(-jitter,jitter),Mathf.Max(.9f,slot.y+Random.Range(-jitter,jitter)));
                actor.Target=actor.Home;actor.Facing=TeamFacing(actor.Team);
                actor.Anim=Anim.Ready;actor.Clock=-1f;actor.Delay=0f;actor.Speed=NpcRunSpeed;
            }
        }
        void ReturnScriptedBall()
        {
            _segTime=-1f;
            var ball=TakePreparedBall();
            if(ball!=null && Drill!=null && Drill.Pool!=null)Drill.Pool.Return(ball,"match rally over");
        }

        // ---------- moving the player: slow-motion glide behind a comfort vignette (the view never blanks) ----------
        void EmbodyRequest(Vector3 head,PlayerTouch touch,bool placeFormation=false,System.Action after=null)
        {
            var me=Me;
            if(me==null || Jump==null || Jump.Floor==null)return;
            if(_embodied!=me)
            {
                // A rotation hands the old spot back to its mannequin once the player has left it.
                ShowPending();_pendingShow=_embodied;
                _embodied=me;
            }
            me.A.gameObject.SetActive(false);
            if(placeFormation)PlaceFormation();
            var delta=Vector3.ProjectOnPlane(head-GroundedEye,Vector3.up);
            if(!_embodying && delta.magnitude<(Station ? .6f : .25f)){ShowPending();ConfigureEmbodiment(touch);after?.Invoke();return;}
            _embodyTouch=touch;_afterEmbody+=after;
            _glideFrom=Jump.Floor.StanceOffset;_glideDelta=delta;
            _glideSeconds=Mathf.Clamp(delta.magnitude/GlideSpeed,GlideMinSeconds,GlideMaxSeconds);
            // Already narrow: re-aim from here without closing the vignette again.
            _embodyClock=_embodying ? VignetteInSeconds : 0f;
            if(!_embodying){_embodying=true;SetSlowMotion(true);}
        }
        void TickEmbody(float real)
        {
            if(!_embodying){VignetteAlpha=Mathf.MoveTowards(VignetteAlpha,0f,real/VignetteOutSeconds);return;}
            _embodyClock+=real;
            VignetteAlpha=Mathf.MoveTowards(VignetteAlpha,1f,real/VignetteInSeconds);
            float u=Mathf.Clamp01((_embodyClock-VignetteInSeconds)/_glideSeconds);
            Jump.Floor.SetStanceOffset(_glideFrom+_glideDelta*Mathf.SmoothStep(0f,1f,u));
            if(u<1f)return;
            _embodying=false;SetSlowMotion(false);ShowPending();
            ConfigureEmbodiment(_embodyTouch);
            var after=_afterEmbody;_afterEmbody=null;after?.Invoke();
        }
        /// <summary>Everyone walks to this rally's spot; mannequins not on court yet appear there.</summary>
        void PlaceFormation()
        {
            foreach(var actor in _cast)
            {
                actor.Target=actor.Home;actor.Anim=Anim.Ready;actor.Clock=-1f;actor.Delay=0f;actor.Speed=NpcRunSpeed;
                actor.A.Role=actor.Slot<=2 ? VolleyOpponentPrototype.MotionRole.Block : VolleyOpponentPrototype.MotionRole.Dig;
                Act(actor.A,0f);SetSolid(actor.A,false);
                if(actor==_embodied || actor==_pendingShow || actor.A.gameObject.activeSelf)continue;
                actor.A.gameObject.SetActive(true);
                actor.A.transform.SetPositionAndRotation(actor.Home,actor.Facing);
            }
        }
        void ShowPending()
        {
            var actor=_pendingShow;_pendingShow=null;
            if(actor==null || actor==_embodied)return;
            actor.A.gameObject.SetActive(true);
            actor.A.transform.SetPositionAndRotation(actor.Home,actor.Facing);
            actor.Target=actor.Home;Act(actor.A,0f);SetSolid(actor.A,false);
        }
        void ConfigureEmbodiment(PlayerTouch touch)
        {
            bool spike=touch==PlayerTouch.Spike;
            Jump.JumpHeight=spike ? SpikeJumpHeight : BlockJumpHeight;
            Jump.Duration=spike ? SpikeJumpSeconds : BlockJumpSeconds;
            bool announce=_roleTouch!=touch || !TurnStarted;
            _roleTouch=touch;CurrentTurn=spike ? RallyTurn.Spike : RallyTurn.Block;TurnStarted=true;
            _approachBase=Jump.Floor.StanceOffset;
            EnableReceiveVolume(touch==PlayerTouch.Receive);
            if(announce)TurnAnnounced?.Invoke(CurrentTurn);
        }
        void SetSlowMotion(bool on)
        {
            if(on==_slowActive)return;
            _slowActive=on;
            // Paused by the menu or lost tracking: the drill restores its saved scale later, so fix it on resume.
            if(Time.timeScale<=0f){_restoreScale=!on;return;}
            Time.timeScale=on ? SlowMotionScale : 1f;
        }
        void EnableReceiveVolume(bool on)
        {
            if(ReceiveVolume==null || Drill==null)return;
            ReceiveVolume.gameObject.SetActive(on);ReceiveVolume.enabled=on;
            if(ReceiveVolume.Volume!=null)ReceiveVolume.Volume.gameObject.SetActive(on);
            Drill.JoinedHands=on ? ReceiveVolume : null;
            if(on)return;
            // Joined sampling disables the individual hand boxes; hand them back.
            if(Drill.Left!=null && Drill.Left.ContactVolume!=null)Drill.Left.ContactVolume.enabled=true;
            if(Drill.Right!=null && Drill.Right.ContactVolume!=null)Drill.Right.ContactVolume.enabled=true;
        }

        // ---------- choosing players and stances ----------
        MatchActor ChooseSetter(Team team)
        {
            var setter=ActorAt(team,0);
            return setter!=null && setter!=_embodied ? setter : ActorAt(team,1);
        }
        /// <summary>
        /// Our sets go to the player whenever the player is not the setter (a back-row player hits from behind the 3 m line).
        /// Otherwise outside hitter 60%, middle 40%; a push to the left (player set) picks the outside.
        /// </summary>
        MatchActor ChooseAttacker(Team team,MatchActor setter,Vector3 hint)
        {
            if(team==Team.Us && Involved(PlayerTouch.Spike) && _embodied!=null && _embodied!=setter)return _embodied;
            var outside=ActorAt(team,2);var middle=ActorAt(team,1);
            if(outside==_embodied)outside=ActorAt(team,0);
            if(middle==_embodied)middle=ActorAt(team,0);
            MatchActor pick;
            if(hint.sqrMagnitude>.01f)pick=Vector3.Dot(hint,TeamRight(team))<0f ? outside : middle;
            else pick=Random.value<.6f ? outside : middle;
            if(pick==setter)pick=pick==outside ? middle : outside;
            return pick;
        }
        MatchActor ChooseReceiver(Team team)
        {
            for(int tries=0;tries<12;tries++)
            {
                var actor=ActorAt(team,3+Random.Range(0,3));
                if(actor!=null && actor!=_embodied)return actor;
            }
            return ActorAt(team,4);
        }
        Vector3 AttackSpot(Team team,MatchActor attacker)=>TeamPoint(team,SlotBase[attacker.Slot].x+Random.Range(-.5f,.5f),1f);
        /// <summary>Approach start for our hitter; a back-row hitter takes off behind the 3 m line.</summary>
        Vector3 AttackHead(MatchActor attacker)=>TeamPoint(Team.Us,Mathf.Clamp(SlotBase[attacker.Slot].x,-3.2f,3.2f),
            attacker.Slot>2 ? BackRowAttackDepth : SpikeStandBackDistance+.3f);
        Vector3 BlockHead(float worldLateral)=>NetCentre+Right*Mathf.Clamp(worldLateral,-3.5f,3.5f)-Forward*BlockStandDistance;
        Vector3 SetHead(Vector3 spot)=>spot-Forward*.25f;

        // ---------- scripted segments and launches ----------
        void StartSegment(Vector3 from,Vector3 to,float seconds,System.Action arrive,System.Action<float> progress=null)
        {
            EnsureScriptedBall(from);
            _segFrom=from;_segTo=to;_segDuration=Mathf.Max(.05f,seconds);_segTime=0f;_segArrive=arrive;_segProgress=progress;
            Phase=RallyPhase.MatchPlay;AwaitedTouch=PlayerTouch.None;
            PlaceScriptedBall(from);
        }
        void TickSegment(float dt)
        {
            if(_segTime<0f || Phase!=RallyPhase.MatchPlay)return;
            _segTime=Mathf.Min(_segDuration,_segTime+dt);
            PlaceScriptedBall(TossPosition(_segFrom,_segTo,_segTime,_segDuration));
            _segProgress?.Invoke(_segTime/_segDuration);
            if(_segTime<_segDuration)return;
            _segTime=-1f;var arrive=_segArrive;_segArrive=null;_segProgress=null;arrive?.Invoke();
        }
        void EnsureScriptedBall(Vector3 at)
        {
            if(_preparedBall!=null)return;
            var ball=Drill.ReleaseActiveBall();
            if(ball!=null){AdoptBall(ball);PlaceScriptedBall(at);return;}
            StageBall(at);
        }
        /// <summary>A physics ball becomes a scripted, collision-free ball with no lifetime clock.</summary>
        void AdoptBall(Ball ball)
        {
            ball.ResetToIdle();ball.gameObject.SetActive(true);
            ball.Body.isKinematic=true;ball.Body.detectCollisions=false;
            _preparedBall=ball;
        }
        void PlaceScriptedBall(Vector3 position)
        {
            if(_preparedBall==null)return;
            _preparedBall.transform.position=position;_preparedBall.Body.position=position;
        }
        void RequestLaunch(System.Func<(Vector3 from,Vector3 to,float seconds)> compute,RallyPhase phase,PlayerTouch touch,System.Action onLaunched=null)
        {
            _launchCompute=compute;_launchPhase=phase;_launchTouch=touch;_onLaunched=onLaunched;_launchPending=true;
            _segTime=-1f; // the ball waits in the hands until the launch
        }
        /// <summary>Flight time an arc needs to pass the tape with this clearance (0.3 s when the net is not crossed).</summary>
        float ClearNetSeconds(Vector3 from,Vector3 to,float clearance)
        {
            float a=Vector3.Dot(from-NetCentre,Forward),b=Vector3.Dot(to-NetCentre,Forward);
            if(a*b>=0f)return .3f;
            float fraction=a/(a-b);
            float need=NetTop+clearance-Mathf.Lerp(from.y,to.y,fraction);
            if(need<=0f)return .3f;
            return Mathf.Sqrt(2f*need/(Mathf.Abs(Physics.gravity.y)*fraction*(1f-fraction)));
        }

        // ---------- serve ----------
        void BeginServe()
        {
            var server=Server(_serving);
            if(_playerServes)
            {
                // Automatic toss in front of the player; the swing decides the serve.
                EnsureScriptedBall(GroundedEye+Forward*.35f-Vector3.up*.45f);
                RequestLaunch(()=>{var head=GroundedEye;return (head+Forward*.35f-Vector3.up*.45f,head+Forward*.45f+Vector3.up*.55f,.45f);},
                    RallyPhase.PlayerServe,PlayerTouch.Serve);
                Cue="SERVE — THE TOSS IS AUTOMATIC, HIT IT OVER";
                return;
            }
            server.A.Role=VolleyOpponentPrototype.MotionRole.Spike;server.Anim=Anim.Spike;server.Clock=-1f;server.Phase=0f;
            var release=server.A.ReleasePositionAt(server.Home,server.Facing);
            var hand=server.Home+Vector3.up*1.2f+TeamForward(_serving)*.3f;
            StartSegment(hand,release,WindupSeconds,()=>NpcServeContact(server,release),p=>server.Phase=p*.52f);
        }
        void NpcServeContact(MatchActor server,Vector3 release)
        {
            server.Clock=0f;_lastTouchTeam=server.Team;PlaySpikeAudio(release);
            // After the serve the server walks back into the court.
            server.Home=TeamPoint(server.Team,SlotBase[server.Slot].x,SlotBase[server.Slot].y);server.Target=server.Home;
            var receiving=Other(server.Team);
            if(receiving==Team.Us && _roleTouch==PlayerTouch.Receive && _embodied!=null)
            {
                LaunchToForearms(release,ServeSeconds,0f);
                Cue="RECEIVE — FOREARMS TOGETHER";
                return;
            }
            var receiver=ChooseReceiver(receiving);
            var target=ClampToCourt(receiver.Home+TeamRight(receiving)*Random.Range(-1f,1f)+TeamForward(receiving)*Random.Range(-1f,1f),receiving);
            var point=target+Vector3.up*(DigHeight+.1f);
            receiver.Target=target-TeamForward(receiving)*.3f;receiver.Speed=NpcRunSpeed;
            // TOSS: our receiver always gets the pass up to the player.
            bool ok=Mode==RallyMode.Set || Random.value<(receiving==Team.Them ? TheirReceiveSuccess : OurReceiveSuccess);
            StartSegment(release,point,Mathf.Max(ServeSeconds,ClearNetSeconds(release,point,.3f)),()=>{
                if(ok){NpcTouch(receiving,receiver,point);return;}
                // Shanked: it deflects off the platform and dies, the serving team scores either way.
                receiver.Anim=Anim.Dig;receiver.Clock=0f;_lastTouchTeam=receiving;
                var away=Random.insideUnitCircle.normalized*2f;
                var land=point+new Vector3(away.x,0f,away.y)-TeamForward(receiving);land.y=FloorY;
                StartSegment(point,land,.6f,()=>ResolveMatch(land));
            });
            Cue=receiving==Team.Us ? "OPPONENT SERVE" : "YOUR TEAM SERVES";
        }

        // ---------- NPC touches ----------
        /// <summary>An NPC plays the ball. First touch passes to the setter, second sets, a third goes over as a free ball.</summary>
        void NpcTouch(Team team,MatchActor actor,Vector3 point)
        {
            _touches[(int)team]++;_touches[(int)Other(team)]=0;_lastTouchTeam=team;
            PlaySpikeAudio(point);
            int touches=_touches[(int)team];
            if(touches==1){actor.Anim=Anim.Dig;actor.Clock=0f;PassToSetter(team,point);}
            else if(touches==2)SetFromActor(team,actor,point);
            else{actor.Anim=Anim.Dig;actor.Clock=0f;FreeBall(team,point);}
        }
        void PassToSetter(Team team,Vector3 from)
        {
            if(team==Team.Us && Mode==RallyMode.Set){StationPassToPlayer(from);return;}
            var setter=ChooseSetter(team);
            var attacker=_plannedAttacker!=null && _plannedAttacker.Team==team && _plannedAttacker!=setter ? _plannedAttacker : ChooseAttacker(team,setter,Vector3.zero);
            _plannedAttacker=attacker;
            attacker.Target=AttackSpot(team,attacker);attacker.Delay=0f;attacker.Speed=NpcRunSpeed;
            var spot=TeamPoint(team,1f,.8f);
            if(team==Team.Them)RequestBlock(Lateral(attacker.Target));
            if(team==Team.Us && Involved(PlayerTouch.Set))
            {
                // Level 3: a teammate's pass flies (physically) to the player, who glides in as the setter.
                EmbodyRequest(SetHead(spot),PlayerTouch.Set);
                RequestLaunch(()=>{var head=GroundedEye;return (from,head+Forward*.25f+Vector3.up*.3f,PassSeconds);},RallyPhase.PlayerSet,PlayerTouch.Set);
                Cue="SET — PUSH IT UP, LEFT FOR THE OUTSIDE";
                return;
            }
            if(team==Team.Us && attacker==_embodied)EmbodyRequest(AttackHead(attacker),PlayerTouch.Spike);
            setter.Target=spot;setter.Speed=SetterSpeed;setter.Delay=0f;
            var toward=Flat(attacker.Target-spot);
            setter.Facing=toward.sqrMagnitude>.01f ? Quaternion.LookRotation(toward.normalized,Vector3.up) : TeamFacing(team);
            setter.A.Role=VolleyOpponentPrototype.MotionRole.Set;setter.Anim=Anim.Set;setter.Clock=-1f;setter.Phase=0f;
            var release=setter.A.ReleasePositionAt(spot,setter.Facing);
            StartSegment(from,release,PassSeconds,()=>SetFromActor(team,setter,release),p=>setter.Phase=p*VolleyOpponentPrototype.SetContactPhase);
            Cue=team==Team.Us ? "GOOD PASS" : "OPPONENT PASS — WATCH THEIR HITTER";
        }
        void SetFromActor(Team team,MatchActor setter,Vector3 release)
        {
            _touches[(int)team]=2;_lastTouchTeam=team;
            setter.A.Role=VolleyOpponentPrototype.MotionRole.Set;setter.Anim=Anim.Set;setter.Clock=0f;
            var attacker=_plannedAttacker!=null && _plannedAttacker.Team==team && _plannedAttacker!=setter ? _plannedAttacker : ChooseAttacker(team,setter,Vector3.zero);
            _plannedAttacker=null;
            // A hitter not already sent to the net (the set came from a scramble) runs in now.
            if(-Vector3.Dot(attacker.Target-NetCentre,TeamForward(team))>2f){attacker.Target=AttackSpot(team,attacker);attacker.Delay=0f;attacker.Speed=NpcRunSpeed;}
            if(team==Team.Us && _embodied==attacker)
            {
                // Set to the player: a physical flight to the spike contact while the opposing front row blocks.
                RequestLaunch(()=>{
                    _allyLateral=Random.Range(-AllySetLateralSpread,AllySetLateralSpread);
                    PlanSpike(Lateral(GroundedEye)+_allyLateral);
                    return (release,SpikeContactPoint(_allyLateral),AllyFlightSeconds);
                },RallyPhase.PlayerSpike,PlayerTouch.Spike,()=>{
                    SpikeJumpTime=Mathf.Max(0f,AllyFlightSeconds-Jump.Duration*.5f+SpikeJumpPeakOffset);_spikeJumpIssued=false;
                    ArmTheirBlock(AllyFlightSeconds);
                });
                Cue=BeginnerBlock ? "AUTO JUMP — SPIKE PAST THE BLOCK" : "JUMP: RAISE BOTH HANDS — SPIKE";
                return;
            }
            // NPC hitter: turns toward the shot for the whole wind-up (the tell), then hits.
            if(team==Team.Them && !PlayerBlocking)RequestBlock(Lateral(attacker.Target));
            bool playerBlocks=team==Team.Them && PlayerBlocking;
            _attackYaw=Random.Range(-AttackBodyYaw,AttackBodyYaw);
            attacker.Facing=TeamFacing(team)*Quaternion.Euler(0f,_attackYaw,0f);
            attacker.A.Role=VolleyOpponentPrototype.MotionRole.Spike;attacker.Anim=Anim.Spike;attacker.Clock=-1f;attacker.Phase=0f;
            var hit=attacker.A.ReleasePositionAt(attacker.Target,attacker.Facing);
            _autoJumpIssued=false;
            float lead=AutoJumpLeadSeconds+Random.Range(-AutoJumpTimingJitter,AutoJumpTimingJitter);
            _autoJumpAt=Mathf.Max(0f,WindupSeconds-Mathf.Clamp(lead,0f,Jump.Duration*.75f));
            ArmBlock(Other(team),Lateral(attacker.Target),WindupSeconds);
            StartSegment(release,hit,WindupSeconds,()=>AttackContact(team,attacker,hit),p=>{
                attacker.Phase=p*.52f;
                if(playerBlocks && BeginnerBlock && !_autoJumpIssued && p*WindupSeconds>=_autoJumpAt)_autoJumpIssued=Jump.TryStartAutomaticJump();
            });
            Cue=playerBlocks ? "READ THE HITTER — BLOCK" : team==Team.Us ? "OUR ATTACK" : "OPPONENT ATTACK";
        }
        void AttackContact(Team team,MatchActor attacker,Vector3 hit)
        {
            attacker.Clock=0f;_touches[(int)team]=3;_lastTouchTeam=team;PlaySpikeAudio(hit);
            var defending=Other(team);
            if(Mode==RallyMode.Pass && defending==Team.Us)
            {
                // Their counter-attack goes at the player, lower and faster than a serve: a poor pass makes it harder.
                // After a free ball they had every option: a harder, lower attack than after a dug spike.
                LaunchToForearms(hit,0f,_counterStrong ? TheirStrongAttackSpeed : TheirAttackSpeed,-(_counterStrong ? TheirStrongAttackDrop : TheirAttackDrop));
                Cue=_counterStrong ? "THEY ATTACK HARD — DIG IT UP!" : "THEY ATTACK — DIG IT UP!";
                _counterStrong=false;
                return;
            }
            if(defending==Team.Us && PlayerBlocking)
            {
                RequestLaunch(()=>{
                    LastWasFaceShot=Random.value<FaceShotChance;
                    var to=Destination(AttackLateral(hit),Random.Range(-HeightSpread,HeightSpread),LastWasFaceShot);
                    return (hit,to,SolveBlockShot(hit,ref to));
                },RallyPhase.OpponentSpike,PlayerTouch.Block);
                Cue="BLOCK!";
                return;
            }
            NpcAttack(team,hit);
        }
        /// <summary>The player blocks their attack (front row only), or is gliding into the block.</summary>
        bool PlayerBlocking=>_embodied!=null && PlayerFrontRow && Involved(PlayerTouch.Block)
            && (_embodying ? _embodyTouch==PlayerTouch.Block : _roleTouch==PlayerTouch.Block);
        /// <summary>A front-row player shifts along the net toward the attack, at most <see cref="PlayerBlockShift"/> from their spot.</summary>
        void RequestBlock(float worldLateral)
        {
            if(_embodied==null || !PlayerFrontRow || !Involved(PlayerTouch.Block))return;
            float own=Lateral(_embodied.Home);
            EmbodyRequest(BlockHead(Mathf.Clamp(worldLateral,own-PlayerBlockShift,own+PlayerBlockShift)),PlayerTouch.Block);
        }
        void NpcAttack(Team team,Vector3 hit)
        {
            if(Station && team==Team.Us){StationAttack(hit);return;}
            var defending=Other(team);
            float roll=Random.value;
            var tape=NetCentre+Right*Lateral(hit)+Vector3.up*(NetTop+.25f);
            if(roll<NpcBlockStuff)
            {
                // Stuffed straight down on the attacking side.
                var land=TeamPoint(team,Random.Range(-2f,2f),Random.Range(.6f,2.2f));
                _lastTouchTeam=defending;
                StartSegment(hit,tape,.15f,()=>{PlaySpikeAudio(tape);StartSegment(tape,land,.55f,()=>ResolveMatch(land));});
                Cue="STUFF BLOCK";
                return;
            }
            if(roll<NpcBlockStuff+NpcBlockTouch)
            {
                // Soft block touch: it pops up on the blocking side and is easy to dig (a block is not a team touch).
                _touches[(int)defending]=0;_lastTouchTeam=defending;
                var pop=ClampToCourt(TeamPoint(defending,Random.Range(-3f,3f),Random.Range(2f,5f)),defending);
                StartSegment(hit,tape,.15f,()=>{PlaySpikeAudio(tape);Defend(defending,tape,pop,1.3f,true);});
                return;
            }
            var target=TeamPoint(defending,Random.Range(-3.8f,3.8f),Random.Range(1.5f,8.2f));
            float seconds=Mathf.Max(Flat(target-hit).magnitude/NpcAttackSpeed,ClearNetSeconds(hit,target,.2f));
            Defend(defending,hit,target,seconds,false);
        }
        /// <summary>Scripted flight into <paramref name="team"/>'s court: the defender who can get there in time digs it, or it lands.</summary>
        void Defend(Team team,Vector3 from,Vector3 target,float seconds,bool soft)
        {
            target.y=FloorY;
            var dig=target+Vector3.up*DigHeight;
            bool them=team==Team.Them;
            float react=them ? TheirDigReaction : OurDigReaction, speed=them ? TheirDigSpeed : OurDigSpeed, reach=them ? TheirDigReach : OurDigReach;
            MatchActor best=null;float bestNeed=float.MaxValue;
            foreach(var actor in _cast)
            {
                if(actor.Team!=team || actor==_embodied || !actor.A.gameObject.activeSelf || actor.Anim==Anim.Block)continue;
                float need=react+Mathf.Max(0f,Flat(target-actor.A.transform.position).magnitude-reach)/speed;
                if(need<bestNeed){bestNeed=need;best=actor;}
            }
            float arrive=seconds*.92f;
            bool reachable=best!=null && bestNeed<=arrive;
            // TOSS: our back row always digs their counter-attack up to the player.
            bool ok=reachable && (Mode==RallyMode.Set && !them || Random.value<(soft ? .97f : them ? TheirDigSuccess : OurDigSuccess));
            if(reachable){best.Target=target-TeamForward(team)*.25f;best.Speed=speed;best.Delay=react;}
            if(ok)StartSegment(from,dig,arrive,()=>NpcTouch(team,best,dig));
            else StartSegment(from,target,seconds,()=>{if(reachable){best.Anim=Anim.Dig;best.Clock=0f;}ResolveMatch(target);});
        }
        void FreeBall(Team team,Vector3 from)
        {
            var other=Other(team);
            var target=ClampToCourt(TeamPoint(other,Random.Range(-3f,3f),Random.Range(3f,6.5f)),other);
            Defend(other,from,target,Mathf.Max(1.4f,ClearNetSeconds(from,target+Vector3.up*DigHeight,.4f)),true);
            Cue=team==Team.Us ? "FREE BALL OVER" : "FREE BALL";
        }
        /// <summary>Front-row NPCs near the attack close to the net and jump so they peak as the ball crosses (visual; not solid).</summary>
        void ArmBlock(Team defending,float worldLateral,float timeToHit)
        {
            foreach(var actor in _cast)
            {
                if(actor.Team!=defending || actor.Slot>2 || actor==_embodied)continue;
                float own=Lateral(actor.A.transform.position);
                if(Mathf.Abs(own-worldLateral)>2.6f)continue;
                actor.Target=NetCentre+Right*Mathf.Lerp(own,worldLateral,.6f)-TeamForward(defending)*BlockerNetDistance;
                actor.Anim=Anim.Block;actor.Clock=0f;
                actor.JumpAt=timeToHit+BlockerReactionSeconds-VolleyOpponentPrototype.BlockPeakPhase*BlockerJumpSeconds;
                actor.A.JumpHeight=Random.Range(Mathf.Min(BlockerJumpMin,BlockerJumpMax),Mathf.Max(BlockerJumpMin,BlockerJumpMax));
            }
        }
        /// <summary>Opposing blockers placed by PlanSpike move in, go solid and jump to meet the player's spike.</summary>
        void ArmTheirBlock(float setFlightSeconds)
        {
            if(Blockers==null)return;
            for(int i=0;i<Blockers.Length && i<_blockerActive.Length;i++)
            {
                var actor=Find(Blockers[i]);
                if(actor==null || actor==_embodied || !_blockerActive[i])continue;
                actor.Target=_blockerTargets[i];actor.Anim=Anim.Block;actor.Clock=0f;
                actor.JumpAt=setFlightSeconds+BlockerReactionSeconds+Random.Range(-BlockerTimingJitter,BlockerTimingJitter)
                    -VolleyOpponentPrototype.BlockPeakPhase*BlockerJumpSeconds;
            }
        }
        void DisarmTheirBlock()
        {
            if(Blockers==null)return;
            foreach(var blocker in Blockers)if(blocker!=null)SetSolid(blocker,false);
        }

        // ---------- player touches ----------
        void OnMatchPlayerTouch()
        {
            switch(Phase)
            {
                case RallyPhase.PlayerSpike: _touches[0]=3;_lastTouchTeam=Team.Us;Cue="SPIKE!";break;
                case RallyPhase.OpponentSpike: _touches[1]=0;_lastTouchTeam=Team.Us;Cue="BLOCK TOUCH";break; // a block is not a team touch
                case RallyPhase.PlayerReceive: _touches[0]=1;_touches[1]=0;_lastTouchTeam=Team.Us;Cue="PASS!";break;
                case RallyPhase.PlayerServe: _touches[0]=_touches[1]=0;_lastTouchTeam=Team.Us;Cue="SERVE!";break;
                case RallyPhase.PlayerSet:
                    // The assisted set flies toward the chosen teammate, who takes it over shortly (SampleMatch).
                    _touches[0]=2;_lastTouchTeam=Team.Us;Cue=Station ? "SET!" : "NICE SET";
                    break;
            }
        }
        /// <summary>Serve and set touches use their own models; everything else keeps the drill's contact physics.</summary>
        public bool OverrideTouchVelocity(Vector3 handVelocity,Vector3 ballPosition,out Vector3 velocity)
            =>OverrideTouchVelocity(handVelocity,ballPosition,Vector3.zero,out velocity);
        /// <param name="physical">The rebound the contact physics computed (used by the pass station).</param>
        public bool OverrideTouchVelocity(Vector3 handVelocity,Vector3 ballPosition,Vector3 physical,out Vector3 velocity)
        {
            velocity=default;
            if(!MatchEngine || _playerTouched)return false;
            if(Station && Phase==RallyPhase.PlayerSet){velocity=StationSetVelocity(ballPosition,handVelocity);return true;}
            if(Station && Phase==RallyPhase.PlayerReceive){velocity=StationPassVelocity(ballPosition,physical);return true;}
            if(Phase==RallyPhase.PlayerServe && handVelocity.magnitude>=Drill.SpikeSwingSpeed)
            {
                velocity=VolleyMath.SpikeVelocity(handVelocity,ServePower,ServeMinimumSpeed,ServeMaximumSpeed,ServeMaximumRise);
                return true;
            }
            if(Phase==RallyPhase.PlayerSet){velocity=AssistedSet(ballPosition,handVelocity);return true;}
            return false;
        }
        /// <summary>Level 3 set: the push picks the hitter (left = outside), the ball flies to where that hitter will strike.</summary>
        public Vector3 AssistedSet(Vector3 ball,Vector3 handVelocity)
        {
            var attacker=ChooseAttacker(Team.Us,_embodied,Flat(handVelocity));
            _plannedAttacker=attacker;
            var head=AttackHead(attacker);
            var contact=head+Forward*(SpikeContactForward+SpikeApproachDistance);
            contact.y=GroundedEye.y+SpikeJumpHeight+SpikeReachAboveEye;
            _allyLateral=0f;
            PlanSpike(Lateral(contact));
            return VolleyMath.ServeVelocity(ball,contact,AllyFlightSeconds,Physics.gravity);
        }

        // ---------- physics after a player touch ----------
        bool SampleMatch(Vector3 position,Vector3 velocity,float radius,float step,float damping)
        {
            bool window=Phase==RallyPhase.PlayerSpike || Phase==RallyPhase.OpponentSpike || Phase==RallyPhase.PlayerServe
                || Phase==RallyPhase.PlayerReceive || Phase==RallyPhase.PlayerSet;
            if(!window)return false;
            _sinceTouch+=step;
            // A missed block keeps going into our court, where our defence may still dig it.
            if(Station && Phase==RallyPhase.PlayerSet && _playerTouched)return TrackStationSet(position,velocity,radius,step,damping);
            // RECEIVE: a pass that touches the net is never played on.
            if(Mode==RallyMode.Pass && Phase==RallyPhase.PlayerReceive && _playerTouched && !_passHitNet
                && Mathf.Abs(Vector3.Dot(position-NetCentre,Forward))<radius+.05f && position.y<NetTop+radius*.5f && Mathf.Abs(Lateral(position))<CourtHalfWidth+.25f)
                _passHitNet=true;
            if(Phase==RallyPhase.PlayerSet && _playerTouched)
            {
                if(_sinceTouch>=SetHandOffSeconds)HandSetToHitter(position);
                return false;
            }
            if(_playerTouched || Phase==RallyPhase.OpponentSpike)
            {
                PlanMatchIntercept(position,velocity,damping);
                if(TryMatchTakeOver(position))return false;
            }
            if(DetectLanding(position,velocity,radius,step,out var landing)){_lastBallPosition=landing;ResolveMatch(landing);}
            return false;
        }
        /// <summary>
        /// Predicts where the ball comes down to playing height and commits the NPC who can get there in time. The side it
        /// comes down on decides the team; that team's touch count decides dig (low) or set (high). Re-run every step.
        /// </summary>
        void PlanMatchIntercept(Vector3 position,Vector3 velocity,float damping)
        {
            var p=position;var v=velocity;float t=0f;const float dt=.02f;
            bool found=false;var side=Team.Us;float height=DigHeight;
            _flightTop=Mathf.Max(_flightTop,position.y);float top=_flightTop;
            for(int i=0;i<200;i++)
            {
                side=SideOf(p);height=_touches[(int)side]==1 ? (Station ? SetterHandsHeight : SetHeight) : DigHeight;
                if(v.y<0f && p.y-FloorY<=height){found=true;break;}
                v+=Physics.gravity*dt;v*=Mathf.Clamp01(1f-damping*dt);p+=v*dt;t+=dt;
                top=Mathf.Max(top,p.y);
            }
            if(!found || !InsideCourt(p,.3f)){ClearIntercept();return;}
            int touches=_touches[(int)side];
            if(Mode==RallyMode.Pass && side==Team.Us && touches==1){PlanStationPass(p,t,top);return;}
            // PASS: only our setter plays the player's pass, and only one that rose clearly above the net.
            // Stations play on only what the rally needs: our setter takes the player's pass (risen clearly over the
            // tape), and in RECEIVE a pass that went over is their chance ball - they dig it and attack the player.
            bool overpass=Mode==RallyMode.Pass && side==Team.Them && touches==0;
            if(Station && !overpass && (side!=Team.Us || touches!=1)){ClearIntercept();return;}
            if(Station && !overpass && top<NetTop+PassMinClearance){_passTooLow=true;ClearIntercept();return;}
            if(Station && !overpass)_passTooLow=false;
            bool them=side==Team.Them, setting=touches==1;
            float react=setting ? .1f : them ? TheirDigReaction : OurDigReaction;
            float speed=setting ? SetterSpeed : them ? TheirDigSpeed : OurDigSpeed;
            float reach=setting ? SetterReach : them ? TheirDigReach : OurDigReach;
            float success=setting ? (Station ? 1f : .97f) : them ? TheirDigSuccess : OurDigSuccess;
            if(overpass){react=.2f;speed=6f;reach=1.5f;success=1f;}
            float moving=t-Mathf.Max(0f,react-_sinceTouch);
            // RECEIVE: whichever of our players the pass comes down nearest sets it (never one across the court).
            var setter=setting ? (Mode==RallyMode.Pass ? NearestTeammate(p) : ChooseSetter(side)) : null;
            MatchActor best=null;float bestNeed=float.MaxValue;
            foreach(var actor in _cast)
            {
                if(actor.Team!=side || actor==_embodied || !actor.A.gameObject.activeSelf)continue;
                if(setting && actor!=setter)continue;
                if(actor.Anim==Anim.Block && actor.Clock<actor.JumpAt+BlockerJumpSeconds)continue; // still in the air
                float need=Mathf.Max(0f,Flat(p-actor.A.transform.position).magnitude-reach)/speed;
                if(need<=moving && need<bestNeed){bestNeed=need;best=actor;}
            }
            if(best==null){ClearIntercept();return;}
            if(best!=_icActor)_icSuccess=Random.value<success; // one roll per defender and ball
            _icActor=best;_icTeam=side;_icPoint=new Vector3(p.x,FloorY,p.z);
            _icHeight=height;_icReach=reach;_icReaction=react;_icSpeed=speed;
            _icRootOffset=Vector3.zero;
            if(Station && setting)
            {
                // RECEIVE: the setter stands where its hands (forehead window) meet the ball and its set pose grows as it arrives.
                best.Facing=TeamFacing(side);
                best.A.Role=VolleyOpponentPrototype.MotionRole.Set;best.Anim=Anim.Set;best.Clock=-1f;
                best.Phase=VolleyOpponentPrototype.SetContactPhase*Mathf.Clamp01(1f-t/.8f);
                _icRootOffset=-Flat(best.A.ReleasePositionAt(Vector3.zero,best.Facing));
            }
            // Our setter is committed: make the player the hitter now, so the switch is done before the set leaves.
            if(side==Team.Us && setting && _plannedAttacker==null)
            {
                _plannedAttacker=ChooseAttacker(Team.Us,best,Vector3.zero);
                if(_plannedAttacker==_embodied)EmbodyRequest(AttackHead(_plannedAttacker),PlayerTouch.Spike);
            }
        }
        /// <summary>The player's set becomes scripted: the teammate it was pushed to attacks it.</summary>
        void HandSetToHitter(Vector3 position)
        {
            var ball=Drill.ReleaseActiveBall();
            if(ball==null)return;
            AdoptBall(ball);PlaceScriptedBall(position);
            AwaitedTouch=PlayerTouch.None;Phase=RallyPhase.MatchPlay;
            SetFromActor(Team.Us,_embodied,position);
        }
        bool TryMatchTakeOver(Vector3 position)
        {
            if(_icActor==null || !_icSuccess)return false;
            float height=position.y-FloorY;
            if(height<.15f || height>_icHeight+.6f)return false;
            if(Flat(position-_icActor.A.transform.position).magnitude>_icReach)return false;
            var ball=Drill.ReleaseActiveBall();
            if(ball==null)return false;
            AdoptBall(ball);PlaceScriptedBall(position);
            var actor=_icActor;var team=_icTeam;
            bool stationSet=Mode==RallyMode.Pass && team==Team.Us && _touches[0]==1;
            if(stationSet)JudgeStationPass(position,actor);
            ClearIntercept();DisarmTheirBlock();EnableReceiveVolume(false);
            AwaitedTouch=PlayerTouch.None;Phase=RallyPhase.MatchPlay;
            if(stationSet && _passClass==1){StationFreeBall(actor,position);return true;}
            if(stationSet)
            {
                // The ball settles into the setter's hands before the set leaves them.
                var hands=actor.A.ReleasePositionAt(actor.A.transform.position,actor.A.transform.rotation);
                actor.Phase=VolleyOpponentPrototype.SetContactPhase;
                StartSegment(position,hands,.1f,()=>NpcTouch(team,actor,hands));
                return true;
            }
            NpcTouch(team,actor,position);
            return true;
        }
        void ClearIntercept(){_icActor=null;_icSuccess=false;}
        /// <summary>Volleyball rules: in bounds, the team whose court it lands in loses the point; out, the last toucher loses it.</summary>
        void ResolveMatch(Vector3 landing)
        {
            var local=landing-NetCentre;
            float depth=Vector3.Dot(local,Forward),side=Vector3.Dot(local,Right);
            bool inBounds=Mathf.Abs(side)<=CourtHalfWidth+LineTolerance && Mathf.Abs(depth)<=CourtDepth+LineTolerance;
            var court=depth>0f ? Team.Them : Team.Us;
            var winner=inBounds ? Other(court) : Other(_lastTouchTeam);
            string text;
            if(Phase==RallyPhase.PlayerSpike && !_playerTouched)text="MISSED"+MissHint();
            else if(Phase==RallyPhase.OpponentSpike && !_playerTouched && court==Team.Us && inBounds)text="NO BLOCK"+MissHint();
            else if(Phase==RallyPhase.PlayerServe && !_playerTouched)text="SERVE MISSED";
            else if(Phase==RallyPhase.PlayerReceive && !_playerTouched)text="ACE";
            else if(!inBounds)text="OUT";
            else text=court==Team.Them ? "POINT!" : "DOWN ON YOUR SIDE";
            if(Station && _playerTouched && (Phase==RallyPhase.PlayerSet || Phase==RallyPhase.PlayerReceive))
            {
                // A low pass (into the net, under the tape), an out, or a set no hitter could reach: the player's miss.
                winner=Team.Them;_stationQuality=0f;if(Mode==RallyMode.Pass)ShowGrade("BAD");
                text=!inBounds ? "OUT" : court==Team.Them ? "OVER THE NET" : Mode==RallyMode.Set ? "SET DROPPED"
                    : _passHitNet ? "INTO THE NET" : _passTooLow ? "TOO LOW — NOBODY COULD PLAY IT" : "PASS DROPPED";
            }
            else if(Phase==RallyPhase.PlayerSet && !_playerTouched)text="SET MISSED";
            if(Mode==RallyMode.Match && winner==Team.Us && _serving==Team.Them)RotatePlayer(); // side-out
            _serving=winner;
            ClearIntercept();DisarmTheirBlock();EnableReceiveVolume(false);
            AwaitedTouch=PlayerTouch.None;_segTime=-1f;_launchPending=false;
            Award(winner==Team.Us,text,landing);
        }

        // ---------- NPC movement and poses ----------
        void TickActors(float dt)
        {
            foreach(var actor in _cast)
            {
                if(actor==_embodied || actor.A==null || !actor.A.gameObject.activeSelf)continue;
                var target=actor.Target;float speed=actor.Speed;
                bool chasing=actor==_icActor && _sinceTouch>=_icReaction;
                if(chasing){target=_icRootOffset!=Vector3.zero ? _icPoint+_icRootOffset : _icPoint-TeamForward(actor.Team)*.25f;speed=_icSpeed;}
                if(actor.Delay>0f)actor.Delay-=dt;
                else
                {
                    var position=actor.A.transform.position;target.y=position.y;
                    actor.A.transform.position=Vector3.MoveTowards(position,target,speed*dt);
                }
                actor.A.transform.rotation=Quaternion.RotateTowards(actor.A.transform.rotation,actor.Facing,540f*dt);
                float phase=0f;
                switch(actor.Anim)
                {
                    case Anim.Ready:
                        if(chasing || actor.Slot>2)
                        {
                            actor.A.Role=VolleyOpponentPrototype.MotionRole.Dig;
                            phase=chasing ? Mathf.Clamp01(1.2f-Flat(target-actor.A.transform.position).magnitude/2f) : Mathf.MoveTowards(actor.A.PreviewPhase,0f,dt*2f);
                        }
                        else actor.A.Role=VolleyOpponentPrototype.MotionRole.Block;
                        break;
                    case Anim.Dig:
                        actor.A.Role=VolleyOpponentPrototype.MotionRole.Dig;
                        actor.Clock+=dt;
                        phase=actor.Clock<.2f ? actor.Clock/.2f : actor.Clock<.6f ? 1f : Mathf.Max(0f,1f-(actor.Clock-.6f)/.4f);
                        if(actor.Clock>=1f)actor.Anim=Anim.Ready;
                        break;
                    case Anim.Set:
                        actor.A.Role=VolleyOpponentPrototype.MotionRole.Set;
                        if(actor.Clock<0f)phase=actor.Phase;
                        else
                        {
                            actor.Clock+=dt;phase=Mathf.Lerp(VolleyOpponentPrototype.SetContactPhase,1f,Mathf.Clamp01(actor.Clock/.8f));
                            if(actor.Clock>=.8f){actor.Anim=Anim.Ready;actor.Facing=TeamFacing(actor.Team);}
                        }
                        break;
                    case Anim.Spike:
                        actor.A.Role=VolleyOpponentPrototype.MotionRole.Spike;
                        if(actor.Clock<0f)phase=actor.Phase;
                        else
                        {
                            actor.Clock+=dt;phase=Mathf.Lerp(.52f,1f,Mathf.Clamp01(actor.Clock/.9f));
                            if(actor.Clock>=.9f){actor.Anim=Anim.Ready;actor.Facing=TeamFacing(actor.Team);actor.Target=actor.Home;phase=0f;}
                        }
                        break;
                    case Anim.Block:
                        actor.A.Role=VolleyOpponentPrototype.MotionRole.Block;
                        actor.Clock+=dt;phase=Mathf.Clamp01((actor.Clock-actor.JumpAt)/BlockerJumpSeconds);
                        if(phase>=1f){actor.Anim=Anim.Ready;actor.Target=actor.Home;phase=0f;}
                        break;
                }
                Act(actor.A,phase);
            }
        }
    }
}
