using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Static collision plane plus cosmetic, local mesh dents. No cloth solver or live haptic events.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VolleyNetResponse : MonoBehaviour
    {
        public MeshFilter Visual;
        [Range(0f, 1f)] public float NormalRetention = .08f;
        [Range(0f, 1f)] public float TangentialRetention = .25f;
        [Min(.01f)] public float MaximumDent = .18f;
        [Min(.1f)] public float DentRadius = .7f;
        [Min(.1f)] public float Decay = 5f;
        [Min(1f)] public float Oscillation = 16f;
        struct Dent { public Vector3 Point; public float Strength, Age; }
        readonly Dent[] _dents = new Dent[4];
        int _next;
        Mesh _original, _mesh;
        Vector3[] _rest, _netPoints, _vertices;

        void Start()
        {
            _original = Visual.sharedMesh;
            _mesh = Instantiate(_original);
            _mesh.name = "Volley net instance deformation";
            _mesh.MarkDynamic(); Visual.sharedMesh = _mesh;
            _rest = _mesh.vertices; _vertices = new Vector3[_rest.Length];
            _netPoints = new Vector3[_rest.Length];
            for (int i = 0; i < _rest.Length; i++)
                _netPoints[i] = transform.InverseTransformPoint(Visual.transform.TransformPoint(_rest[i]));
            var bounds = _mesh.bounds;
            bounds.Expand(Visual.transform.InverseTransformVector(transform.forward * MaximumDent).magnitude * 2f);
            _mesh.bounds = bounds;
        }

        void OnCollisionEnter(Collision collision)
        {
            var body = collision.rigidbody;
            if (body == null || body.isKinematic || !body.TryGetComponent<Ball>(out _)) return;
            var contact = collision.GetContact(0);
            var outward = transform.forward * (Vector3.Dot(body.worldCenterOfMass - transform.position, transform.forward) >= 0f ? 1f : -1f);
            var incoming = collision.relativeVelocity;
            // Collision.relativeVelocity is reported relative to the receiving collider.
            // Orient consistently toward the net on either side.
            if (Vector3.Dot(incoming, outward) > 0f) incoming = -incoming;
            body.linearVelocity = DampedReturn(incoming, outward, NormalRetention, TangentialRetention);
            AddDent(contact.point, incoming);
        }

        public static Vector3 DampedReturn(Vector3 incoming, Vector3 outward, float normalRetention, float tangentRetention)
        {
            float speed = Mathf.Abs(Vector3.Dot(incoming, outward));
            return Vector3.ProjectOnPlane(incoming, outward) * tangentRetention + outward * speed * normalRetention;
        }

        public void AddDent(Vector3 point, Vector3 incoming)
        {
            _dents[_next] = new Dent { Point = transform.InverseTransformPoint(point),
                Strength = Mathf.Clamp(Vector3.Dot(incoming, transform.forward) / 6f, -1f, 1f), Age = 0f };
            _next = (_next + 1) % _dents.Length;
        }

        public static float DentWeight(Vector3 point, Vector3 hit, float radius)
        {
            // Pin the tape at top/bottom and all post/cable geometry outside the net rectangle.
            if (Mathf.Abs(point.x) >= 4.75f || point.y <= 1.43f || point.y >= 2.43f) return 0f;
            var d = new Vector2(point.x - hit.x, point.y - hit.y);
            float edge = Mathf.Clamp01((4.75f - Mathf.Abs(point.x)) / .25f)
                * Mathf.Clamp01(Mathf.Min(point.y - 1.43f, 2.43f - point.y) / .18f);
            return Mathf.Exp(-d.sqrMagnitude / (radius * radius)) * edge;
        }

        void LateUpdate() { Animate(Time.deltaTime); }

        public void Animate(float deltaTime)
        {
            if (_mesh == null) return;
            bool changed = false;
            for (int j = 0; j < _dents.Length; j++)
                if (_dents[j].Strength != 0f) { _dents[j].Age += deltaTime; changed = true; }
            if (!changed) return;
            var meshDirection = Visual.transform.InverseTransformVector(transform.forward);
            for (int i = 0; i < _rest.Length; i++)
            {
                float offset = 0f;
                for (int j = 0; j < _dents.Length; j++)
                {
                    var dent = _dents[j];
                    offset += DentWeight(_netPoints[i], dent.Point, DentRadius) * dent.Strength
                        * Mathf.Sin(dent.Age * Oscillation) * Mathf.Exp(-dent.Age * Decay) * MaximumDent;
                }
                _vertices[i] = _rest[i] + meshDirection * Mathf.Clamp(offset, -MaximumDent, MaximumDent);
            }
            _mesh.vertices = _vertices;
            // Thin cords retain authored normals; avoid per-frame normal rebuilds on Quest.
            for (int j = 0; j < _dents.Length; j++)
                if (_dents[j].Age * Decay > 8f) _dents[j].Strength = 0f;
            bool active = false;
            foreach (var dent in _dents) active |= dent.Strength != 0f;
            if (!active) _mesh.vertices = _rest;
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Visual != null) Visual.sharedMesh = _original;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        }
    }
}
