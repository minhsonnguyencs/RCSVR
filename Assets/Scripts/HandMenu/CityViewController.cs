using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Unity.VRTemplate
{
    public class CityViewController : MonoBehaviour
    {
        // --- City Complexity ------------------------------------------------

        [Header("1000 Buildings")]
        [SerializeField] GameObject m_1000_LOD1;
        [SerializeField] GameObject m_1000_LOD2;
        [SerializeField] GameObject m_1000_LOD3;

        [Header("2000 Buildings")]
        [SerializeField] GameObject m_2000_LOD1;
        [SerializeField] GameObject m_2000_LOD2;
        [SerializeField] GameObject m_2000_LOD3;

        [Header("5000 Buildings")]
        [SerializeField] GameObject m_5000_LOD1;
        [SerializeField] GameObject m_5000_LOD2;
        [SerializeField] GameObject m_5000_LOD3;

        [Header("10000 Buildings")]
        [SerializeField] GameObject m_10000_LOD1;
        [SerializeField] GameObject m_10000_LOD2;
        [SerializeField] GameObject m_10000_LOD3;

        [Header("15000 Buildings")]
        [SerializeField] GameObject m_15000_LOD1;
        [SerializeField] GameObject m_15000_LOD2;
        [SerializeField] GameObject m_15000_LOD3;

        [Header("All Buildings")]
        [SerializeField] GameObject m_All_LOD1;
        [SerializeField] GameObject m_All_LOD2;
        [SerializeField] GameObject m_All_LOD3;

        // --- LOD ------------------------------------------------------------

        [Header("LOD Buttons (optional)")]
        [SerializeField] Button m_BtnLOD1;
        [SerializeField] Button m_BtnLOD2;
        [SerializeField] Button m_BtnLOD3;

        [Header("Complexity Buttons (optional)")]
        [SerializeField] Button m_Btn_City_1000;
        [SerializeField] Button m_Btn_City_2000;
        [SerializeField] Button m_Btn_City_5000;
        [SerializeField] Button m_Btn_City_10000;
        [SerializeField] Button m_Btn_City_15000;
        [SerializeField] Button m_BtnAll;

        // --- Traffic Vehicle Count -------------------------------------------

        [Header("Traffic Spawner")]
        [SerializeField] TrafficSpawner m_TrafficSpawner;

        [Header("Vehicle Count Buttons (optional)")]
        [SerializeField] Button m_Btn500;
        [SerializeField] Button m_Btn1000;
        [SerializeField] Button m_Btn1500;
        [SerializeField] Button m_Btn2000;
        [SerializeField] Button m_Btn2500;
        [SerializeField] Button m_Btn3000;

        [Header("Colors")]
        [Tooltip("Shown when a button is the currently selected LOD/complexity/vehicle-count choice.")]
        [SerializeField] Color m_ActiveColor = new Color(0.18f, 0.56f, 1.00f);
        [Tooltip("Default color for a button that is not the current selection.")]
        [SerializeField] Color m_DefaultColor = Color.white;
        [Tooltip("Shown while hovering/pointing at a non-selected button.")]
        [SerializeField] Color m_HoverColor = new Color(0.4967f, 0.5828f, 0.6623f, 1f);
        [Tooltip("The All-buildings button always shows this color, regardless of selection or hover state.")]
        [SerializeField] Color m_AllBuildingsColor = new Color(0.4416667f, 0.4416667f, 0.4416667f, 0.5973f);
        [SerializeField] TextMeshProUGUI m_FpsCounterText;

        // --- Benchmark & Logging Setup ----------------------------------------
        [Header("Benchmark Camera & Path")]
        [Tooltip("The XR Origin (XR Rig) root transform to fly through the waypoints. Same transform ViewpointController moves.")]
        [SerializeField] Transform m_XROriginTransform;
        [Tooltip("Waypoints the rig flies through, looping back to the first one to form a closed loop.")]
        [SerializeField] Transform[] m_CameraWaypoints;
        [SerializeField] float m_CameraSpeed = 5f;

        [Header("Benchmark Parameters")]
        [SerializeField] bool m_AutoStartBenchmarkOnLaunch = true;
        [SerializeField] bool m_PilotTestOnly = true;
        [SerializeField] float m_WarmupDuration = 3.0f;
        [SerializeField] float m_ThermalCooldownDuration = 4.0f;

        // Profiling Recorders
        ProfilerRecorder m_CpuFrameTimeRecorder;
        ProfilerRecorder m_GpuFrameTimeRecorder;
        ProfilerRecorder m_TotalAllocatedMemoryRecorder;
        XRDisplaySubsystem m_DisplaySubsystem;
        List<XRDisplaySubsystem> m_DisplaySubsystems = new();

        StringBuilder m_CsvBuffer = new StringBuilder();
        bool m_IsLogging = false;
        int m_FrameIndex = 0;
        string m_CurrentMetadataHeader = "";
        string m_SessionCsvFilePath;

        // --- Initial State ------------------------------------------------------------
        int m_LOD = 1;
        int m_Complexity = 1000; // Fixed default value to match lookup
        int m_VehicleCount = 500;
        float m_FpsDeltaTime = 0.0f;

        GameObject[,] m_Objects;

        // X button on the left controller starts the benchmark matrix.
        InputDevice m_LeftControllerDevice;
        bool m_StartBenchmarkButtonWasPressed;

        void OnEnable()
        {
            m_CpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread Machine Frame Time");
            m_GpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GpuFrameTime");
            m_TotalAllocatedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Allocated Memory");

            SubsystemManager.GetSubsystems(m_DisplaySubsystems);
            if (m_DisplaySubsystems.Count > 0) m_DisplaySubsystem = m_DisplaySubsystems[0];

            // One timestamped file per app launch; every run within this launch appends to it.
            string fileName = $"Benchmark_Results_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            m_SessionCsvFilePath = Path.Combine(Application.persistentDataPath, fileName);
        }

        void OnDisable()
        {
            m_CpuFrameTimeRecorder.Dispose();
            m_GpuFrameTimeRecorder.Dispose();
            m_TotalAllocatedMemoryRecorder.Dispose();
        }

        void Awake()
        {
            // Initialize array in Awake() so it is populated before any UI calls or Start() loops
            m_Objects = new GameObject[6, 3]
            {
        { m_1000_LOD1,  m_1000_LOD2,  m_1000_LOD3  },
        { m_2000_LOD1,  m_2000_LOD2,  m_2000_LOD3  },
        { m_5000_LOD1,  m_5000_LOD2,  m_5000_LOD3  },
        { m_10000_LOD1, m_10000_LOD2, m_10000_LOD3 },
        { m_15000_LOD1, m_15000_LOD2, m_15000_LOD3 },
        { m_All_LOD1, m_All_LOD2, m_All_LOD3 },
            };
        }

        void Start()
        {
            Apply();
            HighlightLOD(m_LOD);
            HighlightComplexity(m_Complexity);
            HighlightVehicleCount(m_VehicleCount);

            if (m_AutoStartBenchmarkOnLaunch)
            {
                StartCoroutine(RunBenchmarkMatrixRoutine());
            }
        }

        void Update()
        {
            m_FpsDeltaTime += (Time.unscaledDeltaTime - m_FpsDeltaTime) * 0.1f;

            PollStartBenchmarkButton();

            if (m_FpsCounterText != null)
            {
                float fps = 1.0f / m_FpsDeltaTime;
                m_FpsCounterText.text = $"FPS: {Mathf.RoundToInt(fps)} | LOD: {m_LOD} | Bld: {(m_Complexity == -1 ? "All" : m_Complexity)} | Veh: {m_VehicleCount}";
            }

            if (m_IsLogging)
            {
                RecordPerFrameData();
            }
        }

        [ContextMenu("Start Matrix Benchmark")]
        public void StartBenchmark()
        {
            StartCoroutine(RunBenchmarkMatrixRoutine());
        }

        // X is the left controller's primaryButton (Y is secondaryButton, already
        // used by HandMenuActivator's menu toggle), polled the same way
        // ViewpointController polls its controllers.
        void PollStartBenchmarkButton()
        {
            if (!m_LeftControllerDevice.isValid)
            {
                var devices = new List<InputDevice>();
                InputDevices.GetDevicesWithCharacteristics(
                    InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, devices);
                if (devices.Count > 0) m_LeftControllerDevice = devices[0];
                else return;
            }

            if (m_LeftControllerDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed))
            {
                if (pressed && !m_StartBenchmarkButtonWasPressed)
                {
                    StartBenchmark();
                }
                m_StartBenchmarkButtonWasPressed = pressed;
            }
        }

        IEnumerator RunBenchmarkMatrixRoutine()
        {
            int[] lods = new int[] { 1, 2, 3 };
            // FIX 1: Aligned array values with ComplexityIndex lookup
            int[] buildingComplexities = new int[] { 1000, 2000, 5000, 10000, 15000, -1 };
            int[] vehicleCounts = new int[] { 500, 1000, 1500, 2000, 2500, 3000 };
            int repetitions = 3;

            int runCounter = 0;
            Debug.Log("[CityViewController] Starting Matrix Benchmark Execution...");

            foreach (int lod in lods)
            {
                foreach (int comp in buildingComplexities)
                {
                    foreach (int vehs in vehicleCounts)
                    {
                        for (int rep = 1; rep <= repetitions; rep++)
                        {
                            runCounter++;

                            SetLOD(lod);
                            SetComplexity(comp);
                            SetVehicleCount(vehs);

                            yield return new WaitForSeconds(m_WarmupDuration);

                            string bldLabel = (comp == -1) ? "All" : comp.ToString();
                            m_CurrentMetadataHeader = $"Run_{runCounter},LOD_{lod},Bld_{bldLabel},Veh_{vehs},Rep_{rep}";
                            StartLogging();

                            yield return StartCoroutine(PlayCameraPathRoutine());

                            StopAndSaveCsv($"Run_{runCounter}_LOD{lod}_Bld{bldLabel}_Veh{vehs}_Rep{rep}");

                            yield return new WaitForSeconds(m_ThermalCooldownDuration);

                            if (m_PilotTestOnly && runCounter >= 2)
                            {
                                Debug.Log("[CityViewController] Pilot Test complete! Halting runner.");
                                yield break;
                            }
                        }
                    }
                }
            }

            Debug.Log("[CityViewController] Full Benchmark Matrix complete!");
        }

        CharacterController m_XROriginCC;
        Rigidbody m_XROriginRb;

        // Flies the XR rig through m_CameraWaypoints and back to the first one,
        // forming a closed loop, so every run samples the same full circuit.
        IEnumerator PlayCameraPathRoutine()
        {
            if (m_XROriginTransform == null || m_CameraWaypoints == null || m_CameraWaypoints.Length < 2)
            {
                Debug.LogWarning("[CityViewController] Camera Waypoints not assigned. Fallback delay applied.");
                yield return new WaitForSeconds(5.0f);
                yield break;
            }

            if (m_XROriginCC == null) m_XROriginCC = m_XROriginTransform.GetComponent<CharacterController>();
            if (m_XROriginRb == null) m_XROriginRb = m_XROriginTransform.GetComponent<Rigidbody>();

            // Manual position/rotation assignment fights CharacterController collision
            // resolution and Rigidbody gravity, so both are suspended for the flythrough.
            SuspendXROriginPhysics();

            int loopCount = m_CameraWaypoints.Length;
            for (int i = 0; i < loopCount; i++)
            {
                Transform from = m_CameraWaypoints[i];
                Transform to = m_CameraWaypoints[(i + 1) % loopCount];
                Vector3 startPos = from.position;
                Vector3 endPos = to.position;
                float distance = Vector3.Distance(startPos, endPos);
                float duration = distance / m_CameraSpeed;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    m_XROriginTransform.position = Vector3.Lerp(startPos, endPos, t);

                    Vector3 dir = (endPos - startPos).normalized;
                    if (dir.sqrMagnitude > 0.001f)
                        m_XROriginTransform.rotation = Quaternion.LookRotation(dir);

                    yield return null;
                }
            }

            ResumeXROriginPhysics();
        }

        void SuspendXROriginPhysics()
        {
            if (m_XROriginCC != null) m_XROriginCC.enabled = false;
            if (m_XROriginRb != null) { m_XROriginRb.isKinematic = true; m_XROriginRb.linearVelocity = Vector3.zero; }
        }

        void ResumeXROriginPhysics()
        {
            if (m_XROriginCC != null) m_XROriginCC.enabled = true;
            if (m_XROriginRb != null) m_XROriginRb.isKinematic = false;
        }

        // --- Logging Internal Methods ----------------------------------------------
        void StartLogging()
        {
            m_CsvBuffer.Clear();
            m_FrameIndex = 0;
            m_IsLogging = true;
        }

        //Captures timestamp, delta time, calculated FPS, CPU time (ms), GPU time (ms), allocated RAM (MB), device temperature, and reprojection status on every frame update.
        void RecordPerFrameData()
        {
            m_FrameIndex++;
            float deltaTimeMs = Time.unscaledDeltaTime * 1000f;
            float fps = 1f / Time.unscaledDeltaTime;

            float cpuTimeMs = m_CpuFrameTimeRecorder.Valid ? m_CpuFrameTimeRecorder.LastValue * 1e-6f : -1f;
            float gpuTimeMs = m_GpuFrameTimeRecorder.Valid ? m_GpuFrameTimeRecorder.LastValue * 1e-6f : -1f;
            float ramMb = m_TotalAllocatedMemoryRecorder.Valid ? m_TotalAllocatedMemoryRecorder.LastValue / (1024f * 1024f) : -1f;

            float tempC = GetDeviceTemperature();
            bool isReprojected = CheckIfReprojected(); //measures user visual comfort

            m_CsvBuffer.AppendLine($"{m_CurrentMetadataHeader},{m_FrameIndex},{Time.time:F3},{deltaTimeMs:F2},{fps:F1},{cpuTimeMs:F2},{gpuTimeMs:F2},{ramMb:F1},{tempC:F1},{isReprojected}");
        }

        void StopAndSaveCsv(string runIdentifier)
        {
            m_IsLogging = false;
            try
            {
                // Every run appends to the same session file instead of writing its own CSV,
                // so a full matrix run produces one combined file, not one per run.
                bool fileExists = File.Exists(m_SessionCsvFilePath);

                using (StreamWriter writer = new StreamWriter(m_SessionCsvFilePath, append: true))
                {
                    if (!fileExists)
                    {
                        writer.WriteLine("Run,LOD,BuildingCount,VehicleCount,Repetition,FrameIndex,Timestamp,DeltaTimeMS,FPS,CpuTimeMS,GpuTimeMS,AllocatedRAM_MB,ThermalTempC,IsReprojected");
                    }

                    string[] lines = m_CsvBuffer.ToString().Split('\n');
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            writer.WriteLine(line);
                    }
                }

                Debug.Log($"[CityViewController] SUCCESS! Appended {runIdentifier}. File saved to: {m_SessionCsvFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CityViewController] CRITICAL SAVE ERROR: {ex.Message}");
            }
        }

        bool CheckIfReprojected()
        {
            if (m_DisplaySubsystem != null && m_DisplaySubsystem.TryGetDroppedFrameCount(out int droppedFrames))
            {
                return droppedFrames > 0;
            }
            return Time.unscaledDeltaTime > (1f / 72f);
        }

        //Query's the device battery temperature directly from the Quest operating system.
        float GetDeviceTemperature()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intentFilter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED"))
                using (var batteryStatus = currentActivity.Call<AndroidJavaObject>("registerReceiver", null, intentFilter))
                {
                    int temp = batteryStatus.Call<int>("getIntExtra", "temperature", 0);
                    return temp / 10f;
                }
            }
            catch { return -1f; }
#else
            return 25f;
#endif
        }

        // dynamic parameterized methods taking integers
        public void SetLOD(int level)
        {
            m_LOD = Mathf.Clamp(level, 1, 3);
            Apply();
            HighlightLOD(m_LOD);
        }

        public void SetComplexity(int count)
        {
            m_Complexity = count;
            Apply();
            HighlightComplexity(count);
        }

        public void SetLOD1() { SetLOD(1); }
        public void SetLOD2() { SetLOD(2); }
        public void SetLOD3() { SetLOD(3); }

        public void SetComplexity1000() { SetComplexity(1000); }
        public void SetComplexity2000() { SetComplexity(2000); }
        public void SetComplexity5000() { SetComplexity(5000); }
        public void SetComplexity10000() { SetComplexity(10000); }
        public void SetComplexity15000() { SetComplexity(15000); }
        public void SetComplexityAll() { SetComplexity(-1); }

        public void SetVehicleCount500() { SetVehicleCount(500); }
        public void SetVehicleCount1000() { SetVehicleCount(1000); }
        public void SetVehicleCount1500() { SetVehicleCount(1500); }
        public void SetVehicleCount2000() { SetVehicleCount(2000); }
        public void SetVehicleCount2500() { SetVehicleCount(2500); }
        public void SetVehicleCount3000() { SetVehicleCount(3000); }

        void SetVehicleCount(int count)
        {
            m_VehicleCount = count;
            if (m_TrafficSpawner != null)
                m_TrafficSpawner.RespawnVehicles(count);
            HighlightVehicleCount(count);
        }

        void Apply()
        {
            // Guard against execution before Awake completes
            if (m_Objects == null) return;

            int ci = ComplexityIndex(m_Complexity);
            int li = m_LOD - 1;

            // Bounds checking
            if (ci < 0 || ci >= 6 || li < 0 || li >= 3)
            {
                Debug.LogError($"[CityViewController] Out of bounds access in Apply(): ComplexityIndex={ci}, LODIndex={li}");
                return;
            }

            for (int c = 0; c < 6; c++)
            {
                for (int l = 0; l < 3; l++)
                {
                    if (m_Objects[c, l] != null)
                    {
                        m_Objects[c, l].SetActive(c == ci && l == li);
                    }
                }
            }
        }

        static int ComplexityIndex(int count) => count switch
        {
            1000 => 0,
            2000 => 1,
            5000 => 2,
            10000 => 3,
            15000 => 4,
            -1 => 5,
            _ => 0,
        };

        void HighlightLOD(int level)
        {
            Highlight(m_BtnLOD1, level == 1);
            Highlight(m_BtnLOD2, level == 2);
            Highlight(m_BtnLOD3, level == 3);
        }

        void HighlightComplexity(int count)
        {
            Highlight(m_Btn_City_1000, count == 1000);
            Highlight(m_Btn_City_2000, count == 2000);
            Highlight(m_Btn_City_5000, count == 5000);
            Highlight(m_Btn_City_10000, count == 10000);
            Highlight(m_Btn_City_15000, count == 15000);
            SetAlwaysColor(m_BtnAll, m_AllBuildingsColor);
        }

        void HighlightVehicleCount(int count)
        {
            Highlight(m_Btn500, count == 500);
            Highlight(m_Btn1000, count == 1000);
            Highlight(m_Btn1500, count == 1500);
            Highlight(m_Btn2000, count == 2000);
            Highlight(m_Btn2500, count == 2500);
            Highlight(m_Btn3000, count == 3000);
        }

        void Highlight(Button btn, bool active)
        {
            if (btn == null) return;
            var cb = btn.colors;
            Color normal = active ? m_ActiveColor : m_DefaultColor;
            cb.normalColor = normal;
            cb.highlightedColor = active ? m_ActiveColor : m_HoverColor;
            cb.pressedColor = m_ActiveColor;
            // Matches normalColor so a button doesn't look stuck once EventSystem
            // marks it "Selected" after being clicked (that state outranks Highlighted).
            cb.selectedColor = normal;
            btn.colors = cb;
        }

        void SetAlwaysColor(Button btn, Color color)
        {
            if (btn == null) return;
            var cb = btn.colors;
            cb.normalColor = color;
            cb.highlightedColor = color;
            cb.pressedColor = color;
            cb.selectedColor = color;
            btn.colors = cb;
        }
    }
}