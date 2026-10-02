using System.Collections.Generic;
using System.IO;
using EnergyDuel.Runtime;
using Hapbeat;
using UnityEditor;
using UnityEngine;

namespace EnergyDuel.Editor
{
    /// <summary>One Energy Duel haptic event: EventMap name, clip, device target, default gain.</summary>
    public readonly struct EnergyHapticDefinition
    {
        public readonly EnergyHapticEvent Event;
        public readonly string Name;
        public readonly string DisplayName;
        /// <summary>Clip path. The placeholder clips are the GloveBall kit's shared mono clips, referenced in place.</summary>
        public readonly string ClipPath;
        public readonly string Target;
        public readonly float Gain;
        public readonly bool Loop;
        /// <summary>When set, gain / notes are first taken from this entry of GloveBall's tuned EventMap.</summary>
        public readonly string GloveBallSource;
        public readonly string Notes;

        public EnergyHapticDefinition(EnergyHapticEvent evt, string name, string displayName, string clip, string target, float gain, bool loop, string gloveBallSource, string notes)
        {
            Event = evt;
            Name = name;
            DisplayName = displayName;
            ClipPath = EnergyDuelHaptics.ClipsDir + "/" + clip + ".wav";
            Target = target;
            Gain = gain;
            Loop = loop;
            GloveBallSource = gloveBallSource;
            Notes = notes;
        }
    }

    /// <summary>
    /// Energy Duel's own EventMap. Built additively: a missing entry is added, an existing one is never
    /// touched, so values tuned in the EventMap window survive every re-run.
    /// </summary>
    public static class EnergyDuelHaptics
    {
        public const string EventMapPath = "Assets/EnergyDuel/Haptics/EnergyDuelEventMap.asset";
        public const string Category = "energy-duel";
        public const string ClipsDir = "Assets/GloveBallDemo/Kits/gloveball-kit/stream-clips";
        public const string GloveBallEventMapPath = "Assets/GloveBallDemo/Haptics/GloveBallEventMap.asset";

        public const string LeftWrist = "*/pos_l_wrist";
        public const string RightWrist = "*/pos_r_wrist";
        public const string Neck = "*/pos_neck";

        // Placeholder clips (design section 6); production clips come in Phase 2.
        // grab = z1_pin_hit, incoming_warn = z4_slider_tick, arm_collide = z5_tar_hit_light,
        // body_collide = z5_shot_heavy (see GloveBall's DemoHapticCatalog for the sources).
        public static readonly EnergyHapticDefinition[] Definitions =
        {
            new EnergyHapticDefinition(EnergyHapticEvent.LeftChargeLoop, "l_charge_loop", "Left Charge Loop", "charge_loop", LeftWrist, .4f, true, "l_charge_loop", "Left fist charging; gain follows the charge."),
            new EnergyHapticDefinition(EnergyHapticEvent.RightChargeLoop, "r_charge_loop", "Right Charge Loop", "charge_loop", RightWrist, .4f, true, "r_charge_loop", "Right fist charging; gain follows the charge."),
            new EnergyHapticDefinition(EnergyHapticEvent.LeftChargeFull, "l_charge_full", "Left Charge Full", "grab", LeftWrist, .7f, false, null, "Left charge reached full."),
            new EnergyHapticDefinition(EnergyHapticEvent.RightChargeFull, "r_charge_full", "Right Charge Full", "grab", RightWrist, .7f, false, null, "Right charge reached full."),
            new EnergyHapticDefinition(EnergyHapticEvent.LeftRelease, "l_release", "Left Release", "release", LeftWrist, .5f, false, "l_release", "Left shot fired."),
            new EnergyHapticDefinition(EnergyHapticEvent.RightRelease, "r_release", "Right Release", "release", RightWrist, .5f, false, "r_release", "Right shot fired."),
            new EnergyHapticDefinition(EnergyHapticEvent.LeftShieldUp, "l_shield_up", "Left Shield Up", "incoming_warn", LeftWrist, .4f, false, null, "Left shield raised (light)."),
            new EnergyHapticDefinition(EnergyHapticEvent.RightShieldUp, "r_shield_up", "Right Shield Up", "incoming_warn", RightWrist, .4f, false, null, "Right shield raised (light)."),
            new EnergyHapticDefinition(EnergyHapticEvent.LeftShieldBlock, "l_shield_block", "Left Shield Block", "arm_collide", LeftWrist, .4f, false, null, "Left shield stopped a shot (gentle)."),
            new EnergyHapticDefinition(EnergyHapticEvent.RightShieldBlock, "r_shield_block", "Right Shield Block", "arm_collide", RightWrist, .4f, false, null, "Right shield stopped a shot (gentle)."),
            new EnergyHapticDefinition(EnergyHapticEvent.LeftShieldBreak, "l_shield_break", "Left Shield Break", "body_collide", LeftWrist, 1f, false, null, "Left shield shattered (hard)."),
            new EnergyHapticDefinition(EnergyHapticEvent.RightShieldBreak, "r_shield_break", "Right Shield Break", "body_collide", RightWrist, 1f, false, null, "Right shield shattered (hard)."),
            new EnergyHapticDefinition(EnergyHapticEvent.ShieldBreakNeck, "shield_break_neck", "Shield Break Neck", "body_collide", Neck, 1f, false, null, "A shield shattered: the neck feels it too."),
            new EnergyHapticDefinition(EnergyHapticEvent.LeftArmCollide, "l_arm_collide", "Left Arm Collide", "arm_collide", LeftWrist, .7f, false, "l_arm_collide", "Shot hit the unguarded left arm."),
            new EnergyHapticDefinition(EnergyHapticEvent.RightArmCollide, "r_arm_collide", "Right Arm Collide", "arm_collide", RightWrist, .7f, false, "r_arm_collide", "Shot hit the unguarded right arm."),
            new EnergyHapticDefinition(EnergyHapticEvent.BodyCollide, "body_collide", "Body Collide", "body_collide", Neck, 1f, false, "body_collide", "Shot hit the body or head (hard)."),
            new EnergyHapticDefinition(EnergyHapticEvent.TargetHit, "target_hit", "Target Hit", "target_hit", Neck, .6f, false, "target_hit", "Player shot hit the opponent / target; gain follows the charge."),
        };

        [MenuItem("GloveBall Demo/Energy Duel/Create or Complete Haptic EventMap (additive)")]
        public static void EnsureEventMapMenu() => EnsureEventMap();

        /// <summary>Creates the EventMap if needed and adds only the entries it lacks. Returns the number added.</summary>
        public static int EnsureEventMap()
        {
            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(EventMapPath);
            if (map == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EventMapPath) ?? ".");
                map = ScriptableObject.CreateInstance<HapbeatEventMap>();
                AssetDatabase.CreateAsset(map, EventMapPath);
            }

            var source = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(GloveBallEventMapPath);
            var added = AddMissing(map, source);
            if (added > 0)
            {
                EditorUtility.SetDirty(map);
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[EnergyDuelHaptics] {EventMapPath}: {added} entries added, {map.entries.Count} total");
            return added;
        }

        /// <summary>Adds every catalog entry the map lacks; never modifies an existing entry.</summary>
        public static int AddMissing(HapbeatEventMap map, HapbeatEventMap gloveBallSource)
        {
            var existing = new HashSet<string>();
            foreach (var entry in map.entries) existing.Add(entry.eventName);
            var added = 0;
            foreach (var definition in Definitions)
            {
                if (existing.Contains(definition.Name)) continue;
                map.entries.Add(CreateEntry(definition, gloveBallSource));
                added++;
            }

            return added;
        }

        private static HapbeatEventEntry CreateEntry(EnergyHapticDefinition definition, HapbeatEventMap gloveBallSource)
        {
            var entry = new HapbeatEventEntry
            {
                mode = HapticMode.StreamClip,
                displayName = definition.DisplayName,
                category = Category,
                eventName = definition.Name,
                streamClip = AssetDatabase.LoadAssetAtPath<AudioClip>(definition.ClipPath),
                loop = definition.Loop,
                gain = definition.Gain,
                target = definition.Target,
                notes = definition.Notes,
            };
            if (entry.streamClip == null) Debug.LogError($"[EnergyDuelHaptics] clip missing: {definition.ClipPath}");

            // Reused GloveBall events start from GloveBall's hand-tuned level, not the catalog default.
            var tuned = FindByName(gloveBallSource, definition.GloveBallSource);
            if (tuned != null)
            {
                entry.gain = tuned.gain;
                entry.delayOffsetSeconds = tuned.delayOffsetSeconds;
            }

            // Every clip of the borrowed kit is authored at intensity 1.0; baking it keeps the player
            // build from falling back to raw gain with a warning (the manifest does not ship).
            entry.SetCachedManifestIntensity(tuned != null && tuned.CachedManifestIntensity >= 0f ? tuned.CachedManifestIntensity : 1f);
            // Materialise the lazy id now so it is persisted with the asset.
            _ = entry.id;
            return entry;
        }

        private static HapbeatEventEntry FindByName(HapbeatEventMap map, string eventName)
        {
            if (map == null || string.IsNullOrEmpty(eventName)) return null;
            foreach (var entry in map.entries)
                if (entry.eventName == eventName) return entry;
            return null;
        }
    }
}
