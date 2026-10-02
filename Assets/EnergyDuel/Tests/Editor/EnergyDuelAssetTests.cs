using System.IO;
using System.Linq;
using EnergyDuel.Editor;
using EnergyDuel.Runtime;
using Hapbeat;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EnergyDuel.Tests
{
    public sealed class EnergyDuelHapticsTests
    {
        [Test]
        public void EventMapHasEveryEventWithItsTargetAndClip()
        {
            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(EnergyDuelHaptics.EventMapPath);
            Assert.That(map, Is.Not.Null, "run EnergyDuelBatch.Setup first");
            Assert.That(EnergyDuelHaptics.Definitions.Select(d => d.Event), Is.EquivalentTo(System.Enum.GetValues(typeof(EnergyHapticEvent))),
                "every relay event has exactly one definition");
            foreach (var definition in EnergyDuelHaptics.Definitions)
            {
                var entry = map.entries.SingleOrDefault(e => e.eventName == definition.Name);
                Assert.That(entry, Is.Not.Null, definition.Name);
                Assert.That(entry.target, Is.EqualTo(definition.Target), definition.Name);
                Assert.That(entry.mode, Is.EqualTo(HapticMode.StreamClip), definition.Name);
                Assert.That(entry.loop, Is.EqualTo(definition.Loop), definition.Name);
                Assert.That(entry.streamClip, Is.Not.Null, definition.Name);
                Assert.That(AssetDatabase.GetAssetPath(entry.streamClip), Is.EqualTo(definition.ClipPath), definition.Name);
                Assert.That(entry.CachedManifestIntensity, Is.GreaterThanOrEqualTo(0f), definition.Name);
            }

            var targets = EnergyDuelHaptics.Definitions.ToDictionary(d => d.Name, d => d.Target);
            Assert.That(targets["l_shield_break"], Is.EqualTo("*/pos_l_wrist"));
            Assert.That(targets["r_shield_block"], Is.EqualTo("*/pos_r_wrist"));
            Assert.That(targets["shield_break_neck"], Is.EqualTo("*/pos_neck"));
            Assert.That(targets["body_collide"], Is.EqualTo("*/pos_neck"));
        }

        [Test]
        public void AddingIsAdditiveAndNeverTouchesExistingEntries()
        {
            var map = ScriptableObject.CreateInstance<HapbeatEventMap>();
            try
            {
                var tuned = new HapbeatEventEntry { eventName = "l_shield_up", gain = .123f, target = "*/pos_custom", notes = "hand tuned" };
                map.entries.Add(tuned);
                var added = EnergyDuelHaptics.AddMissing(map, null);
                Assert.That(added, Is.EqualTo(EnergyDuelHaptics.Definitions.Length - 1));
                Assert.That(map.entries.Count(e => e.eventName == "l_shield_up"), Is.EqualTo(1));
                Assert.That(tuned.gain, Is.EqualTo(.123f));
                Assert.That(tuned.target, Is.EqualTo("*/pos_custom"));
                Assert.That(tuned.notes, Is.EqualTo("hand tuned"));
                Assert.That(EnergyDuelHaptics.AddMissing(map, null), Is.Zero, "a second run adds nothing");
                Assert.That(map.entries.Count, Is.EqualTo(EnergyDuelHaptics.Definitions.Length));
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void ReusedEventsStartFromGloveBallsTunedGain()
        {
            var source = ScriptableObject.CreateInstance<HapbeatEventMap>();
            var map = ScriptableObject.CreateInstance<HapbeatEventMap>();
            try
            {
                source.entries.Add(new HapbeatEventEntry { eventName = "body_collide", gain = .77f });
                EnergyDuelHaptics.AddMissing(map, source);
                Assert.That(map.entries.Single(e => e.eventName == "body_collide").gain, Is.EqualTo(.77f));
                Assert.That(map.entries.Single(e => e.eventName == "shield_break_neck").gain, Is.EqualTo(1f), "new events use the catalog default");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void SidedEventsPairLeftAndRight()
        {
            Assert.That(EnergyHapticRelay.Sided(true, EnergyHapticEvent.LeftShieldBreak), Is.EqualTo(EnergyHapticEvent.LeftShieldBreak));
            Assert.That(EnergyHapticRelay.Sided(false, EnergyHapticEvent.LeftShieldBreak), Is.EqualTo(EnergyHapticEvent.RightShieldBreak));
            foreach (var left in new[] { EnergyHapticEvent.LeftChargeLoop, EnergyHapticEvent.LeftChargeFull, EnergyHapticEvent.LeftRelease, EnergyHapticEvent.LeftShieldUp, EnergyHapticEvent.LeftShieldBlock, EnergyHapticEvent.LeftShieldBreak, EnergyHapticEvent.LeftArmCollide })
            {
                Assert.That(EnergyHapticRelay.Sided(false, left).ToString(), Is.EqualTo("Right" + left.ToString().Substring(4)));
            }
        }
    }

    public sealed class EnergyHandMathTests
    {
        [Test]
        public void PalmNormalIsTheJointsMinusYForBothHands()
        {
            // XR Hands' own PalmDirection is rotation * (0,-1,0) regardless of handedness; only the
            // thumb axis is mirrored. A palm joint lying flat, back of the hand up, faces down.
            var flat = Quaternion.identity;
            Assert.That(Vector3.Angle(EnergyHandInput.PalmDirection(flat), Vector3.down), Is.LessThan(.01f));
            // Hand raised for a guard: fingers up (+Z -> up), back of the hand toward the face (+Y -> back).
            var guard = Quaternion.LookRotation(Vector3.up, Vector3.back);
            Assert.That(Vector3.Angle(EnergyHandInput.PalmDirection(guard), Vector3.forward), Is.LessThan(.01f));
        }

        [Test]
        public void AimAssistNudgesOnlyWithinItsCone()
        {
            var go = new GameObject("assist test");
            var target = new GameObject("target");
            try
            {
                var input = go.AddComponent<EnergyHandInput>();
                input.AssistTarget = target.transform;
                input.AssistStrength = .5f;
                input.AssistMaxDegrees = 15f;
                target.transform.position = new Vector3(Mathf.Tan(10f * Mathf.Deg2Rad) * 5f, 0f, 5f);
                var nudged = input.Assist(Vector3.zero, Vector3.forward);
                Assert.That(Vector3.SignedAngle(Vector3.forward, nudged, Vector3.up), Is.EqualTo(5f).Within(.05f));

                target.transform.position = new Vector3(5f, 0f, 5f); // 45 degrees off: outside the cone
                Assert.That(input.Assist(Vector3.zero, Vector3.forward), Is.EqualTo(Vector3.forward));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ClosestApproachFindsShotsThatPassThroughEachOther()
        {
            // Head-on at 20 m/s closing speed, 0.3 m apart: they cross mid-frame.
            var d = EnergyShotPool.ClosestApproach(Vector3.zero, Vector3.forward * 10f, new Vector3(0f, 0f, .3f), Vector3.back * 10f, .02f);
            Assert.That(d, Is.LessThan(.01f));
            var miss = EnergyShotPool.ClosestApproach(Vector3.zero, Vector3.forward * 10f, new Vector3(1f, 0f, .3f), Vector3.back * 10f, .02f);
            Assert.That(miss, Is.GreaterThan(.9f));
        }
    }

    public sealed class EnergyDuelSceneTests
    {
        [Test]
        public void SceneHasTheDuelAndNoGloveBallGameplay()
        {
            Assert.That(File.Exists(EnergyDuelSceneSetup.ScenePath), Is.True, "run EnergyDuelBatch.Setup first");
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(EnergyDuelSceneSetup.ScenePath, OpenSceneMode.Single);
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();

                var missing = all.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                Assert.That(missing, Is.Zero, "missing scripts");

                // A MonoBehaviour declared in a file of another name serializes without a script
                // asset and is lost when the scene loads.
                var orphans = all.SelectMany(t => t.GetComponents<MonoBehaviour>())
                    .Where(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("EnergyDuel"))
                    .Where(c => !AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(c)).StartsWith("Assets/EnergyDuel/Scripts/"))
                    .Select(c => c.GetType().Name)
                    .Distinct()
                    .ToArray();
                Assert.That(orphans, Is.Empty, "components without their own script file");

                var inputs = all.SelectMany(t => t.GetComponents<EnergyHandInput>()).ToArray();
                Assert.That(inputs.Select(i => i.Hand.Side), Is.EquivalentTo(new[] { EnergyHandSide.Left, EnergyHandSide.Right }));
                Assert.That(inputs.All(i => i.Head != null && i.Trigger.reference != null && i.Grip.reference != null), Is.True);
                Assert.That(all.SelectMany(t => t.GetComponents<EnergyShield>()).Count(), Is.EqualTo(2));
                Assert.That(all.SelectMany(t => t.GetComponents<EnergyOpponent>()).Count(), Is.EqualTo(1));
                var duel = all.SelectMany(t => t.GetComponents<EnergyDuelController>()).Single();
                Assert.That(duel.Opponent, Is.Not.Null);
                Assert.That(duel.Pool, Is.Not.Null);
                Assert.That(duel.TutorialTarget, Is.Not.Null);
                Assert.That(duel.SkipActions.Length, Is.EqualTo(3));
                Assert.That(duel.Hurtboxes.Length, Is.EqualTo(4));

                // Every GloveBall gameplay / rig script was removed or replaced in the copy.
                var gloveBall = all.SelectMany(t => t.GetComponents<MonoBehaviour>())
                    .Where(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("GloveBallDemo"))
                    .Select(c => c.GetType().Name)
                    .ToArray();
                Assert.That(gloveBall, Is.Empty);
                Assert.That(all.Any(t => t.name.StartsWith("Volley Drill")), Is.False);

                // Haptics are wired to the duel's own EventMap and the gate guards the SDK.
                var relay = all.SelectMany(t => t.GetComponents<EnergyHapticRelay>()).Single();
                foreach (var definition in EnergyDuelHaptics.Definitions)
                {
                    var trigger = (HapbeatTriggerBase)relay.GetLoopTrigger(definition.Event) ?? relay.GetTrigger(definition.Event);
                    Assert.That(trigger, Is.Not.Null, definition.Name);
                    Assert.That(AssetDatabase.GetAssetPath(trigger.EventMap), Is.EqualTo(EnergyDuelHaptics.EventMapPath), definition.Name);
                    Assert.That(trigger.ResolveEntry()?.eventName, Is.EqualTo(definition.Name));
                }

                var gate = all.SelectMany(t => t.GetComponents<EnergyHapticsGate>()).Single();
                Assert.That(gate.SdkRoot, Is.Not.Null);
                Assert.That(gate.SdkRoot.GetComponent<HapbeatManager>(), Is.Not.Null);
            }
            finally
            {
                if (previous.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previous);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
