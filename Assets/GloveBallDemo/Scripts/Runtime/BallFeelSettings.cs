using System;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    [Serializable]
    public sealed class BallFeel
    {
        public BallKind Kind;
        [Min(.01f)] public float Mass = .25f;
        [Range(0f, 2f)] public float AirResistance;
        public PhysicsMaterial BounceMaterial;
        public AudioClip ImpactClip;
        [Range(0f, 1f)] public float ImpactVolume = .7f;
        [Tooltip("Random pitch range per impact. (1, 1) disables variation; also changes playback speed.")]
        public Vector2 ImpactPitchRange = new Vector2(.95f, 1.05f);
        [Tooltip("Random multiplier of Impact Volume. (1, 1) disables variation.")]
        public Vector2 ImpactVolumeRange = new Vector2(.9f, 1f);

        public static float SampleRange(Vector2 range, float sample, float minimum, float maximum)
        {
            return Mathf.Lerp(Mathf.Clamp(Mathf.Min(range.x, range.y), minimum, maximum),
                Mathf.Clamp(Mathf.Max(range.x, range.y), minimum, maximum), Mathf.Clamp01(sample));
        }
    }

    [CreateAssetMenu(menuName = "GloveBall/Ball Feel Settings")]
    public sealed class BallFeelSettings : ScriptableObject
    {
        [Range(.5f, 1.5f)] public float FirstRoundSpeedMultiplier = .85f;
        [Range(.5f, 1.5f)] public float FinalRoundSpeedMultiplier = .95f;
        [Tooltip("Gameplay tuning, not a real-world mass simulation. One entry per ball kind.")]
        public BallFeel[] Balls = Array.Empty<BallFeel>();
        public BallFeel Find(BallKind kind)
        {
            foreach (var item in Balls) if (item != null && item.Kind == kind) return item;
            return null;
        }
    }
}
