using System;
using EnergyDuel.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// Runs the Energy Duel: Ready (a hand is seen) -> Tutorial (first time only) -> one 30 s round
    /// -> Result -> next match. Wires hands, shields, shots, the opponent and the HUD to the pure
    /// <see cref="EnergyDuelRules"/>, and reports every felt moment to <see cref="EnergyHapticRelay"/>.
    /// </summary>
    public sealed class EnergyDuelController : MonoBehaviour
    {
        public EnergyDuelSettings Settings = new EnergyDuelSettings();

        [Header("Scene")]
        public Transform Head;
        public EnergyHandInput LeftHand;
        public EnergyHandInput RightHand;
        public EnergyShield LeftShield;
        public EnergyShield RightShield;
        public EnergyHurtbox[] Hurtboxes = Array.Empty<EnergyHurtbox>();
        public EnergyShotPool Pool;
        public EnergyOpponent Opponent;
        public EnergyTutorialTarget TutorialTarget;

        [Header("HUD (fixed-size rects; text changes never move the layout)")]
        public Text TitleText;
        public Text BodyText;
        public Text HintText;
        public Text ScoreText;
        public Text StatusText;
        public EnergyHandIcon Icon;

        [Header("Flow")]
        [Tooltip("A hand must be seen this long before the tutorial starts.")]
        [Min(0f)] public float ReadyHoldSeconds = 1f;
        [Tooltip("Operator skip: Quest menu button, B / Y. Keyboard below for the editor.")]
        public InputActionProperty[] SkipActions = Array.Empty<InputActionProperty>();
        public Key SkipKey = Key.Space;

        [Header("Tutorial")]
        [Tooltip("Tutorial target: this far ahead of the head, this far below it.")]
        public float TargetDistance = 3f;
        public float TargetDrop = .25f;
        [Min(.1f)] public float TutorialShotSpeed = 4f;
        public Vector2 TutorialFireInterval = new Vector2(2.5f, 3f);

        [Header("Haptic gains")]
        [Tooltip("Release pulse gain at charge 0 / 1.")]
        public Vector2 ReleaseGain = new Vector2(.6f, 1f);
        [Tooltip("Opponent-hit pulse gain at charge 0 / 1.")]
        public Vector2 TargetHitGain = new Vector2(.5f, 1f);

        [Header("Tracking loss")]
        [Tooltip("Seconds with neither hand nor headset tracked before the opponent holds fire.")]
        [Min(0f)] public float LostHoldSeconds = .5f;

        private EnergyDuelRules _rules;
        private float _handSeen;
        private float _lostSeconds;
        private EnergyDuelPhase _shownPhase = (EnergyDuelPhase)(-1);
        private EnergyTutorialStep _shownStep = (EnergyTutorialStep)(-1);

        public EnergyDuelRules Rules => _rules;

        // Tallies for diagnostics (automated play-through) and the result screen.
        public int PlayerShots { get; private set; }
        public int OpponentHits { get; private set; }
        public int TargetHits { get; private set; }
        public int ShieldBlocks { get; private set; }
        public int ShieldBreaks { get; private set; }
        public int BodyHits { get; private set; }
        public int ArmHits { get; private set; }
        public int TutorialsCompleted { get; private set; }
        public int RoundsStarted { get; private set; }
        public int ResultsShown { get; private set; }
        public int RoundsStartedWithoutTutorial { get; private set; }

        public event Action<EnergyDuelPhase, EnergyTutorialStep> PhaseChanged;

        private void Awake()
        {
            _rules = new EnergyDuelRules(Settings);
            _rules.Changed += OnRulesChanged;
            LeftShield?.Bind(_rules.LeftShield, Settings.ShieldDurability);
            RightShield?.Bind(_rules.RightShield, Settings.ShieldDurability);
        }

        private void OnEnable()
        {
            foreach (var hand in new[] { LeftHand, RightHand })
            {
                if (hand == null) continue;
                hand.Fired += OnPlayerFired;
            }

            foreach (var shield in new[] { LeftShield, RightShield })
            {
                if (shield != null) shield.Struck += OnShieldStruck;
            }

            foreach (var hurtbox in Hurtboxes)
            {
                if (hurtbox != null) hurtbox.Hit += OnPlayerHurt;
            }

            if (Opponent != null) Opponent.Hit += OnOpponentHit;
            if (TutorialTarget != null) TutorialTarget.Hit += OnTargetHit;
            foreach (var skip in SkipActions)
            {
                var action = skip.action;
                if (action != null && !action.enabled) action.Enable();
            }
        }

        private void OnDisable()
        {
            foreach (var hand in new[] { LeftHand, RightHand })
            {
                if (hand != null) hand.Fired -= OnPlayerFired;
            }

            foreach (var shield in new[] { LeftShield, RightShield })
            {
                if (shield != null) shield.Struck -= OnShieldStruck;
            }

            foreach (var hurtbox in Hurtboxes)
            {
                if (hurtbox != null) hurtbox.Hit -= OnPlayerHurt;
            }

            if (Opponent != null) Opponent.Hit -= OnOpponentHit;
            if (TutorialTarget != null) TutorialTarget.Hit -= OnTargetHit;
        }

        private void Start() => Apply();

        private void Update()
        {
            var dt = Time.deltaTime;
            if (SkipPressed()) _rules.Skip();

            var handPresent = (LeftHand != null && LeftHand.Present) || (RightHand != null && RightHand.Present);
            if (_rules.Phase == EnergyDuelPhase.Ready)
            {
                _handSeen = handPresent ? _handSeen + dt : 0f;
                if (_handSeen >= ReadyHoldSeconds) _rules.Begin();
            }

            _rules.Tick(dt);

            _lostSeconds = handPresent || HeadTracked() ? 0f : _lostSeconds + dt;
            if (Opponent != null)
            {
                Opponent.HoldFire = _lostSeconds >= LostHoldSeconds;
                Opponent.SetHp01(_rules.OpponentHp / Mathf.Max(1f, Settings.OpponentHp));
            }

            var assist = _rules.Phase == EnergyDuelPhase.Tutorial && _rules.Step == EnergyTutorialStep.HitTarget && TutorialTarget != null
                ? TutorialTarget.transform
                : Opponent != null ? Opponent.transform : null;
            if (LeftHand != null) LeftHand.AssistTarget = assist;
            if (RightHand != null) RightHand.AssistTarget = assist;

            RefreshHud();
        }

        private bool SkipPressed()
        {
            foreach (var skip in SkipActions)
            {
                var action = skip.action;
                if (action != null && action.enabled && action.WasPressedThisFrame()) return true;
            }

            var keyboard = Keyboard.current;
            return keyboard != null && SkipKey != Key.None && keyboard[SkipKey].wasPressedThisFrame;
        }

        private static bool HeadTracked()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked;
        }

        // ------------------------------------------------------------ phase changes

        private void OnRulesChanged()
        {
            if (_rules.Phase == EnergyDuelPhase.Round && _shownPhase != EnergyDuelPhase.Round)
            {
                RoundsStarted++;
                if (_shownPhase == EnergyDuelPhase.Tutorial) TutorialsCompleted++;
                else RoundsStartedWithoutTutorial++;
            }

            if (_rules.Phase == EnergyDuelPhase.Result) ResultsShown++;
            Apply();
        }

        /// <summary>Puts the scene in the state the current phase / step needs.</summary>
        private void Apply()
        {
            var phase = _rules.Phase;
            var step = _rules.Step;
            var changed = phase != _shownPhase || step != _shownStep;
            _shownPhase = phase;
            _shownStep = step;

            var showTarget = phase == EnergyDuelPhase.Tutorial && step == EnergyTutorialStep.HitTarget;
            if (TutorialTarget != null)
            {
                if (showTarget && !TutorialTarget.gameObject.activeSelf) PlaceTarget();
                TutorialTarget.gameObject.SetActive(showTarget);
            }

            if (Opponent != null)
            {
                if (phase == EnergyDuelPhase.Round)
                {
                    Opponent.Mode = EnergyOpponentMode.Duel;
                    Opponent.FireInterval = _rules.OpponentFireInterval;
                    Opponent.ShotSpeed = _rules.OpponentShotSpeed;
                }
                else if (phase == EnergyDuelPhase.Tutorial && step == EnergyTutorialStep.Guard)
                {
                    Opponent.Mode = EnergyOpponentMode.Tutorial;
                    Opponent.FireInterval = TutorialFireInterval;
                    Opponent.ShotSpeed = TutorialShotSpeed;
                }
                else
                {
                    Opponent.Mode = EnergyOpponentMode.Idle;
                }

                if (changed) Opponent.ResetFireTimer();
            }

            var shieldsAllowed = phase == EnergyDuelPhase.Tutorial || phase == EnergyDuelPhase.Round;
            if (LeftShield != null) LeftShield.Allowed = shieldsAllowed;
            if (RightShield != null) RightShield.Allowed = shieldsAllowed;
            if (changed && phase != EnergyDuelPhase.Round && Pool != null && phase != EnergyDuelPhase.Tutorial) Pool.RetireAll();

            if (changed)
            {
                Debug.Log($"[EnergyDuel] {phase}{(phase == EnergyDuelPhase.Tutorial ? " " + step : "")}");
                PhaseChanged?.Invoke(phase, step);
            }

            RefreshHud();
        }

        private void PlaceTarget()
        {
            if (Head == null) return;
            var forward = EnergyHandGesture.Horizontal(Head.forward);
            TutorialTarget.transform.position = Head.position + (forward * TargetDistance) - (Vector3.up * TargetDrop);
        }

        // ------------------------------------------------------------ gameplay events

        private void OnPlayerFired(EnergyHandInput hand, Vector3 origin, Vector3 direction, float charge)
        {
            if (Pool == null) return;
            Pool.SpawnPlayer(hand.IsLeft, origin, direction, charge);
            PlayerShots++;
            EnergyHapticRelay.Report(EnergyHapticRelay.Sided(hand.IsLeft, EnergyHapticEvent.LeftRelease), origin, Mathf.Lerp(ReleaseGain.x, ReleaseGain.y, charge));
            _rules.ReportPlayerShot(charge);
        }

        private void OnOpponentHit(EnergyOpponent opponent, EnergyShot shot)
        {
            OpponentHits++;
            _rules.ReportOpponentHit(shot.Charge01);
            EnergyHapticRelay.Report(EnergyHapticEvent.TargetHit, shot.transform.position, Mathf.Lerp(TargetHitGain.x, TargetHitGain.y, shot.Charge01));
        }

        private void OnTargetHit(EnergyTutorialTarget target, EnergyShot shot)
        {
            TargetHits++;
            EnergyHapticRelay.Report(EnergyHapticEvent.TargetHit, shot.transform.position, Mathf.Lerp(TargetHitGain.x, TargetHitGain.y, shot.Charge01));
            _rules.ReportTutorialTargetHit();
        }

        private void OnShieldStruck(EnergyShield shield, EnergyShieldBlock result, EnergyShot shot)
        {
            ShieldBlocks++;
            if (result == EnergyShieldBlock.Broken) ShieldBreaks++;
            _rules.ReportShieldBlock();
        }

        private void OnPlayerHurt(EnergyHurtbox hurtbox, EnergyShot shot, Vector3 point)
        {
            switch (hurtbox.Kind)
            {
                case EnergyHurtboxKind.LeftArm:
                    ArmHits++;
                    EnergyHapticRelay.Report(EnergyHapticEvent.LeftArmCollide, point);
                    break;
                case EnergyHurtboxKind.RightArm:
                    ArmHits++;
                    EnergyHapticRelay.Report(EnergyHapticEvent.RightArmCollide, point);
                    break;
                default:
                    BodyHits++;
                    EnergyHapticRelay.Report(EnergyHapticEvent.BodyCollide, point);
                    _rules.ReportPlayerHit();
                    break;
            }
        }

        // ------------------------------------------------------------ HUD

        private void RefreshHud()
        {
            if (_rules == null) return;
            string title, body, hint = string.Empty;
            var icon = EnergyIconPose.None;
            switch (_rules.Phase)
            {
                case EnergyDuelPhase.Ready:
                    title = "ENERGY DUEL";
                    body = "Show your hands to start";
                    icon = EnergyIconPose.Open;
                    break;
                case EnergyDuelPhase.Tutorial:
                    TutorialText(out title, out body, out hint, out icon);
                    break;
                case EnergyDuelPhase.Round:
                    title = Settings.RoundCount > 1 ? $"ROUND {_rules.RoundIndex + 1}/{Settings.RoundCount}" : "DUEL";
                    body = "Fist to charge - punch and open to fire - open hand up to guard";
                    break;
                default:
                    title = _rules.MatchWon ? "YOU WIN" : "YOU LOSE";
                    body = $"Damage {_rules.DamageDealt,3:0}   Hits taken {_rules.HitsTaken,2}";
                    hint = $"Next duel in {Mathf.CeilToInt(Mathf.Max(0f, _rules.ResultRemaining))}";
                    break;
            }

            SetText(TitleText, title);
            SetText(BodyText, body);
            SetText(HintText, hint);
            SetText(ScoreText, _rules.Phase == EnergyDuelPhase.Round || _rules.Phase == EnergyDuelPhase.Result
                ? $"HP {_rules.OpponentHp,3:0}   TIME {_rules.RoundRemaining,4:0.0}   HITS {_rules.HitsTaken,2}"
                : string.Empty);
            SetText(StatusText, Opponent != null && Opponent.HoldFire ? "TRACKING LOST - PAUSED" : string.Empty);
            if (Icon != null) Icon.Show(icon);
        }

        private void TutorialText(out string title, out string body, out string hint, out EnergyIconPose icon)
        {
            var strong = _rules.HintEscalated;
            switch (_rules.Step)
            {
                case EnergyTutorialStep.HitTarget:
                    title = "STEP 1/3  SHOOT";
                    body = "Make a fist, punch forward, open your hand";
                    hint = strong ? "Punch further forward, then open wide" : string.Empty;
                    // Alternate fist / open so the motion reads without words.
                    icon = Mathf.Repeat(Time.time, 1.6f) < .8f ? EnergyIconPose.Fist : EnergyIconPose.Open;
                    break;
                case EnergyTutorialStep.ChargedShot:
                    title = "STEP 2/3  CHARGE";
                    body = "Hold the fist until the orb is big, then fire";
                    hint = strong ? "Keep the fist closed for over a second" : string.Empty;
                    icon = EnergyIconPose.Fist;
                    break;
                default:
                    title = "STEP 3/3  GUARD";
                    body = "Open hand, fingers up, palm out - block the shot";
                    hint = strong ? "Raise the open hand in front of your face" : string.Empty;
                    icon = EnergyIconPose.Guard;
                    break;
            }
        }

        private static void SetText(Text target, string value)
        {
            if (target != null && target.text != value) target.text = value;
        }
    }
}
