using System.Collections.Generic;
using UnityEngine;

public static class LanePathUtility
{
    public static float GetLength(
        IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 2)
            return 0f;

        float length = 0f;

        for (int i = 0; i < points.Count - 1; i++)
        {
            length += Vector3.Distance(
                points[i],
                points[i + 1]
            );
        }

        return length;
    }


    public static Vector3 GetPointAtDistanceFromStart(
        IReadOnlyList<Vector3> points,
        float distance)
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

            float segmentLength =
                Vector3.Distance(a, b);

            if (travelled + segmentLength >= distance)
            {
                float remaining =
                    distance - travelled;

                float t =
                    segmentLength > 0f
                        ? remaining / segmentLength
                        : 0f;

                return Vector3.Lerp(a, b, t);
            }

            travelled += segmentLength;
        }

        return points[points.Count - 1];
    }


    public static Vector3 GetPointAtDistanceFromEnd(
        IReadOnlyList<Vector3> points,
        float distance)
    {
        float totalLength = GetLength(points);

        return GetPointAtDistanceFromStart(
            points,
            Mathf.Max(
                0f,
                totalLength - distance
            )
        );
    }
}