using System.Collections.Generic;
using UnityEngine;

public static class LanePathUtility
{
    public static float GetLength(Lane lane)
    {
        return lane != null ? lane.totalLength : 0f;
    }

    // Retained for non-Lane paths such as temporary Bezier connectors.
    public static float GetLength(IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 2)
            return 0f;

        float length = 0f;
        for (int i = 0; i < points.Count - 1; i++)
            length += Vector3.Distance(points[i], points[i + 1]);

        return length;
    }

    public static Vector3 GetPointAtDistanceFromStart(Lane lane, float distance)
    {
        if (lane == null || lane.points == null || lane.points.Count == 0)
            return Vector3.zero;

        if (lane.points.Count == 1)
            return lane.points[0];

        distance = Mathf.Clamp(distance, 0f, lane.totalLength);
        float[] cumulative = lane.cumulativeDistances;

        int low = 0;
        int high = cumulative.Length - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (cumulative[mid] < distance)
                low = mid + 1;
            else
                high = mid;
        }

        int endIndex = Mathf.Clamp(low, 1, lane.points.Count - 1);
        int startIndex = endIndex - 1;
        float segmentStart = cumulative[startIndex];
        float segmentLength = cumulative[endIndex] - segmentStart;
        float t = segmentLength > 0.0001f
            ? (distance - segmentStart) / segmentLength
            : 0f;

        return Vector3.Lerp(lane.points[startIndex], lane.points[endIndex], Mathf.Clamp01(t));
    }

    public static Vector3 GetPointAtDistanceFromEnd(Lane lane, float distance)
    {
        if (lane == null)
            return Vector3.zero;

        return GetPointAtDistanceFromStart(lane, Mathf.Max(0f, lane.totalLength - distance));
    }

    // Backwards-compatible overloads for arbitrary point lists.
    public static Vector3 GetPointAtDistanceFromStart(IReadOnlyList<Vector3> points, float distance)
    {
        if (points == null || points.Count == 0)
            return Vector3.zero;
        if (points.Count == 1)
            return points[0];

        distance = Mathf.Max(0f, distance);
        float travelled = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[i + 1];
            float segmentLength = Vector3.Distance(a, b);
            if (travelled + segmentLength >= distance)
            {
                float t = segmentLength > 0.0001f ? (distance - travelled) / segmentLength : 0f;
                return Vector3.Lerp(a, b, Mathf.Clamp01(t));
            }
            travelled += segmentLength;
        }
        return points[points.Count - 1];
    }

    public static Vector3 GetPointAtDistanceFromEnd(IReadOnlyList<Vector3> points, float distance)
    {
        return GetPointAtDistanceFromStart(points, Mathf.Max(0f, GetLength(points) - distance));
    }

    /// <summary>
    /// O(1) lane-progress calculation apart from the projection onto the
    /// current segment. targetPointIndex is the next lane point the vehicle
    /// is travelling toward.
    /// </summary>
    public static float GetProgressOnLane(
        RoadNetworkManager network,
        Lane lane,
        int targetPointIndex,
        Vector3 worldPosition)
    {
        if (network == null || lane == null || lane.points == null || lane.points.Count < 2)
            return 0f;

        int segmentEndIndex = Mathf.Clamp(targetPointIndex, 1, lane.points.Count - 1);
        int segmentStartIndex = segmentEndIndex - 1;

        float progress = lane.GetCumulativeDistance(segmentStartIndex);

        Vector3 a = network.LanePointToWorld(lane.points[segmentStartIndex]);
        Vector3 b = network.LanePointToWorld(lane.points[segmentEndIndex]);
        a.y = 0f;
        b.y = 0f;
        worldPosition.y = 0f;

        Vector3 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        if (lengthSq > 0.0001f)
        {
            float t = Mathf.Clamp01(Vector3.Dot(worldPosition - a, ab) / lengthSq);
            float localSegmentLength = lane.GetCumulativeDistance(segmentEndIndex) - lane.GetCumulativeDistance(segmentStartIndex);
            progress += localSegmentLength * t;
        }

        return Mathf.Clamp(progress, 0f, lane.totalLength);
    }
}
