using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Traffic agent with:
/// - smooth acceleration/deceleration
/// - lane-aware vehicle following
/// - conflict-aware intersection yielding
/// - "do not block the intersection" check for the outgoing lane
/// - smooth Bezier turn connectors
/// - timed deadlock escape as a last-resort recovery mechanism
///
/// This class intentionally does NOT use Physics casts for vehicle following.
/// Vehicles only react to other agents travelling on the same logical Lane.
/// </summary>
public class AwareTrafficAgent_DeadlockEscape : TrafficAgentBase
{
    [Header("Cruising")]
    public float cruiseSpeed = 12f;

    [Header("Acceleration")]
    public float acceleration = 3f;
    public float comfortableDeceleration = 5f;
    public float emergencyDeceleration = 10f;

    [Header("Rotation")]
    public float normalRotationSpeed = 120f;
    public float minimumTurnRotationSpeed = 120f;
    public float maximumTurnRotationSpeed = 320f;
    public float rotationSafetyFactor = 1.5f;

    [Header("Intersection Geometry")]
    public float turnStartDistance = 5f;
    public float turnEndDistance = 7f;
    public int turnCurvePoints = 10;

    [Header("Turn Speed")]
    public float minimumTurnSpeed = 4f;
    public float turnSlowdownStrength = 0.08f;

    [Header("Intersection Awareness")]
    public float intersectionAwarenessDistance = 20f;
    public float stopLineDistance = 6f;
    public float stopLineTolerance = 0.25f;

    [Header("Lane-Aware Following")]
    public float vehicleLength = 4f;
    public float minimumGap = 3f;
    public float timeHeadway = 1.3f;

    [Tooltip("Small tolerance used when two agents are at essentially the same lane position. "
           + "A deterministic tie-break prevents both agents from treating the other as 'behind'.")]
    public float progressTieTolerance = 0.1f;

    [Header("Deadlock Escape")]
    [Tooltip("How long the vehicle must remain almost stationary before temporary deadlock escape activates.")]
    public float deadlockTimeout = 30f;

    [Tooltip("How long intersection priority and outgoing-lane blocking rules are ignored once deadlock escape activates.")]
    public float deadlockEscapeDuration = 8f;

    [Tooltip("Maximum speed used while forcing the vehicle through a deadlock.")]
    public float deadlockEscapeSpeed = 3f;

    [Tooltip("A vehicle is considered stationary below this speed.")]
    public float stationarySpeedThreshold = 0.15f;

    [Tooltip("During deadlock escape, do not move if another vehicle is this close in front.")]
    public float deadlockDangerDistance = 5f;

    [Tooltip("Half-width of the forward danger corridor used during deadlock escape.")]
    public float deadlockDangerLateralDistance = 2f;

    [Header("Position")]
    public float heightOffset = 0.7f;
    public float waypointTolerance = 0.4f;

    [Header("Debug")]
    public bool vehicleAhead = false;
    public float detectedGap = -1f;
    public float debugDesiredSpeed = 0f;
    public string detectedVehicle = "None";
    public bool debugIntersectionAllowed = true;
    public bool debugOutgoingLaneClear = true;
    public bool debugDeadlockEscapeActive = false;
    public float debugStationaryTime = 0f;

    private RoadNetworkManager network;
    private IntersectionManager intersectionManager;

    private Lane currentLane;
    private Lane nextLane;

    private int targetPointIndex;

    private float currentSpeed = 0f;
    private float desiredSpeed = 0f;

    private bool isTurning = false;

    private List<Vector3> turnPath;
    private int turnPathIndex;

    private float currentTurnSpeed;
    private float currentTurnRotationSpeed;

    private bool registeredAtIntersection = false;
    private bool insideIntersection = false;
    private long activeIntersectionNode = -1;

    private float stationaryTimer = 0f;
    private float deadlockEscapeTimer = 0f;

    /*
     * Global lightweight registry of traffic agents.
     *
     * This lets us query logical lane occupancy without using Physics casts.
     * For the current prototype this keeps all following behavior inside this
     * class, so RoadNetworkManager does not need to be modified.
     */
    private static readonly HashSet<AwareTrafficAgent_DeadlockEscape> activeAgents =
        new HashSet<AwareTrafficAgent_DeadlockEscape>();


    public override void Initialize(
        RoadNetworkManager networkManager,
        IntersectionManager manager,
        Lane startingLane,
        int startingPointIndex = 0)
    {
        network = networkManager;
        intersectionManager = manager;
        currentLane = startingLane;

        if (currentLane == null ||
            currentLane.points == null ||
            currentLane.points.Count < 2)
        {
            Debug.LogError(
                "AwareTrafficAgent_DeadlockEscape initialized with invalid lane."
            );

            enabled = false;
            return;
        }

        startingPointIndex =
            Mathf.Clamp(
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

        currentSpeed = 0f;
        desiredSpeed = cruiseSpeed;

        activeAgents.Add(this);

        FaceCurrentTarget();
    }


    void Update()
    {
        if (network == null ||
            currentLane == null)
        {
            return;
        }

        UpdateDeadlockEscapeState();

        if (isTurning)
        {
            UpdateTurnDesiredSpeed();
            UpdateCurrentSpeed();
            MoveAlongTurnPath();
        }
        else
        {
            UpdateLaneDesiredSpeed();
            UpdateCurrentSpeed();
            MoveAlongCurrentLane();
        }

        debugDesiredSpeed = desiredSpeed;
        debugDeadlockEscapeActive = IsDeadlockEscapeActive();
        debugStationaryTime = stationaryTimer;
    }


    private void UpdateLaneDesiredSpeed()
    {
        desiredSpeed = cruiseSpeed;

        float remainingDistance =
            GetRemainingLaneDistance();

        /*
         * Choose the next movement BEFORE registering with the intersection
         * manager, because conflict detection needs the planned connector.
         */
        if (remainingDistance <=
            intersectionAwarenessDistance)
        {
            if (nextLane == null)
            {
                nextLane =
                    ChooseNextLane();

                if (nextLane != null)
                {
                    CalculateTurnDynamicsPreview(
                        nextLane
                    );
                }
            }

            RegisterAtIntersectionIfNeeded();

            if (registeredAtIntersection &&
                intersectionManager != null)
            {
                intersectionManager.UpdatePlannedPath(
                    activeIntersectionNode,
                    this
                );
            }
        }

        /*
         * Approach the turn at an appropriate speed instead of snapping
         * immediately to the turn speed.
         */
        if (nextLane != null)
        {
            float distanceToTurnStart =
                Mathf.Max(
                    0f,
                    remainingDistance
                    - turnStartDistance
                );

            float maxSpeedForTurn =
                SpeedToReachTargetSpeed(
                    currentTurnSpeed,
                    distanceToTurnStart,
                    comfortableDeceleration
                );

            desiredSpeed =
                Mathf.Min(
                    desiredSpeed,
                    maxSpeedForTurn
                );
        }

        /*
         * Intersection permission and downstream-space check.
         *
         * A vehicle waits at the stop line if:
         *  1) its planned movement conflicts and it does not have priority, OR
         *  2) there is not enough free space on the outgoing lane.
         *
         * The second rule prevents a queue from extending into the intersection.
         */
        if (registeredAtIntersection &&
            !insideIntersection &&
            !IsDeadlockEscapeActive())
        {
            debugIntersectionAllowed =
                intersectionManager == null
                || intersectionManager.CanEnter(
                    activeIntersectionNode,
                    this
                );

            debugOutgoingLaneClear =
                nextLane == null
                || HasOutgoingLaneSpace(
                    nextLane
                );

            bool allowed =
                debugIntersectionAllowed
                && debugOutgoingLaneClear;

            if (!allowed)
            {
                float distanceToStopLine =
                    remainingDistance
                    - stopLineDistance;

                if (distanceToStopLine <=
                    stopLineTolerance)
                {
                    desiredSpeed = 0f;
                }
                else
                {
                    float allowedSpeed =
                        SpeedForStoppingDistance(
                            distanceToStopLine
                        );

                    desiredSpeed =
                        Mathf.Min(
                            desiredSpeed,
                            allowedSpeed
                        );
                }
            }
        }
        else
        {
            debugIntersectionAllowed = true;
            debugOutgoingLaneClear = true;
        }

        /*
         * During deadlock escape, ignore normal intersection/outgoing-lane
         * blocking. Move forward slowly unless another vehicle is dangerously
         * close directly ahead.
         */
        if (IsDeadlockEscapeActive())
        {
            desiredSpeed =
                IsDangerouslyBlockedAhead()
                    ? 0f
                    : Mathf.Min(
                        cruiseSpeed,
                        deadlockEscapeSpeed
                    );
        }
        else
        {
            /*
             * Lane-aware following.
             *
             * Only a vehicle physically ahead on THIS logical lane can constrain
             * our speed. Cars on crossing lanes or waiting beside the intersection
             * are ignored.
             */
            desiredSpeed =
                Mathf.Min(
                    desiredSpeed,
                    GetLaneFollowingSpeedLimit()
                );
        }

    }


    private void UpdateTurnDesiredSpeed()
    {
        /*
         * Do NOT apply ordinary lane-following while traversing the
         * intersection connector. Cross-traffic is normally governed by the
         * IntersectionManager.
         *
         * During deadlock escape, continue through the connector slowly unless
         * there is a vehicle dangerously close directly ahead.
         */
        if (IsDeadlockEscapeActive())
        {
            desiredSpeed =
                IsDangerouslyBlockedAhead()
                    ? 0f
                    : Mathf.Min(
                        currentTurnSpeed,
                        deadlockEscapeSpeed
                    );
        }
        else
        {
            desiredSpeed =
                currentTurnSpeed;
        }

        vehicleAhead = false;
        detectedGap = -1f;
        detectedVehicle = "None";
    }


    private void UpdateCurrentSpeed()
    {
        if (currentSpeed <
            desiredSpeed)
        {
            currentSpeed =
                Mathf.MoveTowards(
                    currentSpeed,
                    desiredSpeed,
                    acceleration
                    * Time.deltaTime
                );
        }
        else
        {
            float decelerationRate =
                desiredSpeed <= 0.1f
                    ? emergencyDeceleration
                    : comfortableDeceleration;

            currentSpeed =
                Mathf.MoveTowards(
                    currentSpeed,
                    desiredSpeed,
                    decelerationRate
                    * Time.deltaTime
                );
        }
    }


    private void MoveAlongCurrentLane()
    {
        float remainingDistance =
            GetRemainingLaneDistance();

        /*
         * Hard stop-line protection.
         */
        if (registeredAtIntersection &&
            !insideIntersection &&
            !IsDeadlockEscapeActive())
        {
            bool intersectionAllowed =
                intersectionManager == null
                || intersectionManager.CanEnter(
                    activeIntersectionNode,
                    this
                );

            bool outgoingClear =
                nextLane == null
                || HasOutgoingLaneSpace(
                    nextLane
                );

            if ((!intersectionAllowed ||
                 !outgoingClear) &&
                remainingDistance <=
                stopLineDistance
                + stopLineTolerance)
            {
                currentSpeed = 0f;
                desiredSpeed = 0f;
                return;
            }
        }

        /*
         * Start the connector only when both the intersection and the receiving
         * lane are safe.
         */
        if (remainingDistance <=
            turnStartDistance)
        {
            if (nextLane == null)
            {
                nextLane =
                    ChooseNextLane();

                if (nextLane != null)
                {
                    CalculateTurnDynamicsPreview(
                        nextLane
                    );
                }
            }

            if (nextLane != null)
            {
                bool forceThrough =
                    IsDeadlockEscapeActive()
                    && !IsDangerouslyBlockedAhead();

                bool intersectionAllowed =
                    forceThrough
                    || intersectionManager == null
                    || intersectionManager.CanEnter(
                        currentLane.endNode,
                        this
                    );

                bool outgoingClear =
                    forceThrough
                    || HasOutgoingLaneSpace(
                        nextLane
                    );

                if (intersectionAllowed &&
                    outgoingClear)
                {
                    BeginTurn();
                    return;
                }
            }
        }

        if (targetPointIndex >=
            currentLane.points.Count)
        {
            return;
        }

        Vector3 target =
            GetWorldPoint(
                currentLane.points[
                    targetPointIndex
                ]
            );

        Vector3 difference =
            target
            - transform.position;

        difference.y = 0f;

        if (difference.magnitude <=
            waypointTolerance)
        {
            targetPointIndex++;
            return;
        }

        MoveToward(
            target,
            currentSpeed,
            normalRotationSpeed
        );
    }


    private void BeginTurn()
    {
        if (nextLane == null)
            return;

        if (intersectionManager != null)
        {
            intersectionManager.EnterIntersection(
                currentLane.endNode,
                this
            );
        }

        insideIntersection = true;
        activeIntersectionNode =
            currentLane.endNode;

        float outgoingLength =
            LanePathUtility.GetLength(
                nextLane.points
            );

        float actualEndDistance =
            Mathf.Min(
                turnEndDistance,
                outgoingLength * 0.45f
            );

        Vector3 start =
            transform.position;

        Vector3 localEnd =
            LanePathUtility
                .GetPointAtDistanceFromStart(
                    nextLane.points,
                    actualEndDistance
                );

        Vector3 end =
            GetWorldPoint(localEnd);

        if (!network.nodesById.TryGetValue(
                currentLane.endNode,
                out RoadNodeData intersectionNode))
        {
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

        control.y +=
            heightOffset;

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

        /*
         * Do not unregister here.
         *
         * IntersectionManager.EnterIntersection() marks our registered
         * movement as being inside the intersection. It remains available
         * for geometric conflict checks until FinishTurn().
         */
        registeredAtIntersection = false;
    }


    private void MoveAlongTurnPath()
    {
        if (turnPath == null ||
            turnPathIndex >=
            turnPath.Count)
        {
            FinishTurn();
            return;
        }

        Vector3 target =
            turnPath[turnPathIndex];

        Vector3 difference =
            target
            - transform.position;

        difference.y = 0f;

        if (difference.magnitude <=
            waypointTolerance)
        {
            turnPathIndex++;
            return;
        }

        MoveToward(
            target,
            currentSpeed,
            currentTurnRotationSpeed
        );
    }


    private void FinishTurn()
    {
        if (intersectionManager != null &&
            insideIntersection)
        {
            intersectionManager
                .LeaveIntersection(
                    activeIntersectionNode,
                    this
                );
        }

        currentLane =
            nextLane;

        nextLane = null;

        insideIntersection = false;
        registeredAtIntersection = false;
        activeIntersectionNode = -1;

        isTurning = false;

        turnPath = null;
        turnPathIndex = 0;

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


    private void RegisterAtIntersectionIfNeeded()
    {
        if (registeredAtIntersection ||
            insideIntersection ||
            intersectionManager == null ||
            nextLane == null)
        {
            return;
        }

        activeIntersectionNode =
            currentLane.endNode;

        intersectionManager.RegisterApproach(
            activeIntersectionNode,
            this,
            GetIncomingDirection()
        );

        registeredAtIntersection = true;
    }


    private Vector3 GetIncomingDirection()
    {
        List<Vector3> points =
            currentLane.points;

        if (points.Count < 2)
            return transform.forward;

        Vector3 a =
            network.LanePointToWorld(
                points[points.Count - 2]
            );

        Vector3 b =
            network.LanePointToWorld(
                points[points.Count - 1]
            );

        Vector3 direction =
            b - a;

        direction.y = 0f;

        return direction.normalized;
    }


    /*
     * --------------------------
     * Deadlock escape
     * --------------------------
     */

    private void UpdateDeadlockEscapeState()
    {
        /*
         * Count only genuine stationary time. This deliberately does not try
         * to diagnose WHY the vehicle is stopped: the escape mechanism is a
         * last-resort recovery rule.
         */
        if (currentSpeed <= stationarySpeedThreshold)
        {
            stationaryTimer += Time.deltaTime;
        }
        else
        {
            stationaryTimer = 0f;
        }

        if (deadlockEscapeTimer > 0f)
        {
            deadlockEscapeTimer -= Time.deltaTime;

            if (deadlockEscapeTimer <= 0f)
            {
                deadlockEscapeTimer = 0f;
                stationaryTimer = 0f;
            }

            return;
        }

        if (stationaryTimer >= deadlockTimeout)
        {
            deadlockEscapeTimer =
                Mathf.Max(
                    0.1f,
                    deadlockEscapeDuration
                );

            stationaryTimer = 0f;
        }
    }


    private bool IsDeadlockEscapeActive()
    {
        return deadlockEscapeTimer > 0f;
    }


    private bool IsDangerouslyBlockedAhead()
    {
        /*
         * This is intentionally geometric rather than lane-aware.
         *
         * During emergency deadlock escape we may be entering an intersection,
         * so a dangerous vehicle can be on a crossing movement rather than on
         * our current Lane.
         */
        Vector3 forward =
            transform.forward;

        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            return false;

        forward.Normalize();

        Vector3 right =
            new Vector3(
                forward.z,
                0f,
                -forward.x
            );

        foreach (AwareTrafficAgent_DeadlockEscape other
                 in activeAgents)
        {
            if (other == null ||
                other == this ||
                !other.isActiveAndEnabled ||
                other.network != network)
            {
                continue;
            }

            Vector3 delta =
                other.transform.position
                - transform.position;

            delta.y = 0f;

            float forwardDistance =
                Vector3.Dot(
                    delta,
                    forward
                );

            if (forwardDistance <= 0f ||
                forwardDistance >
                deadlockDangerDistance)
            {
                continue;
            }

            float lateralDistance =
                Mathf.Abs(
                    Vector3.Dot(
                        delta,
                        right
                    )
                );

            if (lateralDistance <=
                deadlockDangerLateralDistance)
            {
                vehicleAhead = true;
                detectedGap =
                    Mathf.Max(
                        0f,
                        forwardDistance
                        - vehicleLength
                    );
                detectedVehicle =
                    other.name;

                return true;
            }
        }

        return false;
    }


    /*
     * --------------------------
     * Lane-aware vehicle following
     * --------------------------
     */

    private float GetLaneFollowingSpeedLimit()
    {
        vehicleAhead = false;
        detectedGap = -1f;
        detectedVehicle = "None";

        if (currentLane == null)
            return cruiseSpeed;

        float myProgress =
            GetProgressOnCurrentLane();

        AwareTrafficAgent_DeadlockEscape leader = null;
        float leaderProgress =
            float.PositiveInfinity;

        foreach (AwareTrafficAgent_DeadlockEscape other
                 in activeAgents)
        {
            if (other == null ||
                other == this ||
                !other.isActiveAndEnabled ||
                other.network != network ||
                other.currentLane != currentLane)
            {
                continue;
            }

            float otherProgress =
                other.GetProgressOnCurrentLane();

            float delta =
                otherProgress - myProgress;

            /*
             * Normal case: the other vehicle is clearly ahead.
             */
            bool isAhead =
                delta > progressTieTolerance;

            /*
             * If two vehicles somehow occupy almost exactly the same lane
             * position, use a deterministic tie-break. This prevents the
             * pathological case where BOTH cars decide that neither is ahead
             * and continue overlapping forever.
             */
            if (!isAhead &&
                Mathf.Abs(delta) <=
                progressTieTolerance)
            {
                isAhead =
                    other.GetInstanceID()
                    < GetInstanceID();

                if (isAhead)
                {
                    delta =
                        progressTieTolerance;
                }
            }

            if (!isAhead)
                continue;

            if (otherProgress <
                leaderProgress)
            {
                leaderProgress =
                    otherProgress;

                leader = other;
            }
        }

        if (leader == null)
        {
            return cruiseSpeed;
        }

        float centreDistance =
            Mathf.Max(
                0f,
                leaderProgress
                - myProgress
            );

        /*
         * Convert centre-to-centre separation into an approximate clear gap.
         */
        float gap =
            Mathf.Max(
                0f,
                centreDistance
                - vehicleLength
            );

        vehicleAhead = true;
        detectedGap = gap;
        detectedVehicle = leader.name;

        float desiredGap =
            minimumGap
            + currentSpeed
            * timeHeadway;

        /*
         * Emergency spacing.
         */
        if (gap <= minimumGap)
        {
            return 0f;
        }

        /*
         * Plenty of room.
         */
        if (gap >= desiredGap)
        {
            return cruiseSpeed;
        }

        /*
         * Simple speed-matching controller:
         *
         * - approach the leader's speed as the gap shrinks
         * - run slightly slower than the leader when the gap is below target
         * - allow cruise speed once the gap is sufficiently large
         */
        float gapError =
            gap - desiredGap;

        float correction =
            gapError /
            Mathf.Max(
                timeHeadway,
                0.1f
            );

        float permittedSpeed =
            leader.currentSpeed
            + correction;

        return Mathf.Clamp(
            permittedSpeed,
            0f,
            cruiseSpeed
        );
    }


    private bool HasOutgoingLaneSpace(
        Lane outgoingLane)
    {
        if (outgoingLane == null)
            return false;

        float nearestProgress =
            float.PositiveInfinity;

        foreach (AwareTrafficAgent_DeadlockEscape other
                 in activeAgents)
        {
            if (other == null ||
                other == this ||
                !other.isActiveAndEnabled ||
                other.network != network ||
                other.currentLane != outgoingLane)
            {
                continue;
            }

            float progress =
                other.GetProgressOnCurrentLane();

            if (progress <
                nearestProgress)
            {
                nearestProgress =
                    progress;
            }
        }

        if (float.IsPositiveInfinity(
                nearestProgress))
        {
            return true;
        }

        /*
         * Our connector ends roughly turnEndDistance down the outgoing lane.
         *
         * Require enough empty space beyond that merge point for one vehicle
         * length plus the desired stationary gap.
         */
        float requiredFrontPosition =
            turnEndDistance
            + vehicleLength
            + minimumGap;

        return nearestProgress >=
            requiredFrontPosition;
    }


    private float GetProgressOnCurrentLane()
    {
        if (currentLane == null ||
            currentLane.points == null ||
            currentLane.points.Count < 2)
        {
            return 0f;
        }

        /*
         * While turning, consider this vehicle to be at the end of its incoming
         * lane. This makes followers queue behind the intersection rather than
         * trying to occupy the same end-of-lane position.
         */
        if (isTurning)
        {
            return LanePathUtility.GetLength(
                currentLane.points
            );
        }

        int segmentEndIndex =
            Mathf.Clamp(
                targetPointIndex,
                1,
                currentLane.points.Count - 1
            );

        int segmentStartIndex =
            segmentEndIndex - 1;

        float progress = 0f;

        /*
         * Full completed segments.
         */
        for (int i = 0;
             i < segmentStartIndex;
             i++)
        {
            progress +=
                Vector3.Distance(
                    currentLane.points[i],
                    currentLane.points[i + 1]
                );
        }

        /*
         * Project the current world position onto the current lane segment.
         * We do this in world coordinates so RoadNetwork's alignment transform
         * is handled correctly.
         */
        Vector3 a =
            GetWorldPoint(
                currentLane.points[
                    segmentStartIndex
                ]
            );

        Vector3 b =
            GetWorldPoint(
                currentLane.points[
                    segmentEndIndex
                ]
            );

        a.y = 0f;
        b.y = 0f;

        Vector3 p =
            transform.position;

        p.y = 0f;

        Vector3 ab =
            b - a;

        float segmentLength =
            ab.magnitude;

        if (segmentLength >
            0.0001f)
        {
            float t =
                Vector3.Dot(
                    p - a,
                    ab
                )
                / (
                    segmentLength
                    * segmentLength
                );

            t =
                Mathf.Clamp01(t);

            progress +=
                segmentLength
                * t;
        }

        return progress;
    }


    /*
     * --------------------------
     * Turn geometry / dynamics
     * --------------------------
     */

    private float SpeedForStoppingDistance(
        float distance)
    {
        if (distance <= 0f)
            return 0f;

        return Mathf.Sqrt(
            2f
            * comfortableDeceleration
            * distance
        );
    }


    private float SpeedToReachTargetSpeed(
        float targetSpeed,
        float distance,
        float deceleration)
    {
        if (distance <= 0f)
            return targetSpeed;

        return Mathf.Sqrt(
            targetSpeed * targetSpeed
            + 2f
            * deceleration
            * distance
        );
    }


    private void CalculateTurnDynamicsPreview(
        Lane candidateLane)
    {
        List<Vector3> preview =
            BuildTurnPathPreview(
                candidateLane
            );

        CalculateTurnDynamicsFromPath(
            preview,
            out currentTurnSpeed,
            out currentTurnRotationSpeed
        );
    }


    private void CalculateTurnDynamics()
    {
        CalculateTurnDynamicsFromPath(
            turnPath,
            out currentTurnSpeed,
            out currentTurnRotationSpeed
        );
    }


    private void CalculateTurnDynamicsFromPath(
        List<Vector3> path,
        out float calculatedSpeed,
        out float calculatedRotationSpeed)
    {
        calculatedSpeed =
            cruiseSpeed;

        calculatedRotationSpeed =
            minimumTurnRotationSpeed;

        if (path == null ||
            path.Count < 3)
        {
            return;
        }

        Vector3 incomingDirection =
            path[1]
            - path[0];

        Vector3 outgoingDirection =
            path[path.Count - 1]
            - path[path.Count - 2];

        incomingDirection.y = 0f;
        outgoingDirection.y = 0f;

        incomingDirection.Normalize();
        outgoingDirection.Normalize();

        float turnAngle =
            Vector3.Angle(
                incomingDirection,
                outgoingDirection
            );

        float turnLength = 0f;

        for (int i = 0;
             i < path.Count - 1;
             i++)
        {
            turnLength +=
                Vector3.Distance(
                    path[i],
                    path[i + 1]
                );
        }

        turnLength =
            Mathf.Max(
                turnLength,
                0.1f
            );

        float severity =
            turnAngle
            / turnLength;

        calculatedSpeed =
            cruiseSpeed /
            (
                1f
                + severity
                * turnSlowdownStrength
            );

        calculatedSpeed =
            Mathf.Clamp(
                calculatedSpeed,
                minimumTurnSpeed,
                cruiseSpeed
            );

        float requiredRotationSpeed =
            severity
            * calculatedSpeed
            * rotationSafetyFactor;

        calculatedRotationSpeed =
            Mathf.Clamp(
                requiredRotationSpeed,
                minimumTurnRotationSpeed,
                maximumTurnRotationSpeed
            );
    }


    private List<Vector3> BuildTurnPathPreview(
        Lane candidateLane)
    {
        float outgoingLength =
            LanePathUtility.GetLength(
                candidateLane.points
            );

        float actualEndDistance =
            Mathf.Min(
                turnEndDistance,
                outgoingLength * 0.45f
            );

        Vector3 start =
            transform.position;

        Vector3 localEnd =
            LanePathUtility
                .GetPointAtDistanceFromStart(
                    candidateLane.points,
                    actualEndDistance
                );

        Vector3 end =
            GetWorldPoint(
                localEnd
            );

        RoadNodeData intersectionNode =
            network.nodesById[
                currentLane.endNode
            ];

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

        control.y +=
            heightOffset;

        return BuildQuadraticBezier(
            start,
            control,
            end,
            turnCurvePoints
        );
    }


    public override List<Vector3> GetPlannedIntersectionPath()
    {
        if (nextLane == null)
            return null;

        /*
         * Once actually turning, return the exact connector currently being
         * followed. Otherwise return the current preview.
         */
        if (isTurning &&
            turnPath != null)
        {
            return turnPath;
        }

        return BuildTurnPathPreview(
            nextLane
        );
    }


    public override Vector3 GetApproachDirection()
    {
        return GetIncomingDirection();
    }


    private Lane ChooseNextLane()
    {
        long intersectionNode =
            currentLane.endNode;

        long previousNode =
            currentLane.startNode;

        if (!network.lanesFromNode.TryGetValue(
                intersectionNode,
                out List<Lane> candidates))
        {
            return null;
        }

        if (candidates == null ||
            candidates.Count == 0)
        {
            return null;
        }

        List<Lane> validCandidates =
            candidates.FindAll(
                lane =>
                    lane.endNode
                    != previousNode
            );

        if (validCandidates.Count == 0)
            validCandidates = candidates;

        return validCandidates[
            Random.Range(
                0,
                validCandidates.Count
            )
        ];
    }


    /*
     * --------------------------
     * Lane traversal
     * --------------------------
     */

    private float GetRemainingLaneDistance()
    {
        if (targetPointIndex >=
            currentLane.points.Count)
        {
            return 0f;
        }

        Vector3 target =
            GetWorldPoint(
                currentLane.points[
                    targetPointIndex
                ]
            );

        float remaining =
            Vector3.Distance(
                transform.position,
                target
            );

        for (int i = targetPointIndex;
             i < currentLane.points.Count - 1;
             i++)
        {
            Vector3 a =
                GetWorldPoint(
                    currentLane.points[i]
                );

            Vector3 b =
                GetWorldPoint(
                    currentLane.points[i + 1]
                );

            remaining +=
                Vector3.Distance(
                    a,
                    b
                );
        }

        return remaining;
    }


    private void MoveToward(
        Vector3 target,
        float movementSpeed,
        float angularSpeed)
    {
        Vector3 difference =
            target
            - transform.position;

        difference.y = 0f;

        if (difference.sqrMagnitude <
            0.0001f)
        {
            return;
        }

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
        if (targetPointIndex >=
            currentLane.points.Count)
        {
            return;
        }

        Vector3 target =
            GetWorldPoint(
                currentLane.points[
                    targetPointIndex
                ]
            );

        Vector3 direction =
            target
            - transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude >
            0.001f)
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

        worldPoint.y +=
            heightOffset;

        return worldPoint;
    }


    private int FindPointIndexAfterDistance(
        List<Vector3> points,
        float distance)
    {
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
                return i + 1;
        }

        return points.Count - 1;
    }


    private List<Vector3> BuildQuadraticBezier(
        Vector3 start,
        Vector3 control,
        Vector3 end,
        int pointCount)
    {
        pointCount =
            Mathf.Max(
                pointCount,
                3
            );

        List<Vector3> result =
            new List<Vector3>(
                pointCount
            );

        for (int i = 0;
             i < pointCount;
             i++)
        {
            float t =
                i /
                (float)(pointCount - 1);

            float oneMinusT =
                1f - t;

            Vector3 point =
                oneMinusT
                * oneMinusT
                * start
                + 2f
                * oneMinusT
                * t
                * control
                + t
                * t
                * end;

            result.Add(point);
        }

        return result;
    }


    void OnDestroy()
    {
        activeAgents.Remove(this);

        if (intersectionManager == null)
            return;

        if (registeredAtIntersection)
        {
            intersectionManager
                .UnregisterApproach(
                    activeIntersectionNode,
                    this
                );
        }

        if (insideIntersection)
        {
            intersectionManager
                .LeaveIntersection(
                    activeIntersectionNode,
                    this
                );
        }
    }
}
