using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central live traffic-state index. Agents are grouped by logical Lane, so
/// following queries scale with vehicles on one lane instead of every vehicle
/// in the city. The same API is intended for future congestion-aware routing.
/// </summary>
public class TrafficOccupancyManager : MonoBehaviour
{
    [Header("Dynamic-routing defaults")]
    [Tooltip("Approximate road space occupied by one queued vehicle, used only by congestion helper methods.")]
    public float referenceSpacePerVehicle = 7f;

    [Tooltip("How strongly occupancy inflates the estimated travel time returned to future pathfinding.")]
    public float defaultCongestionSensitivity = 2f;

    private readonly Dictionary<Lane, HashSet<TrafficAgentBase>> agentsByLane =
        new Dictionary<Lane, HashSet<TrafficAgentBase>>();

    private readonly HashSet<TrafficAgentBase> allAgents =
        new HashSet<TrafficAgentBase>();

    public void ResetState()
    {
        agentsByLane.Clear();
        allAgents.Clear();
    }

    public void Register(TrafficAgentBase agent, Lane lane)
    {
        if (agent == null || lane == null)
            return;

        if (!agentsByLane.TryGetValue(lane, out HashSet<TrafficAgentBase> agents))
        {
            agents = new HashSet<TrafficAgentBase>();
            agentsByLane[lane] = agents;
        }

        agents.Add(agent);
        allAgents.Add(agent);
    }

    public void Unregister(TrafficAgentBase agent, Lane lane)
    {
        if (agent == null)
            return;

        allAgents.Remove(agent);

        if (lane == null || !agentsByLane.TryGetValue(lane, out HashSet<TrafficAgentBase> agents))
            return;

        agents.Remove(agent);
        if (agents.Count == 0)
            agentsByLane.Remove(lane);
    }

    public void ChangeLane(TrafficAgentBase agent, Lane oldLane, Lane newLane)
    {
        if (oldLane == newLane)
            return;

        Unregister(agent, oldLane);
        Register(agent, newLane);
    }

    public TrafficAgentBase FindNearestAhead(
        Lane lane,
        TrafficAgentBase requester,
        float requesterProgress,
        float tieTolerance)
    {
        if (lane == null || !agentsByLane.TryGetValue(lane, out HashSet<TrafficAgentBase> agents))
            return null;

        TrafficAgentBase leader = null;
        float bestProgress = float.PositiveInfinity;

        foreach (TrafficAgentBase other in agents)
        {
            if (other == null || other == requester || !other.isActiveAndEnabled)
                continue;

            float otherProgress = other.CurrentLaneProgress;
            float delta = otherProgress - requesterProgress;
            bool ahead = delta > tieTolerance;

            if (!ahead && Mathf.Abs(delta) <= tieTolerance)
                ahead = other.GetInstanceID() < requester.GetInstanceID();

            if (!ahead)
                continue;

            if (otherProgress < bestProgress)
            {
                bestProgress = otherProgress;
                leader = other;
            }
        }

        return leader;
    }

    public float GetNearestProgress(Lane lane, TrafficAgentBase ignore = null)
    {
        if (lane == null || !agentsByLane.TryGetValue(lane, out HashSet<TrafficAgentBase> agents))
            return float.PositiveInfinity;

        float nearest = float.PositiveInfinity;
        foreach (TrafficAgentBase agent in agents)
        {
            if (agent == null || agent == ignore || !agent.isActiveAndEnabled)
                continue;

            nearest = Mathf.Min(nearest, agent.CurrentLaneProgress);
        }
        return nearest;
    }

    public int GetVehicleCount(Lane lane)
    {
        if (lane == null || !agentsByLane.TryGetValue(lane, out HashSet<TrafficAgentBase> agents))
            return 0;

        int count = 0;
        foreach (TrafficAgentBase agent in agents)
            if (agent != null && agent.isActiveAndEnabled)
                count++;
        return count;
    }

    public float GetDensityVehiclesPerKm(Lane lane)
    {
        if (lane == null || lane.totalLength <= 0.01f)
            return 0f;
        return GetVehicleCount(lane) / (lane.totalLength / 1000f);
    }

    public float GetOccupancyRatio(Lane lane)
    {
        if (lane == null || lane.totalLength <= 0.01f)
            return 0f;

        return Mathf.Clamp01(
            GetVehicleCount(lane) * Mathf.Max(0.1f, referenceSpacePerVehicle) / lane.totalLength
        );
    }

    /// <summary>
    /// Convenience cost for future dynamic routing. It does not affect current
    /// vehicle behavior. A pathfinder can use this directly or replace it with
    /// a more sophisticated traffic-flow model later.
    /// </summary>
    public float GetEstimatedTravelTimeSeconds(
        Lane lane,
        float freeFlowSpeedMps,
        float congestionSensitivity = -1f)
    {
        if (lane == null)
            return float.PositiveInfinity;

        freeFlowSpeedMps = Mathf.Max(0.1f, freeFlowSpeedMps);
        if (congestionSensitivity < 0f)
            congestionSensitivity = defaultCongestionSensitivity;

        float freeFlowTime = lane.totalLength / freeFlowSpeedMps;
        return freeFlowTime * (1f + Mathf.Max(0f, congestionSensitivity) * GetOccupancyRatio(lane));
    }
    public TrafficAgentBase FindVehicleInForwardCorridor(
        TrafficAgentBase requester,
        Vector3 position,
        Vector3 forward,
        float maximumForwardDistance,
        float maximumLateralDistance)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return null;

        forward.Normalize();
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        TrafficAgentBase closest = null;
        float closestForward = float.PositiveInfinity;

        foreach (TrafficAgentBase other in allAgents)
        {
            if (other == null || other == requester || !other.isActiveAndEnabled)
                continue;

            Vector3 delta = other.transform.position - position;
            delta.y = 0f;
            float forwardDistance = Vector3.Dot(delta, forward);
            if (forwardDistance <= 0f || forwardDistance > maximumForwardDistance)
                continue;

            float lateralDistance = Mathf.Abs(Vector3.Dot(delta, right));
            if (lateralDistance > maximumLateralDistance)
                continue;

            if (forwardDistance < closestForward)
            {
                closestForward = forwardDistance;
                closest = other;
            }
        }

        return closest;
    }

}
