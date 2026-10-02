using System;
using UnityEngine;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// Decides whether this session may put haptics on the network. A batch editor (automated
    /// verification) sends nothing unless the command line opts in with <see cref="LiveArgument"/>;
    /// interactive Editor Play and player builds always send. The SDK root is deactivated before its
    /// Awake can run, and <see cref="EnergyHapticRelay"/> skips Fire()/Stop() as well.
    /// Same policy as GloveBall's DemoHapticsGate.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class EnergyHapticsGate : MonoBehaviour
    {
        public const string LiveArgument = "-energyHapticsLive";

        [Tooltip("The HapbeatManager object, switched off when this session must not send.")]
        [SerializeField] private GameObject _sdkRoot;

        private static bool _resolved;
        private static bool _liveSend;

        public static bool LiveSendEnabled
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;
                    _liveSend = Resolve();
                }

                return _liveSend;
            }
        }

        public GameObject SdkRoot => _sdkRoot;

        private static bool Resolve()
        {
            if (!Application.isBatchMode)
            {
                return true;
            }

            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg == LiveArgument)
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            if (LiveSendEnabled)
            {
                return;
            }

            if (_sdkRoot != null)
            {
                _sdkRoot.SetActive(false);
            }

            Debug.Log($"[EnergyHaptics] live send: OFF (batch run without {LiveArgument}); the SDK is switched off and no packets leave this machine");
        }
    }
}
