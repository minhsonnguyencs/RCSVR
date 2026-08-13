using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Common traffic-agent API plus destination-based route management.
/// Concrete movement/following/intersection behavior remains in subclasses.
/// </summary>
public abstract class TrafficAgentBase : MonoBehaviour
{
    [Header("Destination routing")]
    [Tooltip("Minimum straight-line separation used when choosing a new random destination node.")]
    [Min(0f)] public float minimumDestinationDistance = 150f;

    [Tooltip("How many random destination candidates are tried before accepting any reachable node.")]
    [Min(1)] public int destinationSearchAttempts = 16;

    [Header("Routing debug")]
    public long debugDestinationNode = -1;
    public int debugRemainingRouteLanes = 0;

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
    private int routeCursor = 0;
    private long destinationNode = -1;
    private long pendingDestinationNode = -1;

    public abstract Lane CurrentLane { get; }
    public virtual Lane PlannedNextLane => null;
    public abstract float CurrentLaneProgress { get; }
    public virtual float CurrentSpeedMps => 0f;
    public abstract float TopSpeedKmh { get; }

    public long DestinationNode => destinationNode;
    public int RemainingRouteLaneCount => Mathf.Max(0, plannedRoute.Count - routeCursor);

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

    /// <summary>
    /// Call once from the concrete Initialize method after currentLane is set.
    /// </summary>
    protected void InitializeDestinationRouting(
        RoadNetworkManager networkManager,
        Lane startingLane)
    {
        routingNetwork = networkManager;
        plannedRoute.Clear();
        routeCursor = 0;
        destinationNode = -1;
        pendingDestinationNode = -1;

        if (routingNetwork != null && startingLane != null)
            PlanNewDestination(startingLane.endNode);

        RefreshRoutingDebug();
    }

    /// <summary>
    /// Concrete agents call this wherever they previously selected a random
    /// outgoing lane. It returns the next lane on the A* route.
    /// </summary>
    protected Lane ChoosePathfindingNextLane(Lane currentLane)
    {
        if (routingNetwork == null || currentLane == null)
            return null;

        long intersectionNode = currentLane.endNode;

        bool routeExhausted = routeCursor >= plannedRoute.Count;
        bool routeDisconnected =
            !routeExhausted &&
            plannedRoute[routeCursor].startNode != intersectionNode;

        if (routeExhausted)
        {
            if (intersectionNode == destinationNode)
            {
                /*
                 * We already need to know the lane AFTER the destination for
                 * intersection arbitration, so prepare the next trip now. The
                 * public destination is not changed until the vehicle actually
                 * transitions across the destination node.
                 */
                PlanPendingDestination(intersectionNode);
            }
            else
            {
                PlanNewDestination(intersectionNode);
            }
        }
        else if (routeDisconnected)
        {
            PlanNewDestination(intersectionNode);
        }

        if (routeCursor >= plannedRoute.Count)
        {
            RefreshRoutingDebug();
            return null;
        }

        Lane next = plannedRoute[routeCursor];
        routeCursor++;
        RefreshRoutingDebug();
        return next;
    }


    /// <summary>
    /// Concrete agents call this after a completed lane transition. It promotes
    /// a preplanned next destination only after the previous destination node
    /// has actually been crossed.
    /// </summary>
    protected void NotifyRouteLaneTransition(Lane previousLane, Lane newLane)
    {
        if (previousLane == null)
            return;

        if (pendingDestinationNode >= 0 &&
            previousLane.endNode == destinationNode)
        {
            destinationNode = pendingDestinationNode;
            pendingDestinationNode = -1;
            RefreshRoutingDebug();
        }
    }

    private bool PlanPendingDestination(long startNode)
    {
        plannedRoute.Clear();
        routeCursor = 0;
        pendingDestinationNode = -1;

        if (routingNetwork == null)
            return false;

        if (routingNetwork.TryCreateRandomRoute(
                startNode,
                TopSpeedKmh,
                minimumDestinationDistance,
                destinationSearchAttempts,
                plannedRoute,
                out long selectedDestination,
                false))
        {
            pendingDestinationNode = selectedDestination;
            RefreshRoutingDebug();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Can be called later by a dynamic-routing policy to discard the remaining
    /// path and compute a new one from the next intersection.
    /// </summary>
    public bool ReplanRoute(bool includeTrafficInCost = false)
    {
        if (routingNetwork == null || CurrentLane == null)
            return false;

        return PlanNewDestination(
            CurrentLane.endNode,
            includeTrafficInCost,
            destinationNode
        );
    }

    private bool PlanNewDestination(
        long startNode,
        bool includeTrafficInCost = false,
        long preferredDestination = -1)
    {
        plannedRoute.Clear();
        routeCursor = 0;
        pendingDestinationNode = -1;

        if (routingNetwork == null)
            return false;

        if (preferredDestination >= 0 && preferredDestination != startNode)
        {
            if (TrafficPathfinder.TryFindRoute(
                    routingNetwork,
                    startNode,
                    preferredDestination,
                    TopSpeedKmh,
                    plannedRoute,
                    includeTrafficInCost) &&
                plannedRoute.Count > 0)
            {
                destinationNode = preferredDestination;
                RefreshRoutingDebug();
                return true;
            }

            plannedRoute.Clear();
        }

        if (routingNetwork.TryCreateRandomRoute(
                startNode,
                TopSpeedKmh,
                minimumDestinationDistance,
                destinationSearchAttempts,
                plannedRoute,
                out long selectedDestination,
                includeTrafficInCost))
        {
            destinationNode = selectedDestination;
            RefreshRoutingDebug();
            return true;
        }

        destinationNode = -1;
        RefreshRoutingDebug();
        return false;
    }

    private void RefreshRoutingDebug()
    {
        debugDestinationNode = destinationNode;
        debugRemainingRouteLanes = RemainingRouteLaneCount;
    }

    private void OnDrawGizmosSelected()
    {
        if (!showRouteGizmos ||
            !Application.isPlaying ||
            routingNetwork == null ||
            CurrentLane == null)
        {
            return;
        }

        DrawRouteGizmos();
        DrawDestinationGizmo();
    }


    private void DrawRouteGizmos()
    {
        Gizmos.color = Color.cyan;

        /*
         * Draw only the part of the current lane that is still ahead of
         * the vehicle. This avoids painting the already-travelled portion
         * of the lane when a car is selected.
         */
        DrawCurrentLaneRemainder();

        /*
         * ChoosePathfindingNextLane() advances routeCursor as soon as an
         * outgoing lane is reserved for the upcoming intersection. While
         * the vehicle is still on its incoming lane that reserved lane is
         * therefore no longer present in plannedRoute[routeCursor..].
         *
         * PlannedNextLane exposes that reserved lane from the concrete
         * controller, so draw it explicitly when appropriate.
         */
        Lane reservedNextLane = PlannedNextLane;

        if (reservedNextLane != null &&
            reservedNextLane != CurrentLane)
        {
            DrawLanePolyline(
                reservedNextLane,
                routeGizmoHeight
            );
        }

        /*
         * Draw all not-yet-consumed route lanes.
         */
        for (int i = routeCursor;
             i < plannedRoute.Count;
             i++)
        {
            Lane lane = plannedRoute[i];

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
