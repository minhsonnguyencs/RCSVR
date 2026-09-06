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
4. [Benchmark Tutorial](#4-benchmark-tutorial)
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

| Action | Input                                   |
|---|-----------------------------------------|
| Walk | Left thumbstick                         |
| Run | Left grip (hold)                        |
| Hand menu | **Y button** to show/hide               |
| Hand menu | **X button** to start/restart benchmark |
| Bird-eye fly XZ | Left thumbstick (while in bird-eye)     |

![Handmenu](public/handmenu.jpg)

---

## 3. Agent Behaviours

![Agent Behaviours](public/agent-behaviours.png)

---

## 4. Benchmark Tutorial
Manually select the benchmark settings from the hand menu. then press **X** to start the benchmark. If the benchmark is not compliant, press **X** again to restart the benchmark. The csv file will live in /sdcard/Android/data/com.DefaultCompany.VRTemplate/files/ of the Meta Quest 3 device. 

---

## 5. Demo
[Watch the demo video](public/rcs-demo-vid.mp4)