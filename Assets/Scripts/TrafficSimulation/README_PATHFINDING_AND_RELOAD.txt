TRAFFIC: RUNTIME ROAD RELOAD + A* PATHFINDING
=============================================

FILES
-----
Replace the existing versions with the files in this package. Add the new
TrafficPathfinder.cs file.

New:
- TrafficPathfinder.cs

Changed for routing:
- TrafficAgentBase.cs
- AwareTrafficAgent.cs
- AwareTrafficAgent_LaneUnaware.cs
- AwareTrafficAgent_DeadlockEscape.cs
- SimpleTrafficVehicle.cs
- RoadNetworkManager.cs

Changed for runtime road reload:
- RoadNetworkManager.cs
- RoadGraphRenderer.cs
- IntersectionManager.cs

Included unchanged/current dependencies:
- TrafficSpawner.cs
- TrafficOccupancyManager.cs
- IntersectionMovement.cs
- Lane.cs
- LanePathUtility.cs
- TrafficTurnPathUtility.cs
- RoadGraphData.cs
- RoadMeshBuilder.cs

IMPORTANT: remove/replace old duplicate scripts such as RoadGraphRenderer(1).cs
or RoadNetworkManager(1).cs. Unity must have only one public class definition
for each class.


UNITY REFERENCES
----------------
On the RoadNetworkManager component assign:

1. Road Network Transform
   Keep the same transform used to align logical lane points with the rendered
   road network.

2. Road Graph Renderer
   Drag the GameObject that contains RoadGraphRenderer here.

3. Traffic Spawner
   Drag the GameObject that contains TrafficSpawner here.

TrafficSpawner should still have:
- Network -> the RoadNetworkManager component
- Intersection Manager -> the IntersectionManager component
- Vehicle Prefab -> one of the four DebugCar prefabs
- Vehicle Count -> desired count
- Minimum / Maximum Top Speed Kmh -> desired speed window

The four prefab variants remain:
- DebugCar_Simple -> SimpleTrafficVehicle only
- DebugCar_LaneUnaware -> AwareTrafficAgent_LaneUnaware only
- DebugCar_LaneAware -> AwareTrafficAgent only
- DebugCar_DeadlockEscape -> AwareTrafficAgent_DeadlockEscape only


ROAD JSON UI
------------
The JSON must be inside StreamingAssets, just as before.

Recommended UI: TMP_InputField + Reload button.

Option A: two-step control
- TMP_InputField End Edit -> RoadNetworkManager.SetRoadFileFromString(string)
- Button OnClick -> RoadNetworkManager.ReloadRoadNetwork()

Option B: immediate reload from the input
- TMP_InputField End Edit -> RoadNetworkManager.ReloadRoadNetworkFromString(string)

You can enter either:
- ingolstadt_road_graph_square.json
or
- ingolstadt_road_graph_square

The .json extension is added automatically when omitted.

Reload sequence:
1. Requested JSON is checked and deserialized.
2. If invalid, current simulation is left untouched.
3. Current cars are cleared.
4. Intersection registrations/conflict cache are reset.
5. Logical lanes are rebuilt.
6. Visual road meshes are regenerated from exactly the same graph object.
7. Cars are respawned using the current TrafficSpawner.vehicleCount.


VEHICLE COUNT UI
----------------
Existing API remains:
- TrafficSpawner.SetVehicleCountFromString(string)
- TrafficSpawner.RespawnVehicles()
- TrafficSpawner.RespawnVehiclesFromString(string)

Recommended:
- input End Edit -> SetVehicleCountFromString
- button OnClick -> RespawnVehicles


PATHFINDING
-----------
Every traffic-agent variant now uses A*.

At spawn:
1. The car is placed on a random starting lane.
2. A random reachable destination node is selected.
3. A* computes a directed-lane route from the end of the current lane.
4. The normal controller follows the selected next lanes instead of randomly
   choosing at every intersection.
5. As the final destination is approached, the next trip is preplanned so the
   intersection manager already knows the outgoing movement.
6. The public/debug destination is promoted only after the previous destination
   node has actually been crossed.

Agent routing Inspector fields:
- Minimum Destination Distance (default 150 m)
- Destination Search Attempts (default 16)
- Debug Destination Node
- Debug Remaining Route Lanes

If a small JSON covers less than 150 m, this is safe: destination generation
first tries the requested distance, then falls back to any reachable node.

A* currently uses free-flow travel time:
    lane length / vehicle top speed

For a single vehicle speed this is equivalent to shortest-distance routing.
The important part is that cost is centralized in:
    RoadNetworkManager.GetRoutingCostSeconds(...)

The pathfinder already accepts includeTrafficInCost, and TrafficAgentBase has:
    ReplanRoute(bool includeTrafficInCost)

No automatic traffic-based rerouting is enabled now. Later a rerouting policy
can periodically or conditionally call ReplanRoute(true), using the existing
TrafficOccupancyManager congestion state, without changing the A* algorithm or
turn/following controllers.
