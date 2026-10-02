using EnergyDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace EnergyDuel.Tests
{
    public sealed class EnergyHandGestureTests
    {
        private const float Dt = .01f;
        private const float FistCurl = .9f;
        private const float OpenCurl = .1f;
        private static readonly Vector3 Head = new Vector3(0f, 1.6f, 0f);

        private EnergyHandGestureSettings _settings;
        private EnergyHandGesture _gesture;
        private Vector3 _hand;
        private int _fires;

        [SetUp]
        public void SetUp()
        {
            _settings = new EnergyHandGestureSettings();
            _gesture = new EnergyHandGesture(_settings);
            _hand = Head + new Vector3(0f, -.35f, .20f);
            _fires = 0;
        }

        private EnergyHandFrame Frame(float curl, bool tracked = true, Vector3? palm = null, Vector3? fingers = null, Vector3? headForward = null) => new EnergyHandFrame
        {
            Dt = Dt,
            Tracked = tracked,
            Curl = curl,
            HandPosition = _hand,
            PalmNormal = palm ?? Vector3.down,
            FingerDirection = fingers ?? Vector3.forward,
            HeadPosition = Head,
            HeadForward = headForward ?? Vector3.forward,
        };

        private void Step(float curl, Vector3 velocity = default, int frames = 1, bool tracked = true, Vector3? palm = null, Vector3? fingers = null, Vector3? headForward = null)
        {
            for (var i = 0; i < frames; i++)
            {
                _hand += velocity * Dt;
                _gesture.Tick(Frame(curl, tracked, palm, fingers, headForward));
                if (_gesture.FireNow) _fires++;
            }
        }

        // ------------------------------------------------------------------ shape

        [Test]
        public void ShapeHysteresisIgnoresJitterAroundTheMiddle()
        {
            Step(OpenCurl);
            Assert.That(_gesture.Shape, Is.EqualTo(EnergyHandShape.Open));
            foreach (var curl in new[] { .5f, .6f, .55f, .65f, .45f, .58f })
            {
                Step(curl);
                Assert.That(_gesture.Shape, Is.EqualTo(EnergyHandShape.Open), $"curl {curl}");
            }

            Step(FistCurl);
            foreach (var curl in new[] { .5f, .6f, .55f, .45f, .52f })
            {
                Step(curl);
                Assert.That(_gesture.Shape, Is.EqualTo(EnergyHandShape.Fist), $"curl {curl}");
            }
        }

        [Test]
        public void UnknownCurlKeepsThePreviousShape()
        {
            Assert.That(_gesture.Shape, Is.EqualTo(EnergyHandShape.Unknown));
            Step(FistCurl);
            Step(float.NaN, frames: 5);
            Assert.That(_gesture.Shape, Is.EqualTo(EnergyHandShape.Fist));
            Step(OpenCurl);
            Step(float.NaN, frames: 5);
            Assert.That(_gesture.Shape, Is.EqualTo(EnergyHandShape.Open));
        }

        // ----------------------------------------------------------------- charge

        [Test]
        public void FistFromNothingStartsACharge()
        {
            Step(FistCurl);
            Assert.That(_gesture.ChargeStarted, Is.True);
            Assert.That(_gesture.Charging, Is.True);
            Step(FistCurl, frames: 60);
            Assert.That(_gesture.Charge01, Is.EqualTo(.5f).Within(.02f), "0.6 s of a 1.2 s charge");
        }

        [Test]
        public void ChargeFullIsReportedOnce()
        {
            Step(FistCurl);
            var full = 0;
            for (var i = 0; i < 200; i++)
            {
                Step(FistCurl);
                if (_gesture.ChargeFull) full++;
            }

            Assert.That(full, Is.EqualTo(1));
            Assert.That(_gesture.Charge01, Is.EqualTo(1f));
        }

        [Test]
        public void OpeningWithoutAPushCancelsAndDoesNotFire()
        {
            Step(FistCurl, frames: 30);
            Step(OpenCurl);
            Assert.That(_gesture.ChargeCanceled, Is.True);
            Assert.That(_gesture.FireNow, Is.False);
            Assert.That(_gesture.Charging, Is.False);
            Assert.That(_fires, Is.Zero);
        }

        // ------------------------------------------------------------------- push

        [Test]
        public void SlowForwardMotionIsNotAPush()
        {
            Step(FistCurl);
            Step(FistCurl, Vector3.forward * 1.0f, frames: 20);
            Assert.That(_gesture.Pushing, Is.False);
        }

        [Test]
        public void FastForwardMotionHeldForSixtyMillisecondsIsAPush()
        {
            Step(FistCurl);
            Step(FistCurl); // first velocity sample needs a previous frame
            Step(FistCurl, Vector3.forward * 1.5f, frames: 5);
            Assert.That(_gesture.Pushing, Is.False, "50 ms is not enough");
            Step(FistCurl, Vector3.forward * 1.5f);
            Assert.That(_gesture.Pushing, Is.True);
        }

        [Test]
        public void ZeroLengthFramesDoNotInterruptAPush()
        {
            Step(FistCurl, frames: 2);
            for (var i = 0; i < 6; i++)
            {
                Step(FistCurl, Vector3.forward * 1.5f);
                _gesture.Tick(new EnergyHandFrame { Dt = 0f, Tracked = true, Curl = FistCurl, HandPosition = _hand, HeadPosition = Head, HeadForward = Vector3.forward });
            }

            Assert.That(_gesture.Pushing, Is.True);
        }

        [Test]
        public void FastSwingAcrossTheHeadForwardIsNotAPush()
        {
            Step(FistCurl);
            Step(FistCurl, Vector3.right * 4f, frames: 20);
            Step(FistCurl, Vector3.up * 4f, frames: 10);
            Assert.That(_gesture.Pushing, Is.False);
            Assert.That(_fires, Is.Zero);
        }

        // ------------------------------------------------------------------- fire

        [Test]
        public void PushThenOpenFiresOnce()
        {
            Step(FistCurl, frames: 2);
            Step(FistCurl, Vector3.forward * 1.5f, frames: 7);
            Assert.That(_gesture.Pushing, Is.True);
            Step(OpenCurl);
            Assert.That(_fires, Is.EqualTo(1));
            Step(OpenCurl, frames: 30);
            Assert.That(_fires, Is.EqualTo(1));
        }

        [Test]
        public void PushReachingFireDistanceFiresOnce()
        {
            Step(FistCurl, frames: 2);
            // Fist kept closed: 2 m/s carries the hand from 0.20 m to 0.50 m in 150 ms (< timeout).
            for (var i = 0; i < 40 && _fires == 0; i++) Step(FistCurl, Vector3.forward * 2f);
            Assert.That(_fires, Is.EqualTo(1));
            Assert.That(_gesture.Reach, Is.GreaterThanOrEqualTo(_settings.FireReach - .03f));
            Step(FistCurl, Vector3.forward * 2f, frames: 30);
            Assert.That(_fires, Is.EqualTo(1));
        }

        [Test]
        public void PushTimeoutFiresOnce()
        {
            _settings.FireReach = 5f; // take (b) out of play
            Step(FistCurl, frames: 2);
            Step(FistCurl, Vector3.forward * 1.5f, frames: 6);
            Assert.That(_gesture.Pushing, Is.True);
            // The hand stops but stays a fist: the timeout fires 250 ms after the push.
            Step(FistCurl, frames: 24);
            Assert.That(_fires, Is.Zero);
            Step(FistCurl, frames: 2);
            Assert.That(_fires, Is.EqualTo(1));
            Step(FistCurl, frames: 50);
            Assert.That(_fires, Is.EqualTo(1));
        }

        [Test]
        public void AnUnchargedPunchStillFiresASmallShot()
        {
            Step(FistCurl);
            Step(FistCurl, Vector3.forward * 1.5f, frames: 7);
            Step(OpenCurl);
            Assert.That(_fires, Is.EqualTo(1));
            Assert.That(_gesture.FireCharge01, Is.LessThan(.1f));
        }

        [Test]
        public void ANewFistAfterFiringChargesAgainImmediately()
        {
            Step(FistCurl);
            Step(FistCurl, Vector3.forward * 1.5f, frames: 7);
            Step(OpenCurl);
            Assert.That(_fires, Is.EqualTo(1));
            Step(FistCurl);
            Assert.That(_gesture.ChargeStarted, Is.True);
            Assert.That(_gesture.Charging, Is.True);
        }

        [Test]
        public void FireDirectionBlendsHeadForwardAndPushDirection()
        {
            var head = Vector3.forward;
            var push = new Vector3(1f, 0f, 1f).normalized;
            Assert.That(Vector3.Angle(EnergyHandGesture.Aim(head, push, 0f), head), Is.LessThan(.01f));
            Assert.That(Vector3.Angle(EnergyHandGesture.Aim(head, push, 1f), push), Is.LessThan(.01f));

            _settings.AimBlend = 0f;
            var lookingLeft = new Vector3(-.3f, 0f, 1f).normalized;
            Step(FistCurl, headForward: lookingLeft);
            Step(FistCurl, lookingLeft * 1.5f, frames: 7, headForward: lookingLeft);
            _hand += Vector3.right * .05f;
            Step(OpenCurl, headForward: lookingLeft);
            Assert.That(_fires, Is.EqualTo(1));
            Assert.That(Vector3.Angle(_gesture.FireDirection, lookingLeft), Is.LessThan(.01f));
        }

        // ------------------------------------------------------------------ guard

        private void Guard(float curl, int frames) =>
            Step(curl, frames: frames, palm: Vector3.forward, fingers: Vector3.up);

        [Test]
        public void GuardTurnsOnAndOffWithDelays()
        {
            _hand = Head + new Vector3(0f, -.2f, .3f);
            Guard(OpenCurl, 11);
            Assert.That(_gesture.Guarding, Is.False, "110 ms < on delay");
            Guard(OpenCurl, 1);
            Assert.That(_gesture.Guarding, Is.True);

            // Fingers point forward: no longer a guard pose, but it holds for the off delay.
            Step(OpenCurl, frames: 14, palm: Vector3.forward, fingers: Vector3.forward);
            Assert.That(_gesture.Guarding, Is.True, "140 ms < off delay");
            Step(OpenCurl, frames: 1, palm: Vector3.forward, fingers: Vector3.forward);
            Assert.That(_gesture.Guarding, Is.False);
        }

        [Test]
        public void GuardRequiresPalmForwardAndHandInFrontOfTheFace()
        {
            _hand = Head + new Vector3(0f, -.2f, .3f);
            Step(OpenCurl, frames: 30, palm: Vector3.back, fingers: Vector3.up);
            Assert.That(_gesture.Guarding, Is.False, "palm toward the face");

            _hand = Head + new Vector3(0f, -.9f, .3f);
            Guard(OpenCurl, 30);
            Assert.That(_gesture.Guarding, Is.False, "too low");
        }

        [Test]
        public void FistIsNeverAGuard()
        {
            _hand = Head + new Vector3(0f, -.2f, .3f);
            Guard(FistCurl, 30);
            Assert.That(_gesture.Guarding, Is.False);

            var gesture = new EnergyHandGesture(_settings);
            _gesture = gesture;
            Guard(OpenCurl, 30);
            Assert.That(_gesture.Guarding, Is.True);
            Guard(FistCurl, 1);
            Assert.That(_gesture.Guarding, Is.False, "a fist drops the guard at once");
        }

        // --------------------------------------------------------------- tracking

        [Test]
        public void UntrackedFramesHoldStateAndNeverFire()
        {
            Step(FistCurl);
            Step(FistCurl, Vector3.forward * 1.5f, frames: 7);
            Assert.That(_gesture.Pushing, Is.True);
            Step(OpenCurl, Vector3.forward * 1.5f, frames: 60, tracked: false);
            Assert.That(_fires, Is.Zero);
            Assert.That(_gesture.Charging, Is.True);
        }

        [Test]
        public void ReleaseDropsChargeAndGuard()
        {
            Step(FistCurl, frames: 10);
            _gesture.Release();
            Assert.That(_gesture.ChargeCanceled, Is.True);
            Assert.That(_gesture.Charging, Is.False);
            Assert.That(_gesture.Guarding, Is.False);
        }
    }
}
