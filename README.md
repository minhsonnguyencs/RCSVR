# Ingolstadt City VR

A photorealistic 3D visualization of Ingolstadt city running on **Meta Quest 3** standalone,
built with Unity 6. The scene lets you walk through the city at ground level, switch between
six building-density levels and three mesh-detail tiers, and jump to a bird's-eye overview.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Project Layout](#2-project-layout)
3. [Using the VR App](#3-using-the-vr-app)
4. [Model Pipeline](#4-model-pipeline)
5. [Unity Scripts Reference](#5-unity-scripts-reference)
6. [Additional Reference](#6-additional-reference)
---

## 1. Prerequisites

| Tool | Version |
|---|---|
| Unity | 6000.4.5f1 |
| Meta XR SDK | 201.0.0 |
| XR Interaction Toolkit | 3.4.1 |
| URP | 17.4.0 |
| Python | 3.10+ |
| Target device | Meta Quest 3 |

---

## 2. Project Layout

```
projects/
├── VR/                             Unity project
│   └── Assets/
│       ├── Scenes/
│       │   └── ingolstadt/         OBJ assets per building-count tier
│       │       ├── 10-buildings/
│       │       ├── 20-buildings/
│       │       ├── 50-buildings/
│       │       ├── 150-buildings/
│       │       ├── 250-buildings/
│       │       └── all-buildings/
│       ├── Scripts/
│       │   ├── City/               Mesh-generation components
│       │   │   ├── CityRoadSpline.cs
│       │   ├── HandMenu/           Runtime VR interaction
│       │   │   ├── CityViewController.cs
│       │   │   ├── ViewpointController.cs
│       │   │   └── HandMenuActivator.cs
│       │   └── Locomotion/
│       │       └── RunController.cs
│       └── Materials/City/         Ground and road PBR materials
```

Each tier folder under `ingolstadt/` holds three files per type:

```
ingolstadt_10_buildings_LOD1.obj / .mtl
ingolstadt_10_buildings_LOD2.obj / .mtl
ingolstadt_10_buildings_LOD3.obj / .mtl
ingolstadt_10_roads_LOD1.obj     / .mtl
...
ingolstadt_10_infrastructure_LOD3.obj / .mtl
```

---

## 3. Using the VR App

### Controls

| Action | Input                                  |
|---|----------------------------------------|
| Walk | Left thumbstick                        |
| Run | Left grip (hold)                       |
| Hand menu | **Y button** to show/hide              |
| Bird-eye fly XZ | Left thumbstick (while in bird-eye)    |

### Hand Menu (Location: XR Origin (XR Rig) -> Camera Offset -> Left Controller)

Open with **Y** on the left controller. The panel has three rows of buttons:

**City Complexity** — choose how many buildings are loaded:

| Button | Buildings shown |
|---|---|
| 10 | 10 nearest buildings |
| 20 | 20 nearest buildings |
| 50 | 50 nearest buildings |
| 150 | 150 nearest buildings |
| 250 | 250 nearest buildings |
| All | All ~22 000 buildings |

**LOD** — choose mesh detail level:

| Button | Detail |
|---|---|
| LOD1 | Simple box geometry, very fast |
| LOD2 | Detailed boxes with UV mapping |
| LOD3 | Full textured CityGML geometry |

**Viewpoints** — teleport to three preset standing positions around the city.

| Button | Detail                         |
|--------|--------------------------------|
| View1  | Viewpoint 1                    |
| View2  | Viewpoint 2                    |
| View3  | Viewpoint 3                    |
| Bird-Eye  | Toggle an overhead flight mode |


---

## 4. Model Pipeline

### Data sources

| File | Description                            |
|---|----------------------------------------|
| `data/Ingolstadt.city.json` | CityJSON source (LOD3)                 |
| `data/ingolstadt/Ingolstadt.gml` | CityGML buildings (LOD1 + LOD2 + LOD3) |
| `data/ingolstadt/lod3-road-space-models/` | Road network and furniture             |

---

## 5. Unity Scripts Reference

### Runtime scripts

#### `CityViewController.cs`

Manages which building-count / LOD combination is visible.

**Inspector fields:** Assign the six building-count sets (10 / 20 / 50 / 150 / 250 / All),
each with three LOD GameObjects. Optionally assign highlight buttons.

**Public methods called by UI buttons:**

```
SetLOD1() / SetLOD2() / SetLOD3()
SetComplexity10() / SetComplexity20() / SetComplexity50()
SetComplexity150() / SetComplexity250() / SetComplexityAll()
```

---

#### `ViewpointController.cs`

Handles teleport to preset viewpoints and bird-eye flight mode.

**Inspector fields:**

| Field | Description                                                             |
|---|-------------------------------------------------------------------------|
| XR Origin Transform | Root transform of the XR rig                                            |
| Viewpoint 1/2/3 | Empty GameObjects at standing positions (Y rotation = facing direction) |
| Bird-Eye Height | Default 150m                                                            |
| Scene Center | XZ origin of the city                                                   |
| Fly Speed | Horizontal flight speed (m/s)                                           |
| Fly Vertical Speed | Vertical flight speed (m/s)                                             |
| Transition Duration | Lerp time between positions (seconds)                                   |

**Public methods:**

```
SetViewpoint1() / SetViewpoint2() / SetViewpoint3()
ToggleBirdEye()
```

---

#### `HandMenuActivator.cs`

Shows / hides the hand menu panel and adjusts its position for bird-eye mode.

**Inspector fields:**

| Field | Description |
|---|---|
| Menu Panel | Root GameObject of the hand menu UI |
| Left Controller Transform | `Near-Far Interactor Left` from the XR Origin hierarchy |
| Viewpoint Controller | Auto-found if left empty |
| City Ground | Ground GameObject — hidden in bird-eye, restored on exit |
| Wrist Offset | Menu position in normal mode |
| Grip Offset | Menu position in bird-eye mode |

---

#### `CityRoadSpline.cs`
Usage: Go to Environment -> CityRoad -> inside Inspector windows draw a spline in Spline container 
-> Then find City Road Spline (Script) -> Right click and choose Gererate Road.

Extrudes a flat road surface along a **SplineContainer** centreline.

**Inspector fields:** Road Width (m), Y Offset (prevents Z-fighting, default 0.005),
Samples Per Metre (smoothness), Road Material.

---

#### `RunController.cs`

Doubles movement speed while the left grip button is held. Assign the **Run Action**
(InputActionReference) and optionally the **Move Provider**; it auto-finds
`DynamicMoveProvider` if left empty.

## 6. Additional Reference

### Add a new viewpoint

1. Create an empty GameObject in the scene at the desired standing position.
2. Set its Y rotation to the direction the player should face.
3. Assign it to **Viewpoint 1/2/3** on the `ViewpointController` component.
4. Wire the corresponding `SetViewpoint1/2/3()` call to a UI button.

---

### Adjust bird-eye entry altitude

Select the GameObject holding `ViewpointController` in the scene.  
Change **Bird-Eye Height** in the Inspector (default: 150 m).
