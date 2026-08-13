using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Common API used by spawning, intersection management, occupancy tracking,
/// and later dynamic pathfinding. Concrete behavior remains in each agent.
/// </summary>
public abstract class TrafficAgentBase : MonoBehaviour
{
    public abstract Lane CurrentLane { get; }
    public virtual Lane PlannedNextLane => null;
    public abstract float CurrentLaneProgress { get; }
    public virtual float CurrentSpeedMps => 0f;
    public abstract float TopSpeedKmh { get; }

    public abstract void Initialize(
        RoadNetworkManager networkManager,
        IntersectionManager intersectionManager,
        Lane startingLane,
        int startingPointIndex = 0);

    public abstract void SetTopSpeedKmh(float speedKmh);

    public virtual List<Vector3> GetPlannedIntersectionPath() => null;
    public virtual List<Vector3> GetConflictIntersectionPath() => GetPlannedIntersectionPath();
    public virtual Vector3 GetApproachDirection() => transform.forward;

    // Included in intersection conflict-cache keys so agents with materially
    // different connector geometry do not accidentally share cached results.
    public virtual int IntersectionGeometryProfileHash => 0;
}
