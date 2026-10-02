using System;
using UnityEngine;

namespace EnergyDuel.Runtime
{
    public enum EnergyHurtboxKind { Body, Head, LeftArm, RightArm }

    /// <summary>
    /// A part of the player an opponent shot can hit. Body and head follow the headset; an arm
    /// follows its hand and only counts while that hand is present and not guarding (a guarding
    /// hand is covered by its shield).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class EnergyHurtbox : MonoBehaviour, IEnergyShotReceiver
    {
        public EnergyHurtboxKind Kind;
        public Transform Head;
        [Tooltip("Body / head: offset from the head, yaw-aligned.")]
        public Vector3 HeadOffset = new Vector3(0f, -.45f, 0f);
        [Tooltip("Arm only.")]
        public EnergyHandInput Hand;

        private Collider _collider;

        public event Action<EnergyHurtbox, EnergyShot, Vector3> Hit;

        public bool IsArm => Kind == EnergyHurtboxKind.LeftArm || Kind == EnergyHurtboxKind.RightArm;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
        }

        private void LateUpdate()
        {
            if (IsArm)
            {
                var active = Hand != null && Hand.Present && !Hand.Guarding;
                _collider.enabled = active;
                if (active) transform.position = Hand.PalmPosition;
                return;
            }

            if (Head == null) return;
            var forward = Head.forward;
            forward.y = 0f;
            var yaw = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            transform.SetPositionAndRotation(Head.position + (yaw * HeadOffset), yaw);
        }

        public bool ReceiveShot(EnergyShot shot, Vector3 point)
        {
            if (shot.Owner != EnergyShotOwner.Opponent || !_collider.enabled) return false;
            Hit?.Invoke(this, shot, point);
            return true;
        }
    }
}
