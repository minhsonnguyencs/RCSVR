using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class TrafficDemandZone
{
    [Tooltip("Unique zone name. The OD matrix refers to this exact string.")]
    public string zoneName = "Zone";

    [Tooltip("Center of the circular demand area in world space.")]
    public Transform center;

    [Min(1f)]
    [Tooltip("Horizontal radius in metres. Membership is tested in the XZ plane.")]
    public float radius = 300f;

    [Min(0f)]
    [Tooltip("Relative probability that a newly spawned vehicle originates in this zone.")]
    public float supplyWeight = 1f;

    [Min(0f)]
    [Tooltip("Fallback destination attraction if no usable OD-matrix row exists.")]
    public float demandWeight = 1f;

    public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.8f);
}

/// <summary>
/// Closed-population origin/destination model.
///
/// Explicit circular zones are configured in the Inspector. Everything not
/// inside an explicit zone belongs to the implicit DEFAULT zone.
///
/// Supply weights control initial spawning. The optional OD matrix controls
/// destination-zone choice conditional on the current origin zone. If no OD
/// row is available, destinationWeight values are used instead.
/// </summary>
public class TrafficDemandManager : MonoBehaviour
{
    public const string DefaultZoneName = "DEFAULT";

    [Header("Demand model")]
    public bool useDemandModel = true;

    [Tooltip("Optional OD matrix JSON stored under StreamingAssets. Leave empty to use only destination demand weights.")]
    public string odMatrixFileName = "traffic_od_matrix.json";

    [Header("Default zone")]
    [Min(0f)] public float defaultSupplyWeight = 1f;
    [Min(0f)] public float defaultDemandWeight = 1f;

    [Header("Explicit circular zones")]
    public List<TrafficDemandZone> zones = new List<TrafficDemandZone>();

    [Header("Scene visualization")]
    [Tooltip("Draw supply/demand zone boundaries and centers in the Scene view.")]
    public bool showZoneGizmos = true;

    [Tooltip("Vertical world-space offset used for zone boundaries and center markers.")]
    public float gizmoHeightOffset = 5f;

    [Tooltip("Number of straight line segments used to approximate each circular zone.")]
    [Range(16, 128)]
    public int gizmoCircleSegments = 64;

    [Tooltip("Radius of the small center marker drawn for each zone.")]
    [Min(0.1f)]
    public float gizmoCenterMarkerRadius = 2f;

    [Tooltip("Force zone gizmos to full opacity even if the stored zone color has a lower alpha.")]
    public bool forceOpaqueGizmos = true;

    [Tooltip("Show zone names and supply/demand weights next to the zone centers in the Scene view.")]
    public bool showZoneLabels = true;

    [Tooltip("World-space offset of the label relative to the zone center marker.")]
    public Vector3 gizmoLabelOffset = new Vector3(2f, 2f, 2f);

    public Color defaultZoneInfoColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    private RoadNetworkManager network;
    private TrafficODMatrixData odMatrix;

    public bool IsODMatrixLoadComplete { get; private set; } = true;
    public bool IsODMatrixLoadInProgress { get; private set; }

    private Coroutine odMatrixLoadCoroutine;

    private readonly Dictionary<string, TrafficDemandZone> zonesByName =
        new Dictionary<string, TrafficDemandZone>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<Lane>> spawnLanesByZone =
        new Dictionary<string, List<Lane>>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<long>> destinationNodesByZone =
        new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<long, string> nodeZoneCache =
        new Dictionary<long, string>();

    private readonly Dictionary<Lane, string> laneZoneCache =
        new Dictionary<Lane, string>();

    private readonly Dictionary<string, TrafficODRow> odRowsByOrigin =
        new Dictionary<string, TrafficODRow>(StringComparer.OrdinalIgnoreCase);

    void Awake()
    {
        ReloadODMatrix();
    }

    [ContextMenu("Reload OD Matrix")]
    public void ReloadODMatrix()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (odMatrixLoadCoroutine != null)
            StopCoroutine(odMatrixLoadCoroutine);

        odMatrixLoadCoroutine =
            StartCoroutine(ReloadODMatrixCoroutine());
#else
        ReloadODMatrixSynchronously();
#endif
    }

    private void ReloadODMatrixSynchronously()
    {
        BeginODMatrixReload();

        if (string.IsNullOrWhiteSpace(odMatrixFileName))
        {
            FinishODMatrixReload();
            return;
        }

        string path = Path.Combine(
            Application.streamingAssetsPath,
            odMatrixFileName
        );

        if (!File.Exists(path))
        {
            Debug.LogWarning(
                "TrafficDemandManager: OD matrix not found: " + path +
                ". Destination weights from the zone inspector will be used."
            );
            FinishODMatrixReload();
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            ParseAndStoreODMatrix(json);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "TrafficDemandManager: failed to load OD matrix: " +
                exception.Message
            );
            odMatrix = null;
            odRowsByOrigin.Clear();
        }

        FinishODMatrixReload();
    }

    public IEnumerator ReloadODMatrixCoroutine()
    {
        BeginODMatrixReload();

        if (string.IsNullOrWhiteSpace(odMatrixFileName))
        {
            FinishODMatrixReload();
            odMatrixLoadCoroutine = null;
            yield break;
        }

        string path = Path.Combine(
            Application.streamingAssetsPath,
            odMatrixFileName
        );

        using (UnityWebRequest request = UnityWebRequest.Get(path))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "TrafficDemandManager: OD matrix could not be loaded: " +
                    path + "\n" + request.error +
                    "\nDestination weights from the zone inspector will be used."
                );

                FinishODMatrixReload();
                odMatrixLoadCoroutine = null;
                yield break;
            }

            ParseAndStoreODMatrix(
                request.downloadHandler.text
            );
        }

        FinishODMatrixReload();
        odMatrixLoadCoroutine = null;
    }

    private void BeginODMatrixReload()
    {
        IsODMatrixLoadComplete = false;
        IsODMatrixLoadInProgress = true;

        odMatrix = null;
        odRowsByOrigin.Clear();
    }

    private void FinishODMatrixReload()
    {
        IsODMatrixLoadInProgress = false;
        IsODMatrixLoadComplete = true;
    }

    private void ParseAndStoreODMatrix(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning(
                "TrafficDemandManager: OD matrix is empty: " +
                odMatrixFileName
            );
            odMatrix = null;
            odRowsByOrigin.Clear();
            return;
        }

        try
        {
            odMatrix =
                JsonUtility.FromJson<TrafficODMatrixData>(json);

            if (odMatrix == null ||
                odMatrix.rows == null)
            {
                Debug.LogWarning(
                    "TrafficDemandManager: could not deserialize OD matrix: " +
                    odMatrixFileName
                );
                odMatrix = null;
                return;
            }

            foreach (TrafficODRow row in odMatrix.rows)
            {
                if (row == null ||
                    string.IsNullOrWhiteSpace(row.originZone))
                {
                    continue;
                }

                odRowsByOrigin[row.originZone.Trim()] = row;
            }

            Debug.Log(
                $"TrafficDemandManager: loaded OD matrix '{odMatrixFileName}' " +
                $"with {odRowsByOrigin.Count} origin rows."
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "TrafficDemandManager: failed to parse OD matrix: " +
                exception.Message
            );

            odMatrix = null;
            odRowsByOrigin.Clear();
        }
    }

    /// <summary>
    /// Rebuilds all zone membership caches after a road-network load/reload.
    /// </summary>
    public void RebuildNetworkCache(RoadNetworkManager roadNetwork)
    {
        network = roadNetwork;

        zonesByName.Clear();
        spawnLanesByZone.Clear();
        destinationNodesByZone.Clear();
        nodeZoneCache.Clear();
        laneZoneCache.Clear();

        EnsureZoneContainers(DefaultZoneName);

        foreach (TrafficDemandZone zone in zones)
        {
            if (zone == null ||
                zone.center == null ||
                string.IsNullOrWhiteSpace(zone.zoneName))
            {
                continue;
            }

            string name = zone.zoneName.Trim();

            if (name.Equals(DefaultZoneName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    $"TrafficDemandManager: '{DefaultZoneName}' is reserved for the implicit default zone."
                );
                continue;
            }

            if (zonesByName.ContainsKey(name))
            {
                Debug.LogWarning(
                    "TrafficDemandManager: duplicate zone name '" + name +
                    "'. Only the first zone with that name will be used."
                );
                continue;
            }

            zonesByName[name] = zone;
            EnsureZoneContainers(name);
        }

        if (network == null)
            return;

        foreach (KeyValuePair<long, RoadNodeData> pair in network.nodesById)
        {
            string zoneName = ClassifyWorldPosition(
                network.LanePointToWorld(
                    new Vector3(
                        pair.Value.position.x,
                        pair.Value.position.y,
                        pair.Value.position.z
                    )
                )
            );

            nodeZoneCache[pair.Key] = zoneName;

            if (!destinationNodesByZone.TryGetValue(
                    zoneName,
                    out List<long> nodes))
            {
                nodes = new List<long>();
                destinationNodesByZone[zoneName] = nodes;
            }

            /*
             * A destination must have at least one outgoing lane so that the
             * closed-population vehicle can continue with its next trip.
             */
            if (network.lanesFromNode.TryGetValue(
                    pair.Key,
                    out List<Lane> outgoing) &&
                outgoing != null &&
                outgoing.Count > 0)
            {
                nodes.Add(pair.Key);
            }
        }

        foreach (Lane lane in network.allLanes)
        {
            if (lane == null || lane.points == null || lane.points.Count < 2)
                continue;

            Vector3 localMidpoint =
                LanePathUtility.GetPointAtDistanceFromStart(
                    lane.points,
                    lane.totalLength * 0.5f
                );

            Vector3 worldMidpoint =
                network.LanePointToWorld(localMidpoint);

            string zoneName =
                ClassifyWorldPosition(worldMidpoint);

            laneZoneCache[lane] = zoneName;

            if (!spawnLanesByZone.TryGetValue(
                    zoneName,
                    out List<Lane> lanes))
            {
                lanes = new List<Lane>();
                spawnLanesByZone[zoneName] = lanes;
            }

            lanes.Add(lane);
        }

        Debug.Log(
            $"TrafficDemandManager: rebuilt zone cache for " +
            $"{network.allLanes.Count} lanes and {network.nodesById.Count} nodes."
        );
    }

    private void EnsureZoneContainers(string zoneName)
    {
        if (!spawnLanesByZone.ContainsKey(zoneName))
            spawnLanesByZone[zoneName] = new List<Lane>();

        if (!destinationNodesByZone.ContainsKey(zoneName))
            destinationNodesByZone[zoneName] = new List<long>();
    }

    public Lane ChooseSpawnLane()
    {
        if (!useDemandModel ||
            network == null ||
            network.allLanes == null ||
            network.allLanes.Count == 0)
        {
            return null;
        }

        List<string> candidates = new List<string>();
        List<float> weights = new List<float>();

        AddSupplyCandidate(DefaultZoneName, defaultSupplyWeight, candidates, weights);

        foreach (KeyValuePair<string, TrafficDemandZone> pair in zonesByName)
        {
            AddSupplyCandidate(
                pair.Key,
                pair.Value.supplyWeight,
                candidates,
                weights
            );
        }

        string selectedZone =
            WeightedChoice(candidates, weights);

        if (selectedZone == null ||
            !spawnLanesByZone.TryGetValue(
                selectedZone,
                out List<Lane> lanes) ||
            lanes.Count == 0)
        {
            return null;
        }

        return lanes[UnityEngine.Random.Range(0, lanes.Count)];
    }

    private void AddSupplyCandidate(
        string zoneName,
        float weight,
        List<string> names,
        List<float> weights)
    {
        if (weight <= 0f ||
            !spawnLanesByZone.TryGetValue(
                zoneName,
                out List<Lane> lanes) ||
            lanes == null ||
            lanes.Count == 0)
        {
            return;
        }

        names.Add(zoneName);
        weights.Add(weight);
    }

    /// <summary>
    /// Chooses a destination node according to the OD row for the origin zone.
    /// If no usable row exists, falls back to per-zone demand weights.
    /// Reachability is deliberately checked by RoadNetworkManager/A* afterwards.
    /// </summary>
    public bool TryChooseDestinationNode(
        long originNode,
        out long destinationNode)
    {
        destinationNode = -1;

        if (!useDemandModel || network == null)
            return false;

        string originZone = GetZoneForNode(originNode);

        string selectedDestinationZone =
            ChooseDestinationZoneFromOD(originZone);

        if (selectedDestinationZone == null)
        {
            selectedDestinationZone =
                ChooseDestinationZoneFromDemandWeights();
        }

        if (selectedDestinationZone == null ||
            !destinationNodesByZone.TryGetValue(
                selectedDestinationZone,
                out List<long> nodes) ||
            nodes == null ||
            nodes.Count == 0)
        {
            return false;
        }

        destinationNode =
            nodes[UnityEngine.Random.Range(0, nodes.Count)];

        return true;
    }

    private string ChooseDestinationZoneFromOD(string originZone)
    {
        if (string.IsNullOrWhiteSpace(originZone) ||
            !odRowsByOrigin.TryGetValue(
                originZone,
                out TrafficODRow row) ||
            row == null ||
            row.destinations == null)
        {
            return null;
        }

        List<string> candidates = new List<string>();
        List<float> weights = new List<float>();

        foreach (TrafficODWeight entry in row.destinations)
        {
            if (entry == null ||
                entry.weight <= 0f ||
                string.IsNullOrWhiteSpace(entry.destinationZone))
            {
                continue;
            }

            string zoneName = entry.destinationZone.Trim();

            if (!destinationNodesByZone.TryGetValue(
                    zoneName,
                    out List<long> nodes) ||
                nodes == null ||
                nodes.Count == 0)
            {
                continue;
            }

            candidates.Add(zoneName);
            weights.Add(entry.weight);
        }

        return WeightedChoice(candidates, weights);
    }

    private string ChooseDestinationZoneFromDemandWeights()
    {
        List<string> candidates = new List<string>();
        List<float> weights = new List<float>();

        AddDemandCandidate(
            DefaultZoneName,
            defaultDemandWeight,
            candidates,
            weights
        );

        foreach (KeyValuePair<string, TrafficDemandZone> pair in zonesByName)
        {
            AddDemandCandidate(
                pair.Key,
                pair.Value.demandWeight,
                candidates,
                weights
            );
        }

        return WeightedChoice(candidates, weights);
    }

    private void AddDemandCandidate(
        string zoneName,
        float weight,
        List<string> names,
        List<float> weights)
    {
        if (weight <= 0f ||
            !destinationNodesByZone.TryGetValue(
                zoneName,
                out List<long> nodes) ||
            nodes == null ||
            nodes.Count == 0)
        {
            return;
        }

        names.Add(zoneName);
        weights.Add(weight);
    }

    private string WeightedChoice(
        List<string> names,
        List<float> weights)
    {
        if (names == null ||
            weights == null ||
            names.Count == 0 ||
            names.Count != weights.Count)
        {
            return null;
        }

        float total = 0f;

        for (int i = 0; i < weights.Count; i++)
            total += Mathf.Max(0f, weights[i]);

        if (total <= 0f)
            return null;

        float roll =
            UnityEngine.Random.value * total;

        for (int i = 0; i < names.Count; i++)
        {
            roll -= Mathf.Max(0f, weights[i]);

            if (roll <= 0f)
                return names[i];
        }

        return names[names.Count - 1];
    }

    public string GetZoneForNode(long nodeId)
    {
        if (nodeZoneCache.TryGetValue(
                nodeId,
                out string cached))
        {
            return cached;
        }

        return DefaultZoneName;
    }

    public string GetZoneForLane(Lane lane)
    {
        if (lane != null &&
            laneZoneCache.TryGetValue(
                lane,
                out string cached))
        {
            return cached;
        }

        return DefaultZoneName;
    }

    private string ClassifyWorldPosition(Vector3 worldPosition)
    {
        string bestZone =
            DefaultZoneName;

        float bestNormalizedDistance =
            float.PositiveInfinity;

        foreach (KeyValuePair<string, TrafficDemandZone> pair
                 in zonesByName)
        {
            TrafficDemandZone zone =
                pair.Value;

            if (zone == null ||
                zone.center == null ||
                zone.radius <= 0f)
            {
                continue;
            }

            Vector3 delta =
                worldPosition
                - zone.center.position;

            delta.y = 0f;

            float distance =
                delta.magnitude;

            if (distance > zone.radius)
                continue;

            float normalized =
                distance / zone.radius;

            /*
             * If zones overlap, assign the point to the zone where it lies
             * proportionally closest to the center.
             */
            if (normalized <
                bestNormalizedDistance)
            {
                bestNormalizedDistance =
                    normalized;

                bestZone =
                    pair.Key;
            }
        }

        return bestZone;
    }

    private void OnDrawGizmos()
    {
        if (!showZoneGizmos ||
            zones == null)
        {
            return;
        }

        int segments =
            Mathf.Clamp(
                gizmoCircleSegments,
                16,
                128
            );

        foreach (TrafficDemandZone zone in zones)
        {
            if (zone == null ||
                zone.center == null ||
                zone.radius <= 0f)
            {
                continue;
            }

            Color drawColor =
                zone.gizmoColor;

            if (forceOpaqueGizmos)
            {
                drawColor.a = 1f;
            }

            Gizmos.color =
                drawColor;

            Vector3 center =
                zone.center.position
                + Vector3.up
                * gizmoHeightOffset;

            DrawZoneCircle(
                center,
                zone.radius,
                segments
            );

            Gizmos.DrawWireSphere(
                center,
                gizmoCenterMarkerRadius
            );

            /*
             * Cross marker makes the exact center obvious even at large
             * Scene-view zoom levels.
             */
            float crossSize =
                Mathf.Max(
                    gizmoCenterMarkerRadius,
                    0.5f
                );

            Gizmos.DrawLine(
                center
                - Vector3.right
                * crossSize,
                center
                + Vector3.right
                * crossSize
            );

            Gizmos.DrawLine(
                center
                - Vector3.forward
                * crossSize,
                center
                + Vector3.forward
                * crossSize
            );


#if UNITY_EDITOR
            if (showZoneLabels)
            {
                GUIStyle labelStyle =
                    new GUIStyle(
                        EditorStyles.boldLabel
                    );

                Color labelColor =
                    drawColor;

                labelColor.a = 1f;
                labelStyle.normal.textColor =
                    labelColor;

                string label =
                    zone.zoneName
                    + "\nSupply: "
                    + zone.supplyWeight.ToString("0.##")
                    + "   Demand: "
                    + zone.demandWeight.ToString("0.##");

                Handles.Label(
                    center
                    + gizmoLabelOffset,
                    label,
                    labelStyle
                );
            }
#endif
        }
    }


    private void DrawZoneCircle(
        Vector3 center,
        float radius,
        int segments)
    {
        if (radius <= 0f ||
            segments < 3)
        {
            return;
        }

        float angleStep =
            Mathf.PI
            * 2f
            / segments;

        Vector3 previous =
            center
            + new Vector3(
                Mathf.Cos(0f)
                * radius,
                0f,
                Mathf.Sin(0f)
                * radius
            );

        for (int i = 1;
             i <= segments;
             i++)
        {
            float angle =
                angleStep
                * i;

            Vector3 next =
                center
                + new Vector3(
                    Mathf.Cos(angle)
                    * radius,
                    0f,
                    Mathf.Sin(angle)
                    * radius
                );

            Gizmos.DrawLine(
                previous,
                next
            );

            previous =
                next;
        }
    }

}