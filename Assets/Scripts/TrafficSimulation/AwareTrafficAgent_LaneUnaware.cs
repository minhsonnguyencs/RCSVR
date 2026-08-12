
using System.Collections.Generic;
using UnityEngine;

public class AwareTrafficAgent_LaneUnaware : TrafficAgentBase
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

    /*
     * Distance from the graph node where a yielding
     * vehicle should stop.
     *
     * This should normally be slightly larger than
     * turnStartDistance.
     */
    public float stopLineDistance = 6f;

    public float stopLineTolerance = 0.25f;

    [Header("Vehicle Following")]
    public LayerMask vehicleLayer;

    public float detectionDistance = 25f;
    public float detectionRadius = 0.8f;

    public float minimumGap = 3f;
    public float timeHeadway = 1.3f;

    /*
     * Very close emergency zone.
     *
     * This compensates for cases where a SphereCast
     * is unreliable because another collider is
     * already extremely close.
     */
    public float emergencyCheckDistance = 2.5f;
    public float emergencyCheckRadius = 1.1f;

    [Header("Position")]
    public float heightOffset = 0.7f;
    public float waypointTolerance = 0.4f;


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

    [Header("Following Debug")]
    public bool showFollowingDebug = true;

    public bool vehicleAhead = false;
    public float detectedGap = -1f;
    public string detectedVehicle = "None";
    public float followingSpeedLimit = -1f;


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
                "AwareTrafficAgent_LaneUnaware initialized with invalid lane."
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
    }


    private void UpdateLaneDesiredSpeed()
    {
        desiredSpeed = cruiseSpeed;

        float remainingDistance =
            GetRemainingLaneDistance();

        /*
         * Start participating in intersection
         * priority calculations early.
         */
        if (remainingDistance <=
            intersectionAwarenessDistance)
        {
            /*
             * FIRST choose the next lane.
             *
             * The IntersectionManager now needs to know
             * our planned movement through the intersection
             * in order to check whether our turn path
             * conflicts with other vehicles.
             */
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

            /*
             * Only register AFTER nextLane is known,
             * so GetPlannedIntersectionPath() can build
             * the correct connector path.
             */
            RegisterAtIntersectionIfNeeded();

            /*
             * Refresh the manager's stored version of
             * our planned path. This is useful because
             * the preview curve depends on our current
             * position and therefore changes slightly
             * while we approach the intersection.
             */
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
         * Slow toward the appropriate turn speed
         * while approaching the turn-start point.
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
         * Intersection yielding.
         */
        if (registeredAtIntersection &&
            !insideIntersection &&
            intersectionManager != null)
        {
            bool allowed =
                intersectionManager.CanEnter(
                    activeIntersectionNode,
                    this
                );

            if (!allowed)
            {
                /*
                 * IMPORTANT:
                 *
                 * We stop at stopLineDistance metres
                 * BEFORE the graph node.
                 */
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

        /*
         * Vehicle-following constraint.
         */
        desiredSpeed =
            Mathf.Min(
                desiredSpeed,
                GetFollowingSpeedLimit()
            );
    }


    private void UpdateTurnDesiredSpeed()
    {
        desiredSpeed =
            currentTurnSpeed;

        desiredSpeed =
            Mathf.Min(
                desiredSpeed,
                GetFollowingSpeedLimit()
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
         *
         * Even if discrete frame updates or braking
         * calculations are imperfect, a yielding car
         * must not simply drive through the stop line.
         */
        if (registeredAtIntersection &&
            !insideIntersection &&
            intersectionManager != null)
        {
            bool allowed =
                intersectionManager.CanEnter(
                    activeIntersectionNode,
                    this
                );

            if (!allowed &&
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
         * Begin the turn once we are inside the turn
         * zone AND have intersection permission.
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
                bool canEnter =
                    intersectionManager == null
                    || intersectionManager.CanEnter(
                        currentLane.endNode,
                        this
                    );

                if (canEnter)
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
            intersectionManager == null)
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


    private float GetFollowingSpeedLimit()
    {
        vehicleAhead = false;
        detectedGap = -1f;
        detectedVehicle = "None";
        followingSpeedLimit = cruiseSpeed;

        Vector3 origin =
            transform.position
            + transform.forward * 1.5f
            + Vector3.up * 0.3f;

        /*
         * Emergency close-range check.
         */
        Vector3 emergencyCenter =
            origin
            + transform.forward
            * emergencyCheckDistance;

        Collider[] nearby =
            Physics.OverlapSphere(
                emergencyCenter,
                emergencyCheckRadius,
                vehicleLayer,
                QueryTriggerInteraction.Ignore
            );

        foreach (Collider other in nearby)
        {
            if (other == null)
                continue;

            /*
             * Ignore only OUR OWN vehicle,
             * not every object sharing the same root.
             */
            AwareTrafficAgent_LaneUnaware otherAgent =
                other.GetComponentInParent<AwareTrafficAgent_LaneUnaware>();

            if (otherAgent == null)
                continue;

            if (otherAgent == this)
                continue;

            vehicleAhead = true;
            detectedGap = 0f;
            detectedVehicle = otherAgent.name;

            followingSpeedLimit = 0f;

            return 0f;
        }

        /*
         * Main forward detector.
         */
        RaycastHit[] hits =
            Physics.SphereCastAll(
                origin,
                detectionRadius,
                transform.forward,
                detectionDistance,
                vehicleLayer,
                QueryTriggerInteraction.Ignore
            );

        RaycastHit? closestHit = null;
        AwareTrafficAgent_LaneUnaware closestAgent = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
                continue;

            AwareTrafficAgent_LaneUnaware otherAgent =
                hit.collider.GetComponentInParent<AwareTrafficAgent_LaneUnaware>();

            if (otherAgent == null)
                continue;

            // Ignore our own collider.
            if (otherAgent == this)
                continue;

            if (!closestHit.HasValue ||
                hit.distance < closestHit.Value.distance)
            {
                closestHit = hit;
                closestAgent = otherAgent;
            }
        }

        if (!closestHit.HasValue)
        {
            return cruiseSpeed;
        }

        RaycastHit detected =
            closestHit.Value;

        vehicleAhead = true;
        detectedGap = detected.distance;

        detectedVehicle =
            closestAgent != null
            ? closestAgent.name
            : detected.collider.name;

        float desiredGap =
            minimumGap
            + currentSpeed
            * timeHeadway;

        if (detectedGap <= minimumGap)
        {
            followingSpeedLimit = 0f;
            return 0f;
        }

        if (detectedGap >= desiredGap)
        {
            followingSpeedLimit =
                cruiseSpeed;

            return cruiseSpeed;
        }

        float t =
            Mathf.InverseLerp(
                minimumGap,
                desiredGap,
                detectedGap
            );

        followingSpeedLimit =
            cruiseSpeed * t;

        return followingSpeedLimit;
    }


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

        /*
         * v_initial² =
         * v_target² + 2ad
         */
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


    public override List<Vector3> GetPlannedIntersectionPath()
    {
        if (nextLane == null)
            return null;

        return BuildTurnPathPreview(nextLane);
    }


    public override Vector3 GetApproachDirection()
    {
        return GetIncomingDirection();
    }


    void OnDestroy()
    {
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


    void OnDrawGizmosSelected()
    {
        if (!showFollowingDebug)
            return;

        Vector3 origin =
            transform.position
            + transform.forward * 1.5f
            + Vector3.up * 0.3f;

        /*
         * Main detection direction.
         */
        Gizmos.color =
            vehicleAhead
            ? Color.green
            : Color.cyan;

        Gizmos.DrawLine(
            origin,
            origin
            + transform.forward
            * detectionDistance
        );

        Gizmos.DrawWireSphere(
            origin
            + transform.forward
            * detectionDistance,
            detectionRadius
        );

        /*
         * Emergency detector.
         */
        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            origin
            + transform.forward
            * emergencyCheckDistance,
            emergencyCheckRadius
        );
    }
}