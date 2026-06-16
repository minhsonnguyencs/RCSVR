using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
public class CityGroundSurface : MonoBehaviour
{
    public enum LodLevel { LOD1 = 10, LOD2 = 25, LOD3 = 50 }

    [Header("LOD")]
    [Tooltip("Mesh subdivision count per axis. LOD3 = highest detail.")]
    public LodLevel lodLevel = LodLevel.LOD2;

    [Header("Surface")]
    [Tooltip("How many metres of world-space map to one UV unit (controls texture tiling).")]
    public float uvTileSize = 10f;

    [Tooltip("Number of points sampled from the spline to compute the bounding box.")]
    [Range(8, 128)]
    public int boundarySamples = 64;

    [Header("Material")]
    public Material groundMaterial;

    // ── private state ──────────────────────────────────────────────────────────

    Mesh _mesh;

    // ── Unity callbacks ────────────────────────────────────────────────────────

    void OnEnable()  => Rebuild();
    void OnValidate() => Rebuild();   // live-update in the editor on any Inspector change

    // ── Public API ─────────────────────────────────────────────────────────────

    [ContextMenu("Generate Surface")]
    public void Rebuild()
    {
        var container = GetComponent<SplineContainer>();
        if (container == null || container.Spline == null || container.Spline.Count == 0)
            return;

        Spline spline = container.Spline;

        // --- 1. Sample boundary in local space, find XZ extents ------------------
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i < boundarySamples; i++)
        {
            float t = (float)i / boundarySamples;
            float3 localPos = SplineUtility.EvaluatePosition(spline, t);
            if (localPos.x < minX) minX = localPos.x;
            if (localPos.x > maxX) maxX = localPos.x;
            if (localPos.z < minZ) minZ = localPos.z;
            if (localPos.z > maxZ) maxZ = localPos.z;
        }

        // Guard: degenerate spline
        if (maxX - minX < 0.1f || maxZ - minZ < 0.1f)
            return;

        // --- 2. Build the mesh ---------------------------------------------------
        BuildGridMesh(minX, maxX, minZ, maxZ, (int)lodLevel);
    }

    // ── Mesh generation ────────────────────────────────────────────────────────

    void BuildGridMesh(float minX, float maxX, float minZ, float maxZ, int subdivs)
    {
        int dim    = subdivs + 1;
        int vCount = dim * dim;
        int tCount = subdivs * subdivs * 6;

        var verts = new Vector3[vCount];
        var uvs   = new Vector2[vCount];
        var tris  = new int[tCount];

        for (int z = 0; z <= subdivs; z++)
        {
            float wz = Mathf.Lerp(minZ, maxZ, (float)z / subdivs);
            for (int x = 0; x <= subdivs; x++)
            {
                float wx   = Mathf.Lerp(minX, maxX, (float)x / subdivs);
                int   idx  = z * dim + x;
                verts[idx] = new Vector3(wx, 0f, wz);
                uvs[idx]   = new Vector2(wx / uvTileSize, wz / uvTileSize);
            }
        }

        int ti = 0;
        for (int z = 0; z < subdivs; z++)
        {
            for (int x = 0; x < subdivs; x++)
            {
                int bl = z * dim + x;
                int br = bl + 1;
                int tl = bl + dim;
                int tr = tl + 1;
                // two CCW triangles per quad (Unity front-face = CCW)
                tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = tr;
                tris[ti++] = bl; tris[ti++] = tr; tris[ti++] = br;
            }
        }

        // --- Assign to mesh component -------------------------------------------
        if (_mesh == null)
            _mesh = new Mesh { name = "CityGroundSurface" };

        _mesh.Clear();
        _mesh.vertices  = verts;
        _mesh.uv        = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = _mesh;

        var mr = GetComponent<MeshRenderer>();
        if (groundMaterial != null)
            mr.sharedMaterial = groundMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        // URP controls receive shadows via material keyword, not renderer property
        if (mr.sharedMaterial != null)
            mr.sharedMaterial.EnableKeyword("_RECEIVE_SHADOWS_OFF");

        // Optional MeshCollider (add component manually if physics is needed)
        var mc = GetComponent<MeshCollider>();
        if (mc != null)
            mc.sharedMesh = _mesh;
    }
}
