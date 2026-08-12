using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Common base class for every traffic-controller complexity level.
/// TrafficSpawner and IntersectionManager depend on this type instead of a
/// specific concrete vehicle controller.
/// </summary>
public abstract class TrafficAgentBase : MonoBehaviour
{
    public abstract void Initialize(
        RoadNetworkManager networkManager,
        IntersectionManager intersectionManager,
        Lane startingLane,
        int startingPointIndex = 0);

    /// <summary>
    /// Only intersection-aware agents override this. SimpleTrafficVehicle
    /// returns null and never registers with IntersectionManager.
    /// </summary>
    public virtual List<Vector3> GetPlannedIntersectionPath()
    {
        return null;
    }

    public virtual Vector3 GetApproachDirection()
    {
        return transform.forward;
    }
}
