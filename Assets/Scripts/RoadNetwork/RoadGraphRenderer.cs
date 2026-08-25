using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[ExecuteAlways]
public class RoadGraphRenderer : MonoBehaviour
{
    [System.Serializable]
    private class AlignmentEnvelope
    {
        public AlignmentMetadata metadata;
    }

    [System.Serializable]
    private class AlignmentMetadata
    {
        public UnityAlignment unity_alignment;
    }

    [System.Serializable]
    private class UnityAlignment
    {
        public AlignmentPosition position;
        public float rotation_y;
        public float scale = 1f;
    }

    [System.Serializable]
    private class AlignmentPosition
    {
        public float x;
        public float y;
        public float z;
    }

    [Header("JSON")]
    public string fileName = "ingolstadt_road_graph_square.json";

    [Header("Road Appearance")]
    public Material roadMaterial;
    public bool useDefaultRoadWidth = false;
    public float defaultRoadWidth = 6f;
    public float yOffset = 0.05f;
    public float textureLength = 10f;

    [Header("Road dimensions")]
    public float defaultLaneWidth = 3.2f;
    public float narrowLaneWidth = 2.8f;
    public float motorwayLaneWidth = 3.5f;
    public float defaultShoulderWidth = 0.25f;
    public float motorwayShoulderWidth = 1.0f;
    public float minimumRoadWidth = 2.5f;

    public void SetRoadFileFromString(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fileName = value.Trim();
    }

    [ContextMenu("Generate Road Meshes")]
    public void GenerateRoadMeshes()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "RoadGraphRenderer: Android StreamingAssets loading requires Play mode."
            );
            return;
        }

        StartCoroutine(GenerateRoadMeshesCoroutine());
#else
        string path = Path.Combine(
            Application.streamingAssetsPath,
            fileName
        );

        if (!File.Exists(path))
        {
            Debug.LogError($"Road JSON file not found: {path}");
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            GenerateRoadMeshesFromJson(json);
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                "RoadGraphRenderer: failed to read '" +
                fileName + "': " +
                exception.Message
            );
        }
#endif
    }

    private IEnumerator GenerateRoadMeshesCoroutine()
    {
        string path = Path.Combine(
            Application.streamingAssetsPath,
            fileName
        );

        using (UnityWebRequest request = UnityWebRequest.Get(path))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    "RoadGraphRenderer: road JSON could not be loaded: " +
                    path + "\n" + request.error
                );
                yield break;
            }

            GenerateRoadMeshesFromJson(
                request.downloadHandler.text
            );
        }
    }

    private void GenerateRoadMeshesFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError(
                "RoadGraphRenderer: road JSON is empty: " +
                fileName
            );
            return;
        }

        RoadGraphData loadedGraph;

        try
        {
            loadedGraph =
                JsonUtility.FromJson<RoadGraphData>(json);
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                "RoadGraphRenderer: could not deserialize '" +
                fileName + "': " +
                exception.Message
            );
            return;
        }

        if (loadedGraph == null ||
            loadedGraph.edges == null)
        {
            Debug.LogError("Could not read road graph JSON.");
            return;
        }

        ReadAndApplyUnityAlignment(json);
        GenerateRoadMeshesFromGraph(loadedGraph);
    }

    private void ReadAndApplyUnityAlignment(string json)
    {
        AlignmentEnvelope envelope = JsonUtility.FromJson<AlignmentEnvelope>(json);

        Vector3 position = Vector3.zero;
        float rotationY = 0f;
        float scale = 1f;

        if (envelope != null &&
            envelope.metadata != null &&
            envelope.metadata.unity_alignment != null)
        {
            UnityAlignment alignment = envelope.metadata.unity_alignment;

            if (alignment.position != null)
            {
                position = new Vector3(
                    alignment.position.x,
                    alignment.position.y,
                    alignment.position.z
                );
            }

            rotationY = alignment.rotation_y;

            if (alignment.scale > 0f)
                scale = alignment.scale;
        }
        else
        {
            Debug.LogWarning(
                $"No metadata.unity_alignment found in {fileName}. " +
                "Using position (0,0,0), rotation 0, scale 1."
            );
        }

        ApplyUnityAlignment(position, rotationY, scale);
    }

    public void ApplyUnityAlignment(
        Vector3 position,
        float rotationY,
        float uniformScale)
    {
        float safeScale = uniformScale > 0f ? uniformScale : 1f;

        transform.localPosition = position;
        transform.localRotation = Quaternion.Euler(0f, rotationY, 0f);
        transform.localScale = Vector3.one * safeScale;
    }

    /// <summary>
    /// Used by RoadNetworkManager during runtime reload so logical lanes and
    /// visual meshes are guaranteed to come from the exact same graph object.
    /// </summary>
    public void GenerateRoadMeshesFromGraph(RoadGraphData loadedGraph)
    {
        if (loadedGraph == null || loadedGraph.edges == null)
        {
            Debug.LogError("RoadGraphRenderer received an invalid graph.");
            return;
        }

        ClearRoadNetwork();

        int roadsCreated = 0;
        foreach (RoadEdgeData edge in loadedGraph.edges)
        {
            CreateRoadMesh(edge);
            roadsCreated++;
        }

        Debug.Log($"Created {roadsCreated} road meshes from {fileName}.");
    }

    private void CreateRoadMesh(RoadEdgeData edge)
    {
        if (edge.centerline == null || edge.centerline.Length < 2)
            return;

        List<Vector3> points = new List<Vector3>(edge.centerline.Length);

        foreach (Vector3Data point in edge.centerline)
        {
            points.Add(new Vector3(
                point.x,
                point.y,
                point.z
            ));
        }

        float roadWidth = useDefaultRoadWidth
            ? defaultRoadWidth
            : GetRoadWidth(edge);

        Mesh roadMesh = RoadMeshBuilder.BuildRoadStrip(
            points,
            roadWidth,
            textureLength,
            yOffset
        );

        GameObject roadObject = new GameObject($"Road_{edge.id}");
        roadObject.transform.SetParent(transform, false);

        MeshFilter meshFilter = roadObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = roadObject.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = roadMesh;
        meshRenderer.sharedMaterial = roadMaterial;
    }

    [ContextMenu("Clear Road Network")]
    public void ClearRoadNetwork()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            MeshFilter filter = child.GetComponent<MeshFilter>();
            Mesh generatedMesh = filter != null ? filter.sharedMesh : null;

            if (Application.isPlaying)
            {
                if (generatedMesh != null)
                    Destroy(generatedMesh);
                Destroy(child);
            }
            else
            {
                if (generatedMesh != null)
                    DestroyImmediate(generatedMesh);
                DestroyImmediate(child);
            }
        }
    }

    private int GetLaneCount(RoadEdgeData edge)
    {
        int laneCount = edge.lanes;

        if (laneCount <= 0)
            laneCount = GetDefaultLaneCount(edge.highway, edge.oneway);

        return laneCount;
    }

    private int GetDefaultLaneCount(string highway, bool oneway)
    {
        switch (NormalizeHighway(highway))
        {
            case "motorway":
            case "motorway_link":
                return oneway ? 2 : 4;

            case "trunk":
            case "trunk_link":
            case "primary":
            case "primary_link":
                return 2;

            case "secondary":
            case "secondary_link":
            case "tertiary":
            case "tertiary_link":
                return oneway ? 1 : 2;

            case "residential":
            case "living_street":
            case "unclassified":
                return oneway ? 1 : 2;

            case "service":
            case "track":
                return 1;

            default:
                return oneway ? 1 : 2;
        }
    }

    private string NormalizeHighway(string highway)
    {
        if (string.IsNullOrWhiteSpace(highway))
            return "unknown";

        string value = highway.Trim().ToLowerInvariant();

        value = value
            .Replace("[", "")
            .Replace("]", "")
            .Replace("\"", "")
            .Replace("'", "");

        if (value.Contains(","))
            value = value.Split(',')[0].Trim();

        if (value.Contains(";"))
            value = value.Split(';')[0].Trim();

        return value;
    }

    private float GetLaneWidth(RoadEdgeData edge)
    {
        switch (NormalizeHighway(edge.highway))
        {
            case "motorway":
            case "motorway_link":
            case "trunk":
            case "trunk_link":
                return motorwayLaneWidth;

            case "living_street":
            case "service":
                return narrowLaneWidth;

            default:
                return defaultLaneWidth;
        }
    }

    private float GetShoulderWidth(RoadEdgeData edge)
    {
        switch (NormalizeHighway(edge.highway))
        {
            case "motorway":
            case "trunk":
                return motorwayShoulderWidth;

            case "motorway_link":
            case "trunk_link":
                return 0.5f;

            default:
                return defaultShoulderWidth;
        }
    }

    private float GetRoadWidth(RoadEdgeData edge)
    {
        int laneCount = GetLaneCount(edge);
        float laneWidth = GetLaneWidth(edge);
        float shoulderWidth = GetShoulderWidth(edge);

        float width = laneCount * laneWidth + shoulderWidth * 2f;
        return Mathf.Max(width, minimumRoadWidth);
    }
}
