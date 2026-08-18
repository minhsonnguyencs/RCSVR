TRAFFIC-AWARE REROUTING + SUPPLY/DEMAND SETUP
================================================

FILES ADDED
-----------
TrafficRoutingPolicy.cs
TrafficDemandManager.cs
TrafficODMatrixData.cs
traffic_od_matrix_example.json

FILES CHANGED
-------------
RoadNetworkManager.cs
TrafficPathfinder.cs
TrafficAgentBase.cs
TrafficSpawner.cs
AwareTrafficAgent.cs
AwareTrafficAgent_LaneUnaware.cs
AwareTrafficAgent_DeadlockEscape.cs
SimpleTrafficVehicle.cs

The remaining scripts in this package are included for version consistency.

1. TRAFFIC-AWARE ROUTING
========================
Select the GameObject with RoadNetworkManager.

Under "Routing Policy":

Mode
    Static
        Free-flow A*. Traffic occupancy never affects route choice and cars
        never reroute because of congestion.

    TrafficAware
        Initial A* may use traffic cost and every car periodically considers
        a new route to its CURRENT destination.

Recommended first values:
    Congestion Weight                 3
    Congestion Exponent               2
    Maximum Congestion Multiplier     5
    Rerouting Interval Seconds       10
    Rerouting Interval Jitter Seconds 3
    Minimum Time Gain Seconds         5
    Minimum Time Gain Percent        10
    Minimum Remaining Route Time     15
    Traffic Aware Initial Routing     true

A traffic-aware lane cost is:

    free-flow time *
    clamp(1 + weight * occupancy^exponent, 1, maxMultiplier)

Occupancy comes from TrafficOccupancyManager.

A reroute is accepted only if BOTH:
    time saved >= Minimum Time Gain Seconds
    percentage saved >= Minimum Time Gain Percent

Cars also randomize the time of each next check by +/- the jitter value so
large populations do not all execute A* in the same frame.

Rerouting is not performed after a car has already reserved/selected its next
intersection movement.

For a clean routing benchmark, keep every other setting and random seed/setup
as consistent as possible and switch only:
    RoadNetworkManager -> Routing Policy -> Mode


2. SUPPLY / DEMAND MANAGER
==========================
Create an empty GameObject, for example:
    TrafficDemand

Add:
    TrafficDemandManager

Then select the GameObject containing RoadNetworkManager and assign:
    Supply / Demand -> Traffic Demand Manager = TrafficDemand

"Use Demand Model" enables/disables both weighted spawning and weighted
destination generation.

Everything OUTSIDE your explicitly configured circles automatically belongs
to the special zone:
    DEFAULT


3. CREATE 5-10 ZONES
=====================
For each area create an empty GameObject positioned at the center of the area.

Example hierarchy:
    TrafficDemand
        Centre
        NorthResidential
        SouthResidential
        Industrial
        University

You do NOT need any collider.

In TrafficDemandManager -> Explicit Circular Zones, add one list item per
zone and configure:

    Zone Name
        Must be unique. It is also the exact name used in the OD JSON.

    Center
        Drag the corresponding empty Transform.

    Radius
        Horizontal radius in Unity/world metres.

    Supply Weight
        Relative probability that a newly spawned vehicle starts in this zone.

    Demand Weight
        Fallback relative destination attraction if there is no usable OD row.

    Gizmo Color
        Scene-view color.

The zone is a circle in the XZ plane, not a 3D volume. If circles overlap,
the road point is assigned to the zone where it is proportionally closest
to the center (distance / radius).

The DEFAULT zone has its own:
    Default Supply Weight
    Default Demand Weight

These weights do not need to sum to 1.


4. ZONE VISUALIZATION
=====================
Enable:
    TrafficDemandManager -> Show Zone Gizmos

With Scene-view Gizmos enabled you will see each configured circular boundary
plus a label showing:
    zone name
    supply weight
    demand weight

The remainder of the map is labelled as DEFAULT near the manager object.

The visualization has no runtime traffic effect.


5. OD MATRIX
============
Copy:
    traffic_od_matrix_example.json

to:
    Assets/StreamingAssets/traffic_od_matrix.json

or keep another filename and set:
    TrafficDemandManager -> OD Matrix File Name

The JSON structure is:

{
  "rows": [
    {
      "originZone": "NorthResidential",
      "destinations": [
        { "destinationZone": "Centre", "weight": 2.0 },
        { "destinationZone": "Industrial", "weight": 1.0 }
      ]
    }
  ]
}

The weights are relative and are normalized automatically.

Important:
    - Zone names in JSON must exactly match Zone Name values in Unity.
    - Use "DEFAULT" for the implicit outside area.
    - You do not need to list every destination in every row.
    - A missing/invalid row falls back to the Inspector Demand Weight values.
    - If a sampled endpoint is unreachable, another endpoint is tried.
    - If demand routing cannot produce a route at all, the existing random
      endpoint generator is used as a final fallback.

The included sample assumes these explicit names:
    Centre
    NorthResidential
    SouthResidential
    Industrial
    University

Rename the JSON entries if you use different zone names.


6. CLOSED POPULATION BEHAVIOR
=============================
Vehicle count stays constant.

Initial population:
    TrafficSpawner chooses a supply zone by Supply Weight, then chooses a
    lane inside that zone and spawns the car there.

Trips:
    When a car needs a destination, its current node determines its origin
    zone. The OD matrix chooses a destination zone, then a routable node in
    that zone is sampled and A* creates the trip.

At the destination:
    The car is NOT destroyed.
    A new OD trip is generated and it continues driving.

This preserves the fixed-population performance-testing model.


7. ROAD NETWORK RELOAD
======================
RoadNetworkManager already coordinates runtime road reloads.

After rebuilding lanes it now also calls:
    TrafficDemandManager.RebuildNetworkCache(...)

so zone memberships are regenerated for the newly loaded city extent before
cars are respawned.

No manual refresh is required.


8. DEBUGGING REROUTING
======================
Select a car in Play mode. TrafficAgentBase now exposes:

    Debug Routing Mode
    Debug Last Current Route Cost
    Debug Last Alternative Route Cost
    Debug Last Reroute Gain Seconds
    Debug Last Reroute Gain Percent
    Debug Successful Reroutes

The existing cyan route and yellow destination gizmos still work.


9. SUGGESTED FIRST TEST
=======================
Start with:
    50-100 LaneAware cars
    Routing Mode = Static

Verify that supply weighting and OD destinations look sensible.

Then change only:
    Routing Mode = TrafficAware

Suggested first traffic parameters:
    weight     = 3
    exponent   = 2
    max mult   = 5
    interval   = 10 s
    jitter     = 3 s
    gain sec   = 5 s
    gain %     = 10%
    min remain = 15 s

For visible rerouting tests you may temporarily use higher congestion weight
or more concentrated OD demand.
