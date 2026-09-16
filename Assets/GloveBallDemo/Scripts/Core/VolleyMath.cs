using UnityEngine;

namespace GloveBallDemo.Core
{
    /// <summary>Button-free paddle contact math. No target direction is consulted.</summary>
    public static class VolleyMath
    {
        public static Vector3 ReturnVelocity(Vector3 incoming, Vector3 handVelocity, Vector3 normal,
            float restitution, float swingGain, float maximumSpeed)
        {
            normal = normal.sqrMagnitude > .001f ? normal.normalized : Vector3.up;
            var relative = incoming - handVelocity;
            var result = Vector3.Reflect(relative, normal) * Mathf.Clamp01(restitution)
                + handVelocity * Mathf.Max(0f, swingGain);
            return Vector3.ClampMagnitude(result, Mathf.Max(.1f, maximumSpeed));
        }

        /// <summary>Continuous relative sweep; catches a fast ball/hand crossing between physics ticks.</summary>
        public static bool SweptContact(Vector3 ballStart, Vector3 ballEnd, Vector3 handStart,
            Vector3 handEnd, float radius, out float fraction)
        {
            var start = ballStart - handStart;
            var delta = (ballEnd - handEnd) - start;
            float c = start.sqrMagnitude - radius * radius;
            if (c <= 0f) { fraction = 0f; return true; }
            float a = delta.sqrMagnitude;
            if (a < 1e-8f) { fraction = 0f; return false; }
            float b = Vector3.Dot(start, delta);
            float discriminant = b * b - a * c;
            if (discriminant < 0f) { fraction = 0f; return false; }
            fraction = (-b - Mathf.Sqrt(discriminant)) / a;
            return fraction >= 0f && fraction <= 1f;
        }

        public static Vector3 ServeVelocity(Vector3 start, Vector3 destination, float seconds, Vector3 gravity)
        {
            seconds = Mathf.Max(.1f, seconds);
            return (destination - start - .5f * gravity * seconds * seconds) / seconds;
        }
    }
}
