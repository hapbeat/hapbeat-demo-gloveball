using System;
using System.Collections;
using Hapbeat.DemoSwitch;
using UnityEngine;
using UnityEngine.UI;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// The Volley tutorials, in the Energy Duel manner: a panel just left of the line of sight to the net (placed again when
    /// the player has moved, and on every recenter; drawn over the net and the players), its text also spoken (Japanese
    /// voice, the shared narration volume), a demonstration hand with its forearm repeating the move below the text, and
    /// goals that are counted on the panel. The ball never waits for the voice: practice starts at once and the next
    /// explanation is spoken over the play; the match starts as soon as the goals are met.
    /// SPIKE + BLOCK: (AUTO JUMP off only: two jumps first, no ball) two strong spikes into their court - the second one is
    /// dug by their defence, so the play goes on into their attack - then two blocks back over, then the match.
    /// RECEIVE: one EXCELLENT (to the setter) and one GOOD (to another player); each grade's consequence is explained the
    /// first time it happens and never again, then the match. With AUTO JUMP off "ジャンプ" is called at the moment to jump,
    /// and a missed or weak rep says whether the jump was early or late.
    /// The first start of the app begins with the tutorial of its mode (<see cref="StartOnLaunch"/>); a retry never does.
    /// </summary>
    public sealed class VolleyTutorial : MonoBehaviour
    {
        public enum Course { None, SpikeBlock, Receive }
        public enum DemoMove { None, Jump, Spike, Block, Receive }

        public VolleyDrillController Drill;
        [Tooltip("Narration clips, named by line id (VolleyTutorialText); a missing one is shown without a voice.")]
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        [Tooltip("The first start of the app (this process) begins with the tutorial of the current mode.")]
        public bool StartOnLaunch = true;
        [Min(1)] public int JumpsRequired = 2;
        [Min(1)] public int SpikesRequired = 2;
        [Min(1)] public int BlocksRequired = 2;
        [Header("Panel (from the eye, toward the net)")]
        [Min(.3f)] public float Distance = .9f;
        [Tooltip("Sideways (+ = right): left of the line of sight, clear of the setter (on the right) and the ball.")]
        public float Side = -.55f;
        [Tooltip("Panel centre height relative to the eyes.")]
        public float Rise = -.25f;
        [Tooltip("Placed again once the eyes are this far (m, horizontally) from where it was placed: a mode change moves the player.")]
        [Min(.2f)] public float ReplaceDistance = .8f;

        public Course Running { get; private set; }
        /// <summary>The current step's line id ("" when not running).</summary>
        public string Step { get; private set; } = "";
        public int Count { get; private set; }
        public DemoMove Move { get; private set; }
        /// <summary>RECEIVE goals met.</summary>
        public bool GotExcellent { get; private set; }
        public bool GotGood { get; private set; }
        public event Action<Course> Finished;

        static bool s_launched;
        /// <summary>The session (option `tutorial` off) or a test: no tutorial at the first start.</summary>
        public static void SkipLaunchTutorial() => s_launched = true;
        public static void ResetLaunchForTests() => s_launched = false;

        bool _explainedBad, _explainedLost;
        int _gradeSerial, _jumps, _savedMatchPoints;
        float _hintAge = 99f, _lingerUntil = -1f;
        bool _finishing, _subscribed;
        VolleyAerialSequence.RallyMode _expected;
        VolleyAerialSequence.RallyMode? _pendingMode;

        Transform _panel, _demo;
        Text _title, _body, _hint, _counter;
        AudioSource _voice, _cue;
        string _queued;
        DemoTutorialHand[] _hands;
        bool _placed;
        Vector3 _placedEye;
        float _clock;

        VolleyAerialSequence Aerial => Drill != null ? Drill.Aerial : null;
        bool AutoJump => Aerial != null && Aerial.BeginnerBlock;

        // ------------------------------------------------------------ course

        void Start() { if (Application.isPlaying) StartCoroutine(LaunchTutorial()); }

        IEnumerator LaunchTutorial()
        {
            // After the session options and a pending menu choice (both applied within the first frame).
            yield return null; yield return null;
            if (s_launched || !StartOnLaunch || Running != Course.None || Drill == null || Drill.Drill != VolleyDrill.Block || Aerial == null) yield break;
            var course = CourseFor(Aerial.Mode);
            if (course != Course.None) Begin(course);
        }

        /// <summary>The tutorial that teaches <paramref name="mode"/> (none for TOSS and the match).</summary>
        public static Course CourseFor(VolleyAerialSequence.RallyMode mode) =>
            mode == VolleyAerialSequence.RallyMode.Pass ? Course.Receive
            : mode == VolleyAerialSequence.RallyMode.Alternate || mode == VolleyAerialSequence.RallyMode.SpikeOnly || mode == VolleyAerialSequence.RallyMode.BlockOnly
                ? Course.SpikeBlock : Course.None;

        public void Begin(Course course)
        {
            if (Drill == null || Aerial == null || course == Course.None) return;
            if (Running != Course.None) Stop(false);
            s_launched = true;
            Running = course;
            _savedMatchPoints = Aerial.MatchPoints;
            Aerial.MatchPoints = 999; // practice points never end a match
            Subscribe(true);
            EnsureBuilt();
            _placed = false; _finishing = false; _pendingMode = null; _lingerUntil = -1f; _queued = null;
            GotExcellent = GotGood = _explainedBad = _explainedLost = false;
            ShowPanel(true);
            if (course == Course.Receive) { Practice("rc_pass", DemoMove.Receive, VolleyAerialSequence.RallyMode.Pass, VolleyAerialSequence.DefenceScript.Natural); UpdateReceiveCounter(); }
            else if (!AutoJump)
            {
                // The jump is practised where it is used (a RECEIVE never jumps).
                Aerial.SetMode(VolleyAerialSequence.RallyMode.SpikeOnly);
                Hold("sb_jump", DemoMove.Jump);
                SetCounter(Count + " / " + JumpsRequired);
            }
            else StartSpikes();
        }

        /// <summary>Ends the tutorial; <paramref name="play"/> starts the course's match (its goals met).</summary>
        public void Stop(bool play)
        {
            var course = Running;
            if (course == Course.None) return;
            Running = Course.None;
            Subscribe(false);
            Step = ""; Move = DemoMove.None; _pendingMode = null; _finishing = false;
            if (Aerial != null)
            {
                Aerial.ScriptedDefence = VolleyAerialSequence.DefenceScript.Natural;
                Aerial.MatchPoints = _savedMatchPoints;
                if (play) Aerial.SetMode(course == Course.Receive ? VolleyAerialSequence.RallyMode.Pass : VolleyAerialSequence.RallyMode.Alternate);
            }
            if (Drill != null) Drill.HoldRally = false;
            // After the goals the last words are still spoken over the match's start; otherwise all goes at once.
            if (play && _queued != null) Say(_queued);
            _queued = null;
            if (play && _voice != null && _voice.isPlaying) _lingerUntil = _clock + Mathf.Max(0f, _voice.clip.length - _voice.time) + .5f;
            else { if (_voice != null) _voice.Stop(); ShowPanel(false); }
            if (play) Finished?.Invoke(course);
        }

        void Hold(string id, DemoMove move)
        {
            Step = id; Move = move; Count = 0;
            Drill.HoldRally = true;
            _expected = Aerial.Mode;
            _jumps = Aerial.Jump != null ? Aerial.Jump.Jumps : 0;
            Show(id, ""); SetCounter("");
            Say(id);
        }

        void Practice(string id, DemoMove move, VolleyAerialSequence.RallyMode mode, VolleyAerialSequence.DefenceScript defence, bool keepPlay = false)
        {
            Step = id; Move = move; Count = 0;
            Drill.HoldRally = false;
            Aerial.ScriptedDefence = defence;
            if (!keepPlay) Aerial.SetMode(mode);
            _expected = Aerial.Mode;
            _gradeSerial = Aerial.GradeSerial;
            Show(id, "");
            Say(id);
        }

        void StartSpikes()
        {
            Practice(AutoJump ? "sb_spike_auto" : "sb_spike", DemoMove.Spike, VolleyAerialSequence.RallyMode.SpikeOnly, VolleyAerialSequence.DefenceScript.NeverDig);
            SetCounter(Count + " / " + SpikesRequired);
        }

        /// <summary>
        /// The block goals. From their dig the play goes on as it is (their attack comes now); BLOCK itself starts once the
        /// current rep has ended (never switched inside a rep).
        /// </summary>
        void StartBlocks(bool fromDig)
        {
            Practice(fromDig ? "sb_block" : "sb_block_plain", DemoMove.Block, VolleyAerialSequence.RallyMode.BlockOnly, VolleyAerialSequence.DefenceScript.NeverDig, true);
            _pendingMode = VolleyAerialSequence.RallyMode.BlockOnly;
            SetCounter(Count + " / " + BlocksRequired);
        }

        /// <summary>The goals are met: the last words (after an explanation still being spoken), and the match once the rep ends.</summary>
        void Finish(string id)
        {
            _finishing = true;
            _title.text = VolleyTutorialText.Get(id).Title; _body.text = VolleyTutorialText.Get(id).Body;
            if (_voice != null && _voice.isPlaying) _queued = id; else Say(id);
        }

        void Subscribe(bool on)
        {
            if (Aerial == null || on == _subscribed) return;
            _subscribed = on;
            if (on) { Aerial.TurnRepResolved += OnRep; Aerial.PointScored += OnPoint; Aerial.RallyContinued += OnContinued; Aerial.ActMoment += OnActMoment; }
            else { Aerial.TurnRepResolved -= OnRep; Aerial.PointScored -= OnPoint; Aerial.RallyContinued -= OnContinued; Aerial.ActMoment -= OnActMoment; }
        }

        // ------------------------------------------------------------ events

        void OnRep(bool block, bool success)
        {
            if (Running != Course.SpikeBlock || _finishing) return;
            if (Move == DemoMove.Spike && !block)
            {
                bool strong = success && Aerial.LastSpikeStrong;
                if (!strong) { Hint(TimingHint() ?? (success ? (AutoJump ? "sb_spike_low_auto" : "sb_spike_low") : "sb_spike_hint")); return; }
                Count++;
                SetCounter(Count + " / " + SpikesRequired);
                if (Count < SpikesRequired)
                {
                    // The next strong spike is dug by their defence: the play goes on into their attack (OnContinued).
                    if (Count == SpikesRequired - 1) Aerial.ScriptedDefence = VolleyAerialSequence.DefenceScript.DigStrongSpikes;
                    Hint("sb_nice");
                    return;
                }
                // Their scripted dig could not get there: the point is ours, the blocks start from the next rep.
                StartBlocks(false);
                return;
            }
            if (Move == DemoMove.Block && block)
            {
                if (!success) { Hint(TimingHint() ?? "sb_block_hint"); return; }
                Count++;
                SetCounter(Count + " / " + BlocksRequired);
                if (Count < BlocksRequired) { Hint("sb_nice_block"); return; }
                Finish("sb_end");
            }
        }

        /// <summary>With AUTO JUMP off: early or late, when the jump was off; null when it was on time (or automatic).</summary>
        string TimingHint()
        {
            if (AutoJump) return null;
            switch (Aerial.LastJumpTiming)
            {
                case VolleyAerialSequence.JumpTiming.Early: return "sb_jump_early";
                case VolleyAerialSequence.JumpTiming.Late: return "sb_jump_late";
                case VolleyAerialSequence.JumpTiming.NoJump: return "sb_jump_none";
                default: return null;
            }
        }

        void OnContinued(VolleyAerialSequence.RallyTurn turn)
        {
            // The second strong spike was dug: it counts, and their attack is the first block.
            if (Running == Course.SpikeBlock && Move == DemoMove.Spike && turn == VolleyAerialSequence.RallyTurn.Block && Count == SpikesRequired - 1)
            {
                Count++;
                StartBlocks(true);
            }
        }

        void OnPoint(bool player, Vector3 position)
        {
            // RECEIVE: a point lost with no playable pass (shanked, into the net, too low) is a miss.
            if (Running != Course.Receive || player || Aerial.GradeSerial != _gradeSerial || _finishing) return;
            if (_explainedLost) { ShowHint("rc_lost"); return; }
            _explainedLost = true;
            Explain("rc_lost");
        }

        void OnActMoment(bool spike, bool jump)
        {
            if (Running != Course.SpikeBlock || _finishing || _cue == null) return;
            // "ジャンプ" at the moment to jump, with AUTO JUMP off (spike and block).
            if (spike && Move != DemoMove.Spike || !spike && Move != DemoMove.Block) return;
            var clip = jump && !AutoJump ? Clip("jump_cue") : null;
            if (clip == null) return;
            DemoNarration.Apply(_cue);
            if (Application.isPlaying) _cue.PlayOneShot(clip);
        }

        void OnGrade(string grade)
        {
            if (_finishing) return;
            if (grade == "EXCELLENT")
            {
                bool first = !GotExcellent;
                GotExcellent = true; UpdateReceiveCounter();
                if (first) Explain("rc_excellent"); else Remaining(false);
            }
            else if (grade == "GOOD")
            {
                bool first = !GotGood;
                GotGood = true; UpdateReceiveCounter();
                if (first) Explain("rc_good"); else Remaining(false);
            }
            else
            {
                if (_explainedBad) { ShowHint("rc_bad"); return; }
                _explainedBad = true;
                Explain("rc_bad");
            }
            if (GotExcellent && GotGood) Finish("rc_end");
        }

        /// <summary>Speaks a grade's consequence (once), then what to aim for next.</summary>
        void Explain(string id)
        {
            ShowHint(id);
            Say(id);
            Remaining(true);
        }

        /// <summary>The panel's goal text: to the setter, or to another player; queued after an explanation when <paramref name="speak"/>.</summary>
        void Remaining(bool speak)
        {
            if (GotExcellent && GotGood) return;
            string id = !GotExcellent ? "rc_try_excellent" : "rc_try_good";
            _body.text = VolleyTutorialText.Get(id).Body;
            if (speak) _queued = id;
        }

        void UpdateReceiveCounter() => SetCounter("EXCELLENT " + (GotExcellent ? "●" : "○") + "  GOOD " + (GotGood ? "●" : "○"));

        void Hint(string id)
        {
            ShowHint(id);
            // Spoken at most every few seconds, so a run of misses does not talk over the play.
            if (_hintAge > 4f) { _hintAge = 0f; Say(id); }
        }

        void ShowHint(string id) { if (_hint != null) _hint.text = VolleyTutorialText.Get(id).Body; }

        // ------------------------------------------------------------ frame

        void OnEnable() => DemoRecenter.Recentered += OnRecentered;
        void OnDisable() { DemoRecenter.Recentered -= OnRecentered; if (Running != Course.None) Stop(false); }
        void OnRecentered() => _placed = false;

        void Update()
        {
            bool paused = DemoPause.IsPaused || GameInputGate.IsBlocked;
            foreach (var source in new[] { _voice, _cue }) if (source != null) { if (paused) source.Pause(); else source.UnPause(); }
            if (paused) return;
            Tick(Time.deltaTime);
        }

        /// <summary>Advances the tutorial by <paramref name="dt"/> seconds (also called by the tests).</summary>
        public void Tick(float dt)
        {
            _clock += dt; _hintAge += dt;
            if (Running == Course.None)
            {
                if (_lingerUntil >= 0f && _clock >= _lingerUntil) { _lingerUntil = -1f; ShowPanel(false); }
                return;
            }
            // The next line after an explanation.
            if (_queued != null && (_voice == null || !_voice.isPlaying || !Application.isPlaying)) { var next = _queued; _queued = null; Say(next); }
            // A mode picked meanwhile (the menu, the session) ends the tutorial there.
            if (_pendingMode == null && Aerial.Mode != _expected) { Stop(false); return; }
            // A rep that went on from their dig ended: from now on the course's own mode.
            if (_pendingMode.HasValue && Aerial.Phase == VolleyAerialSequence.RallyPhase.Recovery)
            {
                var mode = _pendingMode.Value; _pendingMode = null;
                Aerial.SetMode(mode); _expected = mode;
            }
            if (_finishing)
            {
                // The match starts once the rep that met the goals has ended.
                if (Aerial.Phase == VolleyAerialSequence.RallyPhase.Recovery || Aerial.Phase == VolleyAerialSequence.RallyPhase.Idle) Stop(true);
                return;
            }
            if (Move == DemoMove.Jump && Aerial.Jump != null && Aerial.Jump.Jumps > _jumps)
            {
                _jumps = Aerial.Jump.Jumps;
                Count++;
                SetCounter(Count + " / " + JumpsRequired);
                if (Count >= JumpsRequired) StartSpikes();
                return;
            }
            if (Move == DemoMove.Receive && Aerial.GradeSerial != _gradeSerial)
            {
                _gradeSerial = Aerial.GradeSerial;
                OnGrade(Aerial.LastGrade);
            }
        }

        void LateUpdate()
        {
            if (_panel == null || !_panel.gameObject.activeSelf || Drill == null || Drill.Head == null) return;
            var eye = Drill.Head.position;
            if (!_placed || Vector3.ProjectOnPlane(eye - _placedEye, Vector3.up).magnitude > ReplaceDistance) Place();
            PoseDemo(Time.time);
        }

        // ------------------------------------------------------------ voice

        /// <summary>Plays the line (cutting the one playing).</summary>
        void Say(string id)
        {
            var clip = Clip(id);
            if (clip == null || _voice == null) return;
            _voice.Stop();
            DemoNarration.Apply(_voice);
            _voice.clip = clip;
            if (Application.isPlaying) _voice.Play(); // EditMode verification never plays audio
        }

        AudioClip Clip(string id)
        {
            if (Clips == null) return null;
            foreach (var clip in Clips) if (clip != null && clip.name == id) return clip;
            return null;
        }

        // ------------------------------------------------------------ panel

        void EnsureBuilt()
        {
            if (_panel != null) return;
            var font = Resources.Load<Font>("HapbeatDemoSession/NotoSansCJKjp-Regular");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Volley tutorial panel", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            _panel = go.transform;
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 50;
            var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(640, 330); rect.localScale = Vector3.one * .001f;
            var back = go.AddComponent<Image>(); back.color = new Color(.02f, .04f, .07f, .82f); back.raycastTarget = false;
            _title = Label(rect, font, "Title", new Vector2(-70, 132), new Vector2(460, 50), 34, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            _counter = Label(rect, font, "Counter", new Vector2(190, 132), new Vector2(240, 50), 24, FontStyle.Bold, new Color(.35f, 1f, .45f), TextAnchor.MiddleRight);
            _body = Label(rect, font, "Body", new Vector2(0, 20), new Vector2(590, 170), 25, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);
            _hint = Label(rect, font, "Hint", new Vector2(0, -120), new Vector2(590, 70), 23, FontStyle.Normal, new Color(1f, .85f, .3f), TextAnchor.UpperLeft);
            // Over the net and the players (the panel may be placed near the net): no depth test, drawn late.
            var onTop = new Material(Shader.Find("UI/Default")) { name = "Volley tutorial on top" };
            onTop.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            onTop.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            foreach (var graphic in go.GetComponentsInChildren<Graphic>(true)) graphic.material = onTop;
            _voice = go.AddComponent<AudioSource>(); _voice.playOnAwake = false; _voice.spatialBlend = 0f;
            _cue = go.AddComponent<AudioSource>(); _cue.playOnAwake = false; _cue.spatialBlend = 0f;
            // The demonstration hands, below the text, at 0.9 of real size.
            _demo = new GameObject("Volley tutorial demo").transform;
            _demo.SetParent(_panel, false);
            _demo.localPosition = new Vector3(0f, -380f, -60f); // canvas units: about 0.38 m below the panel centre
            _demo.localScale = Vector3.one * 1000f;              // back to metres
            _hands = new DemoTutorialHand[2];
            for (int side = 0; side < 2; side++)
            {
                var hand = new GameObject(side == 0 ? "Demo left hand" : "Demo right hand");
                hand.SetActive(false);
                hand.transform.SetParent(_demo, false);
                var demo = hand.AddComponent<DemoTutorialHand>();
                demo.Left = side == 0; demo.ShowForearm = true; demo.Scale = .9f;
                var arm = Drill.BlockArms != null && side < Drill.BlockArms.Length ? Drill.BlockArms[side] : null;
                if (arm != null)
                {
                    demo.PlaceholderSkeleton = arm.PlaceholderSkeleton;
                    demo.ForearmWristRadii = arm.ForearmWristRadii; demo.ForearmCuffRadii = arm.ForearmCuffRadii;
                    demo.HandFadeStart = arm.HandFadeStart; demo.HandFadeLength = arm.HandFadeLength; demo.SkinUv = arm.SkinUv;
                }
                _hands[side] = demo;
            }
            ShowPanel(false);
        }

        static Text Label(RectTransform parent, Font font, string name, Vector2 at, Vector2 size, int fontSize, FontStyle style, Color colour, TextAnchor anchor)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.rectTransform.anchoredPosition = at; text.rectTransform.sizeDelta = size;
            text.font = font; text.fontSize = fontSize; text.fontStyle = style; text.color = colour; text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        void Show(string id, string hint)
        {
            var line = VolleyTutorialText.Get(id);
            _title.text = line.Title; _body.text = line.Body; _hint.text = hint;
        }

        void SetCounter(string value) { if (_counter != null) _counter.text = value; }

        void ShowPanel(bool shown)
        {
            if (_panel == null) return;
            _panel.gameObject.SetActive(shown);
            if (!shown) foreach (var hand in _hands) if (hand != null) hand.gameObject.SetActive(false);
        }

        public Transform Panel => _panel;
        public string PanelBody => _body != null ? _body.text : "";
        public string PanelHint => _hint != null ? _hint.text : "";
        public string PanelCounter => _counter != null ? _counter.text : "";

        /// <summary>From the eye toward the net, then to the left, a little below the eyes, facing the eye; never past the net.</summary>
        void Place()
        {
            var eye = Drill.Head.position;
            var forward = Vector3.ProjectOnPlane(Drill.CourtFrame != null ? Drill.CourtFrame.forward : Drill.Head.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            float distance = Distance;
            if (Drill.ReceiveNet != null)
            {
                // A block stance is half a metre from the net: the panel stays on our side of it.
                float toNet = Vector3.Dot(Drill.ReceiveNet.transform.position - eye, forward);
                distance = Mathf.Clamp(Mathf.Min(distance, toNet - .15f), .45f, Distance);
            }
            var position = eye + forward * distance + right * Side + Vector3.up * Rise;
            var look = position - eye;
            _panel.SetPositionAndRotation(position, Quaternion.LookRotation(look.sqrMagnitude > 1e-6f ? look.normalized : forward, Vector3.up));
            _placed = true; _placedEye = eye;
        }

        // ------------------------------------------------------------ demonstration hands

        struct Key
        {
            public float T; public Vector3 Wrist, Fingers, Back; public float Curl;
            public Key(float t, Vector3 wrist, Vector3 fingers, Vector3 back, float curl = .1f) { T = t; Wrist = wrist; Fingers = fingers; Back = back; Curl = curl; }
        }

        // The right hand in the demo frame (+z toward the net, +y up, +x right; the left hand mirrors x). XR Hands axes:
        // +Z along the fingers, +Y out of the back of the hand.
        static readonly Key[] SpikeKeys =
        {
            new Key(0f, new Vector3(.16f, -.04f, -.02f), Vector3.up, Vector3.back),
            new Key(.42f, new Vector3(.16f, .28f, -.10f), new Vector3(0f, 1f, -.35f), new Vector3(0f, .35f, -1f)),
            new Key(.58f, new Vector3(.14f, .34f, .10f), new Vector3(0f, 1f, .6f), new Vector3(0f, -.3f, -1f)),
            new Key(.74f, new Vector3(.12f, .02f, .30f), new Vector3(0f, -.6f, 1f), new Vector3(0f, 1f, .4f)),
            new Key(1f, new Vector3(.16f, -.04f, -.02f), Vector3.up, Vector3.back),
        };
        static readonly Key[] BlockKeys =
        {
            new Key(0f, new Vector3(.13f, -.08f, .04f), Vector3.up, Vector3.back),
            new Key(.35f, new Vector3(.12f, .30f, .08f), Vector3.up, Vector3.back),
            new Key(.55f, new Vector3(.12f, .33f, .22f), new Vector3(0f, 1f, .4f), new Vector3(0f, -.4f, -1f)),
            new Key(.8f, new Vector3(.12f, .33f, .22f), new Vector3(0f, 1f, .4f), new Vector3(0f, -.4f, -1f)),
            new Key(1f, new Vector3(.13f, -.08f, .04f), Vector3.up, Vector3.back),
        };
        static readonly Key[] JumpKeys =
        {
            new Key(0f, new Vector3(.13f, -.12f, .06f), Vector3.up, Vector3.back),
            new Key(.3f, new Vector3(.13f, -.12f, .06f), Vector3.up, Vector3.back),
            new Key(.5f, new Vector3(.12f, .32f, .06f), Vector3.up, Vector3.back),
            new Key(.8f, new Vector3(.12f, .32f, .06f), Vector3.up, Vector3.back),
            new Key(1f, new Vector3(.13f, -.12f, .06f), Vector3.up, Vector3.back),
        };
        static readonly Key[] ReceiveKeys =
        {
            new Key(0f, new Vector3(.035f, -.34f, .22f), new Vector3(-.25f, -.35f, 1f), new Vector3(.7f, -.7f, 0f), .5f),
            new Key(.4f, new Vector3(.035f, -.24f, .30f), new Vector3(-.25f, -.2f, 1f), new Vector3(.7f, -.7f, 0f), .5f),
            new Key(.6f, new Vector3(.035f, -.24f, .30f), new Vector3(-.25f, -.2f, 1f), new Vector3(.7f, -.7f, 0f), .5f),
            new Key(1f, new Vector3(.035f, -.34f, .22f), new Vector3(-.25f, -.35f, 1f), new Vector3(.7f, -.7f, 0f), .5f),
        };

        static Key[] KeysFor(DemoMove move, out float loop, out bool both)
        {
            both = move != DemoMove.Spike;
            switch (move)
            {
                case DemoMove.Spike: loop = 2.2f; return SpikeKeys;
                case DemoMove.Block: loop = 2.4f; return BlockKeys;
                case DemoMove.Jump: loop = 1.8f; return JumpKeys;
                case DemoMove.Receive: loop = 2.2f; return ReceiveKeys;
                default: loop = 1f; return null;
            }
        }

        /// <summary>The demo wrist pose (demo frame) of the right hand (<paramref name="left"/> mirrors it) at <paramref name="time"/>.</summary>
        public static bool DemoPose(DemoMove move, bool left, float time, out Vector3 wrist, out Quaternion rotation, out float curl)
        {
            var keys = KeysFor(move, out float loop, out _);
            wrist = default; rotation = Quaternion.identity; curl = 0f;
            if (keys == null) return false;
            float t = Mathf.Repeat(time, loop) / loop;
            int i = 0;
            while (i < keys.Length - 2 && t > keys[i + 1].T) i++;
            var a = keys[i]; var b = keys[i + 1];
            float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.T, b.T, t));
            var mirror = left ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            wrist = Vector3.Scale(Vector3.Lerp(a.Wrist, b.Wrist, u), mirror);
            var ra = Quaternion.LookRotation(Vector3.Scale(a.Fingers, mirror), Vector3.Scale(a.Back, mirror));
            var rb = Quaternion.LookRotation(Vector3.Scale(b.Fingers, mirror), Vector3.Scale(b.Back, mirror));
            rotation = Quaternion.Slerp(ra, rb, u);
            curl = Mathf.Lerp(a.Curl, b.Curl, u);
            return true;
        }

        void PoseDemo(float time)
        {
            if (_hands == null) return;
            var move = Running != Course.None ? Move : DemoMove.None;
            KeysFor(move, out _, out bool both);
            for (int side = 0; side < 2; side++)
            {
                var hand = _hands[side];
                if (hand == null) continue;
                bool left = side == 0;
                bool shown = move != DemoMove.None && (both || !left) && Application.isPlaying && hand.EnsureBuilt();
                if (hand.gameObject.activeSelf != shown) hand.gameObject.SetActive(shown);
                if (!shown || !DemoPose(move, left, time, out var wrist, out var rotation, out var curl)) continue;
                var world = _demo.rotation * rotation;
                hand.ApplyPose(_demo.TransformPoint(wrist), world, curl, DemoTutorialHand.StraightArm(world), .07f);
            }
        }
    }

    /// <summary>The tutorial's lines: panel title and text, and what the voice says (the voice files are made from Voice/voice-lines.json).</summary>
    public static class VolleyTutorialText
    {
        public readonly struct Line
        {
            public readonly string Id, Title, Body, Voice;
            public Line(string id, string title, string body, string voice = null) { Id = id; Title = title; Body = body; Voice = voice ?? body; }
        }

        public static readonly Line[] Lines =
        {
            new Line("sb_jump", "ジャンプ", "まずジャンプの練習です。両手を下から上へ、一気に振り上げるとジャンプします。2回跳んでみましょう。"),
            new Line("sb_spike", "スパイク", "スパイクの練習です。セッターがトスを上げます。「ジャンプ」の声で跳んで、いちばん高いところで、ボールを上から下へ、相手のコートにたたき込みましょう。強いスパイクを2本決めます。"),
            new Line("sb_spike_auto", "スパイク", "スパイクの練習です。セッターがトスを上げます。ジャンプは自動です。ボールがいちばん高いところに来たら、上から下へ、相手のコートにたたき込みましょう。強いスパイクを2本決めます。"),
            new Line("sb_spike_hint", "", "ジャンプして、ボールを上から下へ、相手のコートにたたき込みましょう。"),
            new Line("sb_spike_low", "", "ジャンプのタイミングを合わせて、いちばん高いところで打ちましょう。"),
            new Line("sb_spike_low_auto", "", "もう少し高いところで打ちましょう。ボールがいちばん高いところに来たら、上から下へたたきます。"),
            new Line("sb_jump_early", "", "ジャンプが早すぎました。もう少し引きつけてから跳びましょう。"),
            new Line("sb_jump_late", "", "ジャンプが遅れました。もう少し早く跳びましょう。"),
            new Line("sb_jump_none", "", "ジャンプしていません。「ジャンプ」の声で、両手を振り上げて跳びましょう。"),
            new Line("sb_nice", "", "ナイススパイク！もう1本。"),
            new Line("sb_block", "ブロック", "強いスパイクでも、拾われるとラリーが続きます。今度は相手のスパイクです。ジャンプして両手を高く上げ、ネットの上へ手を出して、ブロックしましょう。"),
            new Line("sb_block_plain", "ブロック", "今度は相手のスパイクです。ジャンプして両手を高く上げ、ネットの上へ手を出して、ブロックしましょう。"),
            new Line("sb_block_hint", "", "打ってくる相手の正面で、ジャンプして両手を前へ出しましょう。"),
            new Line("sb_nice_block", "", "ナイスブロック！もう1本。"),
            new Line("sb_end", "試合開始", "OKです！スパイクは相手のいないところへ強く。ブロックは相手のコートへはじき返します。どちらも、いまいちだとラリーが続きます。では、3点先取の試合です。"),
            new Line("rc_pass", "レシーブ", "レシーブの練習です。相手のサーブを、緑の円の上のセッターへ、両腕をそろえて高く返しましょう。"),
            new Line("rc_excellent", "", "EXCELLENT！セッターへ余裕をもって返せると、強いスパイクで、ほぼ決まります。", "エクセレント！セッターへ余裕をもって返せると、強いスパイクで、ほぼ決まります。"),
            new Line("rc_good", "", "GOOD。セッター以外の選手がトスしても攻撃は続きますが、スパイクは決まりにくく、相手に拾われてラリーが続きます。", "グッド。セッター以外の選手がトスしても攻撃は続きますが、スパイクは決まりにくく、相手に拾われてラリーが続きます。"),
            new Line("rc_bad", "", "BAD。低すぎたりずれたりすると、相手にそのまま返すだけになり、強いスパイクを打たれます。", "バッド。低すぎたりずれたりすると、相手にそのまま返すだけになり、強いスパイクを打たれます。"),
            new Line("rc_lost", "", "ボールが落ちました。ボールの正面で、両腕をそろえて受けましょう。"),
            new Line("rc_try_excellent", "", "緑のセッターへ、高く返しましょう。"),
            new Line("rc_try_good", "", "次は、わざとセッター以外の選手に返してみましょう。"),
            new Line("rc_end", "試合開始", "OKです！では、3点先取の試合を始めます。"),
            new Line("jump_cue", "", "ジャンプ！"),
        };

        public static Line Get(string id)
        {
            foreach (var line in Lines) if (line.Id == id) return line;
            throw new ArgumentException("No tutorial line " + id);
        }
    }
}
