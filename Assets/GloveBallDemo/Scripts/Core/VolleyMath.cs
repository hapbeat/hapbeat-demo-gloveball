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

        /// <summary>
        /// Spike response: the ball leaves along the swing, not along the palm-face reflection, so a fast downward swing
        /// always drives the ball down. Upward swings are flattened to at most maxRise (sine of the climb angle).
        /// </summary>
        public static Vector3 SpikeVelocity(Vector3 handVelocity,float power,float minimumSpeed,float maximumSpeed,float maxRise)
        {
            var direction=handVelocity.sqrMagnitude>1e-6f ? handVelocity.normalized : Vector3.forward;
            if(direction.y>maxRise)
            {
                var flat=new Vector3(direction.x,0f,direction.z);
                flat=flat.sqrMagnitude>1e-6f ? flat.normalized : Vector3.forward;
                direction=(flat*Mathf.Sqrt(1f-maxRise*maxRise)+Vector3.up*maxRise).normalized;
            }
            return direction*Mathf.Clamp(handVelocity.magnitude*power,minimumSpeed,maximumSpeed);
        }

        /// <summary>
        /// Ballistic velocity from <paramref name="start"/> to <paramref name="target"/> whose speed is closest to
        /// <paramref name="speed"/> while it passes the net plane (through <paramref name="netPoint"/>, facing
        /// <paramref name="netForward"/>) no lower than <paramref name="netTop"/>. Flight times 0.12-2 s are tried; the
        /// fastest flights come first, so among equal speeds the flattest-arc, sharpest one wins.
        /// </summary>
        public static Vector3 FlightOver(Vector3 start, Vector3 target, Vector3 netPoint, Vector3 netForward, float netTop, float speed, Vector3 gravity)
        {
            float a = Vector3.Dot(start - netPoint, netForward), b = Vector3.Dot(target - netPoint, netForward);
            bool crosses = a < 0f && b > 0f;
            float fraction = crosses ? -a / (b - a) : 0f;
            var best = Vector3.zero; float bestCost = float.MaxValue;
            for (float seconds = .12f; seconds <= 2f; seconds += .01f)
            {
                var velocity = (target - start - .5f * gravity * seconds * seconds) / seconds;
                if (crosses)
                {
                    float t = fraction * seconds;
                    if (start.y + velocity.y * t + .5f * gravity.y * t * t < netTop) continue;
                }
                float cost = Mathf.Abs(velocity.magnitude - speed);
                if (cost < bestCost) { bestCost = cost; best = velocity; }
            }
            return bestCost < float.MaxValue ? best : ServeVelocity(start, target + Vector3.up * Mathf.Max(0f, netTop - target.y), 1f, gravity);
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
