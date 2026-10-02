using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloveBallDemo.Runtime;
using Hapbeat.DemoSwitch;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class DemoSessionHostTests
    {
        const string Folder = "Assets/GloveBallDemo/Scenes/";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        static string Ticket(string demoId, string optionsJson, bool retry = true) =>
            "{\"version\":1,\"session_id\":\"0f3a9c2e7b1d4a56\",\"index\":0,\"haptics_ui\":false,\"steps\":[" +
            "{\"demo_id\":\"" + demoId + "\",\"title\":\"step\",\"package\":\"jp.hapbeat.volley\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\",\"options\":" + optionsJson + ",\"retry\":" + (retry ? "true" : "false") + "}]," +
            "\"finish\":{\"package\":\"jp.hapbeat.demohub\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\"}}";

        static DemoSessionDescriptor Load(string path)
        {
            Assert.That(DemoSessionDescriptor.TryParse(File.ReadAllText(path), out var descriptor, out var error), Is.True, error);
            return descriptor;
        }

        static void Raise(object target, string eventField, params object[] args)
        {
            var handler = (Delegate)target.GetType().GetField(eventField, Private).GetValue(target);
            Assert.That(handler, Is.Not.Null, eventField + " has no subscriber.");
            handler.DynamicInvoke(args);
        }

        float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            DemoSession.ResetForTests();
            VolleySessionHost.ResetSceneChoiceForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DemoSession.ResetForTests();
            HapticEventRelay.SetHapticsEnabled(true);
            GameInputGate.SetBlocked(false);
            Time.timeScale = _timeScale;
        }

        [Test]
        public void DescriptorsMatchTheContractFixtureAndEachApk()
        {
            var volley = Load("Assets/StreamingAssets/hapbeat-demo-session.json");
            Assert.That(volley.DemoId, Is.EqualTo("volley"));
            Assert.That(volley.SupportsHapticsToggle, Is.True);
            Assert.That(volley.Options.Count, Is.EqualTo(3));
            Assert.That(volley.FindOption("points").WhenOptionId, Is.EqualTo("scene"));
            Assert.That(volley.FindOption("points").Values.Count, Is.EqualTo(3));
            Assert.That(volley.FindOption("balls").Default, Is.EqualTo("10"));
            Assert.That(volley.Title.Ja, Is.EqualTo("バレーボール"));
            Assert.That(volley.Minutes, Is.EqualTo(3));

            var gloveball = Load("Assets/GloveBallDemo/Session/gloveball-demo-session.json");
            var settings = Resources.Load<DemoSwitchSettings>(DemoSwitchSettings.ResourceName);
            Assert.That(gloveball.DemoId, Is.EqualTo(settings.CurrentDemoId), "GloveBall APK keeps the existing demo ID.");
            Assert.That(gloveball.SupportsHapticsToggle, Is.True);
            Assert.That(gloveball.Options, Is.Empty, "The wave run ends naturally.");
        }

        [Test]
        public void BlockSessionPlaysToPointsHoldsTheScoreAndRetriesFromZero()
        {
            EditorSceneManager.OpenScene(Folder + "VolleyBlock-codex.unity");
            var drill = UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
            var aerial = drill.Aerial;
            Assert.That(DemoSession.TryBegin(Ticket("volley", "{\"scene\":\"block\",\"points\":\"3\"}"), "volley",
                Load("Assets/StreamingAssets/hapbeat-demo-session.json"), out var error), Is.True, error);
            var host = drill.gameObject.AddComponent<VolleySessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(aerial.MatchPoints, Is.EqualTo(3));
            Assert.That(aerial.Mode, Is.EqualTo(VolleyAerialSequence.RallyMode.Match));
            Assert.That(aerial.HoldAtMatchEnd, Is.True, "No automatic rematch in session mode.");

            Raise(aerial, "MatchEnded", true);
            Assert.That(DemoSession.IsCompletionShown, Is.True);
            Assert.That(GameInputGate.IsBlocked, Is.True);
            Assert.That(Time.timeScale, Is.Zero);

            DemoSession.RetryFromCompletion();
            Assert.That(DemoSession.IsCompletionShown, Is.False);
            Assert.That(GameInputGate.IsBlocked, Is.False);
            Assert.That(aerial.PlayerScore + aerial.OpponentScore, Is.Zero);
            Assert.That(aerial.Phase, Is.EqualTo(VolleyAerialSequence.RallyPhase.Idle));
        }

        [Test]
        public void ReceiveSessionEndsAfterTheBallCountWithoutChangingTheNormalDrill()
        {
            EditorSceneManager.OpenScene(Folder + "VolleyReceive-codex.unity");
            var drill = UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
            Assert.That(drill.ServeLimit, Is.Zero, "Normal launch is the endless drill.");
            Assert.That(DemoSession.TryBegin(Ticket("volley", "{\"scene\":\"receive\",\"balls\":\"20\"}"), "volley",
                Load("Assets/StreamingAssets/hapbeat-demo-session.json"), out var error), Is.True, error);
            var host = drill.gameObject.AddComponent<VolleySessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(drill.ServeLimit, Is.EqualTo(20));

            typeof(VolleyDrillController).GetProperty("Serves").SetValue(drill, 20);
            typeof(VolleyDrillController).GetMethod("Update", Private).Invoke(drill, null);
            Assert.That(DemoSession.IsCompletionShown, Is.True, "Served balls count, hit or missed.");

            DemoSession.RetryFromCompletion();
            Assert.That(drill.Serves, Is.Zero);
            Assert.That(DemoSession.IsCompletionShown, Is.False);
        }

        [Test]
        public void GloveBallSessionHoldsTheWaveResult()
        {
            EditorSceneManager.OpenScene(Folder + "Demo.unity");
            var game = UnityEngine.Object.FindFirstObjectByType<DemoGameController>();
            typeof(DemoGameController).GetMethod("Awake", Private).Invoke(game, null);
            Assert.That(DemoSession.TryBegin(Ticket("gloveball_v2", "{}", false), "gloveball_v2",
                Load("Assets/GloveBallDemo/Session/gloveball-demo-session.json"), out var error), Is.True, error);
            var host = game.gameObject.AddComponent<GloveBallSessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(game.HoldAtResult, Is.True);
            Assert.That(game.PlayMode, Is.EqualTo(DemoPlayMode.Waves));
            Raise(game, "RunCompleted");
            Assert.That(DemoSession.IsCompletionShown, Is.True);
            Assert.That(GameInputGate.IsBlocked, Is.True);
        }

        [Test]
        public void HapticsSwitchSuppressesRelaySends()
        {
            Assert.That(HapticEventRelay.HapticsEnabled, Is.True, "Each step starts with haptics on.");
            var descriptor = Load("Assets/StreamingAssets/hapbeat-demo-session.json");
            DemoSession.ResetForTests(null, "volley", descriptor);
            var go = new GameObject("host");
            try
            {
                var host = go.AddComponent<GloveBallSessionHost>();
                DemoSession.RegisterHost(host);
                Assert.That(DemoSession.HapticsControls.CanExecuteControl("haptics_off", ""), Is.True);
                DemoSession.SetHapticsEnabled(false);
                Assert.That(HapticEventRelay.HapticsEnabled, Is.False);
                DemoSession.SetHapticsEnabled(true);
                Assert.That(HapticEventRelay.HapticsEnabled, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
