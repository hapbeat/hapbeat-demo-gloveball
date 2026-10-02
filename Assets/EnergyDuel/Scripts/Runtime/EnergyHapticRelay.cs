using System;
using System.Collections.Generic;
using Hapbeat;
using UnityEngine;

namespace EnergyDuel.Runtime
{
    /// <summary>Every Energy Duel moment that is felt. Names match the EventMap entries (l_/r_ prefixes).</summary>
    public enum EnergyHapticEvent
    {
        LeftChargeLoop, RightChargeLoop,
        LeftChargeFull, RightChargeFull,
        LeftRelease, RightRelease,
        LeftShieldUp, RightShieldUp,
        LeftShieldBlock, RightShieldBlock,
        LeftShieldBreak, RightShieldBreak,
        ShieldBreakNeck,
        LeftArmCollide, RightArmCollide,
        BodyCollide,
        TargetHit,
    }

    [Serializable]
    public sealed class EnergyHapticBinding
    {
        public EnergyHapticEvent Event;
        public HapbeatUnityEventTrigger Trigger;
    }

    [Serializable]
    public sealed class EnergyHapticLoopBinding
    {
        public EnergyHapticEvent Event;
        public HapbeatSequenceTrigger Trigger;
    }

    /// <summary>
    /// Single funnel from gameplay to Hapbeat. Gameplay reports an event and how hard this instance
    /// was; the SDK trigger (and so the EventMap entry) owns clip, target and absolute level.
    /// </summary>
    public sealed class EnergyHapticRelay : MonoBehaviour
    {
        [SerializeField] private EnergyHapticBinding[] _bindings = Array.Empty<EnergyHapticBinding>();
        [SerializeField] private EnergyHapticLoopBinding[] _loopBindings = Array.Empty<EnergyHapticLoopBinding>();
        [SerializeField] private bool _verboseLog;

        public static EnergyHapticRelay Instance { get; private set; }

        /// <summary>Observed by diagnostics (batch play-through) whether or not anything is sent.</summary>
        public event Action<EnergyHapticEvent, Vector3, float> EventReported;

        private readonly HashSet<EnergyHapticEvent> _activeLoops = new HashSet<EnergyHapticEvent>();

        public static EnergyHapticEvent Sided(bool left, EnergyHapticEvent leftEvent) =>
            left ? leftEvent : leftEvent + 1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void Report(EnergyHapticEvent evt, Vector3 position, float gain = 1f)
        {
            if (Instance == null) return;
            if (Instance._verboseLog) Debug.Log($"[EnergyHaptics] {evt} gain={gain:0.00}");
            if (EnergyHapticsGate.LiveSendEnabled)
            {
                var trigger = Instance.FindTrigger(evt);
                if (trigger != null)
                {
                    trigger.GainMultiplier = Mathf.Clamp(gain, 0f, 2f);
                    trigger.Fire();
                }
            }

            Instance.EventReported?.Invoke(evt, position, gain);
        }

        /// <summary>Starts a loop; repeated calls while active only update the gain.</summary>
        public static void BeginLoop(EnergyHapticEvent evt, Vector3 position, float gain)
        {
            if (Instance == null) return;
            if (!Instance._activeLoops.Add(evt))
            {
                UpdateLoop(evt, position, gain);
                return;
            }

            if (EnergyHapticsGate.LiveSendEnabled)
            {
                var trigger = Instance.FindLoopTrigger(evt);
                if (trigger != null)
                {
                    trigger.GainMultiplier = Mathf.Clamp(gain, 0f, 2f);
                    trigger.Fire();
                }
            }

            Instance.EventReported?.Invoke(evt, position, gain);
        }

        /// <summary>Pushes a new gain onto a playing loop without restarting it.</summary>
        public static void UpdateLoop(EnergyHapticEvent evt, Vector3 position, float gain)
        {
            if (Instance == null || !Instance._activeLoops.Contains(evt)) return;
            var trigger = Instance.FindLoopTrigger(evt);
            if (trigger != null) trigger.GainMultiplier = Mathf.Clamp(gain, 0f, 2f);
        }

        public static void EndLoop(EnergyHapticEvent evt)
        {
            if (Instance == null || !Instance._activeLoops.Remove(evt)) return;
            var trigger = Instance.FindLoopTrigger(evt);
            if (trigger != null && EnergyHapticsGate.LiveSendEnabled) trigger.Stop();
        }

        public HapbeatUnityEventTrigger GetTrigger(EnergyHapticEvent evt) => FindTrigger(evt);
        public HapbeatSequenceTrigger GetLoopTrigger(EnergyHapticEvent evt) => FindLoopTrigger(evt);

        private HapbeatUnityEventTrigger FindTrigger(EnergyHapticEvent evt)
        {
            foreach (var binding in _bindings)
                if (binding != null && binding.Event == evt) return binding.Trigger;
            return null;
        }

        private HapbeatSequenceTrigger FindLoopTrigger(EnergyHapticEvent evt)
        {
            foreach (var binding in _loopBindings)
                if (binding != null && binding.Event == evt) return binding.Trigger;
            return null;
        }
    }
}
