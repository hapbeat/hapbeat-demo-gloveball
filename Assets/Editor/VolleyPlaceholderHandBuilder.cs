using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the public placeholder hands used when the private XR Hands sample meshes are not linked into the project.
/// Hapbeat-authored: generic hand proportions, one low-poly capsule per bone, skinned rigidly to the bone it follows.
/// The transform hierarchy uses the XR Hands joint names (L_/R_ prefix) so XRHandSkeletonDriver.FindJointsFromRoot maps
/// all 26 joints. Rest pose follows the XR Hands joint frame: +Z toward the fingertips, +Y out of the back of the hand.
/// </summary>
public static class VolleyPlaceholderHandBuilder
{
    const string HandMaterialPath = "Assets/GloveBallDemo/Art/UnityGhostHands/Materials/Unity_Hand_Medium.mat";
    const string DepthOnlyPath = "Assets/GloveBallDemo/Art/UnityGhostHands/Materials/DepthOnly.mat";
    const int RadialSegments = 10;
    const int CapRings = 3;

    struct Joint
    {
        public string Name; public int Parent; public Vector3 Position; public float Radius;
        public Joint(string name, int parent, float x, float y, float z, float radius)
        { Name = name; Parent = parent; Position = new Vector3(x, y, z); Radius = radius; }
    }

    // Right hand, metres, wrist space. Radius = thickness of the capsule from this joint to its child.
    static readonly Joint[] RightJoints =
    {
        new Joint("Wrist", -1, 0f, 0f, 0f, 0f),
        new Joint("Palm", 0, 0f, 0f, .055f, 0f),
        new Joint("ThumbMetacarpal", 0, -.022f, -.012f, .022f, .015f),
        new Joint("ThumbProximal", 2, -.042f, -.018f, .052f, .012f),
        new Joint("ThumbDistal", 3, -.056f, -.019f, .080f, .010f),
        new Joint("ThumbTip", 4, -.064f, -.019f, .103f, 0f),
        new Joint("IndexMetacarpal", 0, -.011f, 0f, .018f, .0125f),
        new Joint("IndexProximal", 6, -.024f, 0f, .092f, .010f),
        new Joint("IndexIntermediate", 7, -.027f, 0f, .133f, .009f),
        new Joint("IndexDistal", 8, -.029f, 0f, .158f, .008f),
        new Joint("IndexTip", 9, -.030f, 0f, .180f, 0f),
        new Joint("MiddleMetacarpal", 0, 0f, 0f, .018f, .0125f),
        new Joint("MiddleProximal", 11, -.003f, 0f, .095f, .010f),
        new Joint("MiddleIntermediate", 12, -.003f, 0f, .140f, .0092f),
        new Joint("MiddleDistal", 13, -.003f, 0f, .168f, .0082f),
        new Joint("MiddleTip", 14, -.003f, 0f, .192f, 0f),
        new Joint("RingMetacarpal", 0, .011f, 0f, .018f, .012f),
        new Joint("RingProximal", 16, .019f, 0f, .090f, .0095f),
        new Joint("RingIntermediate", 17, .022f, 0f, .131f, .0087f),
        new Joint("RingDistal", 18, .024f, 0f, .157f, .0078f),
        new Joint("RingTip", 19, .025f, 0f, .178f, 0f),
        new Joint("LittleMetacarpal", 0, .020f, 0f, .016f, .011f),
        new Joint("LittleProximal", 21, .037f, 0f, .081f, .0085f),
        new Joint("LittleIntermediate", 22, .042f, 0f, .111f, .0078f),
        new Joint("LittleDistal", 23, .045f, 0f, .131f, .007f),
        new Joint("LittleTip", 24, .047f, 0f, .150f, 0f),
    };

    [MenuItem("GloveBall Demo/Volley/Build Placeholder Hands")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var folder = VolleyHandUpgrade.FallbackHandFolder.TrimEnd('/');
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/GloveBallDemo/Art", "FallbackHands");
        var materials = new[] { AssetDatabase.LoadAssetAtPath<Material>(HandMaterialPath), AssetDatabase.LoadAssetAtPath<Material>(DepthOnlyPath) };
        if (materials.Any(m => m == null)) throw new InvalidOperationException("Ghost hand materials are missing.");
        foreach (var left in new[] { true, false }) BuildSide(left, materials);
        AssetDatabase.SaveAssets();
        Debug.Log("[VolleyHands] Placeholder hands built in " + folder + ".");
    }

    static void BuildSide(bool left, Material[] materials)
    {
        var model = left ? "LeftHand" : "RightHand";
        var prefix = left ? "L_" : "R_";
        var joints = RightJoints.Select(j => { if (left) j.Position.x = -j.Position.x; return j; }).ToArray();

        var root = new GameObject(model + "Placeholder");
        try
        {
            var bones = new Transform[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            {
                bones[i] = new GameObject(prefix + joints[i].Name).transform;
                bones[i].SetParent(i == 0 ? root.transform : bones[joints[i].Parent], false);
            }
            for (int i = 0; i < joints.Length; i++)
            {
                int child = Array.FindIndex(joints, j => j.Parent == i && !j.Name.EndsWith("Palm", StringComparison.Ordinal));
                var rotation = Quaternion.identity;
                if (i > 1 && child >= 0) rotation = Quaternion.LookRotation(joints[child].Position - joints[i].Position, Vector3.up);
                else if (i > 1) rotation = bones[joints[i].Parent].rotation; // tip keeps the distal frame
                bones[i].SetPositionAndRotation(joints[i].Position, rotation);
            }

            var meshObject = new GameObject(prefix + "HandMesh");
            meshObject.transform.SetParent(root.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            var mesh = BuildMesh(model + "PlaceholderMesh", joints, bones, meshObject.transform, left);
            var meshPath = VolleyHandUpgrade.FallbackHandFolder + model + "PlaceholderMesh.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing == null) AssetDatabase.CreateAsset(mesh, meshPath);
            else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.sharedMaterials = materials;
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = new Bounds(new Vector3(0f, 0f, .09f), new Vector3(.2f, .1f, .24f));
            PrefabUtility.SaveAsPrefabAsset(root, VolleyHandUpgrade.FallbackHandFolder + model + "Placeholder.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static Mesh BuildMesh(string name, Joint[] joints, Transform[] bones, Transform meshTransform, bool left)
    {
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var weights = new List<BoneWeight>(); var triangles = new List<int>();
        for (int i = 2; i < joints.Length; i++)
        {
            int parent = joints[i].Parent;
            if (joints[parent].Radius <= 0f) continue; // wrist -> metacarpal base has no visible segment
            AddCapsule(joints[parent].Position, joints[i].Position, joints[parent].Radius, parent, vertices, normals, weights, triangles);
        }
        float side = left ? -1f : 1f;
        // Palm slab across the knuckles and a short wrist stub, so the metacarpal capsules read as one palm.
        AddCapsule(new Vector3(-.022f * side, 0f, .068f), new Vector3(.024f * side, 0f, .062f), .016f, 1, vertices, normals, weights, triangles);
        AddCapsule(new Vector3(-.016f * side, 0f, .026f), new Vector3(.018f * side, 0f, .026f), .017f, 0, vertices, normals, weights, triangles);
        AddCapsule(new Vector3(-.012f, 0f, -.01f), new Vector3(.012f, 0f, -.01f), .018f, 0, vertices, normals, weights, triangles);

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.boneWeights = weights.ToArray();
        mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * meshTransform.localToWorldMatrix).ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Low-poly capsule from a to b, every vertex bound fully to one bone.</summary>
    static void AddCapsule(Vector3 a, Vector3 b, float radius, int bone, List<Vector3> vertices, List<Vector3> normals, List<BoneWeight> weights, List<int> triangles)
    {
        var axis = b - a;
        var frame = Quaternion.FromToRotation(Vector3.up, axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up);
        int start = vertices.Count, rings = 0;
        for (int half = 0; half < 2; half++)
        {
            var centre = half == 0 ? a : b;
            for (int k = 0; k <= CapRings; k++)
            {
                float phi = (half == 0 ? -90f + 90f * k / CapRings : 90f * k / CapRings) * Mathf.Deg2Rad;
                for (int s = 0; s < RadialSegments; s++)
                {
                    float theta = 2f * Mathf.PI * s / RadialSegments;
                    var direction = frame * new Vector3(Mathf.Cos(phi) * Mathf.Cos(theta), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(theta));
                    vertices.Add(centre + direction * radius); normals.Add(direction);
                    weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                }
                rings++;
            }
        }
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < RadialSegments; s++)
            {
                int i0 = start + r * RadialSegments + s, i1 = start + r * RadialSegments + (s + 1) % RadialSegments;
                int i2 = i0 + RadialSegments, i3 = i1 + RadialSegments;
                AddTriangle(i0, i2, i1, vertices, normals, triangles);
                AddTriangle(i1, i2, i3, vertices, normals, triangles);
            }
    }

    /// <summary>Skips collapsed pole triangles and winds each face so Unity treats the outward side as front.</summary>
    static void AddTriangle(int i0, int i1, int i2, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
    {
        var face = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
        if (face.sqrMagnitude < 1e-14f) return;
        if (Vector3.Dot(face, normals[i0] + normals[i1] + normals[i2]) < 0f) { var t = i1; i1 = i2; i2 = t; }
        triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
    }
}
