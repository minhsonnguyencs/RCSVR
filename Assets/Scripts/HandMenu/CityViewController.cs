using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

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
        [SerializeField] Button m_Btn0;
        [SerializeField] Button m_Btn100;
        [SerializeField] Button m_Btn500;
        [SerializeField] Button m_Btn1000;
        [SerializeField] Button m_Btn1500;

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
        [SerializeField] float m_CameraSpeed = 23.6f;

        [Header("Benchmark Parameters")]
        [SerializeField] bool m_AutoStartBenchmarkOnLaunch = false;

        [SerializeField] float m_WarmupDuration = 5.0f;
        [SerializeField] float m_ThermalCooldownDuration = 5.0f;
        [SerializeField] float m_LODBuildingSwitchDelay = 3.0f;

        // Profiling Recorders
        ProfilerRecorder m_CpuFrameTimeRecorder;
        ProfilerRecorder m_GpuFrameTimeRecorder;
        XRDisplaySubsystem m_DisplaySubsystem;
        List<XRDisplaySubsystem> m_DisplaySubsystems = new();

        HandMenuActivator m_HandMenuActivator;

        StringBuilder m_CsvBuffer = new StringBuilder();
        bool m_IsLogging = false;
        int m_FrameIndex = 0;
        string m_CurrentMetadataHeader = "";
        string m_SessionCsvFilePath;

        // --- Initial State ------------------------------------------------------------
        int m_LOD = 1;
        int m_Complexity = 1000;
        int m_VehicleCount = 0;
        float m_FpsDeltaTime = 0.0f;

        GameObject[,] m_Objects;

        // X button on the left controller starts the benchmark matrix.
        InputDevice m_LeftControllerDevice;
        bool m_StartBenchmarkButtonWasPressed;
        bool m_SkipNextButtonEdge = true;

        void OnEnable()
        {
            m_CpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            m_GpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time");

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

#if UNITY_ANDROID && !UNITY_EDITOR
            m_CurrentActivity?.Dispose();
            m_CurrentActivity = null;
            m_BatteryIntentFilter?.Dispose();
            m_BatteryIntentFilter = null;
#endif
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

            m_HandMenuActivator = GetComponent<HandMenuActivator>();
        }

        void Start()
        {
            Apply();
            HighlightLOD(m_LOD);
            HighlightComplexity(m_Complexity);
            HighlightVehicleCount(m_VehicleCount);

            if (m_AutoStartBenchmarkOnLaunch)
            {
                RestartBenchmark();
            }
        }

        void Update()
        {
            m_FpsDeltaTime += (Time.unscaledDeltaTime - m_FpsDeltaTime) * 0.1f;

            PollStartBenchmarkButton();

            if (m_FpsCounterText != null)
            {
                float fps = 1.0f / m_FpsDeltaTime;
                float cpuMs = m_CpuFrameTimeRecorder.Valid ? m_CpuFrameTimeRecorder.LastValue * 1e-6f : -1f;
                float gpuMs = (m_DisplaySubsystem != null && m_DisplaySubsystem.TryGetAppGPUTimeLastFrame(out float xrGpuMs)) ? xrGpuMs : (m_GpuFrameTimeRecorder.Valid ? m_GpuFrameTimeRecorder.LastValue * 1e-6f : -1f);
                float ramMb = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
                m_FpsCounterText.text = $"FPS: {Mathf.RoundToInt(fps)} | CPU: {cpuMs:F1}ms | GPU: {gpuMs:F1}ms | RAM: {ramMb:F0}MB | LOD: {m_LOD} | Bld: {(m_Complexity == -1 ? "All" : m_Complexity)} | Veh: {m_VehicleCount}";
            }

            if (m_IsLogging)
            {
                RecordPerFrameData();
            }
        }

        [ContextMenu("Start Matrix Benchmark")]
        public void StartBenchmark()
        {
            RestartBenchmark();
        }

        Coroutine m_ActiveBenchmarkCoroutine;
        bool m_BenchmarkRunning;

        void RestartBenchmark()
        {
            if (m_BenchmarkRunning)
            {
                Debug.LogWarning("[CityViewController] Benchmark restart requested while a sweep is already running; ignoring.");
                return;
            }

            m_BenchmarkRunning = true;
            m_ActiveBenchmarkCoroutine = StartCoroutine(RunBenchmarkMatrixRoutine());
        }

        void PollStartBenchmarkButton()
        {
            if (!m_LeftControllerDevice.isValid)
            {
                var devices = new List<InputDevice>();
                InputDevices.GetDevicesWithCharacteristics(
                    InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, devices);
                if (devices.Count > 0)
                {
                    m_LeftControllerDevice = devices[0];
                    m_SkipNextButtonEdge = true;
                }
                else return;
            }

            if (m_LeftControllerDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed))
            {
                if (pressed && !m_StartBenchmarkButtonWasPressed && !m_SkipNextButtonEdge)
                {
                    StartBenchmark();
                }
                m_StartBenchmarkButtonWasPressed = pressed;
                m_SkipNextButtonEdge = false;
            }
        }

        IEnumerator RunBenchmarkMatrixRoutine()
        {
            int[] lods = new int[] { 1, 2, 3 };
            // FIX 1: Aligned array values with ComplexityIndex lookup
            int[] buildingComplexities = new int[] { 1000, 2000, 5000, 10000, 15000 };
            int repetitions = 1;
            int runCounter = 0;
            // Vehicle count is whatever the user selected before pressing X; the matrix only sweeps LOD x building complexity.
            int vehs = m_VehicleCount;
            Debug.Log($"[CityViewController] Starting Matrix Benchmark Execution... (Veh_{vehs} fixed)");
            ResolveXROriginPhysicsComponents();
            SuspendXROriginPhysics();
            if (m_HandMenuActivator != null) m_HandMenuActivator.SetForcedVisible(true);
            yield return WarmupBuildingShadersRoutine(buildingComplexities, lods);

            int? lastComplexity = null;
            try
            {
                foreach (int comp in buildingComplexities)
                {
                    foreach (int lod in lods)
                    {
                        for (int rep = 1; rep <= repetitions; rep++)
                        {
                            runCounter++;

                            try
                            {
                                SetLOD(lod);
                                if (comp != lastComplexity)
                                {
                                    SetComplexity(comp);
                                    lastComplexity = comp;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[CityViewController] Run_{runCounter} (LOD_{lod}, Bld_{comp}, Veh_{vehs}) setup failed, continuing to next run: {ex}");
                            }

                            // Let the LOD/building complexity switch settle before the general warmup
                            yield return new WaitForSeconds(m_LODBuildingSwitchDelay);

                            yield return new WaitForSeconds(m_WarmupDuration);

                            string bldLabel = (comp == -1) ? "All" : comp.ToString();
                            m_CurrentMetadataHeader = $"Run_{runCounter},LOD_{lod},Bld_{bldLabel},Veh_{vehs},Rep_{rep}";
                            StartLogging();

                            yield return PlayCameraPathRoutine();

                            StopAndSaveCsv($"Run_{runCounter}_LOD{lod}_Bld{bldLabel}_Veh{vehs}_Rep{rep}");

                            yield return new WaitForSeconds(m_ThermalCooldownDuration);
                        }
                    }
                }

                Debug.Log("[CityViewController] Full Benchmark Matrix complete!");
            }
            finally
            {
                ResumeXROriginPhysics();
                if (m_HandMenuActivator != null) m_HandMenuActivator.SetForcedVisible(false);
                m_BenchmarkRunning = false;
                m_ActiveBenchmarkCoroutine = null;
            }
        }

        IEnumerator WarmupBuildingShadersRoutine(int[] buildingComplexities, int[] lods)
        {
            if (m_Objects == null) yield break;

            Debug.Log("[CityViewController] Warming up building shaders/textures...");
            foreach (int comp in buildingComplexities)
            {
                int ci = ComplexityIndex(comp);
                if (ci < 0 || ci >= 6) continue;

                foreach (int lod in lods)
                {
                    int li = lod - 1;
                    if (li < 0 || li >= 3) continue;

                    GameObject go = m_Objects[ci, li];
                    if (go == null) continue;

                    go.SetActive(true);
                    yield return null;
                }
            }
            Debug.Log("[CityViewController] Shader warmup complete.");
        }

        CharacterController m_XROriginCC;
        Rigidbody m_XROriginRb;
        XRBodyTransformer m_XROriginBodyTransformer;

        void ResolveXROriginPhysicsComponents()
        {
            if (m_XROriginTransform == null) return;
            if (m_XROriginCC == null) m_XROriginCC = m_XROriginTransform.GetComponent<CharacterController>();
            if (m_XROriginRb == null) m_XROriginRb = m_XROriginTransform.GetComponent<Rigidbody>();
            if (m_XROriginBodyTransformer == null) m_XROriginBodyTransformer = m_XROriginTransform.GetComponentInChildren<XRBodyTransformer>();
        }

        IEnumerator PlayCameraPathRoutine()
        {
            if (m_XROriginTransform == null || m_CameraWaypoints == null || m_CameraWaypoints.Length < 2)
            {
                Debug.LogWarning("[CityViewController] Camera Waypoints not assigned. Fallback delay applied.");
                yield return new WaitForSeconds(5.0f);
                yield break;
            }

            IEnumerator segments = FlythroughSegments();
            bool moveNext = true;
            while (moveNext)
            {
                try
                {
                    moveNext = segments.MoveNext();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CityViewController] Camera flythrough failed, skipping to next run: {ex}");
                    moveNext = false;
                }

                if (moveNext)
                    yield return segments.Current;
            }
        }

        IEnumerator FlythroughSegments()
        {
            int loopCount = m_CameraWaypoints.Length;
            for (int i = 0; i < loopCount; i++)
            {
                Transform from = m_CameraWaypoints[i];
                Transform to = m_CameraWaypoints[(i + 1) % loopCount];

                if (from == null || to == null)
                {
                    Debug.LogError($"[CityViewController] Camera waypoint at index {i} is missing; skipping segment.");
                    continue;
                }

                Vector3 startPos = from.position;
                Vector3 endPos = to.position;
                float distance = Vector3.Distance(startPos, endPos);
                // m_CameraSpeed <= 0 would make duration Infinity, so elapsed < duration
                // never becomes false and this segment hangs forever.
                if (m_CameraSpeed <= 0f)
                {
                    Debug.LogError("[CityViewController] m_CameraSpeed must be > 0; skipping segment.");
                    continue;
                }
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
        }

        void SuspendXROriginPhysics()
        {
            // Disabled before the CharacterController so it can't sneak in one more
            // Move() call on the same frame the CharacterController goes inactive.
            if (m_XROriginBodyTransformer != null) m_XROriginBodyTransformer.enabled = false;
            if (m_XROriginCC != null) m_XROriginCC.enabled = false;
            if (m_XROriginRb != null) { m_XROriginRb.isKinematic = true; m_XROriginRb.linearVelocity = Vector3.zero; }
        }

        void ResumeXROriginPhysics()
        {
            if (m_XROriginCC != null) m_XROriginCC.enabled = true;
            if (m_XROriginBodyTransformer != null) m_XROriginBodyTransformer.enabled = true;
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
            float gpuTimeMs = (m_DisplaySubsystem != null && m_DisplaySubsystem.TryGetAppGPUTimeLastFrame(out float xrGpuTimeMs)) ? xrGpuTimeMs : (m_GpuFrameTimeRecorder.Valid ? m_GpuFrameTimeRecorder.LastValue * 1e-6f : -1f);
            float ramMb = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);

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

        // Device temperature changes slowly; querying it via JNI/Binder every frame during
        // logging (up to 72x/sec) is wasteful, so the result is cached and refreshed at most once/sec.
        const float kTempQueryIntervalSeconds = 1.0f;
        float m_CachedTempC = 25f;
        float m_LastTempQueryTime = float.NegativeInfinity;
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject m_CurrentActivity;
        AndroidJavaObject m_BatteryIntentFilter;
#endif

        //Query's the device battery temperature directly from the Quest operating system.
        float GetDeviceTemperature()
        {
            if (Time.unscaledTime - m_LastTempQueryTime < kTempQueryIntervalSeconds)
                return m_CachedTempC;
            m_LastTempQueryTime = Time.unscaledTime;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (m_CurrentActivity == null)
                {
                    using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        m_CurrentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }
                if (m_BatteryIntentFilter == null)
                {
                    m_BatteryIntentFilter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED");
                }
                using (var batteryStatus = m_CurrentActivity.Call<AndroidJavaObject>("registerReceiver", null, m_BatteryIntentFilter))
                {
                    int temp = batteryStatus.Call<int>("getIntExtra", "temperature", 0);
                    m_CachedTempC = temp / 10f;
                }
            }
            catch { m_CachedTempC = -1f; }
#else
            m_CachedTempC = 25f;
#endif
            return m_CachedTempC;
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

        public void SetVehicleCount0() { SetVehicleCount(0); }
        public void SetVehicleCount100() { SetVehicleCount(100); }
        public void SetVehicleCount500() { SetVehicleCount(500); }
        public void SetVehicleCount1000() { SetVehicleCount(1000); }
        public void SetVehicleCount1500() { SetVehicleCount(1500); }

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
            Highlight(m_Btn0, count == 0);
            Highlight(m_Btn100, count == 100);
            Highlight(m_Btn500, count == 500);
            Highlight(m_Btn1000, count == 1000);
            Highlight(m_Btn1500, count == 1500);
        }

        void Highlight(Button btn, bool active)
        {
            if (btn == null) return;
            var cb = btn.colors;
            Color normal = active ? m_ActiveColor : m_DefaultColor;
            cb.normalColor = normal;
            cb.highlightedColor = active ? m_ActiveColor : m_HoverColor;
            cb.pressedColor = m_ActiveColor;
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