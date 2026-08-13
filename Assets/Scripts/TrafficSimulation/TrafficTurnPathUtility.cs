using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Smooth tangent-based intersection connectors. Actual driving connectors can
/// start at the car's current position; canonical connectors are fixed for a
/// lane-to-lane movement and are suitable for cached conflict detection.
/// </summary>
public static class TrafficTurnPathUtility
{
    public static List<Vector3> BuildConnector(
        RoadNetworkManager network,
        Lane incomingLane,
        Lane outgoingLane,
        Vector3 startWorld,
        float heightOffset,
        float turnEndDistance,
        int requestedPointCount,
        int minimumPointCount,
        float tangentSampleDistance,
        float handleScale,
        float maximumHandleLength)
    {
        return BuildConnectorInto(
            null, network, incomingLane, outgoingLane, startWorld, heightOffset,
            turnEndDistance, requestedPointCount, minimumPointCount,
            tangentSampleDistance, handleScale, maximumHandleLength
        );
    }

    public static List<Vector3> BuildConnectorInto(
        List<Vector3> buffer,
        RoadNetworkManager network,
        Lane incomingLane,
        Lane outgoingLane,
        Vector3 startWorld,
        float heightOffset,
        float turnEndDistance,
        int requestedPointCount,
        int minimumPointCount,
        float tangentSampleDistance,
        float handleScale,
        float maximumHandleLength)
    {
        if (network == null || incomingLane == null || outgoingLane == null ||
            outgoingLane.points == null || outgoingLane.points.Count < 2)
            return null;

        if (buffer == null)
            buffer = new List<Vector3>(Mathf.Max(requestedPointCount, minimumPointCount));
        else
            buffer.Clear();

        float outgoingLength = outgoingLane.totalLength;
        float actualEndDistance = Mathf.Min(turnEndDistance, outgoingLength * 0.45f);
        Vector3 localEnd = LanePathUtility.GetPointAtDistanceFromStart(outgoingLane, actualEndDistance);
        Vector3 endWorld = network.LanePointToWorld(localEnd);
        endWorld.y += heightOffset;
        startWorld.y = endWorld.y;

        BuildCubicInto(
            buffer,
            startWorld,
            endWorld,
            GetEndTangent(network, incomingLane, tangentSampleDistance),
            GetStartTangent(network, outgoingLane, tangentSampleDistance),
            requestedPointCount,
            minimumPointCount,
            handleScale,
            maximumHandleLength
        );

        return buffer;
    }

    public static List<Vector3> BuildCanonicalConnector(
        List<Vector3> buffer,
        RoadNetworkManager network,
        Lane incomingLane,
        Lane outgoingLane,
        float heightOffset,
        float turnStartDistance,
        float turnEndDistance,
        int requestedPointCount,
        int minimumPointCount,
        float tangentSampleDistance,
        float handleScale,
        float maximumHandleLength)
    {
        if (network == null || incomingLane == null || outgoingLane == null)
            return null;

        Vector3 startLocal = LanePathUtility.GetPointAtDistanceFromEnd(
            incomingLane,
            Mathf.Min(turnStartDistance, incomingLane.totalLength * 0.45f)
        );
        Vector3 startWorld = network.LanePointToWorld(startLocal);
        startWorld.y += heightOffset;

        return BuildConnectorInto(
            buffer, network, incomingLane, outgoingLane, startWorld, heightOffset,
            turnEndDistance, requestedPointCount, minimumPointCount,
            tangentSampleDistance, handleScale, maximumHandleLength
        );
    }

    private static void BuildCubicInto(
        List<Vector3> result,
        Vector3 startWorld,
        Vector3 endWorld,
        Vector3 incomingDirection,
        Vector3 outgoingDirection,
        int requestedPointCount,
        int minimumPointCount,
        float handleScale,
        float maximumHandleLength)
    {
        Vector3 chord = endWorld - startWorld;
        chord.y = 0f;
        float chordLength = chord.magnitude;

        if (chordLength < 0.01f)
        {
            result.Add(startWorld);
            result.Add(endWorld);
            return;
        }

        Vector3 chordDirection = chord / chordLength;
        if (incomingDirection.sqrMagnitude < 0.001f)
            incomingDirection = chordDirection;
        if (outgoingDirection.sqrMagnitude < 0.001f)
            outgoingDirection = chordDirection;

        float handleLength = Mathf.Min(
            maximumHandleLength,
            chordLength * handleScale,
            chordLength * 0.45f
        );
        handleLength = Mathf.Clamp(handleLength, Mathf.Min(0.05f, chordLength * 0.45f), chordLength * 0.45f);

        Vector3 control1 = startWorld + incomingDirection * handleLength;
        Vector3 control2 = endWorld - outgoingDirection * handleLength;

        int pointCount = Mathf.Max(requestedPointCount, minimumPointCount, 4);
        if (result.Capacity < pointCount)
            result.Capacity = pointCount;

        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            float u = 1f - t;
            result.Add(
                u * u * u * startWorld +
                3f * u * u * t * control1 +
                3f * u * t * t * control2 +
                t * t * t * endWorld
            );
        }
    }

    public static Vector3 GetEndTangent(RoadNetworkManager network, Lane lane, float sampleDistance)
    {
        if (network == null || lane == null || lane.points == null || lane.points.Count < 2)
            return Vector3.zero;

        Vector3 end = network.LanePointToWorld(lane.points[lane.points.Count - 1]);
        Vector3 sample = network.LanePointToWorld(
            LanePathUtility.GetPointAtDistanceFromEnd(lane, Mathf.Max(0.01f, sampleDistance))
        );
        end.y = 0f;
        sample.y = 0f;
        Vector3 direction = end - sample;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    public static Vector3 GetStartTangent(RoadNetworkManager network, Lane lane, float sampleDistance)
    {
        if (network == null || lane == null || lane.points == null || lane.points.Count < 2)
            return Vector3.zero;

        Vector3 start = network.LanePointToWorld(lane.points[0]);
        Vector3 sample = network.LanePointToWorld(
            LanePathUtility.GetPointAtDistanceFromStart(lane, Mathf.Max(0.01f, sampleDistance))
        );
        start.y = 0f;
        sample.y = 0f;
        Vector3 direction = sample - start;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }
}
