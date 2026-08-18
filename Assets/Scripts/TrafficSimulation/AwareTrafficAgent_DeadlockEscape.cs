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
    [Tooltip("Per-vehicle top speed in km/h. TrafficSpawner randomizes this at spawn time.")]
    public float topSpeedKmh = 43.2f;

    private float cruiseSpeed => topSpeedKmh / 3.6f;

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

    [Header("Turn Path Quality")]
    [Tooltip("Minimum number of samples used for intersection connectors. Higher values make turns visually smoother.")]
    public int minimumTurnCurvePoints = 24;

    [Tooltip("How far along each lane is sampled to estimate its direction at the intersection.")]
    public float turnTangentSampleDistance = 4f;

    [Tooltip("Bezier control-handle length as a fraction of the connector chord length.")]
    [Range(0.15f, 0.45f)]
    public float turnHandleScale = 0.35f;

    [Tooltip("Upper limit for Bezier control-handle length, in metres.")]
    public float maximumTurnHandleLength = 6f;

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

    [Header("Collision Safety")]
    [Tooltip("Final positional safeguard. The vehicle is never allowed to move closer than this clear gap behind a same-stream leader.")]
    public bool enableHardSafetyClamp = true;

    [Tooltip("Hard clear gap in metres, beyond vehicleLength. This is a last-resort anti-overlap clamp, not the normal following gap.")]
    [Min(0f)]
    public float hardMinimumGap = 0.25f;

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
    public bool debugWaitingForGreenSignal = false;

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



    private List<Vector3> conflictPath;

    public override Lane CurrentLane => currentLane;
    public override Lane PlannedNextLane => nextLane;
    public override float CurrentLaneProgress => GetProgressOnCurrentLane();
    public override float CurrentSpeedMps => currentSpeed;
    public override float TopSpeedKmh => topSpeedKmh;

    public override void SetTopSpeedKmh(float speedKmh)
    {
        topSpeedKmh = Mathf.Max(1f, speedKmh);
    }

    public override int IntersectionGeometryProfileHash
    {
        get
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(turnStartDistance * 100f);
                hash = hash * 31 + Mathf.RoundToInt(turnEndDistance * 100f);
                hash = hash * 31 + Mathf.Max(turnCurvePoints, minimumTurnCurvePoints);
                hash = hash * 31 + Mathf.RoundToInt(turnTangentSampleDistance * 100f);
                hash = hash * 31 + Mathf.RoundToInt(turnHandleScale * 1000f);
                hash = hash * 31 + Mathf.RoundToInt(maximumTurnHandleLength * 100f);
                return hash;
            }
        }
    }

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

        UpdateDynamicRouting();

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
        debugWaitingForGreenSignal = IsWaitingForTrafficSignal();
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
            (
                !IsDeadlockEscapeActive()
                || !IsTrafficSignalCurrentlyPermittingEntry()
            ))
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
            bool signalPermitsEntry =
                insideIntersection
                || !registeredAtIntersection
                || IsTrafficSignalCurrentlyPermittingEntry();

            /*
             * Deadlock escape may override ordinary unsignalized priority and
             * outgoing-lane blocking, but it never overrides a red/yellow
             * traffic signal. If the signal is not green, preserve the
             * stop-line braking speed calculated above.
             */
            if (signalPermitsEntry)
            {
                desiredSpeed =
                    IsDangerouslyBlockedAhead()
                        ? 0f
                        : Mathf.Min(
                            cruiseSpeed,
                            deadlockEscapeSpeed
                        );
            }
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
         * Cross-traffic remains governed exclusively by IntersectionManager.
         * However, identical incoming->outgoing movements are now allowed to
         * form a platoon through the junction.  While on the connector, follow
         * only the immediately preceding vehicle on that SAME movement.
         *
         * This deliberately does not use generic geometric/physics following,
         * so a car on a crossing movement cannot make us stop mid-intersection.
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
                Mathf.Min(
                    currentTurnSpeed,
                    GetIntersectionPlatoonSpeedLimit()
                );
        }
    }


    private float GetIntersectionPlatoonSpeedLimit()
    {
        vehicleAhead = false;
        detectedGap = -1f;
        detectedVehicle = "None";

        if (!insideIntersection)
            return cruiseSpeed;

        TrafficAgentBase leader = null;
        float centreDistance = float.PositiveInfinity;

        /*
         * First preference: a vehicle that is still on the exact same
         * intersection movement.
         */
        if (intersectionManager != null &&
            activeIntersectionNode >= 0)
        {
            leader =
                intersectionManager
                    .GetSameMovementLeaderInside(
                        activeIntersectionNode,
                        this
                    );
        }

        if (leader != null)
        {
            AwareTrafficAgent_DeadlockEscape typedLeader =
                leader as AwareTrafficAgent_DeadlockEscape;

            if (typedLeader != null &&
                typedLeader.insideIntersection)
            {
                centreDistance =
                    Mathf.Max(
                        0f,
                        typedLeader
                            .GetIntersectionConnectorProgress()
                        - GetIntersectionConnectorProgress()
                    );
            }
            else
            {
                Vector3 separation =
                    leader.transform.position
                    - transform.position;

                separation.y = 0f;
                centreDistance = separation.magnitude;
            }
        }
        else
        {
            /*
             * Critical transition bridge:
             *
             * As soon as the leading car finishes the connector it disappears
             * from IntersectionManager and becomes an ordinary occupant of the
             * outgoing lane. Treat that car as the SAME longitudinal stream
             * instead of suddenly declaring the outgoing lane blocked.
             */
            leader =
                GetNearestOutgoingLaneLeader();

            if (leader != null)
            {
                centreDistance =
                    GetCentreDistanceToOutgoingLeader(
                        leader,
                        true
                    );
            }
        }

        if (leader == null ||
            float.IsPositiveInfinity(
                centreDistance))
        {
            return cruiseSpeed;
        }

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

        if (gap <= minimumGap)
            return 0f;

        if (gap >= desiredGap)
            return cruiseSpeed;

        float gapError =
            gap - desiredGap;

        float correction =
            gapError /
            Mathf.Max(
                timeHeadway,
                0.1f
            );

        float permittedSpeed =
            leader.CurrentSpeedMps
            + correction;

        return Mathf.Clamp(
            permittedSpeed,
            0f,
            cruiseSpeed
        );
    }


    public float GetIntersectionConnectorProgress()
    {
        if (!isTurning ||
            turnPath == null ||
            turnPath.Count < 2)
        {
            return 0f;
        }

        int segmentEnd =
            Mathf.Clamp(
                turnPathIndex,
                1,
                turnPath.Count - 1
            );

        int segmentStart =
            segmentEnd - 1;

        float progress = 0f;

        for (int i = 0;
             i < segmentStart;
             i++)
        {
            progress +=
                Vector3.Distance(
                    turnPath[i],
                    turnPath[i + 1]
                );
        }

        Vector3 a =
            turnPath[segmentStart];

        Vector3 b =
            turnPath[segmentEnd];

        Vector3 p =
            transform.position;

        a.y = 0f;
        b.y = 0f;
        p.y = 0f;

        Vector3 ab =
            b - a;

        float denominator =
            Vector3.Dot(
                ab,
                ab
            );

        if (denominator >
            0.00001f)
        {
            float t =
                Mathf.Clamp01(
                    Vector3.Dot(
                        p - a,
                        ab
                    )
                    / denominator
                );

            progress +=
                ab.magnitude
                * t;
        }

        return progress;
    }


    private TrafficAgentBase GetNearestOutgoingLaneLeader()
    {
        if (nextLane == null ||
            network == null ||
            network.occupancyManager == null)
        {
            return null;
        }

        /*
         * The requester is not yet registered on nextLane, so using a small
         * negative progress finds the first active vehicle from the lane start.
         */
        return
            network.occupancyManager
                .FindNearestAhead(
                    nextLane,
                    this,
                    -1f,
                    progressTieTolerance
                );
    }


    private float GetActualOutgoingMergeProgress()
    {
        if (nextLane == null)
            return 0f;

        return Mathf.Min(
            turnEndDistance,
            nextLane.totalLength
            * 0.45f
        );
    }


    private float GetPathLength(
        List<Vector3> path)
    {
        if (path == null ||
            path.Count < 2)
        {
            return 0f;
        }

        float length = 0f;

        for (int i = 0;
             i < path.Count - 1;
             i++)
        {
            length +=
                Vector3.Distance(
                    path[i],
                    path[i + 1]
                );
        }

        return length;
    }


    private float GetRemainingConnectorDistance()
    {
        if (!isTurning ||
            turnPath == null ||
            turnPath.Count < 2)
        {
            return 0f;
        }

        return Mathf.Max(
            0f,
            GetPathLength(turnPath)
            - GetIntersectionConnectorProgress()
        );
    }


    private float GetCentreDistanceToOutgoingLeader(
        TrafficAgentBase leader,
        bool fromCurrentConnectorPosition)
    {
        if (leader == null ||
            nextLane == null)
        {
            return float.PositiveInfinity;
        }

        float mergeProgress =
            GetActualOutgoingMergeProgress();

        float leaderBeyondMerge =
            Mathf.Max(
                0f,
                leader.CurrentLaneProgress
                - mergeProgress
            );

        float connectorDistance;

        if (fromCurrentConnectorPosition &&
            isTurning)
        {
            connectorDistance =
                GetRemainingConnectorDistance();
        }
        else
        {
            /*
             * Before connector entry, build the same final-style connector
             * from the car's current position. This measures the actual
             * longitudinal stream distance rather than looking only at the
             * leader's small outgoing-lane progress value.
             */
            List<Vector3> preview =
                BuildTurnPathPreview(
                    nextLane
                );

            connectorDistance =
                GetPathLength(
                    preview
                );
        }

        return
            connectorDistance
            + leaderBeyondMerge;
    }


    private bool HasSafeSpaceToEnterSameMovementConnector()
    {
        if (!enableHardSafetyClamp)
            return true;

        TrafficAgentBase leader = null;
        float centreDistance = float.PositiveInfinity;

        /*
         * Prefer an identical movement that is still physically inside.
         */
        if (intersectionManager != null &&
            activeIntersectionNode >= 0)
        {
            leader =
                intersectionManager
                    .GetSameMovementLeaderInside(
                        activeIntersectionNode,
                        this
                    );
        }

        if (leader != null)
        {
            AwareTrafficAgent_DeadlockEscape typedLeader =
                leader as AwareTrafficAgent_DeadlockEscape;

            if (typedLeader != null &&
                typedLeader.insideIntersection)
            {
                centreDistance =
                    typedLeader
                        .GetIntersectionConnectorProgress();
            }
            else
            {
                Vector3 delta =
                    leader.transform.position
                    - transform.position;

                delta.y = 0f;
                centreDistance = delta.magnitude;
            }
        }
        else
        {
            /*
             * If the previous car has just left the connector, continue the
             * same-stream spacing check against it on the outgoing lane.
             */
            leader =
                GetNearestOutgoingLaneLeader();

            if (leader != null)
            {
                centreDistance =
                    GetCentreDistanceToOutgoingLeader(
                        leader,
                        false
                    );
            }
        }

        if (leader == null)
            return true;

        /*
         * Use the normal stationary following gap for connector admission.
         * The 0.25 m hardMinimumGap remains only the final geometric floor.
         */
        return
            centreDistance
            >= vehicleLength
            + minimumGap;
    }


    private float GetHardClampedLaneSpeed(
        float requestedSpeed)
    {
        if (!enableHardSafetyClamp ||
            Time.deltaTime <= 0f ||
            network == null ||
            network.occupancyManager == null ||
            currentLane == null ||
            isTurning)
        {
            return requestedSpeed;
        }

        float myProgress =
            GetProgressOnCurrentLane();

        TrafficAgentBase leader =
            network.occupancyManager
                .FindNearestAhead(
                    currentLane,
                    this,
                    myProgress,
                    progressTieTolerance
                );

        if (leader == null)
            return requestedSpeed;

        float centreDistance =
            Mathf.Max(
                0f,
                leader.CurrentLaneProgress
                - myProgress
            );

        float availableMovement =
            centreDistance
            - vehicleLength
            - Mathf.Max(
                0f,
                hardMinimumGap
            );

        if (availableMovement <= 0f)
            return 0f;

        float maximumSafeSpeed =
            availableMovement
            / Time.deltaTime;

        return Mathf.Min(
            requestedSpeed,
            maximumSafeSpeed
        );
    }


    private float GetHardClampedConnectorMovementDistance(
        float requestedDistance)
    {
        if (!enableHardSafetyClamp ||
            requestedDistance <= 0f)
        {
            return requestedDistance;
        }

        TrafficAgentBase leader = null;
        float centreDistance = float.PositiveInfinity;

        if (intersectionManager != null &&
            activeIntersectionNode >= 0)
        {
            leader =
                intersectionManager
                    .GetSameMovementLeaderInside(
                        activeIntersectionNode,
                        this
                    );
        }

        if (leader != null)
        {
            AwareTrafficAgent_DeadlockEscape typedLeader =
                leader as AwareTrafficAgent_DeadlockEscape;

            if (typedLeader != null &&
                typedLeader.insideIntersection)
            {
                centreDistance =
                    typedLeader
                        .GetIntersectionConnectorProgress()
                    - GetIntersectionConnectorProgress();
            }
            else
            {
                Vector3 separation =
                    leader.transform.position
                    - transform.position;

                separation.y = 0f;
                centreDistance = separation.magnitude;
            }
        }
        else
        {
            /*
             * Continue protecting spacing after the leader transitions to the
             * outgoing lane. Previously the clamp lost sight of it at exactly
             * this boundary.
             */
            leader =
                GetNearestOutgoingLaneLeader();

            if (leader != null)
            {
                centreDistance =
                    GetCentreDistanceToOutgoingLeader(
                        leader,
                        true
                    );
            }
        }

        if (leader == null ||
            float.IsPositiveInfinity(
                centreDistance))
        {
            return requestedDistance;
        }

        float availableMovement =
            centreDistance
            - vehicleLength
            - Mathf.Max(
                0f,
                hardMinimumGap
            );

        return Mathf.Clamp(
            requestedDistance,
            0f,
            Mathf.Max(
                0f,
                availableMovement
            )
        );
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
            (
                !IsDeadlockEscapeActive()
                || !IsTrafficSignalCurrentlyPermittingEntry()
            ))
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
                    && IsTrafficSignalCurrentlyPermittingEntry()
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

                bool connectorEntryClear =
                    HasSafeSpaceToEnterSameMovementConnector();

                if (intersectionAllowed &&
                    outgoingClear &&
                    connectorEntryClear)
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

        float hardClampedSpeed =
            GetHardClampedLaneSpeed(
                currentSpeed
            );

        MoveToward(
            target,
            hardClampedSpeed,
            normalRotationSpeed
        );
    }


    private void BeginTurn()
    {
        if (nextLane == null)
            return;

        activeIntersectionNode =
            currentLane.endNode;

        /*
         * Build the FINAL connector from the car's exact current position.
         *
         * Unlike the old quadratic curve, this connector is not attracted to
         * the graph node.  It is constrained by the incoming and outgoing lane
         * tangents, which keeps straight movements in-lane and makes left/right
         * turns follow the correct side of the junction.
         */
        turnPath =
            BuildTurnPathPreview(
                nextLane
            );

        if (turnPath == null ||
            turnPath.Count < 2)
        {
            activeIntersectionNode = -1;
            return;
        }

        if (intersectionManager != null)
        {
            intersectionManager.EnterIntersection(
                currentLane.endNode,
                this
            );
        }

        insideIntersection = true;

        CalculateTurnDynamics();

        turnPathIndex = 1;
        isTurning = true;

        /*
         * IntersectionManager already marked the registered movement as being
         * inside the junction.  Keep that movement there until FinishTurn().
         */
        registeredAtIntersection = false;
    }


    private void MoveAlongTurnPath()
    {
        if (turnPath == null ||
            turnPath.Count < 2 ||
            turnPathIndex >= turnPath.Count)
        {
            FinishTurn();
            return;
        }

        /*
         * Advance by DISTANCE along the sampled connector instead of moving
         * toward one waypoint per frame.  A frame can consume several tiny
         * curve segments, so there is no pause/jerk when a waypoint is reached.
         */
        float movementRemaining =
            GetHardClampedConnectorMovementDistance(
                currentSpeed
                * Time.deltaTime
            );

        Vector3 lastDirection =
            transform.forward;

        while (movementRemaining > 0f &&
               turnPathIndex < turnPath.Count)
        {
            Vector3 target =
                turnPath[turnPathIndex];

            Vector3 difference =
                target - transform.position;

            difference.y = 0f;

            float distance =
                difference.magnitude;

            if (distance < 0.0001f)
            {
                transform.position = target;
                turnPathIndex++;
                continue;
            }

            Vector3 direction =
                difference / distance;

            lastDirection = direction;

            if (movementRemaining >= distance)
            {
                transform.position = target;
                movementRemaining -= distance;
                turnPathIndex++;
            }
            else
            {
                transform.position +=
                    direction * movementRemaining;

                movementRemaining = 0f;
            }
        }

        if (turnPathIndex >= turnPath.Count)
        {
            FinishTurn();
            return;
        }

        /*
         * Face slightly ahead along the curve.  This avoids visually snapping
         * the car's heading from one sampled segment to the next.
         */
        Vector3 lookDirection =
            turnPath[turnPathIndex]
            - transform.position;

        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude < 0.0001f)
            lookDirection = lastDirection;

        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRotation =
                Quaternion.LookRotation(
                    lookDirection.normalized,
                    Vector3.up
                );

            transform.rotation =
                Quaternion.RotateTowards(
                    transform.rotation,
                    desiredRotation,
                    currentTurnRotationSpeed
                    * Time.deltaTime
                );
        }
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

        Lane previousLane = currentLane;

        currentLane =
            nextLane;

        nextLane = null;

        NotifyRouteLaneTransition(previousLane, currentLane);

        if (network != null && network.occupancyManager != null)
            network.occupancyManager.ChangeLane(this, previousLane, currentLane);

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
        Vector3 direction =
            TrafficTurnPathUtility.GetEndTangent(
                network,
                currentLane,
                turnTangentSampleDistance
            );

        if (direction.sqrMagnitude < 0.001f)
            return transform.forward;

        return direction;
    }


    /*
     * --------------------------
     * Deadlock escape
     * --------------------------
     */

    private void UpdateDeadlockEscapeState()
    {
        /*
         * Waiting for a red/yellow/all-red traffic signal is intentional,
         * not a deadlock. Do not accumulate impatience in that state.
         *
         * If an escape window was already active when the signal changed,
         * cancel it immediately. Once the light becomes green again, the
         * stationary timer starts fresh from zero. Therefore a genuine
         * post-green deadlock can still trigger the normal escape mechanism
         * after deadlockTimeout seconds.
         */
        if (IsWaitingForTrafficSignal())
        {
            stationaryTimer = 0f;
            deadlockEscapeTimer = 0f;
            return;
        }

        /*
         * Count only stationary time that is NOT explained by a traffic light.
         * The escape mechanism still deliberately avoids diagnosing every
         * possible unsignalized deadlock cause.
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


    private bool IsWaitingForTrafficSignal()
    {
        /*
         * IsTrafficSignalCurrentlyPermittingEntry() returns true at an
         * unsignalized intersection, so this becomes true only for an
         * actually signal-controlled red/yellow/all-red approach.
         */
        return
            registeredAtIntersection &&
            !insideIntersection &&
            !IsTrafficSignalCurrentlyPermittingEntry();
    }


    private bool IsDeadlockEscapeActive()
    {
        return deadlockEscapeTimer > 0f;
    }


    private bool IsTrafficSignalCurrentlyPermittingEntry()
    {
        if (intersectionManager == null)
            return true;

        long nodeId =
            activeIntersectionNode >= 0
                ? activeIntersectionNode
                : currentLane != null
                    ? currentLane.endNode
                    : -1;

        if (nodeId < 0)
            return true;

        return
            intersectionManager
                .IsTrafficSignalPermitting(
                    nodeId,
                    this
                );
    }


    private bool IsDangerouslyBlockedAhead()
    {
        if (network == null ||
            network.occupancyManager == null)
        {
            return false;
        }

        TrafficAgentBase other =
            network.occupancyManager.FindVehicleInForwardCorridor(
                this,
                transform.position,
                transform.forward,
                deadlockDangerDistance,
                deadlockDangerLateralDistance
            );

        if (other == null)
            return false;

        Vector3 delta =
            other.transform.position
            - transform.position;

        delta.y = 0f;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            forward.Normalize();

        float forwardDistance =
            Mathf.Max(
                0f,
                Vector3.Dot(delta, forward)
            );

        vehicleAhead = true;
        detectedGap =
            Mathf.Max(
                0f,
                forwardDistance
                - vehicleLength
            );
        detectedVehicle = other.name;

        return true;
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

        if (currentLane == null ||
            network == null ||
            network.occupancyManager == null)
        {
            return cruiseSpeed;
        }

        float myProgress =
            GetProgressOnCurrentLane();

        TrafficAgentBase leader =
            network.occupancyManager.FindNearestAhead(
                currentLane,
                this,
                myProgress,
                progressTieTolerance
            );

        if (leader == null)
            return cruiseSpeed;

        float centreDistance =
            Mathf.Max(
                0f,
                leader.CurrentLaneProgress
                - myProgress
            );

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

        if (gap <= minimumGap)
            return 0f;

        if (gap >= desiredGap)
            return cruiseSpeed;

        float gapError =
            gap - desiredGap;

        float correction =
            gapError /
            Mathf.Max(
                timeHeadway,
                0.1f
            );

        float permittedSpeed =
            leader.CurrentSpeedMps
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

        if (network == null ||
            network.occupancyManager == null)
        {
            return true;
        }

        TrafficAgentBase nearestVehicle =
            network.occupancyManager
                .FindNearestAhead(
                    outgoingLane,
                    this,
                    -1f,
                    progressTieTolerance
                );

        if (nearestVehicle == null)
            return true;

        float nearestProgress =
            nearestVehicle
                .CurrentLaneProgress;

        /*
         * Keep the original conservative "do not block the intersection"
         * rule for a genuinely queued/stationary receiving lane.
         */
        float requiredFrontPosition =
            turnEndDistance
            + vehicleLength
            + minimumGap;

        if (nearestProgress >=
            requiredFrontPosition)
        {
            return true;
        }

        /*
         * Important same-stream exception:
         *
         * A car that has just finished this connector appears on outgoingLane
         * at roughly turnEndDistance progress. The old rule instantly changed
         * Outgoing Lane Clear to false and forced its follower to stop at the
         * line, even though the leader was actively driving away.
         *
         * If the first outgoing-lane car is MOVING, treat it as a longitudinal
         * leader. Admission is allowed when there is already at least one
         * ordinary stopped-following gap along the combined connector + lane
         * path. Connector following and the 0.25 m hard clamp continue to
         * protect the spacing after entry.
         *
         * If that vehicle is stationary, retain the conservative original rule
         * so a real downstream queue cannot be extended into the intersection.
         */
        if (nearestVehicle.CurrentSpeedMps <=
            stationarySpeedThreshold)
        {
            return false;
        }

        float centreDistance =
            GetCentreDistanceToOutgoingLeader(
                nearestVehicle,
                false
            );

        return
            centreDistance
            >= vehicleLength
            + minimumGap;
    }



    private float GetProgressOnCurrentLane()
    {
        if (currentLane == null)
            return 0f;

        if (isTurning)
            return currentLane.totalLength;

        return LanePathUtility.GetProgressOnLane(
            network,
            currentLane,
            targetPointIndex,
            transform.position
        );
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
        return TrafficTurnPathUtility.BuildConnector(
            network,
            currentLane,
            candidateLane,
            transform.position,
            heightOffset,
            turnEndDistance,
            turnCurvePoints,
            minimumTurnCurvePoints,
            turnTangentSampleDistance,
            turnHandleScale,
            maximumTurnHandleLength
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

    public override List<Vector3> GetConflictIntersectionPath()
    {
        if (nextLane == null)
            return null;

        conflictPath = TrafficTurnPathUtility.BuildCanonicalConnector(
            conflictPath,
            network,
            currentLane,
            nextLane,
            heightOffset,
            turnStartDistance,
            turnEndDistance,
            turnCurvePoints,
            minimumTurnCurvePoints,
            turnTangentSampleDistance,
            turnHandleScale,
            maximumTurnHandleLength
        );

        return conflictPath;
    }



    public override Vector3 GetApproachDirection()
    {
        return GetIncomingDirection();
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


    /*
     * --------------------------
     * Lane traversal
     * --------------------------
     */

    private float GetRemainingLaneDistance()
    {
        if (currentLane == null)
            return 0f;

        return Mathf.Max(
            0f,
            currentLane.totalLength
            - GetProgressOnCurrentLane()
        );
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





    void OnDestroy()
    {
        if (network != null && network.occupancyManager != null)
            network.occupancyManager.Unregister(this, currentLane);

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
