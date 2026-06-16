using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
public class CityRoadSpline : MonoBehaviour
{
    [Header("Road Geometry")]
    [Tooltip("Road surface width in metres.")]
    [Min(0.5f)]
    public float roadWidth = 7f;

    [Tooltip("Elevation above ground. Prevents Z-fighting with CityGroundSurface.")]
    public float yOffset = 0.005f;

    [Tooltip("Mesh samples per metre of spline length. Higher = smoother curves.")]
    [Range(1, 10)]
    public int samplesPerMetre = 3;

    [Header("Material")]
    public Material roadMaterial;

    // --- private state ---------------------------------------------------------

    Mesh _mesh;

    // --- Unity callbacks ---------------------------------------------------------

    void OnEnable()   => Rebuild();
    void OnValidate() => Rebuild();

    // --- Public API ---------------------------------------------------------

    [ContextMenu("Generate Road")]
    public void Rebuild()
    {
        var container = GetComponent<SplineContainer>();
        if (container == null || container.Splines == null || container.Splines.Count == 0)
            return;

        // Collect per-spline meshes, then combine into one
        var combines = new System.Collections.Generic.List<CombineInstance>();
        foreach (var spline in container.Splines)
        {
            if (spline == null || spline.Count == 0) continue;
            float length = spline.GetLength();
            if (length < 0.1f) continue;

            int samples = Mathf.Max(2, Mathf.RoundToInt(length * samplesPerMetre));
            var m = BuildSplineMesh(spline, samples);
            if (m != null)
                combines.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
        }

        if (combines.Count == 0) return;

        if (_mesh == null) _mesh = new Mesh { name = "CityRoadSpline" };
        _mesh.Clear();
        _mesh.CombineMeshes(combines.ToArray(), true, false);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = _mesh;

        var mr = GetComponent<MeshRenderer>();
        if (roadMaterial != null) mr.sharedMaterial = roadMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        if (mr.sharedMaterial != null)
            mr.sharedMaterial.EnableKeyword("_RECEIVE_SHADOWS_OFF");

        var mc = GetComponent<MeshCollider>();
        if (mc != null) mc.sharedMesh = _mesh;
    }

    // --- Mesh generation ---------------------------------------------------------

    Mesh BuildSplineMesh(Spline spline, int samples)
    {
        // Two vertices per sample (left edge + right edge)
        int vCount = (samples + 1) * 2;
        var verts  = new Vector3[vCount];
        var uvs    = new Vector2[vCount];
        var tris   = new int[samples * 6];

        float uvAlong = 0f;         // accumulated road-length UV

        Vector3 prevLeft = Vector3.zero;

        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            SplineUtility.Evaluate(spline, t,
                out float3 pos, out float3 tangent, out float3 up);

            // Flatten tangent to XZ plane so the road stays level
            float3 tangentXZ = math.normalize(new float3(tangent.x, 0f, tangent.z));

            // Right vector: cross( up=(0,1,0), tangentXZ )
            float3 right = math.normalize(math.cross(new float3(0f, 1f, 0f), tangentXZ));

            float halfW = roadWidth * 0.5f;
            var leftPt  = new Vector3(pos.x - right.x * halfW, yOffset, pos.z - right.z * halfW);
            var rightPt = new Vector3(pos.x + right.x * halfW, yOffset, pos.z + right.z * halfW);

            // Accumulate UV along road
            if (i > 0)
                uvAlong += Vector3.Distance(prevLeft, leftPt);

            int li = i * 2;
            verts[li]     = leftPt;
            verts[li + 1] = rightPt;
            // U: 0=left, 1=right  |  V tiles every roadWidth metres along road
            uvs[li]     = new Vector2(0f, uvAlong / roadWidth);
            uvs[li + 1] = new Vector2(1f, uvAlong / roadWidth);

            prevLeft = leftPt;
        }

        // Quad strip triangles
        int ti = 0;
        for (int i = 0; i < samples; i++)
        {
            int bl = i * 2,  br = bl + 1;
            int tl = bl + 2, tr = bl + 3;
            tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = tr;
            tris[ti++] = bl; tris[ti++] = tr; tris[ti++] = br;
        }

        var mesh = new Mesh();
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
