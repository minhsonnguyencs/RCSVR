using System.Collections.Generic;
using UnityEngine;

public class IntersectionManager : MonoBehaviour
{
    [Header("Conflict Detection")]
    public float conflictDistance = 2.2f;

    /*
     * Vehicles registered at each graph node.
     */
    private readonly Dictionary<long, List<IntersectionMovement>>
        movementsByNode =
            new Dictionary<long, List<IntersectionMovement>>();


    public void RegisterApproach(
        long nodeId,
        TrafficAgentBase vehicle,
        Vector3 incomingDirection)
    {
        if (!movementsByNode.TryGetValue(
                nodeId,
                out List<IntersectionMovement> movements))
        {
            movements =
                new List<IntersectionMovement>();

            movementsByNode[nodeId] =
                movements;
        }

        /*
         * Don't register the same vehicle twice.
         */
        foreach (IntersectionMovement movement in movements)
        {
            if (movement.vehicle == vehicle)
                return;
        }

        List<Vector3> plannedPath =
            vehicle.GetPlannedIntersectionPath();

        movements.Add(
            new IntersectionMovement
            {
                vehicle = vehicle,

                nodeId = nodeId,

                incomingDirection =
                    incomingDirection.normalized,

                path = plannedPath,

                arrivalTime = Time.time,

                insideIntersection = false
            }
        );
    }


    public void UpdatePlannedPath(
        long nodeId,
        TrafficAgentBase vehicle)
    {
        if (!movementsByNode.TryGetValue(
                nodeId,
                out List<IntersectionMovement> movements))
        {
            return;
        }

        IntersectionMovement movement =
            movements.Find(
                m => m.vehicle == vehicle
            );

        if (movement == null)
            return;

        movement.path =
            vehicle.GetPlannedIntersectionPath();
    }


    public void UnregisterApproach(
        long nodeId,
        TrafficAgentBase vehicle)
    {
        if (!movementsByNode.TryGetValue(
                nodeId,
                out List<IntersectionMovement> movements))
        {
            return;
        }

        movements.RemoveAll(
            movement =>
                movement.vehicle == null
                || movement.vehicle == vehicle
        );

        if (movements.Count == 0)
        {
            movementsByNode.Remove(nodeId);
        }
    }


    public bool CanEnter(
        long nodeId,
        TrafficAgentBase vehicle)
    {
        if (!movementsByNode.TryGetValue(
                nodeId,
                out List<IntersectionMovement> movements))
        {
            return true;
        }

        movements.RemoveAll(
            movement =>
                movement.vehicle == null
        );

        IntersectionMovement me =
            movements.Find(
                movement =>
                    movement.vehicle == vehicle
            );

        if (me == null)
            return true;

        /*
         * If we don't yet have a path, refresh it.
         */
        if (me.path == null ||
            me.path.Count < 2)
        {
            me.path =
                vehicle.GetPlannedIntersectionPath();
        }

        /*
         * Compare only against movements that
         * geometrically conflict with ours.
         */
        List<IntersectionMovement> conflicts =
            new List<IntersectionMovement>();

        foreach (IntersectionMovement other in movements)
        {
            if (other == me ||
                other.vehicle == null)
            {
                continue;
            }

            if (other.path == null ||
                other.path.Count < 2)
            {
                continue;
            }

            if (PathsConflict(
                    me.path,
                    other.path))
            {
                conflicts.Add(other);
            }
        }

        /*
         * Nobody has a conflicting trajectory.
         *
         * We may enter even if other vehicles
         * are traversing the same intersection.
         */
        if (conflicts.Count == 0)
            return true;


        /*
         * A car already traversing a conflicting
         * movement always has priority.
         */
        foreach (IntersectionMovement other in conflicts)
        {
            if (other.insideIntersection)
                return false;
        }


        /*
         * Among approaching conflicting vehicles,
         * apply right-before-left.
         */
        List<IntersectionMovement> vehiclesOnRight =
            new List<IntersectionMovement>();

        foreach (IntersectionMovement other in conflicts)
        {
            if (IsVehicleOnRight(
                    me.incomingDirection,
                    other.incomingDirection))
            {
                vehiclesOnRight.Add(other);
            }
        }

        if (vehiclesOnRight.Count == 0)
        {
            /*
             * No conflicting vehicle on our right.
             *
             * If several vehicles qualify,
             * oldest arrival wins to avoid ambiguity.
             */
            List<IntersectionMovement> eligible =
                new List<IntersectionMovement>();

            eligible.Add(me);

            foreach (IntersectionMovement other in conflicts)
            {
                bool blocked =
                    IsVehicleOnRight(
                        other.incomingDirection,
                        me.incomingDirection
                    );

                if (!blocked)
                    eligible.Add(other);
            }

            IntersectionMovement oldest =
                GetOldest(eligible);

            return oldest == me;
        }


        /*
         * Normally, yield to the vehicle(s)
         * approaching from the right.
         *
         * However, if this creates a cycle,
         * break it by arrival order.
         */
        if (HasPriorityCycle(
                me,
                conflicts))
        {
            List<IntersectionMovement> cycle =
                new List<IntersectionMovement>(
                    conflicts
                );

            cycle.Add(me);

            IntersectionMovement oldest =
                GetOldest(cycle);

            return oldest == me;
        }

        return false;
    }


    public void EnterIntersection(
        long nodeId,
        TrafficAgentBase vehicle)
    {
        if (!movementsByNode.TryGetValue(
                nodeId,
                out List<IntersectionMovement> movements))
        {
            return;
        }

        IntersectionMovement movement =
            movements.Find(
                m => m.vehicle == vehicle
            );

        if (movement != null)
        {
            movement.insideIntersection = true;

            /*
             * Use the actual final curve rather
             * than an earlier preview.
             */
            movement.path =
                vehicle.GetPlannedIntersectionPath();
        }
    }


    public void LeaveIntersection(
        long nodeId,
        TrafficAgentBase vehicle)
    {
        UnregisterApproach(
            nodeId,
            vehicle
        );
    }


    private bool PathsConflict(
        List<Vector3> pathA,
        List<Vector3> pathB)
    {
        float maxDistanceSquared =
            conflictDistance
            * conflictDistance;

        /*
         * Compare each pair of small line segments.
         */
        for (int a = 0;
             a < pathA.Count - 1;
             a++)
        {
            Vector3 a1 =
                pathA[a];

            Vector3 a2 =
                pathA[a + 1];

            for (int b = 0;
                 b < pathB.Count - 1;
                 b++)
            {
                Vector3 b1 =
                    pathB[b];

                Vector3 b2 =
                    pathB[b + 1];

                float distanceSquared =
                    SegmentDistanceSquaredXZ(
                        a1,
                        a2,
                        b1,
                        b2
                    );

                if (distanceSquared <=
                    maxDistanceSquared)
                {
                    return true;
                }
            }
        }

        return false;
    }


    private float SegmentDistanceSquaredXZ(
        Vector3 a1,
        Vector3 a2,
        Vector3 b1,
        Vector3 b2)
    {
        /*
         * Intersection movements are horizontal,
         * so reduce everything to XZ.
         */
        Vector2 A1 =
            new Vector2(
                a1.x,
                a1.z
            );

        Vector2 A2 =
            new Vector2(
                a2.x,
                a2.z
            );

        Vector2 B1 =
            new Vector2(
                b1.x,
                b1.z
            );

        Vector2 B2 =
            new Vector2(
                b2.x,
                b2.z
            );

        /*
         * Actual line intersection means
         * distance is exactly zero.
         */
        if (SegmentsIntersect(
                A1,
                A2,
                B1,
                B2))
        {
            return 0f;
        }

        float d1 =
            PointSegmentDistanceSquared(
                A1,
                B1,
                B2
            );

        float d2 =
            PointSegmentDistanceSquared(
                A2,
                B1,
                B2
            );

        float d3 =
            PointSegmentDistanceSquared(
                B1,
                A1,
                A2
            );

        float d4 =
            PointSegmentDistanceSquared(
                B2,
                A1,
                A2
            );

        return Mathf.Min(
            Mathf.Min(d1, d2),
            Mathf.Min(d3, d4)
        );
    }


    private bool SegmentsIntersect(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d)
    {
        float denominator =
            (b.x - a.x)
            * (d.y - c.y)
            -
            (b.y - a.y)
            * (d.x - c.x);

        if (Mathf.Abs(denominator)
            < 0.00001f)
        {
            return false;
        }

        float t =
            (
                (c.x - a.x)
                * (d.y - c.y)
                -
                (c.y - a.y)
                * (d.x - c.x)
            )
            / denominator;

        float u =
            (
                (c.x - a.x)
                * (b.y - a.y)
                -
                (c.y - a.y)
                * (b.x - a.x)
            )
            / denominator;

        return
            t >= 0f &&
            t <= 1f &&
            u >= 0f &&
            u <= 1f;
    }


    private float PointSegmentDistanceSquared(
        Vector2 point,
        Vector2 a,
        Vector2 b)
    {
        Vector2 ab =
            b - a;

        float denominator =
            Vector2.Dot(
                ab,
                ab
            );

        if (denominator <
            0.00001f)
        {
            return (
                point - a
            ).sqrMagnitude;
        }

        float t =
            Vector2.Dot(
                point - a,
                ab
            )
            / denominator;

        t =
            Mathf.Clamp01(t);

        Vector2 closest =
            a + ab * t;

        return (
            point - closest
        ).sqrMagnitude;
    }


    private bool IsVehicleOnRight(
        Vector3 myIncomingDirection,
        Vector3 otherIncomingDirection)
    {
        myIncomingDirection.y = 0f;
        otherIncomingDirection.y = 0f;

        if (myIncomingDirection.sqrMagnitude <
                0.001f ||
            otherIncomingDirection.sqrMagnitude <
                0.001f)
        {
            return false;
        }

        myIncomingDirection.Normalize();
        otherIncomingDirection.Normalize();

        Vector3 otherApproachSide =
            -otherIncomingDirection;

        Vector3 myRight =
            new Vector3(
                myIncomingDirection.z,
                0f,
                -myIncomingDirection.x
            );

        float rightDot =
            Vector3.Dot(
                myRight,
                otherApproachSide
            );

        return rightDot > 0.35f;
    }


    private bool HasPriorityCycle(
        IntersectionMovement me,
        List<IntersectionMovement> conflicts)
    {
        /*
         * Simple practical cycle detection:
         *
         * if everyone in the conflicting group
         * has somebody else on their right,
         * there is no naturally eligible vehicle.
         */

        List<IntersectionMovement> group =
            new List<IntersectionMovement>(
                conflicts
            );

        group.Add(me);

        foreach (IntersectionMovement candidate
                 in group)
        {
            bool hasRightVehicle =
                false;

            foreach (IntersectionMovement other
                     in group)
            {
                if (candidate == other)
                    continue;

                if (IsVehicleOnRight(
                        candidate.incomingDirection,
                        other.incomingDirection))
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


    private IntersectionMovement GetOldest(
        List<IntersectionMovement> movements)
    {
        IntersectionMovement oldest =
            movements[0];

        foreach (IntersectionMovement movement
                 in movements)
        {
            if (movement.arrivalTime <
                oldest.arrivalTime)
            {
                oldest = movement;
            }
        }

        return oldest;
    }
}