using System.Collections.Generic;
using System.IO;
using UnityEngine;

[ExecuteAlways]
public class RoadGraphRenderer : MonoBehaviour
{
    [Header("JSON")]
    public string fileName = "ingolstadt_road_graph.json";

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


    [ContextMenu("Generate Road Meshes")]
    public void GenerateRoadMeshes()
    {
        ClearRoadNetwork();

        string path = Path.Combine(
            Application.streamingAssetsPath,
            fileName
        );

        if (!File.Exists(path))
        {
            Debug.LogError($"Road JSON file not found: {path}");
            return;
        }

        string json = File.ReadAllText(path);

        RoadGraphData graph =
            JsonUtility.FromJson<RoadGraphData>(json);

        if (graph == null || graph.edges == null)
        {
            Debug.LogError("Could not read road graph JSON.");
            return;
        }

        int roadsCreated = 0;

        foreach (RoadEdgeData edge in graph.edges)
        {
            CreateRoadMesh(edge);
            roadsCreated++;
        }

        Debug.Log($"Created {roadsCreated} road meshes.");
    }

    private void CreateRoadMesh(RoadEdgeData edge)
    {
        if (edge.centerline == null || edge.centerline.Length < 2)
            return;

        List<Vector3> points = new List<Vector3>();

        foreach (Vector3Data point in edge.centerline)
        {
            points.Add(new Vector3(
                point.x,
                point.y,
                point.z
            ));
        }

        float roadWidth = defaultRoadWidth;

        if (!useDefaultRoadWidth) {
            roadWidth = GetRoadWidth(edge);
        }

        Mesh roadMesh = RoadMeshBuilder.BuildRoadStrip(
            points,
            roadWidth,
            textureLength,
            yOffset
        );

        GameObject roadObject =
            new GameObject($"Road_{edge.id}");

        roadObject.transform.SetParent(
            transform,
            false
        );

        MeshFilter meshFilter =
            roadObject.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer =
            roadObject.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = roadMesh;
        meshRenderer.sharedMaterial = roadMaterial;
    }

    [ContextMenu("Clear Road Network")]
    public void ClearRoadNetwork()
    {
        while (transform.childCount > 0)
        {
            DestroyImmediate(
                transform.GetChild(0).gameObject
            );
        }
    }

    private int GetLaneCount(RoadEdgeData edge)
    {
        int laneCount = edge.lanes;

        // Handle missing or invalid lane data.
        if (laneCount <= 0)
        {
            laneCount = GetDefaultLaneCount(edge.highway, edge.oneway);
        }

        return laneCount;
    }

    private int GetDefaultLaneCount(string highway, bool oneway)
    {
        switch (highway)
        {
            case "motorway":
            case "motorway_link":
                return oneway ? 2 : 4;

            case "trunk":
            case "trunk_link":
            case "primary":
            case "primary_link":
                return oneway ? 2 : 2;

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

        string value = highway
            .Trim()
            .ToLowerInvariant();

        // Handles values such as "['primary', 'secondary']"
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

        float width =
            laneCount * laneWidth
            + shoulderWidth * 2f;

        return Mathf.Max(width, minimumRoadWidth);
    }
}



/**
using System.IO;
using UnityEngine;

[ExecuteAlways]
public class RoadGraphRenderer : MonoBehaviour
{
    public string fileName = "ingolstadt_road_graph.json";
    public Material roadLineMaterial;
    public float lineWidth = 1.5f;
    public float yOffset = 0.1f;

    [ContextMenu("Generate Road Network")]
    public void GenerateRoadNetwork()
    {
        ClearRoadNetwork();

        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        string json = File.ReadAllText(path);
        RoadGraphData graph = JsonUtility.FromJson<RoadGraphData>(json);

        foreach (RoadEdgeData edge in graph.edges)
            DrawEdge(edge);

        Debug.Log($"Generated road graph: {graph.nodes.Length} nodes, {graph.edges.Length} edges");
    }

    [ContextMenu("Clear Road Network")]
    public void ClearRoadNetwork()
    {
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }
    }

    void DrawEdge(RoadEdgeData edge)
    {
        GameObject obj = new GameObject("Road_" + edge.id);
        obj.transform.parent = transform;

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.material = roadLineMaterial;
        lr.startColor = Color.red;
        lr.endColor = Color.red;
        lr.widthMultiplier = lineWidth;
        lr.positionCount = edge.centerline.Length;
        lr.useWorldSpace = false;

        Vector3[] points = new Vector3[edge.centerline.Length];

        for (int i = 0; i < edge.centerline.Length; i++)
        {
            points[i] = new Vector3(
                edge.centerline[i].x,
                edge.centerline[i].y + yOffset,
                edge.centerline[i].z
            );
        }

        lr.SetPositions(points);
    }
}
**/