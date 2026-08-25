using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Unity.VRTemplate
{
    public class CityViewController : MonoBehaviour
    {
        // --- City Complexity ------------------------------------------------

        [Header("10 Buildings")]
        [SerializeField] GameObject m_10_LOD1;
        [SerializeField] GameObject m_10_LOD2;
        [SerializeField] GameObject m_10_LOD3;

        [Header("20 Buildings")]
        [SerializeField] GameObject m_20_LOD1;
        [SerializeField] GameObject m_20_LOD2;
        [SerializeField] GameObject m_20_LOD3;

        [Header("50 Buildings")]
        [SerializeField] GameObject m_50_LOD1;
        [SerializeField] GameObject m_50_LOD2;
        [SerializeField] GameObject m_50_LOD3;

        [Header("150 Buildings")]
        [SerializeField] GameObject m_150_LOD1;
        [SerializeField] GameObject m_150_LOD2;
        [SerializeField] GameObject m_150_LOD3;

        [Header("250 Buildings")]
        [SerializeField] GameObject m_250_LOD1;
        [SerializeField] GameObject m_250_LOD2;
        [SerializeField] GameObject m_250_LOD3;

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
        [SerializeField] Button m_Btn10;
        [SerializeField] Button m_Btn20;
        [SerializeField] Button m_Btn50;
        [SerializeField] Button m_Btn150;
        [SerializeField] Button m_Btn250;
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
        [SerializeField] Color m_ActiveColor = new Color(0.18f, 0.56f, 1.00f);
        [SerializeField] Color m_InactiveColor = new Color(0.13f, 0.13f, 0.16f);
        [SerializeField] Text m_FpsCounterText;

        // --- Benchmark & Logging Setup ----------------------------------------
        [Header("Benchmark Camera & Path")]
        [SerializeField] Transform m_BenchmarkCamera;
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

        // --- Initial State ------------------------------------------------------------
        int m_LOD = 1;
        int m_Complexity = 10; // Fixed default value to match lookup
        int m_VehicleCount = 500;
        float m_FpsDeltaTime = 0.0f;

        GameObject[,] m_Objects;

        void OnEnable()
        {
            m_CpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread Machine Frame Time");
            m_GpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GpuFrameTime");
            m_TotalAllocatedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Allocated Memory");

            SubsystemManager.GetSubsystems(m_DisplaySubsystems);
            if (m_DisplaySubsystems.Count > 0) m_DisplaySubsystem = m_DisplaySubsystems[0];
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
        { m_10_LOD1,  m_10_LOD2,  m_10_LOD3  },
        { m_20_LOD1,  m_20_LOD2,  m_20_LOD3  },
        { m_50_LOD1,  m_50_LOD2,  m_50_LOD3  },
        { m_150_LOD1, m_150_LOD2, m_150_LOD3 },
        { m_250_LOD1, m_250_LOD2, m_250_LOD3 },
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

        IEnumerator RunBenchmarkMatrixRoutine()
        {
            int[] lods = new int[] { 1, 2, 3 };
            // FIX 1: Aligned array values with ComplexityIndex lookup
            int[] buildingComplexities = new int[] { 10, 20, 50, 150, 250, -1 };
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

        IEnumerator PlayCameraPathRoutine()
        {
            if (m_BenchmarkCamera == null || m_CameraWaypoints == null || m_CameraWaypoints.Length < 2)
            {
                Debug.LogWarning("[CityViewController] Camera Waypoints not assigned. Fallback delay applied.");
                yield return new WaitForSeconds(5.0f);
                yield break;
            }

            for (int i = 0; i < m_CameraWaypoints.Length - 1; i++)
            {
                Vector3 startPos = m_CameraWaypoints[i].position;
                Vector3 endPos = m_CameraWaypoints[i + 1].position;
                float distance = Vector3.Distance(startPos, endPos);
                float duration = distance / m_CameraSpeed;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    m_BenchmarkCamera.position = Vector3.Lerp(startPos, endPos, t);

                    Vector3 dir = (endPos - startPos).normalized;
                    if (dir.sqrMagnitude > 0.001f)
                        m_BenchmarkCamera.rotation = Quaternion.LookRotation(dir);

                    yield return null;
                }
            }
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
                // FIX 2: Root path export without custom directory sub-folders for direct SideQuest visibility
                // 2. Define the file name
                string safeIdentifier = runIdentifier.Replace(",", "_").Replace(" ", "");
                string fileName = $"{safeIdentifier}.csv";
                // 3. Combine folder path and file name into a full path
                string fullFilePath = Path.Combine(Application.persistentDataPath, fileName);
                Debug.Log($"[CityViewController] Start of the method: {fullFilePath}");
                // 4. Pass the combined path to StreamWriter
                using (StreamWriter writer = new StreamWriter(fullFilePath, append: false))
                {
                    writer.WriteLine("Run,LOD,BuildingCount,VehicleCount,Repetition,FrameIndex,Timestamp,DeltaTimeMS,FPS,CpuTimeMS,GpuTimeMS,AllocatedRAM_MB,ThermalTempC,IsReprojected");

                    string[] lines = m_CsvBuffer.ToString().Split('\n');
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            writer.WriteLine(line);
                    }
                }

                Debug.Log($"[CityViewController] SUCCESS! File saved to: {fullFilePath}");
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

        public void SetComplexity10() { SetComplexity(10); }
        public void SetComplexity20() { SetComplexity(20); }
        public void SetComplexity50() { SetComplexity(50); }
        public void SetComplexity150() { SetComplexity(150); }
        public void SetComplexity250() { SetComplexity(250); }
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
            10 => 0,
            20 => 1,
            50 => 2,
            150 => 3,
            250 => 4,
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
            Highlight(m_Btn10, count == 10);
            Highlight(m_Btn20, count == 20);
            Highlight(m_Btn50, count == 50);
            Highlight(m_Btn150, count == 150);
            Highlight(m_Btn250, count == 250);
            Highlight(m_BtnAll, count == -1);
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
            cb.normalColor = active ? m_ActiveColor : m_InactiveColor;
            btn.colors = cb;
        }
    }
}