using System.Collections.Generic;
using System.IO;
using UnityEngine;

using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class RoadNetworkManager : MonoBehaviour
{
    [Header("Road Graph")]
    public string fileName =
        "ingolstadt_road_graph_square.json";

    [Header("Lane Geometry")]
    public float laneOffset = 1.5f;

    public RoadGraphData graph;

    public Dictionary<long, RoadNodeData> nodesById =
        new Dictionary<long, RoadNodeData>();

    public Dictionary<long, List<Lane>> lanesFromNode =
        new Dictionary<long, List<Lane>>();

    public List<Lane> allLanes =
        new List<Lane>();

    [Header("Coordinate Alignment")]
    public Transform roadNetworkTransform;

    void Awake()
    {
        LoadGraph();
    }

    void LoadGraph()
    {
        string path = Path.Combine(
            Application.streamingAssetsPath,
            fileName
        );

        if (!File.Exists(path))
        {
            Debug.LogError(
                "Road graph JSON not found: " + path
            );

            return;
        }

        string json = File.ReadAllText(path);

        graph =
            JsonUtility.FromJson<RoadGraphData>(json);

        if (graph == null)
        {
            Debug.LogError(
                "Could not deserialize road graph."
            );

            return;
        }

        nodesById.Clear();
        lanesFromNode.Clear();
        allLanes.Clear();

        // Build node lookup
        foreach (RoadNodeData node in graph.nodes)
        {
            nodesById[node.id] = node;

            lanesFromNode[node.id] =
                new List<Lane>();
        }

        // Turn every directed edge into one lane
        foreach (RoadEdgeData edge in graph.edges)
        {
            if (edge.centerline == null ||
                edge.centerline.Length < 2)
            {
                continue;
            }

            List<Vector3> offsetPoints =
                LaneGeometry.BuildOffsetLane(
                    edge.centerline,
                    laneOffset
                );

            Lane lane =
                new Lane(
                    edge,
                    offsetPoints
                );

            allLanes.Add(lane);

            if (!lanesFromNode.ContainsKey(
                lane.startNode))
            {
                lanesFromNode[lane.startNode] =
                    new List<Lane>();
            }

            lanesFromNode[lane.startNode]
                .Add(lane);
        }

        Debug.Log(
            $"Traffic graph loaded. " +
            $"{graph.nodes.Length} nodes, " +
            $"{graph.edges.Length} raw edges, " +
            $"{allLanes.Count} lanes."
        );
    }

    public Vector3 LanePointToWorld(Vector3 localPoint)
    {
        return roadNetworkTransform.TransformPoint(localPoint);
    }

    void OnDrawGizmosSelected()
    {
        if (allLanes == null || roadNetworkTransform == null)
            return;

        foreach (Lane lane in allLanes)
        {
            if (lane.points == null || lane.points.Count < 2)
                continue;

            for (int i = 0; i < lane.points.Count - 1; i++)
            {
                Vector3 p1 = roadNetworkTransform.TransformPoint(
                    lane.points[i]
                );

                Vector3 p2 = roadNetworkTransform.TransformPoint(
                    lane.points[i + 1]
                );

                p1 += Vector3.up * 0.2f;
                p2 += Vector3.up * 0.2f;

                Vector3 direction = p2 - p1;
                direction.y = 0f;

                if (direction.sqrMagnitude < 0.0001f)
                    continue;

                direction.Normalize();

                // Convert direction to angle around the Y axis.
                // 0° = north (+Z), 90° = east (+X), etc.
                float angle =
                    Mathf.Atan2(direction.x, direction.z)
                    * Mathf.Rad2Deg;

                if (angle < 0f)
                    angle += 360f;

                // Map 0–360 degrees onto HSV hue 0–1.
                float hue = angle / 360f;

                Gizmos.color = Color.HSVToRGB(
                    hue,
                    1f,
                    1f
                );

                Gizmos.DrawLine(p1, p2);

                DrawArrow(
                    (p1 + p2) * 0.5f,
                    direction,
                    3.0f
                );
            }
        }
    }

    private void DrawArrow(
        Vector3 position,
        Vector3 direction,
        float size)
    {
        Vector3 right = new Vector3(
            direction.z,
            0f,
            -direction.x
        );

        Vector3 tip =
            position + direction * size;

        Vector3 leftWing =
            position
            - direction * size * 0.4f
            + right * size * 0.4f;

        Vector3 rightWing =
            position
            - direction * size * 0.4f
            - right * size * 0.4f;

        Gizmos.DrawLine(tip, leftWing);
        Gizmos.DrawLine(tip, rightWing);
    }
}

/**
public class RoadNetworkManager : MonoBehaviour
{
    public string fileName = "ingolstadt_road_graph.json";

    public RoadGraphData graph;

    public Dictionary<long, RoadNodeData> nodesById =
        new Dictionary<long, RoadNodeData>();

    public Dictionary<long, List<RoadEdgeData>> edgesFromNode =
        new Dictionary<long, List<RoadEdgeData>>();

    void Awake()
    {
        LoadGraph();
    }

    void LoadGraph()
    {
        string path = Path.Combine(
            Application.streamingAssetsPath,
            fileName
        );

        string json = File.ReadAllText(path);

        graph = JsonUtility.FromJson<RoadGraphData>(json);

        foreach (RoadNodeData node in graph.nodes)
        {
            nodesById[node.id] = node;
            edgesFromNode[node.id] = new List<RoadEdgeData>();
        }

        foreach (RoadEdgeData edge in graph.edges)
        {
            if (!edgesFromNode.ContainsKey(edge.from))
                edgesFromNode[edge.from] = new List<RoadEdgeData>();

            edgesFromNode[edge.from].Add(edge);
        }

        Debug.Log(
            $"Traffic graph loaded: {graph.nodes.Length} nodes, " +
            $"{graph.edges.Length} edges"
        );
    }
}
**/