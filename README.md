# Ingolstadt City VR

A photorealistic 3D visualization of Ingolstadt city running on **Meta Quest 3** standalone,
built with Unity 6. The scene lets you walk through the city at ground level, switch between
six building-density levels, three mesh-detail tiers, six traffic levels, and jump to a bird's-eye overview.

This project is to determine how much real-world, data-driven urban complexity a fully standalone VR headset — the Meta Quest 3 — can sustain before its fixed thermal and compute budget.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Using the VR App](#2-using-the-vr-app)
3. [Agent Behaviours](#3-agent-behaviours)
4. [Threshhold Compliant](#4-threshold-compliant)
5. [Demo](#5-demo)
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

## 2. Using the VR App

### Controls

| Action | Input                                  |
|---|----------------------------------------|
| Walk | Left thumbstick                        |
| Run | Left grip (hold)                       |
| Hand menu | **Y button** to show/hide              |
| Bird-eye fly XZ | Left thumbstick (while in bird-eye)    |

![Handmenu](public\handmenu.png)

---

## 3. Agent Behaviours

<div style="background-color: white; padding: 20px; color: black;">
    <img src="public\agent-behaviours.png" alt="Agent Behaviours">
</div>

---

## 4. Threshold Compliant

|LoD|	Buildings|	Vehicles|	Frames|	% Complaint|	Mean FPS|	Mean CPU util. %|
|---|-------|-------|-------|-------|-------|------|
|1	|1,000	|500	|283	|5.65%	|70.16	|103.6%|
|2	|1,000	|500	|268	|5.60%	|67.86	|109.6%|

No other tested configuration — including LoD3/1,000/500, and every configuration at 2,000+ buildings — produces a single compliant frame

---

## 5. Demo

### 5.1. Manual testing

### 5.2. Automation testing