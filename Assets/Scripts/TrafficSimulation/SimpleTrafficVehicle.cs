using System.Collections.Generic;
using UnityEngine;

public class SimpleTrafficVehicle : TrafficAgentBase
{
    [Header("Movement")]
    [Tooltip("Per-vehicle top speed. TrafficSpawner randomizes this at spawn time.")]
    public float topSpeedKmh = 36f;

    private float speed => topSpeedKmh / 3.6f;
    public float rotationSpeed = 120f;
    public float waypointTolerance = 0.5f;

    [Header("Position")]
    public float heightOffset = 0.7f;

    [Header("Intersection Turning")]
    public float turnStartDistance = 5f;
    public float turnEndDistance = 7f;
    public int turnCurvePoints = 8;

    [Header("Turn Dynamics")]
    public float minimumTurnSpeed = 4f;
    public float turnSlowdownStrength = 0.08f;
    public float rotationSafetyFactor = 1.4f;
    public float minimumTurnRotationSpeed = 120f;
    public float maximumTurnRotationSpeed = 300f;

    private RoadNetworkManager network;

    private Lane currentLane;
    private Lane nextLane;

    private int targetPointIndex;

    private bool isTurning = false;
    private List<Vector3> turnPath;
    private int turnPathIndex;

    private float currentTurnSpeed;
    private float currentTurnRotationSpeed;

    public override Lane CurrentLane => currentLane;
    public override Lane PlannedNextLane => nextLane;
    public override float CurrentLaneProgress =>
        isTurning && currentLane != null
            ? currentLane.totalLength
            : LanePathUtility.GetProgressOnLane(network, currentLane, targetPointIndex, transform.position);
    public override float CurrentSpeedMps => isTurning ? currentTurnSpeed : speed;
    public override float TopSpeedKmh => topSpeedKmh;

    public override void SetTopSpeedKmh(float speedKmh)
    {
        topSpeedKmh = Mathf.Max(1f, speedKmh);
    }


    public override void Initialize(
        RoadNetworkManager networkManager,
        IntersectionManager intersectionManager,
        Lane startingLane,
        int startingPointIndex = 0)
    {
        network = networkManager;
        currentLane = startingLane;

        if (currentLane == null ||
            currentLane.points == null ||
            currentLane.points.Count < 2)
        {
            Debug.LogError("Vehicle initialized with invalid lane.");
            return;
        }

        startingPointIndex = Mathf.Clamp(
            startingPointIndex,
            0,
            currentLane.points.Count - 2
        );

        transform.position =
            GetWorldPoint(
                currentLane.points[startingPointIndex]
            );

        targetPointIndex =
            startingPointIndex + 1;

        if (network != null && network.occupancyManager != null)
            network.occupancyManager.Register(this, currentLane);

        InitializeDestinationRouting(network, currentLane);

        FaceCurrentTarget();
    }


    void Update()
    {
        if (network == null ||
            currentLane == null)
        {
            return;
        }

        if (isTurning)
        {
            MoveAlongTurnPath();
        }
        else
        {
            MoveAlongCurrentLane();
        }
    }


    private void MoveAlongCurrentLane()
    {
        float remainingDistance =
            GetRemainingLaneDistance();

        if (remainingDistance <= turnStartDistance)
        {
            BeginTurn();
            return;
        }

        if (targetPointIndex >=
            currentLane.points.Count)
        {
            BeginTurn();
            return;
        }

        Vector3 target =
            GetWorldPoint(
                currentLane.points[targetPointIndex]
            );

        Vector3 difference =
            target - transform.position;

        difference.y = 0f;

        if (difference.magnitude <= waypointTolerance)
        {
            targetPointIndex++;
            return;
        }

        MoveToward(
            target,
            speed,
            rotationSpeed
        );
    }


    private float GetRemainingLaneDistance()
    {
        if (currentLane == null)
            return 0f;

        return Mathf.Max(0f, currentLane.totalLength - CurrentLaneProgress);
    }


    private void BeginTurn()
    {
        Lane chosenLane =
            ChooseNextLane();

        if (chosenLane == null)
        {
            return;
        }

        nextLane = chosenLane;

        float outgoingLength =
            LanePathUtility.GetLength(nextLane);

        /*
         * Prevent the turn target from lying beyond
         * a very short outgoing edge.
         */
        float actualEndDistance =
            Mathf.Min(
                turnEndDistance,
                outgoingLength * 0.45f
            );

        /*
         * Begin the curve exactly where the car
         * currently is.
         *
         * turnStartDistance controls WHEN we begin,
         * rather than forcing the car toward some
         * ideal point behind it.
         */
        Vector3 start =
            transform.position;

        Vector3 localEnd =
            LanePathUtility.GetPointAtDistanceFromStart(nextLane, actualEndDistance);

        Vector3 end =
            GetWorldPoint(localEnd);

        /*
         * Use the actual graph node as the control
         * point for the turn.
         */
        if (!network.nodesById.TryGetValue(
                currentLane.endNode,
                out RoadNodeData intersectionNode))
        {
            Debug.LogWarning(
                "Intersection node not found: "
                + currentLane.endNode
            );

            return;
        }

        Vector3 localIntersection =
            new Vector3(
                intersectionNode.position.x,
                intersectionNode.position.y,
                intersectionNode.position.z
            );

        Vector3 control =
            network.LanePointToWorld(
                localIntersection
            );

        control.y += heightOffset;

        turnPath =
            BuildQuadraticBezier(
                start,
                control,
                end,
                turnCurvePoints
            );

        CalculateTurnDynamics();

        turnPathIndex = 1;
        isTurning = true;
    }


    private Lane ChooseNextLane()
    {
        Lane routed = ChoosePathfindingNextLane(currentLane);
        if (routed != null)
            return routed;

        /*
         * Rare fallback for disconnected or malformed graph data. The normal
         * path is always A*. This keeps a vehicle from becoming permanently
         * frozen if no route can be constructed from an isolated component.
         */
        if (network == null || currentLane == null ||
            !network.lanesFromNode.TryGetValue(
                currentLane.endNode,
                out List<Lane> candidates) ||
            candidates == null || candidates.Count == 0)
        {
            return null;
        }

        Lane fallback = null;
        int eligible = 0;

        foreach (Lane candidate in candidates)
        {
            if (candidate == null)
                continue;

            if (candidate.endNode == currentLane.startNode &&
                candidates.Count > 1)
            {
                continue;
            }

            eligible++;
            if (Random.Range(0, eligible) == 0)
                fallback = candidate;
        }

        return fallback ?? candidates[0];
    }


    private void MoveAlongTurnPath()
    {
        if (turnPath == null ||
            turnPathIndex >= turnPath.Count)
        {
            FinishTurn();
            return;
        }

        Vector3 target =
            turnPath[turnPathIndex];

        Vector3 difference =
            target - transform.position;

        difference.y = 0f;

        if (difference.magnitude <= waypointTolerance)
        {
            turnPathIndex++;
            return;
        }

        MoveToward(
            target,
            currentTurnSpeed,
            currentTurnRotationSpeed
        );
    }


    private void FinishTurn()
    {
        if (nextLane == null)
        {
            isTurning = false;
            turnPath = null;
            return;
        }

        Lane previousLane = currentLane;
        currentLane = nextLane;
        nextLane = null;

        NotifyRouteLaneTransition(previousLane, currentLane);

        if (network != null && network.occupancyManager != null)
            network.occupancyManager.ChangeLane(this, previousLane, currentLane);

        isTurning = false;
        turnPath = null;
        turnPathIndex = 0;

        /*
         * Resume following the outgoing lane
         * from approximately turnEndDistance
         * beyond the intersection.
         */
        targetPointIndex =
            FindPointIndexAfterDistance(
                currentLane.points,
                turnEndDistance
            );

        targetPointIndex =
            Mathf.Clamp(
                targetPointIndex,
                1,
                currentLane.points.Count - 1
            );
    }


    private int FindPointIndexAfterDistance(
        List<Vector3> points,
        float distance)
    {
        if (points == null ||
            points.Count < 2)
        {
            return 0;
        }

        float travelled = 0f;

        for (int i = 0;
             i < points.Count - 1;
             i++)
        {
            travelled +=
                Vector3.Distance(
                    points[i],
                    points[i + 1]
                );

            if (travelled >= distance)
            {
                return i + 1;
            }
        }

        return points.Count - 1;
    }


    private void MoveToward(
        Vector3 target,
        float movementSpeed,
        float angularSpeed)
    {
        Vector3 difference =
            target - transform.position;

        difference.y = 0f;

        if (difference.sqrMagnitude < 0.0001f)
            return;

        Vector3 direction =
            difference.normalized;

        transform.position +=
            direction
            * movementSpeed
            * Time.deltaTime;

        Quaternion desiredRotation =
            Quaternion.LookRotation(
                direction,
                Vector3.up
            );

        transform.rotation =
            Quaternion.RotateTowards(
                transform.rotation,
                desiredRotation,
                angularSpeed
                * Time.deltaTime
            );
    }


    private void FaceCurrentTarget()
    {
        if (currentLane == null ||
            targetPointIndex >=
            currentLane.points.Count)
        {
            return;
        }

        Vector3 target =
            GetWorldPoint(
                currentLane.points[targetPointIndex]
            );

        Vector3 direction =
            target - transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation =
                Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up
                );
        }
    }


    private Vector3 GetWorldPoint(
        Vector3 localLanePoint)
    {
        Vector3 worldPoint =
            network.LanePointToWorld(
                localLanePoint
            );

        worldPoint.y += heightOffset;

        return worldPoint;
    }


    private List<Vector3> BuildQuadraticBezier(
        Vector3 start,
        Vector3 control,
        Vector3 end,
        int pointCount)
    {
        pointCount =
            Mathf.Max(pointCount, 3);

        List<Vector3> result =
            new List<Vector3>(pointCount);

        for (int i = 0;
             i < pointCount;
             i++)
        {
            float t =
                i / (float)(pointCount - 1);

            float oneMinusT =
                1f - t;

            Vector3 point =
                oneMinusT * oneMinusT * start
                + 2f
                * oneMinusT
                * t
                * control
                + t * t * end;

            result.Add(point);
        }

        return result;
    }

    private void CalculateTurnDynamics()
    {
        if (turnPath == null || turnPath.Count < 3)
        {
            currentTurnSpeed = speed;
            currentTurnRotationSpeed = minimumTurnRotationSpeed;
            return;
        }

        // Direction when entering the turn
        Vector3 incomingDirection =
            turnPath[1] - turnPath[0];

        // Direction when leaving the turn
        Vector3 outgoingDirection =
            turnPath[turnPath.Count - 1]
            - turnPath[turnPath.Count - 2];

        incomingDirection.y = 0f;
        outgoingDirection.y = 0f;

        incomingDirection.Normalize();
        outgoingDirection.Normalize();

        // Total heading change
        float turnAngle =
            Vector3.Angle(
                incomingDirection,
                outgoingDirection
            );

        // Approximate physical length of the turn connector
        float turnLength = 0f;

        for (int i = 0; i < turnPath.Count - 1; i++)
        {
            turnLength += Vector3.Distance(
                turnPath[i],
                turnPath[i + 1]
            );
        }

        turnLength = Mathf.Max(turnLength, 0.1f);

        // Degrees of rotation required per metre travelled
        float severity =
            turnAngle / turnLength;

        /*
         * More severe turns get a lower forward speed.
         *
         * Example:
         * gentle curve -> close to normal speed
         * tight corner -> approaches minimumTurnSpeed
         */
        currentTurnSpeed =
            speed / (
                1f
                + severity * turnSlowdownStrength
            );

        currentTurnSpeed =
            Mathf.Clamp(
                currentTurnSpeed,
                minimumTurnSpeed,
                speed
            );

        /*
         * severity = degrees / metre
         * speed    = metres / second
         *
         * Therefore:
         *
         * severity * speed = degrees / second
         *
         * which is approximately the angular velocity
         * required to follow this curve.
         */
        float requiredRotationSpeed =
            severity
            * currentTurnSpeed
            * rotationSafetyFactor;

        currentTurnRotationSpeed =
            Mathf.Clamp(
                requiredRotationSpeed,
                minimumTurnRotationSpeed,
                maximumTurnRotationSpeed
            );
    }
    void OnDestroy()
    {
        if (network != null && network.occupancyManager != null)
            network.occupancyManager.Unregister(this, currentLane);
    }

}