using System.Collections.Generic;
using System.IO;
using UnityEngine;

[RequireComponent(typeof(TrafficOccupancyManager))]
public class RoadNetworkManager : MonoBehaviour
{
    [Header("Road Graph")]
    public string fileName = "ingolstadt_road_graph_square.json";

    [Header("Lane Geometry")]
    public float laneOffset = 1.5f;

    public RoadGraphData graph;

    public Dictionary<long, RoadNodeData> nodesById =
        new Dictionary<long, RoadNodeData>();

    public Dictionary<long, List<Lane>> lanesFromNode =
        new Dictionary<long, List<Lane>>();

    public List<Lane> allLanes = new List<Lane>();

    [Header("Coordinate Alignment")]
    public Transform roadNetworkTransform;

    [Header("Live Traffic State")]
    public TrafficOccupancyManager occupancyManager;

    void Awake()
    {
        occupancyManager = GetComponent<TrafficOccupancyManager>();
        if (occupancyManager == null)
            occupancyManager = gameObject.AddComponent<TrafficOccupancyManager>();

        occupancyManager.ResetState();

        LoadGraph();
    }

    void LoadGraph()
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);

        if (!File.Exists(path))
        {
            Debug.LogError("Road graph JSON not found: " + path);
            return;
        }

        string json = File.ReadAllText(path);
        graph = JsonUtility.FromJson<RoadGraphData>(json);

        if (graph == null)
        {
            Debug.LogError("Could not deserialize road graph.");
            return;
        }

        nodesById.Clear();
        lanesFromNode.Clear();
        allLanes.Clear();

        foreach (RoadNodeData node in graph.nodes)
        {
            nodesById[node.id] = node;
            lanesFromNode[node.id] = new List<Lane>();
        }

        int laneId = 0;
        foreach (RoadEdgeData edge in graph.edges)
        {
            if (edge.centerline == null || edge.centerline.Length < 2)
                continue;

            List<Vector3> offsetPoints = LaneGeometry.BuildOffsetLane(edge.centerline, laneOffset);
            Lane lane = new Lane(laneId++, edge, offsetPoints);

            allLanes.Add(lane);

            if (!lanesFromNode.ContainsKey(lane.startNode))
                lanesFromNode[lane.startNode] = new List<Lane>();

            lanesFromNode[lane.startNode].Add(lane);
        }

        Debug.Log(
            $"Traffic graph loaded. {graph.nodes.Length} nodes, " +
            $"{graph.edges.Length} raw edges, {allLanes.Count} lanes. " +
            "Lane lengths were precomputed for traffic and future pathfinding."
        );
    }

    public Vector3 LanePointToWorld(Vector3 localPoint)
    {
        if (roadNetworkTransform == null)
            return localPoint;
        return roadNetworkTransform.TransformPoint(localPoint);
    }

    public int GetLaneVehicleCount(Lane lane)
    {
        return occupancyManager != null ? occupancyManager.GetVehicleCount(lane) : 0;
    }

    public float GetLaneOccupancyRatio(Lane lane)
    {
        return occupancyManager != null ? occupancyManager.GetOccupancyRatio(lane) : 0f;
    }

    public float GetLaneEstimatedTravelTimeSeconds(
        Lane lane,
        float freeFlowSpeedKmh,
        float congestionSensitivity = -1f)
    {
        if (occupancyManager == null)
            return lane != null ? lane.totalLength / Mathf.Max(0.1f, freeFlowSpeedKmh / 3.6f) : float.PositiveInfinity;

        return occupancyManager.GetEstimatedTravelTimeSeconds(
            lane,
            Mathf.Max(0.1f, freeFlowSpeedKmh / 3.6f),
            congestionSensitivity
        );
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
                Vector3 p1 = roadNetworkTransform.TransformPoint(lane.points[i]);
                Vector3 p2 = roadNetworkTransform.TransformPoint(lane.points[i + 1]);
                p1 += Vector3.up * 0.2f;
                p2 += Vector3.up * 0.2f;

                Vector3 direction = p2 - p1;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f)
                    continue;

                direction.Normalize();
                float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                if (angle < 0f)
                    angle += 360f;

                Gizmos.color = Color.HSVToRGB(angle / 360f, 1f, 1f);
                Gizmos.DrawLine(p1, p2);
                DrawArrow((p1 + p2) * 0.5f, direction, 3f);
            }
        }
    }

    private void DrawArrow(Vector3 position, Vector3 direction, float size)
    {
        Vector3 right = new Vector3(direction.z, 0f, -direction.x);
        Vector3 tip = position + direction * size;
        Vector3 leftWing = position - direction * size * 0.4f + right * size * 0.4f;
        Vector3 rightWing = position - direction * size * 0.4f - right * size * 0.4f;

        Gizmos.DrawLine(tip, leftWing);
        Gizmos.DrawLine(tip, rightWing);
    }
}
