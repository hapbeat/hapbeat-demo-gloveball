using System;
using UnityEngine;

namespace EnergyDuel.Core
{
    public enum EnergyDuelPhase { Ready, Tutorial, Round, Result }

    public enum EnergyTutorialStep { HitTarget, ChargedShot, Guard, Done }

    public enum EnergyTutorialMode { FirstSessionOnly, Always, Never }

    [Serializable]
    public sealed class EnergyDuelSettings
    {
        [Header("Flow")]
        public EnergyTutorialMode Tutorial = EnergyTutorialMode.FirstSessionOnly;
        [Min(1f)] public float RoundSeconds = 30f;
        [Min(1)] public int RoundCount = 1;
        [Tooltip("Seconds the result stays up before the next match starts (tutorial skipped).")]
        [Min(0f)] public float ResultSeconds = 6f;

        [Header("Tutorial")]
        [Tooltip("Step 2 succeeds with a shot charged at least this much.")]
        [Range(0f, 1f)] public float TutorialChargeRequired = .8f;
        [Tooltip("Seconds without success before the hint gets more explicit.")]
        [Min(0f)] public float TutorialHintSeconds = 8f;
        [Tooltip("Exhibition safety net: a step advances by itself after this many seconds. 0 = never.")]
        [Min(0f)] public float TutorialSafetyTimeoutSeconds;

        [Header("Damage")]
        [Min(1f)] public float OpponentHp = 100f;
        [Min(0f)] public float MinimumDamage = 5f;
        [Min(0f)] public float MaximumDamage = 25f;
        [Tooltip("Time-out verdict: damage dealt - hits taken x this. Positive wins.")]
        [Min(0f)] public float HitPenalty = 10f;

        [Header("Opponent difficulty, first round -> last round")]
        public Vector2 FirstRoundFireInterval = new Vector2(1.8f, 2.5f);
        public Vector2 LastRoundFireInterval = new Vector2(1.2f, 1.6f);
        [Min(.1f)] public float FirstRoundShotSpeed = 6f;
        [Min(.1f)] public float LastRoundShotSpeed = 10f;

        [Header("Shield")]
        [Min(1)] public int ShieldDurability = 3;
        [Min(0f)] public float ShieldCooldownSeconds = 4f;
    }

    public enum EnergyShieldBlock { Ignored, Blocked, Broken }

    /// <summary>One hand's shield: durability in blocks, then a cooldown during which it cannot be raised.</summary>
    public sealed class EnergyShieldState
    {
        private readonly EnergyDuelSettings _settings;

        public EnergyShieldState(EnergyDuelSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Durability = settings.ShieldDurability;
        }

        public int Durability { get; private set; }
        public float CooldownRemaining { get; private set; }
        public bool CanGuard => CooldownRemaining <= 0f;

        public EnergyShieldBlock Block()
        {
            if (!CanGuard)
            {
                return EnergyShieldBlock.Ignored;
            }

            Durability--;
            if (Durability > 0)
            {
                return EnergyShieldBlock.Blocked;
            }

            Durability = 0;
            CooldownRemaining = Mathf.Max(1e-4f, _settings.ShieldCooldownSeconds);
            return EnergyShieldBlock.Broken;
        }

        public void Tick(float dt)
        {
            if (CooldownRemaining <= 0f)
            {
                return;
            }

            CooldownRemaining -= Mathf.Max(0f, dt);
            if (CooldownRemaining <= 0f)
            {
                CooldownRemaining = 0f;
                Durability = _settings.ShieldDurability;
            }
        }

        public void Reset()
        {
            CooldownRemaining = 0f;
            Durability = _settings.ShieldDurability;
        }
    }

    /// <summary>
    /// Match flow: Ready -> Tutorial (first session only by default) -> Round x N -> Result -> next match.
    /// Tutorial steps advance only on success, an operator skip, or the optional safety timeout,
    /// so the demo never waits on a clock nor on an impossible state.
    /// </summary>
    public sealed class EnergyDuelRules
    {
        private readonly EnergyDuelSettings _settings;
        private bool _tutorialDone;

        public EnergyDuelRules(EnergyDuelSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            LeftShield = new EnergyShieldState(settings);
            RightShield = new EnergyShieldState(settings);
            OpponentHp = settings.OpponentHp;
        }

        public EnergyDuelSettings Settings => _settings;
        public EnergyDuelPhase Phase { get; private set; } = EnergyDuelPhase.Ready;
        public EnergyTutorialStep Step { get; private set; } = EnergyTutorialStep.Done;
        public float StepSeconds { get; private set; }
        public bool HintEscalated => Phase == EnergyDuelPhase.Tutorial && _settings.TutorialHintSeconds > 0f && StepSeconds >= _settings.TutorialHintSeconds;

        public int RoundIndex { get; private set; }
        public float RoundRemaining { get; private set; }
        public float ResultRemaining { get; private set; }
        public float OpponentHp { get; private set; }
        public float DamageDealt { get; private set; }
        public int HitsTaken { get; private set; }
        public int RoundsWon { get; private set; }
        public int RoundsLost { get; private set; }
        public bool LastRoundWon { get; private set; }
        public int MatchesPlayed { get; private set; }

        public EnergyShieldState LeftShield { get; }
        public EnergyShieldState RightShield { get; }

        public float TimeoutScore => DamageDealt - (HitsTaken * _settings.HitPenalty);
        public bool MatchWon => RoundsWon > RoundsLost;

        /// <summary>Raised once per change of phase, step or round.</summary>
        public event Action Changed;

        /// <summary>Leaves Ready. Called when a hand is first seen (or by the operator).</summary>
        public void Begin()
        {
            if (Phase != EnergyDuelPhase.Ready)
            {
                return;
            }

            if (TutorialWanted())
            {
                Phase = EnergyDuelPhase.Tutorial;
                Step = EnergyTutorialStep.HitTarget;
                StepSeconds = 0f;
                Changed?.Invoke();
                return;
            }

            StartMatch();
        }

        public void Tick(float dt)
        {
            dt = Mathf.Max(0f, dt);
            LeftShield.Tick(dt);
            RightShield.Tick(dt);
            switch (Phase)
            {
                case EnergyDuelPhase.Tutorial:
                    StepSeconds += dt;
                    if (_settings.TutorialSafetyTimeoutSeconds > 0f && StepSeconds >= _settings.TutorialSafetyTimeoutSeconds)
                    {
                        AdvanceStep();
                    }

                    break;
                case EnergyDuelPhase.Round:
                    RoundRemaining -= dt;
                    if (RoundRemaining <= 0f)
                    {
                        RoundRemaining = 0f;
                        EndRound(TimeoutScore > 0f);
                    }

                    break;
                case EnergyDuelPhase.Result:
                    ResultRemaining -= dt;
                    if (ResultRemaining <= 0f)
                    {
                        Restart();
                    }

                    break;
            }
        }

        /// <summary>Operator skip: next tutorial step, or out of Ready. Ignored in a round.</summary>
        public void Skip()
        {
            if (Phase == EnergyDuelPhase.Ready)
            {
                Begin();
            }
            else if (Phase == EnergyDuelPhase.Tutorial)
            {
                AdvanceStep();
            }
        }

        // ------------------------------------------------------------ tutorial

        public void ReportTutorialTargetHit()
        {
            if (Phase == EnergyDuelPhase.Tutorial && Step == EnergyTutorialStep.HitTarget) AdvanceStep();
        }

        public void ReportPlayerShot(float charge01)
        {
            if (Phase == EnergyDuelPhase.Tutorial && Step == EnergyTutorialStep.ChargedShot && charge01 >= _settings.TutorialChargeRequired)
            {
                AdvanceStep();
            }
        }

        public void ReportShieldBlock()
        {
            if (Phase == EnergyDuelPhase.Tutorial && Step == EnergyTutorialStep.Guard) AdvanceStep();
        }

        private void AdvanceStep()
        {
            Step++;
            StepSeconds = 0f;
            if (Step >= EnergyTutorialStep.Done)
            {
                _tutorialDone = true;
                StartMatch();
                return;
            }

            Changed?.Invoke();
        }

        private bool TutorialWanted()
        {
            switch (_settings.Tutorial)
            {
                case EnergyTutorialMode.Always: return true;
                case EnergyTutorialMode.Never: return false;
                default: return !_tutorialDone;
            }
        }

        // ------------------------------------------------------------ match

        public float DamageFor(float charge01) =>
            Mathf.Lerp(_settings.MinimumDamage, _settings.MaximumDamage, Mathf.Clamp01(charge01));

        /// <summary>A player shot reached the opponent. Returns the damage applied (0 outside a round).</summary>
        public float ReportOpponentHit(float charge01)
        {
            if (Phase != EnergyDuelPhase.Round)
            {
                return 0f;
            }

            var damage = Mathf.Min(OpponentHp, DamageFor(charge01));
            OpponentHp -= damage;
            DamageDealt += damage;
            if (OpponentHp <= 0f)
            {
                OpponentHp = 0f;
                EndRound(true);
            }

            return damage;
        }

        /// <summary>An opponent shot hit the player's body or head.</summary>
        public void ReportPlayerHit()
        {
            if (Phase == EnergyDuelPhase.Round)
            {
                HitsTaken++;
            }
        }

        public bool OpponentActive => Phase == EnergyDuelPhase.Round;

        /// <summary>0 on the first round, 1 on the last.</summary>
        public float Difficulty01 => _settings.RoundCount > 1 ? Mathf.Clamp01(RoundIndex / (float)(_settings.RoundCount - 1)) : 0f;

        public Vector2 OpponentFireInterval => Vector2.Lerp(_settings.FirstRoundFireInterval, _settings.LastRoundFireInterval, Difficulty01);

        public float OpponentShotSpeed => Mathf.Lerp(_settings.FirstRoundShotSpeed, _settings.LastRoundShotSpeed, Difficulty01);

        private void StartMatch()
        {
            RoundsWon = 0;
            RoundsLost = 0;
            StartRound(0);
        }

        private void StartRound(int index)
        {
            Phase = EnergyDuelPhase.Round;
            Step = EnergyTutorialStep.Done;
            RoundIndex = index;
            RoundRemaining = _settings.RoundSeconds;
            OpponentHp = _settings.OpponentHp;
            DamageDealt = 0f;
            HitsTaken = 0;
            LeftShield.Reset();
            RightShield.Reset();
            Changed?.Invoke();
        }

        private void EndRound(bool won)
        {
            LastRoundWon = won;
            if (won) RoundsWon++;
            else RoundsLost++;

            if (RoundIndex + 1 < _settings.RoundCount)
            {
                StartRound(RoundIndex + 1);
                return;
            }

            Phase = EnergyDuelPhase.Result;
            ResultRemaining = _settings.ResultSeconds;
            MatchesPlayed++;
            Changed?.Invoke();
        }

        /// <summary>Next match straight away; the tutorial runs again only if configured to.</summary>
        public void Restart()
        {
            Phase = EnergyDuelPhase.Ready;
            Begin();
        }
    }
}
