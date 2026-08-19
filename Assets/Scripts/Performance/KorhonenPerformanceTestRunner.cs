using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using TMPro;

namespace Unity.VRTemplate
{
    public class KorhonenPerformanceTestRunner : MonoBehaviour
    {
        [System.Serializable]
        public struct TestScenario
        {
            public string scenarioName;
            public string roadFileName;
            public int vehicleCount;
            public GameObject vehiclePrefab; // Simple, LaneUnaware, or DeadlockEscape
            public RoutingMode routingPolicy; // Random, Static, TrafficAware
            public bool useDemandModel;
            public bool enableTrafficLights;
        }

        public enum RoutingMode { Random, Static, TrafficAware }

        [Header("Automated Framework Matrix Setup")]
        [SerializeField] private List<TestScenario> m_TestMatrix = new List<TestScenario>();
        [SerializeField] private float m_WarmupDuration = 5.0f;  // Allow traffic flow to stabilize
        [SerializeField] private float m_SamplingDuration = 15.0f; // Sample performance for 15s per scenario
        [SerializeField] private float m_SampleInterval = 1.0f;     // Capture metrics every 1s

        [Header("UI & Feedback")]
        [SerializeField] private TextMeshProUGUI m_StatusText;

        // System References
        [Header("System References")]
        [SerializeField] private RoadNetworkManager m_RoadNetworkManager;
        [SerializeField] private TrafficSpawner m_TrafficSpawner;
        [SerializeField] private TrafficDemandManager m_TrafficDemandManager;
        [SerializeField] private TrafficLightSystem m_TrafficLightSystem;

        // Hardware Profilers
        private ProfilerRecorder m_CpuRecorder;
        private ProfilerRecorder m_GpuRecorder;
        private float m_FpsDeltaTime = 0.0f;
        private List<string> m_CsvData = new List<string>();

        void OnEnable()
        {
            m_CpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            m_GpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time");
        }

        void OnDisable()
        {
            m_CpuRecorder.Dispose();
            m_GpuRecorder.Dispose();
        }

        void Start()
        {
            // Set up standardized CSV headers matching our clean format
            m_CsvData.Add("Timestamp_s,ScenarioName,RoadFile,VehicleCount,PrefabName,RoutingPolicy,DemandModel,TrafficLights,FPS,FrameTime_ms,CPUTime_ms,GPUTime_ms,AllocatedRAM_MB,ReservedRAM_MB");

            if (m_TestMatrix.Count > 0)
            {
                StartCoroutine(RunKorhonenBenchmarkSuite());
            }
            else
            {
                Debug.LogWarning("[Korhonen Test] Test matrix is empty! Add scenarios in Inspector.");
            }
        }

        void Update()
        {
            // Frame timing calculation
            m_FpsDeltaTime += (Time.unscaledDeltaTime - m_FpsDeltaTime) * 0.1f;
        }

        private IEnumerator RunKorhonenBenchmarkSuite()
        {
            Debug.Log($"[Korhonen Test] Starting automated performance suite ({m_TestMatrix.Count} scenarios)...");

            for (int i = 0; i < m_TestMatrix.Count; i++)
            {
                TestScenario scenario = m_TestMatrix[i];
                UpdateUI($"Running Test [{i + 1}/{m_TestMatrix.Count}]: {scenario.scenarioName}");

                // 1. Configure System Axis via Reflection/Inspector setup
                ApplyScenarioSettings(scenario);

                // 2. Warmup Phase (let traffic populate routes)
                UpdateUI($"[{scenario.scenarioName}] Warming up ({m_WarmupDuration}s)...");
                yield return new WaitForSeconds(m_WarmupDuration);

                // 3. Automated Data Sampling Phase
                float elapsedTime = 0f;
                while (elapsedTime < m_SamplingDuration)
                {
                    yield return new WaitForSeconds(m_SampleInterval);
                    elapsedTime += m_SampleInterval;

                    RecordPerformanceMetric(scenario);
                    UpdateUI($"[{scenario.scenarioName}] Progress: {elapsedTime:F0}/{m_SamplingDuration}s");
                }

                // 4. Save progress automatically after each test set
                ExportCsvFile();
            }

            UpdateUI("Korhonen Automated Testing Complete! CSV Exported.");
            Debug.Log("[Korhonen Test] Benchmark suite finished successfully.");
        }

        private void ApplyScenarioSettings(TestScenario config)
        {
            // Apply Road File
            if (m_RoadNetworkManager != null)
                SetFieldValue(m_RoadNetworkManager, "FileName", config.roadFileName);

            // Apply Spawner Controls
            if (m_TrafficSpawner != null)
            {
                SetFieldValue(m_TrafficSpawner, "VehicleCount", config.vehicleCount);
                SetFieldValue(m_TrafficSpawner, "VehiclePrefab", config.vehiclePrefab);
            }

            // Apply Routing & Demand Policy
            if (m_TrafficDemandManager != null)
                SetFieldValue(m_TrafficDemandManager, "UseDemandModel", config.useDemandModel);

            // Apply Traffic Light System Toggle
            if (m_TrafficLightSystem != null)
                SetFieldValue(m_TrafficLightSystem, "EnableTrafficLights", config.enableTrafficLights);
        }

        private void RecordPerformanceMetric(TestScenario config)
        {
            float fps = 1.0f / m_FpsDeltaTime;
            float totalFrameTimeMs = m_FpsDeltaTime * 1000f;

            float cpuTimeMs = m_CpuRecorder.Valid && m_CpuRecorder.Count > 0 ? (float)(m_CpuRecorder.LastValue * 1e-6) : totalFrameTimeMs;
            float gpuTimeMs = m_GpuRecorder.Valid && m_GpuRecorder.Count > 0 ? (float)(m_GpuRecorder.LastValue * 1e-6) : 0f;

            float allocatedRamMB = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
            float reservedRamMB = Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f);

            string prefabName = config.vehiclePrefab != null ? config.vehiclePrefab.name : "None";

            // Enforce InvariantCulture (dots for decimals) across clean, distinct CSV columns
            string row = string.Format(CultureInfo.InvariantCulture,
                "{0:F2},{1},{2},{3},{4},{5},{6},{7},{8},{9:F2},{10:F2},{11:F2},{12:F1},{13:F1}",
                Time.time, config.scenarioName, config.roadFileName, config.vehicleCount,
                prefabName, config.routingPolicy.ToString(), config.useDemandModel,
                config.enableTrafficLights, Mathf.RoundToInt(fps), totalFrameTimeMs,
                cpuTimeMs, gpuTimeMs, allocatedRamMB, reservedRamMB);

            m_CsvData.Add(row);
        }

        public void ExportCsvFile()
        {
            string filePath = Path.Combine(Application.persistentDataPath, "Korhonen_Automated_Performance_Log.csv");
            File.WriteAllLines(filePath, m_CsvData);
            Debug.Log($"[Korhonen Test] CSV file updated: {filePath}");
        }

        private void UpdateUI(string message)
        {
            if (m_StatusText != null) m_StatusText.text = message;
        }

        private void SetFieldValue(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
            }
            else
            {
                var prop = target.GetType().GetProperty(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite) prop.SetValue(target, value, null);
            }
        }
    }
}
