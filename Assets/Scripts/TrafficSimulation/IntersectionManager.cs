using System;
using System.Collections.Generic;
using UnityEngine;

public class IntersectionManager : MonoBehaviour
{
    [Header("Conflict Detection")]
    public float conflictDistance = 2.2f;

    private readonly Dictionary<long, List<IntersectionMovement>> movementsByNode =
        new Dictionary<long, List<IntersectionMovement>>();

    private readonly Dictionary<ConflictKey, bool> conflictCache =
        new Dictionary<ConflictKey, bool>();

    private float cachedConflictDistance = -1f;

    private struct MovementKey : IEquatable<MovementKey>
    {
        public int incomingLaneId;
        public int outgoingLaneId;
        public int profileHash;

        public bool Equals(MovementKey other)
        {
            return incomingLaneId == other.incomingLaneId &&
                   outgoingLaneId == other.outgoingLaneId &&
                   profileHash == other.profileHash;
        }

        public override bool Equals(object obj)
        {
            return obj is MovementKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + incomingLaneId;
                hash = hash * 31 + outgoingLaneId;
                hash = hash * 31 + profileHash;
                return hash;
            }
        }
    }

    private struct ConflictKey : IEquatable<ConflictKey>
    {
        public MovementKey a;
        public MovementKey b;

        public bool Equals(ConflictKey other)
        {
            return a.Equals(other.a) && b.Equals(other.b);
        }

        public override bool Equals(object obj)
        {
            return obj is ConflictKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return a.GetHashCode() * 397 ^ b.GetHashCode();
            }
        }
    }

    public void RegisterApproach(long nodeId, TrafficAgentBase vehicle, Vector3 incomingDirection)
    {
        if (vehicle == null)
            return;

        if (!movementsByNode.TryGetValue(nodeId, out List<IntersectionMovement> movements))
        {
            movements = new List<IntersectionMovement>();
            movementsByNode[nodeId] = movements;
        }

        for (int i = 0; i < movements.Count; i++)
            if (movements[i].vehicle == vehicle)
                return;

        movements.Add(new IntersectionMovement
        {
            vehicle = vehicle,
            nodeId = nodeId,
            incomingLane = vehicle.CurrentLane,
            outgoingLane = vehicle.PlannedNextLane,
            geometryProfileHash = vehicle.IntersectionGeometryProfileHash,
            incomingDirection = incomingDirection.normalized,
            conflictPath = vehicle.GetConflictIntersectionPath(),
            arrivalTime = Time.time,
            insideIntersection = false
        });
    }

    /// <summary>
    /// Kept for backwards compatibility. Conflict paths are now canonical and
    /// do not need rebuilding every frame; this method only fills a missing path.
    /// </summary>
    public void UpdatePlannedPath(long nodeId, TrafficAgentBase vehicle)
    {
        IntersectionMovement movement = FindMovement(nodeId, vehicle);
        if (movement == null)
            return;

        if (movement.conflictPath == null || movement.conflictPath.Count < 2)
            movement.conflictPath = vehicle.GetConflictIntersectionPath();
    }

    public void UnregisterApproach(long nodeId, TrafficAgentBase vehicle)
    {
        if (!movementsByNode.TryGetValue(nodeId, out List<IntersectionMovement> movements))
            return;

        for (int i = movements.Count - 1; i >= 0; i--)
        {
            if (movements[i].vehicle == null || movements[i].vehicle == vehicle)
                movements.RemoveAt(i);
        }

        if (movements.Count == 0)
            movementsByNode.Remove(nodeId);
    }

    public bool CanEnter(long nodeId, TrafficAgentBase vehicle)
    {
        if (!movementsByNode.TryGetValue(nodeId, out List<IntersectionMovement> movements))
            return true;

        RemoveDestroyed(movements);
        IntersectionMovement me = FindMovementInList(movements, vehicle);
        if (me == null)
            return true;

        EnsureConflictCacheCurrent();

        // 1. An already-entered conflicting movement always has priority.
        for (int i = 0; i < movements.Count; i++)
        {
            IntersectionMovement other = movements[i];
            if (other == me || other.vehicle == null)
                continue;

            if (MovementsConflict(me, other) && other.insideIntersection)
                return false;
        }

        // 2. Check whether a conflicting approach is on our right.
        bool hasVehicleOnRight = false;
        for (int i = 0; i < movements.Count; i++)
        {
            IntersectionMovement other = movements[i];
            if (other == me || other.vehicle == null || other.insideIntersection)
                continue;

            if (!MovementsConflict(me, other))
                continue;

            if (IsVehicleOnRight(me.incomingDirection, other.incomingDirection))
            {
                hasVehicleOnRight = true;
                break;
            }
        }

        if (!hasVehicleOnRight)
        {
            // Preserve the old oldest-arrival tie break among simultaneously
            // eligible conflicting approaches without allocating a temporary list.
            IntersectionMovement oldestEligible = me;

            for (int i = 0; i < movements.Count; i++)
            {
                IntersectionMovement other = movements[i];
                if (other == me || other.vehicle == null || other.insideIntersection)
                    continue;

                if (!MovementsConflict(me, other))
                    continue;

                bool blockedByMe = IsVehicleOnRight(other.incomingDirection, me.incomingDirection);
                if (!blockedByMe && other.arrivalTime < oldestEligible.arrivalTime)
                    oldestEligible = other;
            }

            return oldestEligible == me;
        }

        if (HasPriorityCycle(me, movements))
        {
            IntersectionMovement oldest = me;
            for (int i = 0; i < movements.Count; i++)
            {
                IntersectionMovement other = movements[i];
                if (other == me || other.vehicle == null || other.insideIntersection)
                    continue;

                if (MovementsConflict(me, other) && other.arrivalTime < oldest.arrivalTime)
                    oldest = other;
            }
            return oldest == me;
        }

        return false;
    }

    public void EnterIntersection(long nodeId, TrafficAgentBase vehicle)
    {
        IntersectionMovement movement = FindMovement(nodeId, vehicle);
        if (movement != null)
            movement.insideIntersection = true;
    }

    public void LeaveIntersection(long nodeId, TrafficAgentBase vehicle)
    {
        UnregisterApproach(nodeId, vehicle);
    }

    public void ClearAllRegistrations()
    {
        movementsByNode.Clear();
    }

    private IntersectionMovement FindMovement(long nodeId, TrafficAgentBase vehicle)
    {
        if (!movementsByNode.TryGetValue(nodeId, out List<IntersectionMovement> movements))
            return null;
        return FindMovementInList(movements, vehicle);
    }

    private IntersectionMovement FindMovementInList(List<IntersectionMovement> movements, TrafficAgentBase vehicle)
    {
        for (int i = 0; i < movements.Count; i++)
            if (movements[i].vehicle == vehicle)
                return movements[i];
        return null;
    }

    private void RemoveDestroyed(List<IntersectionMovement> movements)
    {
        for (int i = movements.Count - 1; i >= 0; i--)
            if (movements[i].vehicle == null)
                movements.RemoveAt(i);
    }

    private void EnsureConflictCacheCurrent()
    {
        if (!Mathf.Approximately(cachedConflictDistance, conflictDistance))
        {
            cachedConflictDistance = conflictDistance;
            conflictCache.Clear();
        }
    }

    private MovementKey GetMovementKey(IntersectionMovement movement)
    {
        return new MovementKey
        {
            incomingLaneId = movement.incomingLane != null ? movement.incomingLane.id : -1,
            outgoingLaneId = movement.outgoingLane != null ? movement.outgoingLane.id : -1,
            profileHash = movement.geometryProfileHash
        };
    }

    private ConflictKey MakeConflictKey(IntersectionMovement a, IntersectionMovement b)
    {
        MovementKey keyA = GetMovementKey(a);
        MovementKey keyB = GetMovementKey(b);

        // Canonical ordering makes A-vs-B and B-vs-A share one cache entry.
        if (CompareMovementKeys(keyA, keyB) <= 0)
            return new ConflictKey { a = keyA, b = keyB };
        return new ConflictKey { a = keyB, b = keyA };
    }

    private int CompareMovementKeys(MovementKey a, MovementKey b)
    {
        int compare = a.incomingLaneId.CompareTo(b.incomingLaneId);
        if (compare != 0)
            return compare;

        compare = a.outgoingLaneId.CompareTo(b.outgoingLaneId);
        if (compare != 0)
            return compare;

        return a.profileHash.CompareTo(b.profileHash);
    }

    private bool MovementsConflict(IntersectionMovement a, IntersectionMovement b)
    {
        if (a.conflictPath == null || a.conflictPath.Count < 2 ||
            b.conflictPath == null || b.conflictPath.Count < 2)
            return false;

        ConflictKey key = MakeConflictKey(a, b);
        if (conflictCache.TryGetValue(key, out bool cached))
            return cached;

        bool result = PathsConflict(a.conflictPath, b.conflictPath);
        conflictCache[key] = result;
        return result;
    }

    private bool PathsConflict(List<Vector3> pathA, List<Vector3> pathB)
    {
        float maxDistanceSquared = conflictDistance * conflictDistance;

        for (int a = 0; a < pathA.Count - 1; a++)
        {
            Vector3 a1 = pathA[a];
            Vector3 a2 = pathA[a + 1];

            for (int b = 0; b < pathB.Count - 1; b++)
            {
                float distanceSquared = SegmentDistanceSquaredXZ(a1, a2, pathB[b], pathB[b + 1]);
                if (distanceSquared <= maxDistanceSquared)
                    return true;
            }
        }
        return false;
    }

    private float SegmentDistanceSquaredXZ(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
    {
        Vector2 A1 = new Vector2(a1.x, a1.z);
        Vector2 A2 = new Vector2(a2.x, a2.z);
        Vector2 B1 = new Vector2(b1.x, b1.z);
        Vector2 B2 = new Vector2(b2.x, b2.z);

        if (SegmentsIntersect(A1, A2, B1, B2))
            return 0f;

        float d1 = PointSegmentDistanceSquared(A1, B1, B2);
        float d2 = PointSegmentDistanceSquared(A2, B1, B2);
        float d3 = PointSegmentDistanceSquared(B1, A1, A2);
        float d4 = PointSegmentDistanceSquared(B2, A1, A2);
        return Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4));
    }

    private bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float denominator = (b.x - a.x) * (d.y - c.y) - (b.y - a.y) * (d.x - c.x);
        if (Mathf.Abs(denominator) < 0.00001f)
            return false;

        float t = ((c.x - a.x) * (d.y - c.y) - (c.y - a.y) * (d.x - c.x)) / denominator;
        float u = ((c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x)) / denominator;
        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }

    private float PointSegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denominator = Vector2.Dot(ab, ab);
        if (denominator < 0.00001f)
            return (point - a).sqrMagnitude;

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
        Vector2 closest = a + ab * t;
        return (point - closest).sqrMagnitude;
    }

    private bool IsVehicleOnRight(Vector3 myIncomingDirection, Vector3 otherIncomingDirection)
    {
        myIncomingDirection.y = 0f;
        otherIncomingDirection.y = 0f;
        if (myIncomingDirection.sqrMagnitude < 0.001f || otherIncomingDirection.sqrMagnitude < 0.001f)
            return false;

        myIncomingDirection.Normalize();
        otherIncomingDirection.Normalize();
        Vector3 otherApproachSide = -otherIncomingDirection;
        Vector3 myRight = new Vector3(myIncomingDirection.z, 0f, -myIncomingDirection.x);
        return Vector3.Dot(myRight, otherApproachSide) > 0.35f;
    }

    private bool HasPriorityCycle(IntersectionMovement me, List<IntersectionMovement> movements)
    {
        // Same practical rule as before, but without temporary List allocations.
        for (int i = 0; i < movements.Count; i++)
        {
            IntersectionMovement candidate = movements[i];
            if (candidate.vehicle == null || candidate.insideIntersection)
                continue;

            // Only movements in the connected conflict group with 'me' matter.
            if (candidate != me && !MovementsConflict(me, candidate))
                continue;

            bool hasRightVehicle = false;
            for (int j = 0; j < movements.Count; j++)
            {
                IntersectionMovement other = movements[j];
                if (other == candidate || other.vehicle == null || other.insideIntersection)
                    continue;

                if (candidate != me && other != me && !MovementsConflict(candidate, other))
                    continue;
                if (candidate == me && !MovementsConflict(me, other))
                    continue;

                if (IsVehicleOnRight(candidate.incomingDirection, other.incomingDirection))
                {
                    hasRightVehicle = true;
                    break;
                }
            }

            if (!hasRightVehicle)
                return false;
        }
        return true;
    }
}
