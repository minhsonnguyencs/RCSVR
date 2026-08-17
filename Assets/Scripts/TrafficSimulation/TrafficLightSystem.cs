using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally creates a simple fixed-cycle traffic signal policy from the
/// currently loaded road network.
///
/// Junction selection:
/// - requires at least minimumApproachCount distinct incoming approach groups
/// - requires at least one incident road whose OSM/JSON lane count is at least
///   minimumRoadLaneCount
///
/// Signal policy:
/// - two approach phases
/// - Phase A is aligned with the dominant/widest incoming road
/// - Phase B contains the remaining approaches
/// - yellow and all-red periods permit no new entries
///
/// The system is purely logical. Scene-view visualization uses independent
/// green Gizmo arrows and can be toggled without affecting road/lane Gizmos.
/// </summary>
public class TrafficLightSystem : MonoBehaviour
{
    [Header("System")]
    public bool enableTrafficLights = true;

    [Tooltip("An intersection is signalized only if at least one incident road has this many lanes in the source JSON.")]
    [Min(1)]
    public int minimumRoadLaneCount = 2;

    [Tooltip("Minimum number of distinct incoming approach directions required before a node can receive traffic lights.")]
    [Min(2)]
    public int minimumApproachCount = 3;

    [Tooltip("Directions closer than this angle are treated as the same physical approach when counting approaches.")]
    [Range(5f, 60f)]
    public float approachMergeAngleDegrees = 20f;

    [Header("Fixed Signal Cycle")]
    [Tooltip("Green time for the dominant/widest-road direction group.")]
    [Min(0.1f)]
    public float phaseAGreenDuration = 25f;

    [Tooltip("Green time for the second direction group.")]
    [Min(0.1f)]
    public float phaseBGreenDuration = 25f;

    [Min(0f)]
    public float yellowDuration = 3f;

    [Tooltip("Short all-red clearance period between opposing green phases.")]
    [Min(0f)]
    public float allRedDuration = 1f;

    [Header("Phase Grouping")]
    [Tooltip("Incoming approaches within this angular deviation of the dominant road axis (or its opposite direction) belong to Phase A. The others belong to Phase B.")]
    [Range(15f, 75f)]
    public float phaseAAlignmentAngleDegrees = 35f;

    [Header("Start Phase")]
    [Tooltip("Give independent signal groups a deterministic pseudo-random phase offset so the entire city does not change lights simultaneously.")]
    public bool randomizeInitialPhaseOffset = true;

    [Tooltip("Changing this value produces a different, but still reproducible, set of phase offsets.")]
    public int phaseOffsetSeed = 12345;

    [Header("Nearby Signal Synchronization")]
    [Tooltip("Synchronize signalized intersections that are physically close to one another. Useful when one large real-world junction is represented by several nearby graph nodes.")]
    public bool synchronizeNearbySignals = true;

    [Tooltip("Signalized nodes within this world-space distance are assigned to the same synchronized cycle group.")]
    [Min(0f)]
    public float signalSynchronizationRadius = 50f;

    [Tooltip("Draw a faint line between signalized nodes that belong to the same synchronized group.")]
    public bool showSynchronizationLinks = false;

    [Header("Traffic Light Gizmos")]
    [Tooltip("Independent toggle for traffic-light Scene-view visualization.")]
    public bool showTrafficLightGizmos = true;

    [Tooltip("Vertical world-space offset for traffic-light arrows.")]
    [Min(0f)]
    public float gizmoHeight = 8f;

    [Tooltip("Length of each permitted-direction arrow.")]
    [Min(0.5f)]
    public float gizmoArrowLength = 10f;

    [Tooltip("Size of the arrow head.")]
    [Min(0.1f)]
    public float gizmoArrowHeadSize = 2f;

    [Tooltip("Radius of the small marker above each signalized junction.")]
    [Min(0.1f)]
    public float gizmoIntersectionMarkerRadius = 1.5f;

    [Tooltip("Draw a subtle marker at every signalized node in addition to green permitted-direction arrows.")]
    public bool showSignalizedIntersectionMarkers = true;

    [Header("Debug")]
    [SerializeField]
    private int debugSignalizedIntersectionCount = 0;


    public enum SignalStage
    {
        PhaseAGreen,
        PhaseAYellow,
        AllRedAfterA,
        PhaseBGreen,
        PhaseBYellow,
        AllRedAfterB
    }


    [Serializable]
    private class SignalizedIntersection
    {
        public long nodeId;
        public Vector3 phaseAxis;
        public float phaseOffset;
        public int synchronizationGroup = -1;

        public readonly List<Lane> incomingLanes =
            new List<Lane>();

        public readonly HashSet<int> phaseALaneIds =
            new HashSet<int>();

        public readonly HashSet<int> phaseBLaneIds =
            new HashSet<int>();
    }


    private RoadNetworkManager network;

    private readonly Dictionary<long, SignalizedIntersection>
        signalsByNode =
            new Dictionary<long, SignalizedIntersection>();

    private readonly Dictionary<long, List<Lane>>
        incomingLanesByNode =
            new Dictionary<long, List<Lane>>();


    public int SignalizedIntersectionCount =>
        signalsByNode.Count;


    /// <summary>
    /// Rebuild all procedural signal definitions after a road network load.
    /// </summary>
    public void RebuildFromNetwork(
        RoadNetworkManager networkManager)
    {
        network = networkManager;

        signalsByNode.Clear();
        incomingLanesByNode.Clear();

        if (network == null ||
            network.allLanes == null)
        {
            debugSignalizedIntersectionCount = 0;
            return;
        }

        BuildIncomingLaneIndex();

        if (!enableTrafficLights)
        {
            debugSignalizedIntersectionCount = 0;
            return;
        }

        foreach (KeyValuePair<long, List<Lane>> pair
                 in incomingLanesByNode)
        {
            long nodeId =
                pair.Key;

            List<Lane> incoming =
                pair.Value;

            if (!ShouldSignalizeNode(
                    nodeId,
                    incoming))
            {
                continue;
            }

            SignalizedIntersection signal =
                BuildSignal(
                    nodeId,
                    incoming
                );

            if (signal == null)
                continue;

            /*
             * Both groups must contain at least one approach. Otherwise the
             * junction does not benefit from a two-phase signal.
             */
            if (signal.phaseALaneIds.Count == 0 ||
                signal.phaseBLaneIds.Count == 0)
            {
                continue;
            }

            signalsByNode[nodeId] =
                signal;
        }

        SynchronizeNearbySignalGroups();

        debugSignalizedIntersectionCount =
            signalsByNode.Count;

        Debug.Log(
            "TrafficLightSystem: generated "
            + signalsByNode.Count
            + " signalized intersections in "
            + CountSynchronizationGroups()
            + " synchronization groups."
        );
    }


    public void ClearSignals()
    {
        signalsByNode.Clear();
        incomingLanesByNode.Clear();
        debugSignalizedIntersectionCount = 0;
    }


    public bool IsSignalized(
        long nodeId)
    {
        return
            enableTrafficLights &&
            signalsByNode.ContainsKey(
                nodeId
            );
    }


    /// <summary>
    /// Returns true for unsignalized junctions. At a signalized junction it
    /// returns true only if the incoming lane's phase currently has green.
    /// Yellow and all-red stages reject new entries.
    /// </summary>
    public bool IsMovementPermitted(
        long nodeId,
        Lane incomingLane)
    {
        if (!enableTrafficLights ||
            incomingLane == null ||
            !signalsByNode.TryGetValue(
                nodeId,
                out SignalizedIntersection signal))
        {
            return true;
        }

        SignalStage stage =
            GetCurrentStage(
                signal
            );

        if (stage ==
            SignalStage.PhaseAGreen)
        {
            return
                signal.phaseALaneIds.Contains(
                    incomingLane.id
                );
        }

        if (stage ==
            SignalStage.PhaseBGreen)
        {
            return
                signal.phaseBLaneIds.Contains(
                    incomingLane.id
                );
        }

        return false;
    }


    public SignalStage GetCurrentStage(
        long nodeId)
    {
        if (!signalsByNode.TryGetValue(
                nodeId,
                out SignalizedIntersection signal))
        {
            return SignalStage.PhaseAGreen;
        }

        return GetCurrentStage(
            signal
        );
    }


    public float GetCycleDuration()
    {
        return
            Mathf.Max(
                0.1f,
                phaseAGreenDuration
            )
            + Mathf.Max(
                0f,
                yellowDuration
            )
            + Mathf.Max(
                0f,
                allRedDuration
            )
            + Mathf.Max(
                0.1f,
                phaseBGreenDuration
            )
            + Mathf.Max(
                0f,
                yellowDuration
            )
            + Mathf.Max(
                0f,
                allRedDuration
            );
    }


    private void BuildIncomingLaneIndex()
    {
        for (int i = 0;
             i < network.allLanes.Count;
             i++)
        {
            Lane lane =
                network.allLanes[i];

            if (lane == null)
                continue;

            if (!incomingLanesByNode.TryGetValue(
                    lane.endNode,
                    out List<Lane> incoming))
            {
                incoming =
                    new List<Lane>();

                incomingLanesByNode[
                    lane.endNode
                ] = incoming;
            }

            incoming.Add(lane);
        }
    }


    private bool ShouldSignalizeNode(
        long nodeId,
        List<Lane> incoming)
    {
        if (incoming == null ||
            incoming.Count < 2)
        {
            return false;
        }

        int distinctApproaches =
            CountDistinctApproaches(
                incoming
            );

        if (distinctApproaches <
            minimumApproachCount)
        {
            return false;
        }

        /*
         * Check every incident directed lane, both incoming and outgoing.
         * A road with sufficiently many source-data lanes makes this junction
         * eligible for a procedural traffic signal.
         */
        bool hasQualifyingRoad =
            false;

        for (int i = 0;
             i < incoming.Count;
             i++)
        {
            Lane lane =
                incoming[i];

            if (GetSourceRoadLaneCount(
                    lane)
                >= minimumRoadLaneCount)
            {
                hasQualifyingRoad =
                    true;

                break;
            }
        }

        if (!hasQualifyingRoad &&
            network.lanesFromNode.TryGetValue(
                nodeId,
                out List<Lane> outgoing))
        {
            for (int i = 0;
                 i < outgoing.Count;
                 i++)
            {
                if (GetSourceRoadLaneCount(
                        outgoing[i])
                    >= minimumRoadLaneCount)
                {
                    hasQualifyingRoad =
                        true;

                    break;
                }
            }
        }

        return hasQualifyingRoad;
    }


    private SignalizedIntersection BuildSignal(
        long nodeId,
        List<Lane> incoming)
    {
        Lane dominantLane =
            GetDominantIncomingLane(
                incoming
            );

        if (dominantLane == null)
            return null;

        Vector3 axis =
            GetIncomingDirection(
                dominantLane
            );

        if (axis.sqrMagnitude <
            0.0001f)
        {
            return null;
        }

        axis.Normalize();

        SignalizedIntersection signal =
            new SignalizedIntersection
            {
                nodeId = nodeId,
                phaseAxis = axis,
                phaseOffset =
                    GetPhaseOffset(
                        nodeId
                    )
            };

        float phaseDotThreshold =
            Mathf.Cos(
                phaseAAlignmentAngleDegrees
                * Mathf.Deg2Rad
            );

        for (int i = 0;
             i < incoming.Count;
             i++)
        {
            Lane lane =
                incoming[i];

            if (lane == null)
                continue;

            signal.incomingLanes.Add(
                lane
            );

            Vector3 direction =
                GetIncomingDirection(
                    lane
                );

            if (direction.sqrMagnitude <
                0.0001f)
            {
                continue;
            }

            direction.Normalize();

            /*
             * abs(dot) makes the forward and reverse directions of one road
             * belong to the same phase.
             */
            float alignment =
                Mathf.Abs(
                    Vector3.Dot(
                        direction,
                        axis
                    )
                );

            if (alignment >=
                phaseDotThreshold)
            {
                signal.phaseALaneIds.Add(
                    lane.id
                );
            }
            else
            {
                signal.phaseBLaneIds.Add(
                    lane.id
                );
            }
        }

        return signal;
    }


    private Lane GetDominantIncomingLane(
        List<Lane> incoming)
    {
        Lane best = null;
        int bestLaneCount = -1;
        float bestLength = -1f;

        for (int i = 0;
             i < incoming.Count;
             i++)
        {
            Lane lane =
                incoming[i];

            if (lane == null)
                continue;

            int sourceLaneCount =
                GetSourceRoadLaneCount(
                    lane
                );

            /*
             * Prefer the widest source road. Use physical lane length as a
             * deterministic secondary signal when source lane counts tie.
             */
            if (best == null ||
                sourceLaneCount >
                bestLaneCount ||
                (
                    sourceLaneCount ==
                    bestLaneCount &&
                    lane.totalLength >
                    bestLength
                ))
            {
                best =
                    lane;

                bestLaneCount =
                    sourceLaneCount;

                bestLength =
                    lane.totalLength;
            }
        }

        return best;
    }


    private int GetSourceRoadLaneCount(
        Lane lane)
    {
        if (lane == null ||
            lane.edge == null)
        {
            return 0;
        }

        return Mathf.Max(
            0,
            lane.edge.lanes
        );
    }


    private int CountDistinctApproaches(
        List<Lane> incoming)
    {
        List<Vector3> representatives =
            new List<Vector3>();

        float dotThreshold =
            Mathf.Cos(
                approachMergeAngleDegrees
                * Mathf.Deg2Rad
            );

        for (int i = 0;
             i < incoming.Count;
             i++)
        {
            Vector3 direction =
                GetIncomingDirection(
                    incoming[i]
                );

            if (direction.sqrMagnitude <
                0.0001f)
            {
                continue;
            }

            direction.Normalize();

            bool alreadyRepresented =
                false;

            for (int r = 0;
                 r < representatives.Count;
                 r++)
            {
                /*
                 * Direction matters here: opposite directions are separate
                 * physical approaches to the intersection.
                 */
                if (Vector3.Dot(
                        direction,
                        representatives[r])
                    >= dotThreshold)
                {
                    alreadyRepresented =
                        true;

                    break;
                }
            }

            if (!alreadyRepresented)
            {
                representatives.Add(
                    direction
                );
            }
        }

        return representatives.Count;
    }


    private Vector3 GetIncomingDirection(
        Lane lane)
    {
        if (lane == null ||
            lane.points == null ||
            lane.points.Count < 2)
        {
            return Vector3.zero;
        }

        Vector3 a =
            lane.points[
                lane.points.Count - 2
            ];

        Vector3 b =
            lane.points[
                lane.points.Count - 1
            ];

        Vector3 direction =
            b - a;

        direction.y = 0f;

        return direction.normalized;
    }


    private SignalStage GetCurrentStage(
        SignalizedIntersection signal)
    {
        float cycle =
            GetCycleDuration();

        float t =
            Mathf.Repeat(
                Time.time
                + signal.phaseOffset,
                cycle
            );

        float phaseA =
            Mathf.Max(
                0.1f,
                phaseAGreenDuration
            );

        if (t < phaseA)
            return SignalStage.PhaseAGreen;

        t -= phaseA;

        float yellow =
            Mathf.Max(
                0f,
                yellowDuration
            );

        if (t < yellow)
            return SignalStage.PhaseAYellow;

        t -= yellow;

        float allRed =
            Mathf.Max(
                0f,
                allRedDuration
            );

        if (t < allRed)
            return SignalStage.AllRedAfterA;

        t -= allRed;

        float phaseB =
            Mathf.Max(
                0.1f,
                phaseBGreenDuration
            );

        if (t < phaseB)
            return SignalStage.PhaseBGreen;

        t -= phaseB;

        if (t < yellow)
            return SignalStage.PhaseBYellow;

        return SignalStage.AllRedAfterB;
    }


    private void SynchronizeNearbySignalGroups()
    {
        if (signalsByNode.Count == 0)
            return;

        List<SignalizedIntersection> signals =
            new List<SignalizedIntersection>(
                signalsByNode.Values
            );

        /*
         * If synchronization is disabled, each signal is its own group and
         * keeps the deterministic offset derived from its own node ID.
         */
        if (!synchronizeNearbySignals ||
            signalSynchronizationRadius <= 0f)
        {
            for (int i = 0;
                 i < signals.Count;
                 i++)
            {
                signals[i].synchronizationGroup = i;

                signals[i].phaseOffset =
                    GetPhaseOffset(
                        signals[i].nodeId
                    );
            }

            return;
        }

        int count =
            signals.Count;

        int[] parent =
            new int[count];

        for (int i = 0;
             i < count;
             i++)
        {
            parent[i] = i;
        }

        float radiusSquared =
            signalSynchronizationRadius
            * signalSynchronizationRadius;

        /*
         * Union nearby signalized nodes. Because union-find is transitive,
         * A near B and B near C puts all three in the same synchronized
         * cluster even if A and C are slightly farther than the radius.
         */
        for (int i = 0;
             i < count;
             i++)
        {
            Vector3 a =
                GetNodeWorldPosition(
                    signals[i].nodeId
                );

            a.y = 0f;

            for (int j = i + 1;
                 j < count;
                 j++)
            {
                Vector3 b =
                    GetNodeWorldPosition(
                        signals[j].nodeId
                    );

                b.y = 0f;

                if ((a - b).sqrMagnitude <=
                    radiusSquared)
                {
                    UnionGroups(
                        parent,
                        i,
                        j
                    );
                }
            }
        }

        Dictionary<int, List<int>> membersByRoot =
            new Dictionary<int, List<int>>();

        for (int i = 0;
             i < count;
             i++)
        {
            int root =
                FindGroupRoot(
                    parent,
                    i
                );

            if (!membersByRoot.TryGetValue(
                    root,
                    out List<int> members))
            {
                members =
                    new List<int>();

                membersByRoot[root] =
                    members;
            }

            members.Add(i);
        }

        int groupIndex = 0;

        foreach (KeyValuePair<int, List<int>> pair
                 in membersByRoot)
        {
            List<int> members =
                pair.Value;

            /*
             * Use the smallest node ID as a stable representative so the
             * group's phase is reproducible regardless of dictionary order.
             */
            long representativeNodeId =
                long.MaxValue;

            for (int m = 0;
                 m < members.Count;
                 m++)
            {
                long nodeId =
                    signals[
                        members[m]
                    ].nodeId;

                if (nodeId <
                    representativeNodeId)
                {
                    representativeNodeId =
                        nodeId;
                }
            }

            float sharedOffset =
                GetPhaseOffset(
                    representativeNodeId
                );

            for (int m = 0;
                 m < members.Count;
                 m++)
            {
                SignalizedIntersection signal =
                    signals[
                        members[m]
                    ];

                signal.synchronizationGroup =
                    groupIndex;

                signal.phaseOffset =
                    sharedOffset;
            }

            groupIndex++;
        }
    }


    private int FindGroupRoot(
        int[] parent,
        int index)
    {
        int root =
            index;

        while (parent[root] !=
            root)
        {
            root =
                parent[root];
        }

        while (parent[index] !=
            index)
        {
            int next =
                parent[index];

            parent[index] =
                root;

            index =
                next;
        }

        return root;
    }


    private void UnionGroups(
        int[] parent,
        int a,
        int b)
    {
        int rootA =
            FindGroupRoot(
                parent,
                a
            );

        int rootB =
            FindGroupRoot(
                parent,
                b
            );

        if (rootA != rootB)
        {
            parent[rootB] =
                rootA;
        }
    }


    private int CountSynchronizationGroups()
    {
        HashSet<int> groups =
            new HashSet<int>();

        foreach (SignalizedIntersection signal
                 in signalsByNode.Values)
        {
            groups.Add(
                signal.synchronizationGroup
            );
        }

        return groups.Count;
    }


    private float GetPhaseOffset(
        long nodeId)
    {
        if (!randomizeInitialPhaseOffset)
            return 0f;

        unchecked
        {
            long hash =
                nodeId;

            hash =
                hash * 486187739L
                + phaseOffsetSeed;

            hash ^=
                hash >> 13;

            hash *=
                1274126177L;

            uint positive =
                (uint)(
                    hash & 0xffffffffL
                );

            float fraction =
                positive
                / (float)uint.MaxValue;

            return
                fraction
                * GetCycleDuration();
        }
    }


    private Vector3 GetNodeWorldPosition(
        long nodeId)
    {
        if (network == null ||
            !network.nodesById.TryGetValue(
                nodeId,
                out RoadNodeData node))
        {
            return transform.position;
        }

        Vector3 local =
            new Vector3(
                node.position.x,
                node.position.y,
                node.position.z
            );

        return
            network.LanePointToWorld(
                local
            );
    }


    private Vector3 GetIncomingWorldDirection(
        Lane lane)
    {
        if (network == null ||
            lane == null ||
            lane.points == null ||
            lane.points.Count < 2)
        {
            return Vector3.zero;
        }

        Vector3 a =
            network.LanePointToWorld(
                lane.points[
                    lane.points.Count - 2
                ]
            );

        Vector3 b =
            network.LanePointToWorld(
                lane.points[
                    lane.points.Count - 1
                ]
            );

        Vector3 direction =
            b - a;

        direction.y = 0f;

        return direction.normalized;
    }


    private void OnDrawGizmos()
    {
        if (!showTrafficLightGizmos ||
            network == null ||
            signalsByNode.Count == 0)
        {
            return;
        }

        if (showSynchronizationLinks &&
            synchronizeNearbySignals)
        {
            DrawSynchronizationLinks();
        }

        foreach (KeyValuePair<long, SignalizedIntersection> pair
                 in signalsByNode)
        {
            SignalizedIntersection signal =
                pair.Value;

            Vector3 nodePosition =
                GetNodeWorldPosition(
                    signal.nodeId
                );

            nodePosition.y +=
                gizmoHeight;

            if (showSignalizedIntersectionMarkers)
            {
                Gizmos.color =
                    new Color(
                        1f,
                        1f,
                        1f,
                        0.7f
                    );

                Gizmos.DrawWireSphere(
                    nodePosition,
                    gizmoIntersectionMarkerRadius
                );
            }

            SignalStage stage =
                GetCurrentStage(
                    signal
                );

            bool phaseAIsGreen =
                stage ==
                SignalStage.PhaseAGreen;

            bool phaseBIsGreen =
                stage ==
                SignalStage.PhaseBGreen;

            if (!phaseAIsGreen &&
                !phaseBIsGreen)
            {
                continue;
            }

            Gizmos.color =
                Color.green;

            for (int i = 0;
                 i < signal.incomingLanes.Count;
                 i++)
            {
                Lane lane =
                    signal.incomingLanes[i];

                if (lane == null)
                    continue;

                bool permitted =
                    phaseAIsGreen
                        ? signal.phaseALaneIds.Contains(
                            lane.id
                        )
                        : signal.phaseBLaneIds.Contains(
                            lane.id
                        );

                if (!permitted)
                    continue;

                Vector3 direction =
                    GetIncomingWorldDirection(
                        lane
                    );

                if (direction.sqrMagnitude <
                    0.0001f)
                {
                    continue;
                }

                DrawDirectionArrow(
                    nodePosition,
                    direction
                );
            }
        }
    }


    private void DrawSynchronizationLinks()
    {
        List<SignalizedIntersection> signals =
            new List<SignalizedIntersection>(
                signalsByNode.Values
            );

        Gizmos.color =
            new Color(
                0f,
                1f,
                1f,
                0.35f
            );

        for (int i = 0;
             i < signals.Count;
             i++)
        {
            SignalizedIntersection a =
                signals[i];

            if (a.synchronizationGroup < 0)
                continue;

            Vector3 positionA =
                GetNodeWorldPosition(
                    a.nodeId
                );

            positionA.y +=
                gizmoHeight
                * 0.75f;

            for (int j = i + 1;
                 j < signals.Count;
                 j++)
            {
                SignalizedIntersection b =
                    signals[j];

                if (a.synchronizationGroup !=
                    b.synchronizationGroup)
                {
                    continue;
                }

                /*
                 * Only draw direct radius-neighbour links. The synchronization
                 * group itself may be larger through transitive connections.
                 */
                Vector3 positionB =
                    GetNodeWorldPosition(
                        b.nodeId
                    );

                positionB.y +=
                    gizmoHeight
                    * 0.75f;

                Vector3 flatA =
                    positionA;

                Vector3 flatB =
                    positionB;

                flatA.y = 0f;
                flatB.y = 0f;

                if (Vector3.Distance(
                        flatA,
                        flatB)
                    <= signalSynchronizationRadius)
                {
                    Gizmos.DrawLine(
                        positionA,
                        positionB
                    );
                }
            }
        }
    }


    private void DrawDirectionArrow(
        Vector3 nodePosition,
        Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude <
            0.0001f)
        {
            return;
        }

        direction.Normalize();

        float length =
            Mathf.Max(
                0.5f,
                gizmoArrowLength
            );

        Vector3 start =
            nodePosition
            - direction
            * length
            * 0.75f;

        Vector3 tip =
            nodePosition
            + direction
            * length
            * 0.25f;

        Gizmos.DrawLine(
            start,
            tip
        );

        Vector3 right =
            new Vector3(
                direction.z,
                0f,
                -direction.x
            );

        float head =
            Mathf.Max(
                0.1f,
                gizmoArrowHeadSize
            );

        Vector3 leftWing =
            tip
            - direction
            * head
            + right
            * head
            * 0.6f;

        Vector3 rightWing =
            tip
            - direction
            * head
            - right
            * head
            * 0.6f;

        Gizmos.DrawLine(
            tip,
            leftWing
        );

        Gizmos.DrawLine(
            tip,
            rightWing
        );
    }
}
