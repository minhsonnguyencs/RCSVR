OPTIMIZED TRAFFIC SETUP
=======================

Replace the corresponding scripts in Assets/Scripts with the files in this package.
TrafficOccupancyManager.cs is NEW and must also be added.

No manual TrafficOccupancyManager setup is required: RoadNetworkManager gets/creates
one at runtime and exposes it as occupancyManager.

MAIN PERFORMANCE CHANGES
------------------------
1. Lane.cs precomputes cumulative point distances and totalLength once.
2. LanePathUtility can calculate lane progress without walking the full lane polyline.
3. TrafficOccupancyManager indexes cars by Lane. Lane-aware following/outgoing-space
   checks inspect only the relevant lane instead of every traffic agent in the city.
4. IntersectionManager uses a canonical lane-to-lane conflict path and caches whether
   movement pairs conflict. It also avoids several temporary List allocations.
5. AwareTrafficAgent_LaneUnaware uses NonAlloc physics queries in the normal case,
   with allocating fallbacks only if the reusable buffers overflow.
6. Intersection preview paths are no longer rebuilt every approach frame merely for
   conflict detection. Actual driving connectors are still generated normally.

DYNAMIC PATHFINDING PREPARATION
-------------------------------
RoadNetworkManager / TrafficOccupancyManager now expose live lane traffic information:
- GetLaneVehicleCount(Lane)
- GetLaneOccupancyRatio(Lane)
- GetLaneEstimatedTravelTimeSeconds(Lane, freeFlowSpeedKmh, congestionSensitivity)
- occupancyManager.GetDensityVehiclesPerKm(Lane)

These methods DO NOT currently change route choice. They are intended to become edge
costs for A*/Dijkstra later. Thus future routing can use the same occupancy data that
the traffic simulation already maintains.

RANDOM TOP SPEED
----------------
TrafficSpawner now has:
- Minimum Top Speed Kmh (default 35)
- Maximum Top Speed Kmh (default 50)

Every spawned car receives a uniformly random top speed in this interval. Internally
agents convert km/h to m/s for movement. Each controller also shows Top Speed Kmh in
its Inspector, useful for manually placed/debug cars.

RUNTIME VEHICLE COUNT / UI
--------------------------
TrafficSpawner now has these public methods:

RespawnVehicles()
    Clears current traffic and respawns TrafficSpawner.vehicleCount cars.

RespawnVehicles(int newCount)
    Clears current traffic and respawns exactly newCount cars.

SetVehicleCountFromString(string value)
    Parses an input field value and changes vehicleCount WITHOUT immediately respawning.

RespawnVehiclesFromString(string value)
    Parses the value and immediately clears/respawns traffic.

Recommended UI setup:
1. Add a TMP_InputField (or legacy InputField).
2. On its End Edit event, drag TrafficSpawner into the event target and select:
       TrafficSpawner -> SetVehicleCountFromString(string)
3. Add a Button labelled e.g. "Respawn Traffic".
4. On Button OnClick, drag TrafficSpawner and select:
       TrafficSpawner -> RespawnVehicles()

Alternatively wire the input field End Edit directly to RespawnVehiclesFromString
if you want traffic to be regenerated immediately when editing finishes.

PREFABS
-------
The same four-prefab arrangement remains valid:
- DebugCar_Simple                 -> SimpleTrafficVehicle
- DebugCar_LaneUnaware            -> AwareTrafficAgent_LaneUnaware
- DebugCar_LaneAware              -> AwareTrafficAgent
- DebugCar_DeadlockEscape         -> AwareTrafficAgent_DeadlockEscape

Keep exactly ONE TrafficAgentBase-derived controller on each prefab.

NOTE ABOUT SPEED PARAMETERS
---------------------------
Only top/cruise speed was converted to km/h because that is the driver-like parameter
being randomized. Acceleration, braking distance calculations, minimum turn speed,
and deadlock escape speed remain in SI simulation units (m/s and m/s^2) so their
existing behavior is preserved.
