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

    [Header("Coordinate Alignment")]
    public Transform roadNetworkTransform;

    [Header("Runtime reload dependencies")]
    [Tooltip("Optional visual road renderer. If assigned, runtime reload rebuilds the road meshes from the same JSON.")]
    public RoadGraphRenderer roadGraphRenderer;

    [Tooltip("Optional spawner. If assigned, runtime reload clears traffic before replacing the graph and respawns it afterwards.")]
    public TrafficSpawner trafficSpawner;

    [Header("Live Traffic State")]
    public TrafficOccupancyManager occupancyManager;

    [Header("Routing Policy")]
    public TrafficRoutingPolicy routingPolicy =
        new TrafficRoutingPolicy();

    [Header("Supply / Demand")]
    [Tooltip("Optional demand manager. When assigned and enabled, initial spawn locations and trip destinations use its zone/OD model.")]
    public TrafficDemandManager trafficDemandManager;

    [Header("Traffic Lights")]
    [Tooltip("Optional procedural traffic-light system. It is rebuilt automatically whenever the road JSON is reloaded.")]
    public TrafficLightSystem trafficLightSystem;

    public RoadGraphData graph;

    public Dictionary<long, RoadNodeData> nodesById =
        new Dictionary<long, RoadNodeData>();

    public Dictionary<long, List<Lane>> lanesFromNode =
        new Dictionary<long, List<Lane>>();

    public List<Lane> allLanes = new List<Lane>();

    private readonly List<long> routableDestinationNodes = new List<long>();

    void Awake()
    {
        occupancyManager = GetComponent<TrafficOccupancyManager>();
        if (occupancyManager == null)
            occupancyManager = gameObject.AddComponent<TrafficOccupancyManager>();

        occupancyManager.ResetState();

        if (roadNetworkTransform == null)
            roadNetworkTransform = transform;

        if (trafficDemandManager == null)
            trafficDemandManager = FindObjectOfType<TrafficDemandManager>();

        if (trafficLightSystem == null)
            trafficLightSystem = FindObjectOfType<TrafficLightSystem>();

        LoadGraphFromConfiguredFile(false);
    }

    /// <summary>
    /// UI-friendly setter. It changes the configured filename but does not
    /// immediately reload. Pair it with ReloadRoadNetwork() on a button.
    /// </summary>
    public void SetRoadFileFromString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Debug.LogWarning("RoadNetworkManager: road JSON filename cannot be empty.");
            return;
        }

        fileName = NormalizeRoadFileName(value);

        if (roadGraphRenderer != null)
            roadGraphRenderer.fileName = fileName;
    }

    /// <summary>
    /// UI-friendly one-call alternative: set the filename and immediately
    /// rebuild the logical network, visual roads, and traffic.
    /// </summary>
    public void ReloadRoadNetworkFromString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Debug.LogWarning("RoadNetworkManager: road JSON filename cannot be empty.");
            return;
        }

        string requested = NormalizeRoadFileName(value);
        if (TryReadGraph(requested, out RoadGraphData loadedGraph))
        {
            fileName = requested;
            ApplyReloadedGraph(loadedGraph, true);
        }
    }

    private string NormalizeRoadFileName(string value)
    {
        string normalized = value.Trim();
        if (string.IsNullOrEmpty(Path.GetExtension(normalized)))
            normalized += ".json";
        return normalized;
    }

    [ContextMenu("Reload Road Network")]
    public void ReloadRoadNetwork()
    {
        LoadGraphFromConfiguredFile(true);
    }

    private bool LoadGraphFromConfiguredFile(bool coordinatedRuntimeReload)
    {
        if (!TryReadGraph(fileName, out RoadGraphData loadedGraph))
            return false;

        ApplyReloadedGraph(loadedGraph, coordinatedRuntimeReload);
        return true;
    }

    private bool TryReadGraph(string requestedFileName, out RoadGraphData loadedGraph)
    {
        loadedGraph = null;

        string path = Path.Combine(
            Application.streamingAssetsPath,
            requestedFileName
        );

        if (!File.Exists(path))
        {
            Debug.LogError("Road graph JSON not found: " + path);
            return false;
        }

        string json = File.ReadAllText(path);
        loadedGraph = JsonUtility.FromJson<RoadGraphData>(json);

        if (loadedGraph == null ||
            loadedGraph.nodes == null ||
            loadedGraph.edges == null)
        {
            Debug.LogError("Could not deserialize road graph: " + requestedFileName);
            loadedGraph = null;
            return false;
        }

        return true;
    }

    private void ApplyReloadedGraph(
        RoadGraphData loadedGraph,
        bool coordinatedRuntimeReload)
    {
        /*
         * Traffic is cleared only AFTER the new file has been validated.
         * Therefore a bad UI filename does not destroy the running simulation.
         */
        if (coordinatedRuntimeReload && trafficSpawner != null)
            trafficSpawner.ClearVehicles();

        if (occupancyManager != null)
            occupancyManager.ResetState();

        graph = loadedGraph;
        BuildLaneGraph();

        if (trafficLightSystem != null)
            trafficLightSystem.RebuildFromNetwork(this);

        if (trafficDemandManager != null)
            trafficDemandManager.RebuildNetworkCache(this);

        if (roadGraphRenderer != null)
        {
            roadGraphRenderer.fileName = fileName;
            roadGraphRenderer.GenerateRoadMeshesFromGraph(graph);
        }

        if (coordinatedRuntimeReload && trafficSpawner != null)
            trafficSpawner.RespawnVehicles();
    }

    private void BuildLaneGraph()
    {
        nodesById.Clear();
        lanesFromNode.Clear();
        allLanes.Clear();
        routableDestinationNodes.Clear();

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

            List<Vector3> offsetPoints =
                LaneGeometry.BuildOffsetLane(edge.centerline, laneOffset);

            Lane lane = new Lane(laneId++, edge, offsetPoints);
            allLanes.Add(lane);

            if (!lanesFromNode.ContainsKey(lane.startNode))
                lanesFromNode[lane.startNode] = new List<Lane>();

            lanesFromNode[lane.startNode].Add(lane);
        }

        foreach (KeyValuePair<long, List<Lane>> pair in lanesFromNode)
        {
            if (pair.Value != null && pair.Value.Count > 0)
                routableDestinationNodes.Add(pair.Key);
        }

        Debug.Log(
            $"Traffic graph loaded from {fileName}. " +
            $"{graph.nodes.Length} nodes, {graph.edges.Length} raw edges, " +
            $"{allLanes.Count} directed lanes."
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
        return occupancyManager != null
            ? occupancyManager.GetVehicleCount(lane)
            : 0;
    }

    public float GetLaneOccupancyRatio(Lane lane)
    {
        return occupancyManager != null
            ? occupancyManager.GetOccupancyRatio(lane)
            : 0f;
    }

    public float GetLaneEstimatedTravelTimeSeconds(
        Lane lane,
        float freeFlowSpeedKmh,
        float congestionSensitivity = -1f)
    {
        if (lane == null)
            return float.PositiveInfinity;

        if (occupancyManager == null)
        {
            return lane.totalLength /
                Mathf.Max(0.1f, freeFlowSpeedKmh / 3.6f);
        }

        return occupancyManager.GetEstimatedTravelTimeSeconds(
            lane,
            Mathf.Max(0.1f, freeFlowSpeedKmh / 3.6f),
            congestionSensitivity
        );
    }

    /// <summary>
    /// Single routing-cost entry point. Current A* calls this with
    /// includeTraffic=false. Future congestion-aware routing can flip that
    /// policy without changing TrafficPathfinder or the movement controllers.
    /// </summary>
    public float GetRoutingCostSeconds(
        Lane lane,
        float vehicleTopSpeedKmh,
        bool includeTraffic,
        float congestionWeight = -1f,
        float congestionExponent = -1f,
        float maximumCongestionMultiplier = -1f)
    {
        if (lane == null)
            return float.PositiveInfinity;

        float speedKmh =
            Mathf.Max(
                1f,
                vehicleTopSpeedKmh
            );

        float freeFlowTime =
            lane.totalLength
            / (speedKmh / 3.6f);

        if (!includeTraffic ||
            occupancyManager == null)
        {
            return freeFlowTime;
        }

        TrafficRoutingPolicy policy =
            routingPolicy
            ?? new TrafficRoutingPolicy();

        float weight =
            congestionWeight >= 0f
                ? congestionWeight
                : policy.congestionWeight;

        float exponent =
            congestionExponent > 0f
                ? congestionExponent
                : policy.congestionExponent;

        float maxMultiplier =
            maximumCongestionMultiplier >= 1f
                ? maximumCongestionMultiplier
                : policy.maximumCongestionMultiplier;

        float occupancy =
            Mathf.Clamp01(
                GetLaneOccupancyRatio(lane)
            );

        float multiplier =
            1f
            + Mathf.Max(0f, weight)
            * Mathf.Pow(
                occupancy,
                Mathf.Max(0.1f, exponent)
            );

        multiplier =
            Mathf.Clamp(
                multiplier,
                1f,
                Mathf.Max(1f, maxMultiplier)
            );

        return freeFlowTime * multiplier;
    }

    public float GetRouteCostSeconds(
        IReadOnlyList<Lane> route,
        int startIndex,
        float vehicleTopSpeedKmh,
        bool includeTraffic,
        float congestionWeight = -1f,
        float congestionExponent = -1f,
        float maximumCongestionMultiplier = -1f)
    {
        if (route == null)
            return float.PositiveInfinity;

        float total = 0f;
        int first =
            Mathf.Clamp(
                startIndex,
                0,
                route.Count
            );

        for (int i = first;
             i < route.Count;
             i++)
        {
            float laneCost =
                GetRoutingCostSeconds(
                    route[i],
                    vehicleTopSpeedKmh,
                    includeTraffic,
                    congestionWeight,
                    congestionExponent,
                    maximumCongestionMultiplier
                );

            if (float.IsInfinity(laneCost))
                return float.PositiveInfinity;

            total += laneCost;
        }

        return total;
    }

    /// <summary>
    /// Chooses a random reachable endpoint and returns an A* route to it.
    /// Farther candidates are preferred for the first N attempts so cars do
    /// not constantly receive trivial one-intersection trips.
    /// </summary>
    public bool TryCreateRandomRoute(
        long startNode,
        float vehicleTopSpeedKmh,
        float minimumStraightLineDistance,
        int attempts,
        List<Lane> result,
        out long destinationNode,
        bool includeTrafficInCost = false,
        float congestionWeight = -1f,
        float congestionExponent = -1f,
        float maximumCongestionMultiplier = -1f)
    {
        result.Clear();
        destinationNode = -1;

        if (routableDestinationNodes.Count == 0 ||
            !nodesById.ContainsKey(startNode))
        {
            return false;
        }

        attempts = Mathf.Max(1, attempts);

        for (int pass = 0; pass < 2; pass++)
        {
            float requiredDistance = pass == 0
                ? Mathf.Max(0f, minimumStraightLineDistance)
                : 0f;

            for (int i = 0; i < attempts; i++)
            {
                long candidate = routableDestinationNodes[
                    Random.Range(0, routableDestinationNodes.Count)
                ];

                if (candidate == startNode)
                    continue;

                if (requiredDistance > 0f &&
                    GetNodeStraightLineDistance(startNode, candidate) < requiredDistance)
                {
                    continue;
                }

                if (TrafficPathfinder.TryFindRoute(
                        this,
                        startNode,
                        candidate,
                        vehicleTopSpeedKmh,
                        result,
                        includeTrafficInCost,
                        congestionWeight,
                        congestionExponent,
                        maximumCongestionMultiplier) &&
                    result.Count > 0)
                {
                    destinationNode = candidate;
                    return true;
                }
            }
        }

        result.Clear();
        return false;
    }

    /// <summary>
    /// Creates a route whose destination is sampled from the configured
    /// supply/demand model. If the demand model is unavailable or cannot
    /// provide a reachable endpoint, callers can fall back to random routing.
    /// </summary>
    public bool TryCreateDemandRoute(
        long startNode,
        float vehicleTopSpeedKmh,
        float minimumStraightLineDistance,
        int attempts,
        List<Lane> result,
        out long destinationNode,
        bool includeTrafficInCost = false,
        float congestionWeight = -1f,
        float congestionExponent = -1f,
        float maximumCongestionMultiplier = -1f)
    {
        result.Clear();
        destinationNode = -1;

        if (trafficDemandManager == null ||
            !trafficDemandManager.useDemandModel ||
            !nodesById.ContainsKey(startNode))
        {
            return false;
        }

        attempts =
            Mathf.Max(
                1,
                attempts
            );

        for (int pass = 0;
             pass < 2;
             pass++)
        {
            float requiredDistance =
                pass == 0
                    ? Mathf.Max(
                        0f,
                        minimumStraightLineDistance
                    )
                    : 0f;

            for (int i = 0;
                 i < attempts;
                 i++)
            {
                if (!trafficDemandManager
                    .TryChooseDestinationNode(
                        startNode,
                        out long candidate))
                {
                    continue;
                }

                if (candidate == startNode)
                    continue;

                if (requiredDistance > 0f &&
                    GetNodeStraightLineDistance(
                        startNode,
                        candidate
                    )
                    < requiredDistance)
                {
                    continue;
                }

                if (TrafficPathfinder.TryFindRoute(
                        this,
                        startNode,
                        candidate,
                        vehicleTopSpeedKmh,
                        result,
                        includeTrafficInCost,
                        congestionWeight,
                        congestionExponent,
                        maximumCongestionMultiplier) &&
                    result.Count > 0)
                {
                    destinationNode =
                        candidate;

                    return true;
                }
            }
        }

        result.Clear();
        return false;
    }

    public float GetNodeStraightLineDistance(long nodeA, long nodeB)
    {
        if (!nodesById.TryGetValue(nodeA, out RoadNodeData a) ||
            !nodesById.TryGetValue(nodeB, out RoadNodeData b))
        {
            return 0f;
        }

        Vector3 pa = new Vector3(a.position.x, a.position.y, a.position.z);
        Vector3 pb = new Vector3(b.position.x, b.position.y, b.position.z);
        return Vector3.Distance(pa, pb);
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
