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

        /// <summary>Continuous sphere sweep against a hand-local box expanded by ball radius.
        /// Corner padding is conservative; face normals include the thumb-side wall.</summary>
        public static bool SweptBoxContact(Vector3 start, Vector3 end, Vector3 halfSize, float radius,
            out float fraction, out Vector3 normal)
        {
            var extent = halfSize + Vector3.one * radius;
            var delta = end - start;
            float enter = 0f, leave = 1f;
            normal = Vector3.zero;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(delta[axis]) < 1e-7f)
                {
                    if (Mathf.Abs(start[axis]) > extent[axis]) { fraction = 0f; return false; }
                    continue;
                }
                float a = (-extent[axis] - start[axis]) / delta[axis];
                float b = (extent[axis] - start[axis]) / delta[axis];
                var face = Vector3.zero; face[axis] = -Mathf.Sign(delta[axis]);
                if (a > b) { float swap = a; a = b; b = swap; }
                if (a > enter) { enter = a; normal = face; }
                leave = Mathf.Min(leave, b);
                if (enter > leave) { fraction = 0f; return false; }
            }
            fraction = enter;
            if (normal == Vector3.zero)
            {
                // Initial overlap: select the nearest outer face instead of always using palm-up.
                int nearest = 0;
                for (int axis = 1; axis < 3; axis++)
                    if (extent[axis] - Mathf.Abs(start[axis]) < extent[nearest] - Mathf.Abs(start[nearest])) nearest = axis;
                normal[nearest] = start[nearest] >= 0f ? 1f : -1f;
            }
            return true;
        }
    }
}
