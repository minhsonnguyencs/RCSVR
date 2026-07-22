/*using System.IO;
using UnityEngine;

public class RoadGraphRenderer : MonoBehaviour
{
    public string fileName = "ingolstadt_road_graph.json";
    public Material roadLineMaterial;
    public float lineWidth = 1.5f;
    public float yOffset = 0.1f;

    void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);

        if (!File.Exists(path))
        {
            Debug.LogError("Road graph JSON not found at: " + path);
            return;
        }

        string json = File.ReadAllText(path);
        RoadGraphData graph = JsonUtility.FromJson<RoadGraphData>(json);

        Debug.Log($"Loaded road graph: {graph.nodes.Length} nodes, {graph.edges.Length} edges");

        foreach (RoadEdgeData edge in graph.edges)
        {
            DrawEdge(edge);
        }
    }

    void DrawEdge(RoadEdgeData edge)
    {
        GameObject obj = new GameObject("Road_" + edge.id);
        obj.transform.parent = transform;

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.material = roadLineMaterial;
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
}*/

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