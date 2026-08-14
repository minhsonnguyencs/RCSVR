using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Common traffic-agent API plus destination-based route management.
/// Concrete movement/following/intersection behavior remains in subclasses.
///
/// Routing policy is global on RoadNetworkManager:
/// - Random: choose a random valid outgoing lane at every intersection.
/// - Static: free-flow A*, no congestion rerouting.
/// - TrafficAware: occupancy-weighted A* plus periodic beneficial rerouting.
///
/// Destination generation can optionally use TrafficDemandManager zones and
/// an OD matrix. The vehicle population remains closed: reaching a destination
/// immediately creates the next trip.
/// </summary>
public abstract class TrafficAgentBase : MonoBehaviour
{
    [Header("Destination routing")]
    [Tooltip("Minimum straight-line separation used when choosing a new destination node.")]
    [Min(0f)] public float minimumDestinationDistance = 150f;

    [Tooltip("How many destination candidates are tried before the distance condition is relaxed.")]
    [Min(1)] public int destinationSearchAttempts = 16;

    [Header("Routing debug")]
    public long debugDestinationNode = -1;
    public int debugRemainingRouteLanes = 0;
    public string debugRoutingMode = "Static";
    public float debugLastCurrentRouteCost = -1f;
    public float debugLastAlternativeRouteCost = -1f;
    public float debugLastRerouteGainSeconds = 0f;
    public float debugLastRerouteGainPercent = 0f;
    public int debugSuccessfulReroutes = 0;

    [Header("Scene route visualization")]
    [Tooltip("Draw the selected vehicle's current route in the Scene view.")]
    public bool showRouteGizmos = true;

    [Tooltip("Vertical offset above the road surface used for the route line.")]
    [Min(0f)] public float routeGizmoHeight = 1.5f;

    [Tooltip("Radius of the sphere drawn at the current destination node.")]
    [Min(0.1f)] public float destinationGizmoRadius = 3f;

    [Tooltip("Draw a vertical marker above the destination to make it easier to find in dense road geometry.")]
    public bool showDestinationMarker = true;

    [Tooltip("Height of the optional vertical destination marker.")]
    [Min(0f)] public float destinationMarkerHeight = 12f;

    private RoadNetworkManager routingNetwork;
    private readonly List<Lane> plannedRoute = new List<Lane>();
    private readonly List<Lane> rerouteCandidate = new List<Lane>();

    /*
     * Snapshot of the route replaced by the most recent successful
     * traffic-aware reroute. It exists only for the visual comparison.
     */
    private readonly List<Lane> previousRouteSnapshot = new List<Lane>();

    private int routeCursor = 0;
    private long destinationNode = -1;
    private long pendingDestinationNode = -1;
    private float nextRerouteEvaluationTime = float.PositiveInfinity;

    /*
     * Tracks runtime changes of RoadNetworkManager.routingPolicy.mode.
     * This lets the simulation switch between Random / Static / TrafficAware
     * without respawning the vehicles.
     */
    private TrafficRoutingMode lastObservedRoutingMode =
        TrafficRoutingMode.Static;

    private Renderer[] rerouteHighlightRenderers;
    private MaterialPropertyBlock rerouteHighlightPropertyBlock;
    private float rerouteHighlightUntil = -1f;
    private bool rerouteHighlightActive = false;

    private static readonly int BaseColorProperty =
        Shader.PropertyToID("_BaseColor");

    private static readonly int ColorProperty =
        Shader.PropertyToID("_Color");

    /*
     * Successful-reroute visualization is configured centrally on
     * TrafficSpawner so every concrete traffic-agent type uses the same
     * debugging settings.
     */
    private TrafficSpawner VisualizationSpawner =>
        routingNetwork != null
            ? routingNetwork.trafficSpawner
            : null;

    private bool HighlightSuccessfulReroutes =>
        VisualizationSpawner == null
            ? true
            : VisualizationSpawner.highlightSuccessfulReroutes;

    private float RerouteHighlightDuration =>
        VisualizationSpawner == null
            ? 10f
            : Mathf.Max(
                0f,
                VisualizationSpawner.rerouteHighlightDuration
            );

    private Color RerouteHighlightColor =>
        VisualizationSpawner == null
            ? Color.cyan
            : VisualizationSpawner.rerouteHighlightColor;

    private bool ShowRerouteRouteComparison =>
        VisualizationSpawner == null
            ? true
            : VisualizationSpawner.showRerouteRouteComparison;

    private Color PreviousRouteGizmoColor =>
        VisualizationSpawner == null
            ? Color.magenta
            : VisualizationSpawner.previousRouteGizmoColor;

    private Color NewRouteGizmoColor =>
        VisualizationSpawner == null
            ? Color.green
            : VisualizationSpawner.newRouteGizmoColor;


    public abstract Lane CurrentLane { get; }
    public virtual Lane PlannedNextLane => null;
    public abstract float CurrentLaneProgress { get; }
    public virtual float CurrentSpeedMps => 0f;
    public abstract float TopSpeedKmh { get; }

    public long DestinationNode => destinationNode;
    public int RemainingRouteLaneCount =>
        Mathf.Max(
            0,
            plannedRoute.Count - routeCursor
        );

    public abstract void Initialize(
        RoadNetworkManager networkManager,
        IntersectionManager intersectionManager,
        Lane startingLane,
        int startingPointIndex = 0);

    public abstract void SetTopSpeedKmh(float speedKmh);

    public virtual List<Vector3> GetPlannedIntersectionPath() => null;
    public virtual List<Vector3> GetConflictIntersectionPath() => GetPlannedIntersectionPath();
    public virtual Vector3 GetApproachDirection() => transform.forward;
    public virtual int IntersectionGeometryProfileHash => 0;

    protected void InitializeDestinationRouting(
        RoadNetworkManager networkManager,
        Lane startingLane)
    {
        routingNetwork = networkManager;
        plannedRoute.Clear();
        rerouteCandidate.Clear();
        previousRouteSnapshot.Clear();
        routeCursor = 0;
        destinationNode = -1;
        pendingDestinationNode = -1;
        debugSuccessfulReroutes = 0;

        RestoreRerouteHighlight();
        CacheRerouteHighlightRenderers();

        lastObservedRoutingMode =
            GetCurrentRoutingMode();

        if (routingNetwork != null &&
            startingLane != null &&
            lastObservedRoutingMode !=
                TrafficRoutingMode.Random)
        {
            PlanNewDestination(
                startingLane.endNode
            );
        }

        ScheduleNextRerouteEvaluation();
        RefreshRoutingDebug();
    }

    /// <summary>
    /// Call once from each concrete vehicle Update().
    /// In Static mode this is effectively free. In TrafficAware mode the method
    /// periodically checks whether a new route to the SAME destination is
    /// sufficiently faster than the current remaining route.
    /// </summary>
    protected void UpdateDynamicRouting()
    {
        UpdateRerouteHighlight();

        if (routingNetwork == null ||
            CurrentLane == null)
        {
            return;
        }

        SynchronizeRoutingModeIfNeeded();

        TrafficRoutingPolicy policy =
            routingNetwork.routingPolicy;

        TrafficRoutingMode mode =
            GetCurrentRoutingMode();

        debugRoutingMode =
            mode.ToString();

        /*
         * Random mode deliberately has no destination and no A* work.
         */
        if (mode ==
            TrafficRoutingMode.Random)
        {
            return;
        }

        if (destinationNode < 0)
        {
            /*
             * This can happen when switching from Random back to a routed mode
             * while the concrete controller has already reserved its next lane.
             * Wait until it is safe to create a new route.
             */
            if (PlannedNextLane == null)
            {
                PlanNewDestination(
                    CurrentLane.endNode
                );
            }

            return;
        }

        if (policy == null ||
            mode != TrafficRoutingMode.TrafficAware)
        {
            return;
        }

        if (Time.time <
            nextRerouteEvaluationTime)
        {
            return;
        }

        ScheduleNextRerouteEvaluation();

        /*
         * Do not change route after a concrete controller has already selected
         * an outgoing lane for the upcoming intersection. This keeps the
         * intersection reservation and physical turn plan consistent.
         */
        if (PlannedNextLane != null)
            return;

        if (CurrentLane.endNode == destinationNode)
            return;

        if (routeCursor >= plannedRoute.Count)
            return;

        float currentCost =
            routingNetwork.GetRouteCostSeconds(
                plannedRoute,
                routeCursor,
                TopSpeedKmh,
                true,
                policy.congestionWeight,
                policy.congestionExponent,
                policy.maximumCongestionMultiplier
            );

        debugLastCurrentRouteCost =
            currentCost;

        if (float.IsInfinity(currentCost) ||
            currentCost <
            policy.minimumRemainingRouteTimeSeconds)
        {
            return;
        }

        rerouteCandidate.Clear();

        bool found =
            TrafficPathfinder.TryFindRoute(
                routingNetwork,
                CurrentLane.endNode,
                destinationNode,
                TopSpeedKmh,
                rerouteCandidate,
                true,
                policy.congestionWeight,
                policy.congestionExponent,
                policy.maximumCongestionMultiplier
            );

        if (!found ||
            rerouteCandidate.Count == 0)
        {
            return;
        }

        float alternativeCost =
            routingNetwork.GetRouteCostSeconds(
                rerouteCandidate,
                0,
                TopSpeedKmh,
                true,
                policy.congestionWeight,
                policy.congestionExponent,
                policy.maximumCongestionMultiplier
            );

        debugLastAlternativeRouteCost =
            alternativeCost;

        if (float.IsInfinity(alternativeCost))
            return;

        float gainSeconds =
            currentCost
            - alternativeCost;

        float gainPercent =
            currentCost > 0.001f
                ? gainSeconds
                  / currentCost
                  * 100f
                : 0f;

        debugLastRerouteGainSeconds =
            gainSeconds;

        debugLastRerouteGainPercent =
            gainPercent;

        /*
         * Both thresholds must be met. The absolute threshold filters tiny
         * numerical wins; the percentage threshold prevents rerouting a long
         * trip for a negligible relative improvement.
         */
        if (gainSeconds <
                policy.minimumTimeGainSeconds ||
            gainPercent <
                policy.minimumTimeGainPercent)
        {
            return;
        }

        /*
         * Preserve the route we are about to abandon so it can be shown
         * alongside the accepted route for the highlight duration.
         */
        previousRouteSnapshot.Clear();

        for (int i = routeCursor;
             i < plannedRoute.Count;
             i++)
        {
            Lane oldLane = plannedRoute[i];

            if (oldLane != null)
            {
                previousRouteSnapshot.Add(
                    oldLane
                );
            }
        }

        plannedRoute.Clear();
        plannedRoute.AddRange(
            rerouteCandidate
        );

        routeCursor = 0;
        pendingDestinationNode = -1;
        debugSuccessfulReroutes++;

        StartRerouteHighlight();
        RefreshRoutingDebug();
    }

    private void CacheRerouteHighlightRenderers()
    {
        rerouteHighlightRenderers =
            GetComponentsInChildren<Renderer>(
                true
            );

        if (rerouteHighlightPropertyBlock == null)
        {
            rerouteHighlightPropertyBlock =
                new MaterialPropertyBlock();
        }
    }


    private void StartRerouteHighlight()
    {
        if (!HighlightSuccessfulReroutes ||
            RerouteHighlightDuration <= 0f)
        {
            return;
        }

        if (rerouteHighlightRenderers == null ||
            rerouteHighlightRenderers.Length == 0)
        {
            CacheRerouteHighlightRenderers();
        }

        rerouteHighlightUntil =
            Time.time
            + RerouteHighlightDuration;

        ApplyRerouteHighlight();
    }


    private void UpdateRerouteHighlight()
    {
        if (!rerouteHighlightActive)
            return;

        if (Time.time <
            rerouteHighlightUntil)
        {
            return;
        }

        RestoreRerouteHighlight();
    }


    private void ApplyRerouteHighlight()
    {
        if (rerouteHighlightRenderers == null)
            return;

        rerouteHighlightActive = true;

        foreach (Renderer renderer
                 in rerouteHighlightRenderers)
        {
            if (renderer == null)
                continue;

            Material sharedMaterial =
                renderer.sharedMaterial;

            if (sharedMaterial == null)
                continue;

            renderer.GetPropertyBlock(
                rerouteHighlightPropertyBlock
            );

            if (sharedMaterial.HasProperty(
                    BaseColorProperty))
            {
                rerouteHighlightPropertyBlock
                    .SetColor(
                        BaseColorProperty,
                        RerouteHighlightColor
                    );
            }

            if (sharedMaterial.HasProperty(
                    ColorProperty))
            {
                rerouteHighlightPropertyBlock
                    .SetColor(
                        ColorProperty,
                        RerouteHighlightColor
                    );
            }

            renderer.SetPropertyBlock(
                rerouteHighlightPropertyBlock
            );

            rerouteHighlightPropertyBlock.Clear();
        }
    }


    private void RestoreRerouteHighlight()
    {
        rerouteHighlightUntil = -1f;

        if (!rerouteHighlightActive)
            return;

        rerouteHighlightActive = false;
        previousRouteSnapshot.Clear();

        if (rerouteHighlightRenderers == null)
            return;

        foreach (Renderer renderer
                 in rerouteHighlightRenderers)
        {
            if (renderer == null)
                continue;

            /*
             * The traffic prefabs do not currently use custom per-renderer
             * property blocks, so clearing the block restores the shared
             * material's normal appearance without instantiating materials.
             */
            renderer.SetPropertyBlock(null);
        }
    }


    protected virtual void OnDisable()
    {
        RestoreRerouteHighlight();
    }


    private void ScheduleNextRerouteEvaluation()
    {
        if (routingNetwork == null ||
            routingNetwork.routingPolicy == null ||
            routingNetwork.routingPolicy.mode !=
                TrafficRoutingMode.TrafficAware)
        {
            nextRerouteEvaluationTime =
                float.PositiveInfinity;

            return;
        }

        TrafficRoutingPolicy policy =
            routingNetwork.routingPolicy;

        float jitter =
            Mathf.Max(
                0f,
                policy.reroutingIntervalJitterSeconds
            );

        float interval =
            Mathf.Max(
                0.5f,
                policy.reroutingIntervalSeconds
                + Random.Range(
                    -jitter,
                    jitter
                )
            );

        nextRerouteEvaluationTime =
            Time.time
            + interval;
    }

    protected Lane ChoosePathfindingNextLane(
        Lane currentLane)
    {
        if (routingNetwork == null ||
            currentLane == null)
        {
            return null;
        }

        SynchronizeRoutingModeIfNeeded();

        if (GetCurrentRoutingMode() ==
            TrafficRoutingMode.Random)
        {
            Lane randomLane =
                ChooseRandomOutgoingLane(
                    currentLane
                );

            RefreshRoutingDebug();
            return randomLane;
        }

        long intersectionNode =
            currentLane.endNode;

        bool routeExhausted =
            routeCursor >=
            plannedRoute.Count;

        bool routeDisconnected =
            !routeExhausted &&
            plannedRoute[routeCursor]
                .startNode
            != intersectionNode;

        if (routeExhausted)
        {
            if (intersectionNode ==
                destinationNode)
            {
                /*
                 * The outgoing lane has to be known before the destination node
                 * is physically crossed. Preplan the next trip but do not expose
                 * the new destination until NotifyRouteLaneTransition().
                 */
                PlanPendingDestination(
                    intersectionNode
                );
            }
            else
            {
                PlanNewDestination(
                    intersectionNode
                );
            }
        }
        else if (routeDisconnected)
        {
            PlanNewDestination(
                intersectionNode
            );
        }

        if (routeCursor >=
            plannedRoute.Count)
        {
            RefreshRoutingDebug();
            return null;
        }

        Lane next =
            plannedRoute[routeCursor];

        routeCursor++;

        RefreshRoutingDebug();
        return next;
    }

    protected void NotifyRouteLaneTransition(
        Lane previousLane,
        Lane newLane)
    {
        if (previousLane == null)
            return;

        SynchronizeRoutingModeIfNeeded();

        if (GetCurrentRoutingMode() ==
            TrafficRoutingMode.Random)
        {
            RefreshRoutingDebug();
            return;
        }

        if (pendingDestinationNode >= 0 &&
            previousLane.endNode ==
                destinationNode)
        {
            destinationNode =
                pendingDestinationNode;

            pendingDestinationNode = -1;

            ScheduleNextRerouteEvaluation();
            RefreshRoutingDebug();
        }
    }

    private TrafficRoutingMode GetCurrentRoutingMode()
    {
        if (routingNetwork == null ||
            routingNetwork.routingPolicy == null)
        {
            return TrafficRoutingMode.Static;
        }

        return
            routingNetwork
                .routingPolicy
                .mode;
    }


    private void SynchronizeRoutingModeIfNeeded()
    {
        TrafficRoutingMode currentMode =
            GetCurrentRoutingMode();

        if (currentMode ==
            lastObservedRoutingMode)
        {
            return;
        }

        lastObservedRoutingMode =
            currentMode;

        /*
         * A routing-mode switch invalidates the previous interpretation of
         * plannedRoute. Clear it rather than mixing a previously generated
         * A* path with random movement.
         */
        plannedRoute.Clear();
        rerouteCandidate.Clear();
        previousRouteSnapshot.Clear();

        routeCursor = 0;
        destinationNode = -1;
        pendingDestinationNode = -1;

        RestoreRerouteHighlight();

        debugLastCurrentRouteCost = -1f;
        debugLastAlternativeRouteCost = -1f;
        debugLastRerouteGainSeconds = 0f;
        debugLastRerouteGainPercent = 0f;

        if (currentMode ==
            TrafficRoutingMode.Random)
        {
            nextRerouteEvaluationTime =
                float.PositiveInfinity;

            RefreshRoutingDebug();
            return;
        }

        ScheduleNextRerouteEvaluation();

        /*
         * If a concrete controller has not yet committed to an outgoing lane,
         * routing can restart immediately. Otherwise ChoosePathfindingNextLane
         * will generate a fresh destination at the next junction.
         */
        if (routingNetwork != null &&
            CurrentLane != null &&
            PlannedNextLane == null)
        {
            PlanNewDestination(
                CurrentLane.endNode
            );
        }

        RefreshRoutingDebug();
    }


    private Lane ChooseRandomOutgoingLane(
        Lane currentLane)
    {
        if (routingNetwork == null ||
            currentLane == null)
        {
            return null;
        }

        long intersectionNode =
            currentLane.endNode;

        if (!routingNetwork.lanesFromNode.TryGetValue(
                intersectionNode,
                out List<Lane> candidates) ||
            candidates == null ||
            candidates.Count == 0)
        {
            return null;
        }

        /*
         * Preserve the behavior of the pre-pathfinding traffic agents:
         * avoid immediately travelling back to the node we just came from.
         * At a true dead end, allow the U-turn so the vehicle does not stop.
         */
        List<Lane> validCandidates =
            new List<Lane>();

        for (int i = 0;
             i < candidates.Count;
             i++)
        {
            Lane candidate =
                candidates[i];

            if (candidate == null)
                continue;

            if (candidate.endNode !=
                currentLane.startNode)
            {
                validCandidates.Add(
                    candidate
                );
            }
        }

        List<Lane> selectionPool =
            validCandidates.Count > 0
                ? validCandidates
                : candidates;

        /*
         * candidates should normally contain no nulls, but select defensively
         * in case a malformed graph produces one.
         */
        int attempts =
            selectionPool.Count;

        while (attempts > 0)
        {
            Lane selected =
                selectionPool[
                    Random.Range(
                        0,
                        selectionPool.Count
                    )
                ];

            if (selected != null)
                return selected;

            attempts--;
        }

        return null;
    }


    private bool PlanPendingDestination(
        long startNode)
    {
        if (GetCurrentRoutingMode() ==
            TrafficRoutingMode.Random)
        {
            return false;
        }

        plannedRoute.Clear();
        routeCursor = 0;
        pendingDestinationNode = -1;

        if (routingNetwork == null)
            return false;

        bool includeTraffic =
            ShouldUseTrafficCostForNewTrip();

        if (TryCreateDestinationRoute(
                startNode,
                plannedRoute,
                out long selectedDestination,
                includeTraffic))
        {
            pendingDestinationNode =
                selectedDestination;

            RefreshRoutingDebug();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Public manual reroute hook. The destination stays unchanged.
    /// </summary>
    public bool ReplanRoute(
        bool includeTrafficInCost = false)
    {
        SynchronizeRoutingModeIfNeeded();

        if (GetCurrentRoutingMode() ==
            TrafficRoutingMode.Random)
        {
            return false;
        }

        if (routingNetwork == null ||
            CurrentLane == null ||
            destinationNode < 0)
        {
            return false;
        }

        plannedRoute.Clear();
        routeCursor = 0;
        pendingDestinationNode = -1;

        bool found =
            TrafficPathfinder.TryFindRoute(
                routingNetwork,
                CurrentLane.endNode,
                destinationNode,
                TopSpeedKmh,
                plannedRoute,
                includeTrafficInCost
            );

        RefreshRoutingDebug();
        return found &&
            plannedRoute.Count > 0;
    }

    private bool PlanNewDestination(
        long startNode,
        bool? includeTrafficOverride = null,
        long preferredDestination = -1)
    {
        if (GetCurrentRoutingMode() ==
            TrafficRoutingMode.Random)
        {
            plannedRoute.Clear();
            routeCursor = 0;
            destinationNode = -1;
            pendingDestinationNode = -1;
            RefreshRoutingDebug();
            return false;
        }

        plannedRoute.Clear();
        routeCursor = 0;
        pendingDestinationNode = -1;

        if (routingNetwork == null)
            return false;

        bool includeTraffic =
            includeTrafficOverride
            ?? ShouldUseTrafficCostForNewTrip();

        if (preferredDestination >= 0 &&
            preferredDestination != startNode)
        {
            if (TrafficPathfinder.TryFindRoute(
                    routingNetwork,
                    startNode,
                    preferredDestination,
                    TopSpeedKmh,
                    plannedRoute,
                    includeTraffic,
                    GetCongestionWeight(),
                    GetCongestionExponent(),
                    GetMaximumCongestionMultiplier()) &&
                plannedRoute.Count > 0)
            {
                destinationNode =
                    preferredDestination;

                RefreshRoutingDebug();
                return true;
            }

            plannedRoute.Clear();
        }

        if (TryCreateDestinationRoute(
                startNode,
                plannedRoute,
                out long selectedDestination,
                includeTraffic))
        {
            destinationNode =
                selectedDestination;

            RefreshRoutingDebug();
            return true;
        }

        destinationNode = -1;
        RefreshRoutingDebug();
        return false;
    }

    private bool TryCreateDestinationRoute(
        long startNode,
        List<Lane> result,
        out long selectedDestination,
        bool includeTraffic)
    {
        selectedDestination = -1;

        bool useDemand =
            routingNetwork.trafficDemandManager != null &&
            routingNetwork.trafficDemandManager
                .useDemandModel;

        if (useDemand &&
            routingNetwork.TryCreateDemandRoute(
                startNode,
                TopSpeedKmh,
                minimumDestinationDistance,
                destinationSearchAttempts,
                result,
                out selectedDestination,
                includeTraffic,
                GetCongestionWeight(),
                GetCongestionExponent(),
                GetMaximumCongestionMultiplier()))
        {
            return true;
        }

        /*
         * Fallback keeps the simulation running if the OD matrix or zone
         * configuration cannot produce a reachable destination.
         */
        return routingNetwork.TryCreateRandomRoute(
            startNode,
            TopSpeedKmh,
            minimumDestinationDistance,
            destinationSearchAttempts,
            result,
            out selectedDestination,
            includeTraffic,
            GetCongestionWeight(),
            GetCongestionExponent(),
            GetMaximumCongestionMultiplier()
        );
    }

    private bool ShouldUseTrafficCostForNewTrip()
    {
        if (routingNetwork == null ||
            routingNetwork.routingPolicy == null)
        {
            return false;
        }

        TrafficRoutingPolicy policy =
            routingNetwork.routingPolicy;

        return policy.mode ==
                TrafficRoutingMode.TrafficAware
            && policy.trafficAwareInitialRouting;
    }

    private float GetCongestionWeight()
    {
        return routingNetwork != null &&
               routingNetwork.routingPolicy != null
            ? routingNetwork.routingPolicy
                .congestionWeight
            : -1f;
    }

    private float GetCongestionExponent()
    {
        return routingNetwork != null &&
               routingNetwork.routingPolicy != null
            ? routingNetwork.routingPolicy
                .congestionExponent
            : -1f;
    }

    private float GetMaximumCongestionMultiplier()
    {
        return routingNetwork != null &&
               routingNetwork.routingPolicy != null
            ? routingNetwork.routingPolicy
                .maximumCongestionMultiplier
            : -1f;
    }

    private void RefreshRoutingDebug()
    {
        debugDestinationNode =
            destinationNode;

        debugRemainingRouteLanes =
            RemainingRouteLaneCount;

        debugRoutingMode =
            routingNetwork != null &&
            routingNetwork.routingPolicy != null
                ? routingNetwork
                    .routingPolicy
                    .mode
                    .ToString()
                : "Static";
    }

    private void OnDrawGizmosSelected()
    {
        if (!showRouteGizmos ||
            !Application.isPlaying ||
            routingNetwork == null ||
            CurrentLane == null ||
            GetCurrentRoutingMode() ==
                TrafficRoutingMode.Random)
        {
            return;
        }

        DrawRouteGizmos();
        DrawDestinationGizmo();
    }


    private void DrawRouteGizmos()
    {
        /*
         * The remainder of the current lane is common to both alternatives:
         * traffic-aware rerouting is only allowed before a concrete outgoing
         * lane has been reserved for the next intersection.
         */
        Gizmos.color = Color.cyan;
        DrawCurrentLaneRemainder();

        /*
         * During the same window in which the vehicle is tinted cyan:
         *
         *   magenta = previous / abandoned route
         *   green   = newly accepted route
         *
         * After the highlight expires, only the normal cyan route remains.
         */
        if (ShowRerouteRouteComparison &&
            rerouteHighlightActive &&
            previousRouteSnapshot.Count > 0)
        {
            DrawLaneSequence(
                previousRouteSnapshot,
                0,
                PreviousRouteGizmoColor,
                null
            );

            DrawLaneSequence(
                plannedRoute,
                routeCursor,
                NewRouteGizmoColor,
                PlannedNextLane
            );

            return;
        }

        DrawLaneSequence(
            plannedRoute,
            routeCursor,
            Color.cyan,
            PlannedNextLane
        );
    }


    private void DrawLaneSequence(
        IReadOnlyList<Lane> route,
        int startIndex,
        Color color,
        Lane reservedNextLane)
    {
        Gizmos.color = color;

        /*
         * ChoosePathfindingNextLane() advances routeCursor as soon as an
         * outgoing lane is reserved for an approaching intersection.
         * Draw that lane explicitly when needed.
         */
        if (reservedNextLane != null &&
            reservedNextLane != CurrentLane)
        {
            DrawLanePolyline(
                reservedNextLane,
                routeGizmoHeight
            );
        }

        if (route == null)
            return;

        int safeStart =
            Mathf.Clamp(
                startIndex,
                0,
                route.Count
            );

        for (int i = safeStart;
             i < route.Count;
             i++)
        {
            Lane lane = route[i];

            if (lane == null ||
                lane == reservedNextLane)
            {
                continue;
            }

            DrawLanePolyline(
                lane,
                routeGizmoHeight
            );
        }
    }


    private void DrawCurrentLaneRemainder()
    {
        if (CurrentLane == null ||
            CurrentLane.points == null ||
            CurrentLane.points.Count < 2)
        {
            return;
        }

        List<Vector3> points =
            CurrentLane.points;

        /*
         * Find the lane segment closest to the vehicle in world space.
         * This keeps the debug path visually attached to the actual lane
         * even when the current controller is between sampled lane points.
         */
        int closestSegment = 0;
        float closestT = 0f;
        float bestDistanceSquared =
            float.PositiveInfinity;

        Vector3 carPosition =
            transform.position;

        carPosition.y = 0f;

        for (int i = 0;
             i < points.Count - 1;
             i++)
        {
            Vector3 a =
                routingNetwork.LanePointToWorld(
                    points[i]
                );

            Vector3 b =
                routingNetwork.LanePointToWorld(
                    points[i + 1]
                );

            a.y = 0f;
            b.y = 0f;

            Vector3 ab =
                b - a;

            float denominator =
                Vector3.Dot(
                    ab,
                    ab
                );

            float t = 0f;

            if (denominator > 0.00001f)
            {
                t =
                    Mathf.Clamp01(
                        Vector3.Dot(
                            carPosition - a,
                            ab
                        )
                        / denominator
                    );
            }

            Vector3 closest =
                a + ab * t;

            float distanceSquared =
                (
                    carPosition - closest
                ).sqrMagnitude;

            if (distanceSquared <
                bestDistanceSquared)
            {
                bestDistanceSquared =
                    distanceSquared;

                closestSegment = i;
                closestT = t;
            }
        }

        Vector3 segmentStart =
            routingNetwork.LanePointToWorld(
                points[closestSegment]
            );

        Vector3 segmentEnd =
            routingNetwork.LanePointToWorld(
                points[closestSegment + 1]
            );

        Vector3 projectedStart =
            Vector3.Lerp(
                segmentStart,
                segmentEnd,
                closestT
            );

        projectedStart.y +=
            routeGizmoHeight;

        /*
         * Connect the car to the projected lane position, then follow the
         * actual lane polyline to its end.
         */
        Vector3 carDebugPosition =
            transform.position;

        carDebugPosition.y +=
            routeGizmoHeight;

        Gizmos.DrawLine(
            carDebugPosition,
            projectedStart
        );

        Vector3 previous =
            projectedStart;

        for (int i = closestSegment + 1;
             i < points.Count;
             i++)
        {
            Vector3 next =
                routingNetwork.LanePointToWorld(
                    points[i]
                );

            next.y +=
                routeGizmoHeight;

            Gizmos.DrawLine(
                previous,
                next
            );

            previous = next;
        }
    }


    private void DrawLanePolyline(
        Lane lane,
        float heightOffset)
    {
        if (lane == null ||
            lane.points == null ||
            lane.points.Count < 2 ||
            routingNetwork == null)
        {
            return;
        }

        Vector3 previous =
            routingNetwork.LanePointToWorld(
                lane.points[0]
            );

        previous.y +=
            heightOffset;

        for (int i = 1;
             i < lane.points.Count;
             i++)
        {
            Vector3 next =
                routingNetwork.LanePointToWorld(
                    lane.points[i]
                );

            next.y +=
                heightOffset;

            Gizmos.DrawLine(
                previous,
                next
            );

            previous = next;
        }
    }


    private void DrawDestinationGizmo()
    {
        if (destinationNode < 0 ||
            routingNetwork == null ||
            !routingNetwork.nodesById.TryGetValue(
                destinationNode,
                out RoadNodeData destination))
        {
            return;
        }

        Vector3 localPosition =
            new Vector3(
                destination.position.x,
                destination.position.y,
                destination.position.z
            );

        Vector3 worldPosition =
            routingNetwork.LanePointToWorld(
                localPosition
            );

        worldPosition.y +=
            routeGizmoHeight;

        Gizmos.color =
            Color.yellow;

        Gizmos.DrawWireSphere(
            worldPosition,
            destinationGizmoRadius
        );

        Gizmos.DrawSphere(
            worldPosition,
            destinationGizmoRadius * 0.2f
        );

        if (showDestinationMarker &&
            destinationMarkerHeight > 0f)
        {
            Vector3 markerTop =
                worldPosition
                + Vector3.up
                * destinationMarkerHeight;

            Gizmos.DrawLine(
                worldPosition,
                markerTop
            );

            Gizmos.DrawWireSphere(
                markerTop,
                destinationGizmoRadius * 0.5f
            );
        }
    }

}
