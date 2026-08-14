SIMPLE PROCEDURAL TRAFFIC LIGHTS
================================

Files in this package
---------------------
NEW:
- TrafficLightSystem.cs

REPLACE:
- IntersectionManager.cs
- RoadNetworkManager.cs
- AwareTrafficAgent_DeadlockEscape.cs

No change is required to:
- TrafficAgentBase
- TrafficSpawner
- TrafficPathfinder
- TrafficDemandManager
- Lane / LanePathUtility
- RoadGraphRenderer
- AwareTrafficAgent
- AwareTrafficAgent_LaneUnaware
- SimpleTrafficVehicle

Unity setup
-----------
1. Create an empty GameObject, for example:
      TrafficLights

2. Add TrafficLightSystem to it.

3. On RoadNetworkManager:
      Traffic Light System -> drag TrafficLights

4. On IntersectionManager:
      Traffic Light System -> drag TrafficLights

Both managers also try to FindObjectOfType<TrafficLightSystem>() at runtime,
but explicit Inspector references are recommended.

5. TrafficLightSystem defaults:
      Enable Traffic Lights            true
      Minimum Road Lane Count          2
      Minimum Approach Count           3
      Approach Merge Angle             20 deg

      Phase A Green Duration           25 s
      Phase B Green Duration           25 s
      Yellow Duration                   3 s
      All Red Duration                  1 s

      Phase A Alignment Angle          35 deg
      Randomize Initial Phase Offset   true
      Phase Offset Seed                12345

6. Gizmos:
      Show Traffic Light Gizmos        true

   This toggle is independent of RoadNetworkManager's lane/direction Gizmos.
   The currently green approaches are drawn as green arrows above the
   signalized junction. Yellow/all-red stages show no permitted arrows.

7. The signals rebuild automatically whenever RoadNetworkManager reloads a
   different road JSON.

Signalization rule
------------------
A graph node becomes signalized if:
- it has at least Minimum Approach Count distinct incoming directions, AND
- at least one incident RoadEdgeData has edge.lanes >= Minimum Road Lane Count.

Phase A follows the dominant/widest incoming-road axis. The opposite direction
of the same road is in the same phase. Remaining incoming approaches form
Phase B.

Behavior
--------
Green does NOT bypass the existing IntersectionManager safety rules.

CanEnter is effectively:
    signal is green
    AND existing conflict/right-before-left logic permits entry

This means same-movement platoons still work, while crossing/left-turn
conflicts remain protected.

Deadlock escape
---------------
AwareTrafficAgent_DeadlockEscape may still bypass ordinary unsignalized
intersection priority/outgoing-lane deadlocks, but it will NOT deliberately
run a red or yellow traffic signal.

SimpleTrafficVehicle
--------------------
SimpleTrafficVehicle has no IntersectionManager behavior at all, so it does
not obey signals. This is intentional for the simple baseline controller.
The three aware controllers use IntersectionManager and therefore obey signals.
