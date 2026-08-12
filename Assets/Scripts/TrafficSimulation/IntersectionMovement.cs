using System.Collections.Generic;
using UnityEngine;

public class IntersectionMovement
{
    public TrafficAgentBase vehicle;

    public long nodeId;

    public Vector3 incomingDirection;

    public List<Vector3> path;

    public float arrivalTime;

    public bool insideIntersection;
}