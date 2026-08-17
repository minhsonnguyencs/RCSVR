using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One directed traffic lane. Static path metrics are precomputed once so
/// agents and future pathfinding do not repeatedly walk the whole polyline.
/// </summary>
public class Lane
{
    public int id;
    public RoadEdgeData edge;
    public long startNode;
    public long endNode;
    public List<Vector3> points;

    public float[] cumulativeDistances;
    public float totalLength;

    public Lane(int laneId, RoadEdgeData sourceEdge, List<Vector3> lanePoints)
    {
        id = laneId;
        edge = sourceEdge;
        startNode = sourceEdge.from;
        endNode = sourceEdge.to;
        points = lanePoints;
        RebuildPathMetrics();
    }

    public void RebuildPathMetrics()
    {
        if (points == null || points.Count == 0)
        {
            cumulativeDistances = new float[0];
            totalLength = 0f;
            return;
        }

        cumulativeDistances = new float[points.Count];
        cumulativeDistances[0] = 0f;

        float distance = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            distance += Vector3.Distance(points[i - 1], points[i]);
            cumulativeDistances[i] = distance;
        }

        totalLength = distance;
    }

    public float GetCumulativeDistance(int pointIndex)
    {
        if (cumulativeDistances == null || cumulativeDistances.Length == 0)
            return 0f;

        pointIndex = Mathf.Clamp(pointIndex, 0, cumulativeDistances.Length - 1);
        return cumulativeDistances[pointIndex];
    }
}
