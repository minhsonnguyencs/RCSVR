using System.Collections.Generic;
using UnityEngine;

public class Lane
{
    public RoadEdgeData edge;

    public long startNode;
    public long endNode;

    public List<Vector3> points;

    public Lane(
        RoadEdgeData sourceEdge,
        List<Vector3> lanePoints)
    {
        edge = sourceEdge;

        startNode = sourceEdge.from;
        endNode = sourceEdge.to;

        points = lanePoints;
    }
}