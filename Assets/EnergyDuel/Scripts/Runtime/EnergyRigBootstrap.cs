using UnityEngine;
using UnityEngine.InputSystem;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// Enables the rig's input actions at startup (the TrackedPoseDrivers read action references,
    /// which they do not enable themselves) and logs whether an HMD is present. Same role as
    /// GloveBall's XrRigBootstrap.
    /// </summary>
    public sealed class EnergyRigBootstrap : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Transform _head;

        private void Awake()
        {
            if (_actions == null)
            {
                Debug.LogWarning("[EnergyRig] no InputActionAsset assigned; pose tracking will stay at the authored transform");
                return;
            }

            _actions.Enable();
        }

        private void Start()
        {
            var hmd = InputSystem.GetDevice<UnityEngine.InputSystem.XR.XRHMD>();
            Debug.Log(hmd != null
                ? $"[EnergyRig] HMD detected: {hmd.displayName}"
                : "[EnergyRig] no HMD present; the rig stays at its authored transform (expected for batch runs)");
            if (_head == null) Debug.LogWarning("[EnergyRig] no head transform assigned");
        }
    }
}
