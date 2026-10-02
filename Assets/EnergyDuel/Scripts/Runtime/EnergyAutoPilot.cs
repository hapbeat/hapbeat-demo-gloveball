using EnergyDuel.Core;
using UnityEngine;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// Automated player for unattended verification. Turns the head toward whatever it should aim at
    /// and injects synthetic hand frames (curl, palm, fingers, position) into both
    /// <see cref="EnergyHandInput"/>s, so the real gesture logic runs end to end: punch to fire,
    /// charge, guard, and stand still to get hit. Only ever added at runtime by the batch runner.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class EnergyAutoPilot : MonoBehaviour
    {
        private const float Fist = .95f;
        private const float Open = .05f;

        public EnergyDuelController Duel;

        [Tooltip("Round plan, repeated: attack, guard (to see blocks and a break), stand open (to get hit).")]
        public float AttackSeconds = 6f;
        public float GuardSeconds = 6f;
        public float ExposedSeconds = 3f;

        private float _clock;
        private float _punchClock;
        private float _chargeSeconds;

        private void Update()
        {
            if (Duel == null || Duel.Head == null) return;
            var dt = Time.deltaTime;
            _clock += dt;
            var rules = Duel.Rules;
            var head = Duel.Head;

            var aimAt = AimPoint(rules);
            if (aimAt.HasValue)
            {
                var look = aimAt.Value - head.position;
                if (look.sqrMagnitude > 1e-4f) head.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }

            var left = Rest(true);
            var right = Rest(false);
            switch (rules.Phase)
            {
                case EnergyDuelPhase.Tutorial when rules.Step == EnergyTutorialStep.HitTarget:
                    right = Punch(dt, .2f);
                    break;
                case EnergyDuelPhase.Tutorial when rules.Step == EnergyTutorialStep.ChargedShot:
                    right = Punch(dt, 1.3f);
                    break;
                case EnergyDuelPhase.Tutorial:
                    left = Guard();
                    break;
                case EnergyDuelPhase.Round:
                    var cycle = Mathf.Repeat(_clock, AttackSeconds + GuardSeconds + ExposedSeconds);
                    if (cycle < AttackSeconds) right = Punch(dt, 1.3f);
                    else if (cycle < AttackSeconds + GuardSeconds) left = Guard();
                    else right = Exposed();
                    break;
            }

            Duel.LeftHand.Inject(left);
            Duel.RightHand.Inject(right);
        }

        private Vector3? AimPoint(EnergyDuelRules rules)
        {
            if (rules.Phase == EnergyDuelPhase.Tutorial && rules.Step == EnergyTutorialStep.HitTarget && Duel.TutorialTarget != null)
            {
                return Duel.TutorialTarget.transform.position;
            }

            return Duel.Opponent != null ? Duel.Opponent.transform.position : (Vector3?)null;
        }

        private EnergyHandFrame Frame(Vector3 position, float curl, Vector3 palm, Vector3 fingers) => new EnergyHandFrame
        {
            Tracked = true,
            Curl = curl,
            HandPosition = position,
            PalmNormal = palm,
            FingerDirection = fingers,
            HeadPosition = Duel.Head.position,
            HeadForward = Duel.Head.forward,
        };

        private Vector3 Forward => EnergyHandGesture.Horizontal(Duel.Head.forward);
        private Vector3 Right => Vector3.Cross(Vector3.up, Forward);

        private EnergyHandFrame Rest(bool left)
        {
            var side = left ? -1f : 1f;
            var position = Duel.Head.position + (Forward * .15f) + (Right * (.2f * side)) - (Vector3.up * .55f);
            return Frame(position, Open, Vector3.down, Forward);
        }

        /// <summary>Open hand held low and forward, so nothing covers the body.</summary>
        private EnergyHandFrame Exposed() => Rest(false);

        private EnergyHandFrame Guard()
        {
            var position = Duel.Head.position + (Forward * .35f) - (Vector3.up * .2f);
            return Frame(position, Open, Forward, Vector3.up);
        }

        /// <summary>Rest -> fist for the charge -> push forward at 2.5 m/s -> open (fires) -> rest.</summary>
        private EnergyHandFrame Punch(float dt, float chargeSeconds)
        {
            _chargeSeconds = chargeSeconds;
            _punchClock += dt;
            var restSeconds = .3f;
            var pushSeconds = .14f;
            var start = Duel.Head.position + (Forward * .15f) + (Right * .15f) - (Vector3.up * .3f);
            var t = _punchClock;
            if (t < restSeconds) return Frame(start, Open, Vector3.down, Forward);
            t -= restSeconds;
            if (t < _chargeSeconds) return Frame(start, Fist, Vector3.down, Forward);
            t -= _chargeSeconds;
            if (t < pushSeconds) return Frame(start + (Forward * (2.5f * t)), Fist, Vector3.down, Forward);
            _punchClock = 0f;
            return Frame(start + (Forward * (2.5f * pushSeconds)), Open, Vector3.down, Forward);
        }
    }
}
