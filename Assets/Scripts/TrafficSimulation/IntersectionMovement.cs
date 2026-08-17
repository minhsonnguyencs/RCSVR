using System.Collections.Generic;
using UnityEngine;

public class IntersectionMovement
{
    public TrafficAgentBase vehicle;
    public long nodeId;
    public Lane incomingLane;
    public Lane outgoingLane;
    public int geometryProfileHash;
    public Vector3 incomingDirection;
    public List<Vector3> conflictPath;
    public float arrivalTime;
    public bool insideIntersection;
}
