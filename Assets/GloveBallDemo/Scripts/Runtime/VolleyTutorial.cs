using System;
using System.Collections.Generic;
using Hapbeat.DemoSwitch;
using UnityEngine;
using UnityEngine.UI;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// The Volley tutorials, in the Energy Duel manner: a panel fixed in the world just left of the line of sight to
    /// the net (placed when it appears and on every recenter), its title, text and hint also spoken (Japanese voice,
    /// the shared narration volume), a demonstration hand with its forearm repeating the move below the text, and steps
    /// that advance when the player has done them ("OKです"), with a counter in the counted steps.
    /// SPIKE + BLOCK: intro, the jump (only with AUTO JUMP off), two spikes into their court (a point, or dug), two
    /// blocks that go back over, then the turns start. RECEIVE: intro, two passes graded GOOD or EXCELLENT, the outcomes
    /// explained, then RECEIVE starts. Between the practice steps the rally waits (<see cref="VolleyDrillController.HoldRally"/>).
    /// </summary>
    public sealed class VolleyTutorial : MonoBehaviour
    {
        public enum Course { None, SpikeBlock, Receive }
        public enum DemoMove { None, Jump, Spike, Block, Receive }

        public VolleyDrillController Drill;
        [Tooltip("Narration clips, named by line id (VolleyTutorialText); a missing one is shown without a voice.")]
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        [Min(1)] public int SpikesRequired = 2;
        [Min(1)] public int BlocksRequired = 2;
        [Min(1)] public int PassesRequired = 2;
        [Header("Panel (from the eye, toward the net)")]
        [Min(.3f)] public float Distance = 1.15f;
        [Tooltip("Sideways (+ = right): left of the line of sight, clear of the setter and the ball above the net.")]
        public float Side = -.6f;
        [Tooltip("Panel centre height relative to the eyes.")]
        public float Rise = -.2f;
        [Tooltip("Without a voice clip a text stays this long at least (s), plus a little per character.")]
        [Min(.5f)] public float ReadSeconds = 3.5f;

        public Course Running { get; private set; }
        /// <summary>The current step's line id ("" when not running).</summary>
        public string Step => _step != null ? _step.Id : "";
        public int Count { get; private set; }
        public DemoMove Move => _step != null ? _step.Move : DemoMove.None;
        public event Action<Course> Finished;

        sealed class StepDef
        {
            public string Id; public DemoMove Move; public int Required; public string Hint;
            public bool Practice; public VolleyAerialSequence.RallyMode Mode;
        }

        readonly List<StepDef> _steps = new List<StepDef>();
        int _index = -1;
        StepDef _step;
        float _age, _wait;
        bool _ok, _hintPending;
        float _hintAge = 99f;
        int _savedMatchPoints, _gradeSerial, _jumps;
        bool _subscribed;
        VolleyAerialSequence.RallyMode _expected;

        Transform _panel, _demo;
        Text _title, _body, _hint, _counter;
        AudioSource _voice;
        DemoTutorialHand[] _hands;
        bool _placed;

        VolleyAerialSequence Aerial => Drill != null ? Drill.Aerial : null;

        // ------------------------------------------------------------ course

        public void Begin(Course course)
        {
            if (Drill == null || Aerial == null || course == Course.None) return;
            if (Running != Course.None) Stop(false);
            _steps.Clear();
            bool auto = Aerial.BeginnerBlock;
            if (course == Course.SpikeBlock)
            {
                _steps.Add(new StepDef { Id = "sb_intro" });
                if (!auto) _steps.Add(new StepDef { Id = "sb_jump", Move = DemoMove.Jump, Required = 1 });
                _steps.Add(new StepDef { Id = auto ? "sb_spike_auto" : "sb_spike", Move = DemoMove.Spike, Required = SpikesRequired, Hint = "sb_spike_hint", Practice = true, Mode = VolleyAerialSequence.RallyMode.SpikeOnly });
                _steps.Add(new StepDef { Id = "sb_block", Move = DemoMove.Block, Required = BlocksRequired, Hint = "sb_block_hint", Practice = true, Mode = VolleyAerialSequence.RallyMode.BlockOnly });
                _steps.Add(new StepDef { Id = "sb_end" });
            }
            else
            {
                _steps.Add(new StepDef { Id = "rc_intro" });
                _steps.Add(new StepDef { Id = "rc_pass", Move = DemoMove.Receive, Required = PassesRequired, Hint = "rc_pass_hint", Practice = true, Mode = VolleyAerialSequence.RallyMode.Pass });
                _steps.Add(new StepDef { Id = "rc_end" });
            }
            Running = course;
            _savedMatchPoints = Aerial.MatchPoints;
            Aerial.MatchPoints = 999; // no match end while practising
            Subscribe(true);
            EnsureBuilt();
            _placed = false;
            Go(0);
        }

        /// <summary>Ends the tutorial; <paramref name="play"/> starts the course's mode (its last step done).</summary>
        public void Stop(bool play)
        {
            var course = Running;
            if (course == Course.None) return;
            Running = Course.None;
            Subscribe(false);
            _step = null; _index = -1;
            if (Aerial != null)
            {
                Aerial.MatchPoints = _savedMatchPoints;
                if (play) Aerial.SetMode(course == Course.Receive ? VolleyAerialSequence.RallyMode.Pass : VolleyAerialSequence.RallyMode.Alternate);
            }
            if (Drill != null) Drill.HoldRally = false;
            if (_voice != null) _voice.Stop();
            ShowPanel(false);
            if (play) Finished?.Invoke(course);
        }

        void Go(int index)
        {
            if (index >= _steps.Count) { Stop(true); return; }
            _index = index; _step = _steps[index];
            _age = 0f; _ok = false; _hintPending = false; Count = 0; _hintAge = 99f;
            var line = VolleyTutorialText.Get(_step.Id);
            SetTexts(line.Title, line.Body, "", _step.Required > 1 ? Counter() : "");
            _wait = Speak(_step.Id, line.Body);
            if (_step.Practice)
            {
                _expected = _step.Mode;
                Aerial.SetMode(_step.Mode);
                _gradeSerial = Aerial.GradeSerial;
            }
            else _expected = Aerial.Mode;
            _jumps = Aerial.Jump != null ? Aerial.Jump.Jumps : 0;
            // The ball waits while a text is read; in the practice steps (and the jump) it plays.
            Drill.HoldRally = !_step.Practice;
            ShowPanel(true);
        }

        string Counter() => Count + " / " + _step.Required;

        void Done()
        {
            Count++;
            if (_step.Required > 1) _counter.text = Counter();
            if (Count < _step.Required) { if (_hint != null) _hint.text = ""; return; }
            _ok = true; _age = 0f;
            var ok = VolleyTutorialText.Get("ok");
            SetTexts(_title.text, ok.Body, "", _counter.text);
            _wait = Speak("ok", ok.Body);
        }

        void Missed()
        {
            if (_step == null || string.IsNullOrEmpty(_step.Hint) || _ok) return;
            var hint = VolleyTutorialText.Get(_step.Hint);
            _hint.text = hint.Body;
            // Spoken at most every few seconds, so a run of misses does not talk over the play.
            if (_hintAge > 5f) { _hintAge = 0f; Speak(_step.Hint, hint.Body); }
        }

        void Subscribe(bool on)
        {
            if (Aerial == null || on == _subscribed) return;
            _subscribed = on;
            if (on) { Aerial.TurnRepResolved += OnRep; Aerial.PointScored += OnPoint; }
            else { Aerial.TurnRepResolved -= OnRep; Aerial.PointScored -= OnPoint; }
        }

        void OnRep(bool block, bool success)
        {
            if (_step == null || _ok || !_step.Practice) return;
            bool mine = block ? _step.Move == DemoMove.Block : _step.Move == DemoMove.Spike;
            if (!mine) return;
            if (success) Done(); else Missed();
        }

        void OnPoint(bool player, Vector3 position)
        {
            // RECEIVE: a point lost with no playable pass (shanked, into the net, too low) is a miss.
            if (_step != null && _step.Move == DemoMove.Receive && !player && Aerial.GradeSerial == _gradeSerial) Missed();
        }

        // ------------------------------------------------------------ frame

        void OnEnable() => DemoRecenter.Recentered += OnRecentered;
        void OnDisable() { DemoRecenter.Recentered -= OnRecentered; if (Running != Course.None) Stop(false); }
        void OnRecentered() => _placed = false;

        void Update()
        {
            if (Running == Course.None) return;
            bool paused = DemoPause.IsPaused || GameInputGate.IsBlocked;
            if (_voice != null) { if (paused) _voice.Pause(); else _voice.UnPause(); }
            if (paused) return;
            Tick(Time.deltaTime);
        }

        /// <summary>Advances the course by <paramref name="dt"/> seconds (also called by the tests).</summary>
        public void Tick(float dt)
        {
            if (Running == Course.None || _step == null) return;
            // Someone picked another mode (the menu): the tutorial ends there.
            if (_step.Practice && Aerial.Mode != _expected) { Stop(false); return; }
            _age += dt; _hintAge += dt;
            if (_ok || !_step.Practice && _step.Required == 0)
            {
                if (_age >= _wait) Go(_index + 1);
                return;
            }
            if (_step.Move == DemoMove.Jump && Aerial.Jump != null && Aerial.Jump.Jumps > _jumps) { _jumps = Aerial.Jump.Jumps; Done(); return; }
            if (_step.Move == DemoMove.Receive && Aerial.GradeSerial != _gradeSerial)
            {
                _gradeSerial = Aerial.GradeSerial;
                if (Aerial.LastGrade == "EXCELLENT" || Aerial.LastGrade == "GOOD") Done(); else Missed();
            }
        }

        void LateUpdate()
        {
            if (Running == Course.None || _panel == null) return;
            if (!_placed && Drill.Head != null) Place();
            PoseDemo(Time.time);
        }

        // ------------------------------------------------------------ voice

        /// <summary>Plays the line's clip (cutting the one playing); returns how long the text should stay.</summary>
        float Speak(string id, string text)
        {
            float read = ReadSeconds + text.Length * .06f;
            var clip = Clip(id);
            if (clip == null || _voice == null) return read;
            _voice.Stop();
            DemoNarration.Apply(_voice);
            _voice.clip = clip;
            if (Application.isPlaying) _voice.Play(); // EditMode verification never plays audio
            return clip.length + .8f;
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
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(640, 330); rect.localScale = Vector3.one * .001f;
            var back = go.AddComponent<Image>(); back.color = new Color(.02f, .04f, .07f, .82f); back.raycastTarget = false;
            _title = Label(rect, font, "Title", new Vector2(-40, 132), new Vector2(520, 50), 34, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            _counter = Label(rect, font, "Counter", new Vector2(250, 132), new Vector2(110, 50), 30, FontStyle.Bold, new Color(.35f, 1f, .45f), TextAnchor.MiddleRight);
            _body = Label(rect, font, "Body", new Vector2(0, 20), new Vector2(590, 170), 25, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);
            _hint = Label(rect, font, "Hint", new Vector2(0, -120), new Vector2(590, 70), 23, FontStyle.Normal, new Color(1f, .85f, .3f), TextAnchor.UpperLeft);
            _voice = go.AddComponent<AudioSource>(); _voice.playOnAwake = false; _voice.spatialBlend = 0f;
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
                demo.PlaceholderSkeleton = arm != null ? arm.PlaceholderSkeleton : null;
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

        void SetTexts(string title, string body, string hint, string counter)
        {
            _title.text = title; _body.text = body; _hint.text = hint; _counter.text = counter;
        }

        void ShowPanel(bool shown)
        {
            if (_panel == null) return;
            _panel.gameObject.SetActive(shown);
            if (!shown) foreach (var hand in _hands) if (hand != null) hand.gameObject.SetActive(false);
        }

        public Transform Panel => _panel;

        /// <summary>From the eye toward the net, then to the left, a little below the eyes, facing the eye.</summary>
        void Place()
        {
            var eye = Drill.Head.position;
            var forward = Vector3.ProjectOnPlane(Drill.CourtFrame != null ? Drill.CourtFrame.forward : Drill.Head.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var position = eye + forward * Distance + right * Side + Vector3.up * Rise;
            var look = position - eye;
            _panel.SetPositionAndRotation(position, Quaternion.LookRotation(look.sqrMagnitude > 1e-6f ? look.normalized : forward, Vector3.up));
            _placed = true;
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
            var move = Move;
            KeysFor(move, out _, out bool both);
            for (int side = 0; side < 2; side++)
            {
                var hand = _hands[side];
                if (hand == null) continue;
                bool left = side == 0;
                bool shown = _panel.gameObject.activeSelf && move != DemoMove.None && (both || !left) && Application.isPlaying && hand.EnsureBuilt();
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
            new Line("sb_intro", "スパイクとブロック", "スパイクとブロックを練習します。スパイクやブロックの出来しだいで、ラリーがそのまま続きます。"),
            new Line("sb_jump", "ジャンプ", "両手を胸の前から、一気に上へ振り上げるとジャンプします。一度ジャンプしてみましょう。"),
            new Line("sb_spike", "スパイク", "セッターがトスを上げます。ジャンプして、いちばん高いところで、ボールを上から下へたたきつけましょう。高い打点ほど、強いボールが打てます。"),
            new Line("sb_spike_auto", "スパイク", "セッターがトスを上げます。ジャンプは自動です。いちばん高いところで、ボールを上から下へたたきつけましょう。高い打点ほど、強いボールが打てます。"),
            new Line("sb_spike_hint", "", "ボールの少し後ろから、手のひらで上から強くたたきましょう。"),
            new Line("sb_block", "ブロック", "相手のスパイクをブロックします。ジャンプして両手を高く上げ、ネットの上へ手を出して、ボールを相手のコートへたたき落としましょう。"),
            new Line("sb_block_hint", "", "打ってくる相手の正面で、両手を広げて前へ出しましょう。"),
            new Line("sb_end", "試合開始", "スパイクは、相手のいないところへ強く。ブロックは、ボールを相手のコートの下へはじき返します。では、試合を始めましょう。"),
            new Line("rc_intro", "レシーブ", "レシーブを練習します。相手のサーブやスパイクを、セッターがトスしやすいように、高く上げましょう。"),
            new Line("rc_pass", "セッターへ返す", "緑の円と SETTER の札がある選手がセッターです。両腕をそろえて、セッターへ高く返しましょう。",
                "緑の円と、セッターの札がある選手がセッターです。両腕をそろえて、セッターへ高く返しましょう。"),
            new Line("rc_pass_hint", "", "腕をそろえて面を作り、ネット際のセッターへ、山なりに返しましょう。"),
            new Line("rc_end", "試合開始", "セッターへ余裕をもって返せると、ほぼスパイクが決まります。ほかの選手に返しても、攻撃は続けられます。レシーブが乱れると、点を取られたり、相手の強いスパイクを受けます。では、始めましょう。"),
            new Line("ok", "", "OKです！", "OKです。"),
        };

        public static Line Get(string id)
        {
            foreach (var line in Lines) if (line.Id == id) return line;
            throw new ArgumentException("No tutorial line " + id);
        }
    }
}
