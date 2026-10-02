using EnergyDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace EnergyDuel.Tests
{
    public sealed class EnergyDuelRulesTests
    {
        private static void Run(EnergyDuelRules rules, float seconds)
        {
            for (var t = 0f; t < seconds; t += .05f) rules.Tick(.05f);
        }

        private static EnergyDuelRules InRound(EnergyDuelSettings settings = null)
        {
            settings ??= new EnergyDuelSettings { Tutorial = EnergyTutorialMode.Never };
            var rules = new EnergyDuelRules(settings);
            rules.Begin();
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Round));
            return rules;
        }

        [Test]
        public void TutorialAdvancesOnlyOnSuccess()
        {
            var rules = new EnergyDuelRules(new EnergyDuelSettings());
            rules.Begin();
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Tutorial));
            Assert.That(rules.Step, Is.EqualTo(EnergyTutorialStep.HitTarget));

            Run(rules, 120f);
            Assert.That(rules.Step, Is.EqualTo(EnergyTutorialStep.HitTarget), "no safety timeout by default");
            Assert.That(rules.HintEscalated, Is.True);

            rules.ReportPlayerShot(1f);
            rules.ReportShieldBlock();
            Assert.That(rules.Step, Is.EqualTo(EnergyTutorialStep.HitTarget), "other steps' successes do not count");

            rules.ReportTutorialTargetHit();
            Assert.That(rules.Step, Is.EqualTo(EnergyTutorialStep.ChargedShot));
            Assert.That(rules.HintEscalated, Is.False);

            rules.ReportPlayerShot(.79f);
            Assert.That(rules.Step, Is.EqualTo(EnergyTutorialStep.ChargedShot), "charge below 0.8");
            rules.ReportPlayerShot(.8f);
            Assert.That(rules.Step, Is.EqualTo(EnergyTutorialStep.Guard));

            rules.ReportShieldBlock();
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Round));
            Assert.That(rules.RoundRemaining, Is.EqualTo(30f));
        }

        [Test]
        public void OperatorSkipAdvancesEveryStep()
        {
            var rules = new EnergyDuelRules(new EnergyDuelSettings());
            rules.Skip();
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Tutorial));
            rules.Skip();
            rules.Skip();
            Assert.That(rules.Step, Is.EqualTo(EnergyTutorialStep.Guard));
            rules.Skip();
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Round));
            rules.Skip();
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Round), "skip never ends a round");
        }

        [Test]
        public void SafetyTimeoutAdvancesWhenConfigured()
        {
            var rules = new EnergyDuelRules(new EnergyDuelSettings { TutorialSafetyTimeoutSeconds = 5f });
            rules.Begin();
            Run(rules, 5.1f);
            Assert.That(rules.Step, Is.EqualTo(EnergyTutorialStep.ChargedShot));
        }

        [Test]
        public void SecondMatchSkipsTheTutorialAndPlaysOneThirtySecondRound()
        {
            var rules = new EnergyDuelRules(new EnergyDuelSettings());
            rules.Begin();
            rules.Skip(); rules.Skip(); rules.Skip();
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Round));

            Run(rules, 30.2f);
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Result), "a single round by default");
            Run(rules, 6.2f);
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Round));
            Assert.That(rules.RoundIndex, Is.Zero);
            Assert.That(rules.RoundRemaining, Is.GreaterThan(29f));
        }

        [Test]
        public void TutorialModeAlwaysRepeatsIt()
        {
            var rules = new EnergyDuelRules(new EnergyDuelSettings { Tutorial = EnergyTutorialMode.Always });
            rules.Begin();
            rules.Skip(); rules.Skip(); rules.Skip();
            Run(rules, 37f);
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Tutorial));
        }

        [Test]
        public void DamageIsLinearInChargeAndZeroHpWinsTheRound()
        {
            var rules = InRound();
            Assert.That(rules.DamageFor(0f), Is.EqualTo(5f));
            Assert.That(rules.DamageFor(.5f), Is.EqualTo(15f));
            Assert.That(rules.DamageFor(1f), Is.EqualTo(25f));

            Assert.That(rules.ReportOpponentHit(.5f), Is.EqualTo(15f));
            Assert.That(rules.OpponentHp, Is.EqualTo(85f));
            for (var i = 0; i < 3; i++) rules.ReportOpponentHit(1f);
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Round));
            Assert.That(rules.ReportOpponentHit(1f), Is.EqualTo(10f), "damage is capped at the remaining HP");
            Assert.That(rules.Phase, Is.EqualTo(EnergyDuelPhase.Result));
            Assert.That(rules.LastRoundWon, Is.True);
            Assert.That(rules.MatchWon, Is.True);
        }

        [Test]
        public void TimeOutIsJudgedOnDamageMinusHits()
        {
            var win = InRound();
            win.ReportOpponentHit(1f); // 25
            win.ReportPlayerHit();     // -10
            Run(win, 30.2f);
            Assert.That(win.Phase, Is.EqualTo(EnergyDuelPhase.Result));
            Assert.That(win.TimeoutScore, Is.EqualTo(15f));
            Assert.That(win.LastRoundWon, Is.True);

            var lose = InRound();
            lose.ReportOpponentHit(0f); // 5
            lose.ReportPlayerHit();     // -10
            Run(lose, 30.2f);
            Assert.That(lose.LastRoundWon, Is.False);
            Assert.That(lose.MatchWon, Is.False);
        }

        [Test]
        public void LaterRoundsFireFasterAndMoreOften()
        {
            var rules = InRound(new EnergyDuelSettings { Tutorial = EnergyTutorialMode.Never, RoundCount = 3 });
            var firstSpeed = rules.OpponentShotSpeed;
            var firstInterval = rules.OpponentFireInterval;
            Run(rules, 30.2f);
            Assert.That(rules.RoundIndex, Is.EqualTo(1));
            Run(rules, 30.2f);
            Assert.That(rules.RoundIndex, Is.EqualTo(2));
            Assert.That(rules.OpponentShotSpeed, Is.GreaterThan(firstSpeed));
            Assert.That(rules.OpponentFireInterval.y, Is.LessThan(firstInterval.y));
            Assert.That(firstSpeed, Is.EqualTo(6f));
            Assert.That(rules.OpponentShotSpeed, Is.EqualTo(10f));
        }

        [Test]
        public void ShieldBreaksOnTheThirdBlockAndRecoversAfterCooldown()
        {
            var rules = InRound();
            var shield = rules.LeftShield;
            Assert.That(shield.Block(), Is.EqualTo(EnergyShieldBlock.Blocked));
            Assert.That(shield.Block(), Is.EqualTo(EnergyShieldBlock.Blocked));
            Assert.That(shield.Block(), Is.EqualTo(EnergyShieldBlock.Broken));
            Assert.That(shield.CanGuard, Is.False);
            Assert.That(shield.Block(), Is.EqualTo(EnergyShieldBlock.Ignored));
            Assert.That(rules.RightShield.CanGuard, Is.True, "shields are per hand");

            Run(rules, 3.9f);
            Assert.That(shield.CanGuard, Is.False);
            Run(rules, .2f);
            Assert.That(shield.CanGuard, Is.True);
            Assert.That(shield.Durability, Is.EqualTo(3));
        }
    }

    public sealed class OpponentMovePlannerTests
    {
        private static readonly Bounds Court = new Bounds(new Vector3(1.5f, 1.4f, 0f), new Vector3(4.5f, .4f, 3f));

        [Test]
        public void SameSeedGivesTheSameSequence()
        {
            var a = new OpponentMovePlanner(42, new OpponentMoveSettings());
            var b = new OpponentMovePlanner(42, new OpponentMoveSettings());
            var c = new OpponentMovePlanner(7, new OpponentMoveSettings());
            var pa = Court.center;
            var pb = Court.center;
            var differs = false;
            for (var i = 0; i < 50; i++)
            {
                var sa = a.Next(pa, Court);
                var sb = b.Next(pb, Court);
                var sc = c.Next(pa, Court);
                Assert.That(sa.Target, Is.EqualTo(sb.Target));
                Assert.That(sa.Speed, Is.EqualTo(sb.Speed));
                Assert.That(sa.PauseSeconds, Is.EqualTo(sb.PauseSeconds));
                differs |= sa.Target != sc.Target;
                pa = sa.Target;
                pb = sb.Target;
            }

            Assert.That(differs, Is.True);
        }

        [Test]
        public void EveryWaypointStaysInsideTheCourt()
        {
            var settings = new OpponentMoveSettings();
            var planner = new OpponentMovePlanner(3, settings);
            var p = Court.center;
            var expanded = Court;
            expanded.Expand(1e-4f);
            for (var i = 0; i < 500; i++)
            {
                var step = planner.Next(p, Court);
                Assert.That(expanded.Contains(step.Target), Is.True, $"{step.Target}");
                Assert.That(step.Speed, Is.InRange(settings.MinimumSpeed, settings.MaximumSpeed));
                Assert.That(step.PauseSeconds, Is.InRange(0f, settings.MaximumPauseSeconds));
                p = step.Target;
            }
        }
    }
}
