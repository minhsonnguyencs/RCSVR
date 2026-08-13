using System;
using UnityEngine;

public enum TrafficRoutingMode
{
    Static,
    TrafficAware
}

/// <summary>
/// Global routing policy shared by all spawned vehicles.
/// Static mode uses free-flow A* and never reroutes for congestion.
/// TrafficAware mode uses occupancy-weighted A* and periodically evaluates
/// whether the current route should be replaced by a meaningfully faster one.
/// </summary>
[Serializable]
public class TrafficRoutingPolicy
{
    public TrafficRoutingMode mode = TrafficRoutingMode.Static;

    [Header("Congestion cost")]
    [Tooltip("Strength of the occupancy penalty. 0 makes TrafficAware equivalent to Static.")]
    [Min(0f)] public float congestionWeight = 3f;

    [Tooltip("Exponent applied to lane occupancy. 1 = linear, 2 = congestion grows more sharply near capacity.")]
    [Min(0.1f)] public float congestionExponent = 2f;

    [Tooltip("Upper bound on the total traffic multiplier for one lane.")]
    [Min(1f)] public float maximumCongestionMultiplier = 5f;

    [Header("Rerouting")]
    [Tooltip("Mean time between congestion checks for one vehicle.")]
    [Min(0.5f)] public float reroutingIntervalSeconds = 10f;

    [Tooltip("Random +/- jitter added to each vehicle's next rerouting interval. This prevents all cars from running A* on the same frame.")]
    [Min(0f)] public float reroutingIntervalJitterSeconds = 3f;

    [Tooltip("A candidate route must save at least this many seconds before it is accepted.")]
    [Min(0f)] public float minimumTimeGainSeconds = 5f;

    [Tooltip("A candidate route must also save at least this percentage of the current route cost.")]
    [Range(0f, 100f)] public float minimumTimeGainPercent = 10f;

    [Tooltip("Do not reroute when the remaining route is already shorter than this estimate.")]
    [Min(0f)] public float minimumRemainingRouteTimeSeconds = 15f;

    [Tooltip("If enabled, newly generated trips in TrafficAware mode also use traffic-aware A* immediately.")]
    public bool trafficAwareInitialRouting = true;
}
