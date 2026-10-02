using UnityEngine;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// The charge you can see and feel: a glowing orb in front of the palm that grows with the charge,
    /// and the wrist charge loop whose gain follows it (0.25 -> 1.0), capped by a charge-full pulse.
    /// </summary>
    public sealed class EnergyCharge : MonoBehaviour
    {
        public EnergyHandInput Input;
        public Material OrbMaterial;
        [Min(0f)] public float MinimumDiameter = .05f;
        [Min(0f)] public float MaximumDiameter = .20f;
        [Range(0f, 2f)] public float LoopGainEmpty = .25f;
        [Range(0f, 2f)] public float LoopGainFull = 1f;

        private Transform _orb;
        private bool _looping;

        private EnergyHapticEvent LoopEvent => EnergyHapticRelay.Sided(Input.IsLeft, EnergyHapticEvent.LeftChargeLoop);

        private void Awake()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Charge Orb";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            if (OrbMaterial != null) go.GetComponent<MeshRenderer>().sharedMaterial = OrbMaterial;
            _orb = go.transform;
            _orb.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (Input == null) return;
            Input.ChargeStarted += OnChargeStarted;
            Input.ChargeEnded += OnChargeEnded;
            Input.ChargeFull += OnChargeFull;
        }

        private void OnDisable()
        {
            if (Input != null)
            {
                Input.ChargeStarted -= OnChargeStarted;
                Input.ChargeEnded -= OnChargeEnded;
                Input.ChargeFull -= OnChargeFull;
            }

            OnChargeEnded(Input);
        }

        private void OnChargeStarted(EnergyHandInput _)
        {
            _looping = true;
            EnergyHapticRelay.BeginLoop(LoopEvent, Input.ChargePoint, LoopGainEmpty);
        }

        private void OnChargeEnded(EnergyHandInput _)
        {
            if (_orb != null) _orb.gameObject.SetActive(false);
            if (!_looping) return;
            _looping = false;
            EnergyHapticRelay.EndLoop(LoopEvent);
        }

        private void OnChargeFull(EnergyHandInput _) =>
            EnergyHapticRelay.Report(EnergyHapticRelay.Sided(Input.IsLeft, EnergyHapticEvent.LeftChargeFull), Input.ChargePoint);

        private void LateUpdate()
        {
            if (!_looping || Input == null) return;
            var charge = Input.Charge01;
            _orb.gameObject.SetActive(true);
            _orb.position = Input.ChargePoint;
            _orb.localScale = Vector3.one * Mathf.Lerp(MinimumDiameter, MaximumDiameter, charge);
            EnergyHapticRelay.UpdateLoop(LoopEvent, Input.ChargePoint, Mathf.Lerp(LoopGainEmpty, LoopGainFull, charge));
        }
    }
}
