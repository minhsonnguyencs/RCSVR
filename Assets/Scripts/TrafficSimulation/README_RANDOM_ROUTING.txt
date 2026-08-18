RANDOM MOVEMENT ROUTING MODE
============================

Replace:
- TrafficRoutingPolicy.cs
- TrafficAgentBase.cs

No concrete vehicle-controller scripts need changes.

Unity
-----
RoadNetworkManager -> Routing Policy -> Mode now contains:

1. Random
   - no destination
   - no A*
   - at every intersection a random outgoing lane is selected
   - immediate U-turns are avoided unless the road is a dead end
   - no traffic-aware rerouting

2. Static
   - destination-based free-flow A*
   - no congestion rerouting

3. TrafficAware
   - destination-based occupancy-weighted A*
   - periodic beneficial congestion rerouting

The mode can be changed while Play mode is running. Existing agents discard
the old route state and adapt automatically.

Supply / demand interaction
---------------------------
In Random mode:
- SUPPLY weighting can still affect where cars are initially spawned.
- DEMAND / OD destination weighting does not apply, because Random mode has
  no destinations.

Static and TrafficAware modes use the existing demand/OD destination model.

Traffic lights / vehicle behavior
---------------------------------
This change does not alter intersection behavior. Aware agents still obey the
same IntersectionManager / TrafficLightSystem rules regardless of routing mode.
SimpleTrafficVehicle still does not obey traffic lights, as before.
