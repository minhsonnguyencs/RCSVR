INTERSECTION FLOW + ANTI-OVERLAP FIX
====================================

Replace:
- IntersectionManager.cs
- AwareTrafficAgent_DeadlockEscape.cs

No other scripts need to change.

WHAT CHANGED
------------

1. Signalized intersection arbitration
---------------------------------------
At a signalized junction:
- the current vehicle must have green;
- conflicting vehicles already inside the junction still block entry;
- APPROACHING vehicles whose signal is red/yellow/all-red are completely
  ignored by the old right-before-left / arrival-order arbitration.

This prevents a car waiting on red from unnecessarily stopping a green stream.

2. Same-movement connector following
-------------------------------------
AwareTrafficAgent_DeadlockEscape now measures distance to its same-movement
leader using progress ALONG the intersection connector instead of straight-line
world distance.

3. Hard longitudinal safety clamp
---------------------------------
New Inspector section on AwareTrafficAgent_DeadlockEscape:

Collision Safety
    Enable Hard Safety Clamp = true
    Hard Minimum Gap         = 0.25 m

This is NOT the normal desired following distance. Minimum Gap + Time Headway
still control ordinary following.

Immediately before movement, the clamp prevents the vehicle from moving far
enough to reduce clear longitudinal spacing below:

    vehicleLength + hardMinimumGap

The clamp applies:
- on ordinary lanes;
- while following the same movement through an intersection connector.

4. Connector-entry spacing
--------------------------
A follower does not begin the same intersection connector until the preceding
same-movement vehicle has progressed at least:

    vehicleLength + hardMinimumGap

along the connector.

This prevents two queued cars from being admitted into essentially the same
connector-start position.

5. Deadlock escape
------------------
Existing behavior remains:
- it can bypass ordinary unsignalized priority after its timeout;
- it cannot deliberately run a red/yellow traffic signal;
- the hard safety clamp still limits longitudinal overlap.

RECOMMENDED INITIAL SETTINGS
----------------------------
Vehicle Length       4.0 m
Minimum Gap          3.0 m
Time Headway         1.3 s

Enable Hard Safety Clamp  true
Hard Minimum Gap          0.25 m

Do not reduce Minimum Gap to 0.25 m. The 0.25 m value is only the emergency
geometric floor.
