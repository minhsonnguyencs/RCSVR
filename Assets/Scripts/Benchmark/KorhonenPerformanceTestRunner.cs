using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using TMPro;

namespace Unity.VRTemplate
{
    public class KorhonenPerformanceTestRunner : MonoBehaviour
    {
        public enum RoutingMode { Random, Static, TrafficAware }

        [System.Serializable]
        public struct TestScenario
        {
            [Header("Scenario Identification")]
            public string scenarioName;

            [Header("1. City & Geometry Controls")]
            public string roadFileName;        // Road map JSON file name
            [Range(1, 3)] public int lodLevel; // 1 = Low, 2 = Medium, 3 = High LOD

            [Header("2. Traffic Agent & Spawner")]
            public int vehicleCount;            // Vehicle population level
            public GameObject vehiclePrefab;    // Car_Simple, Car_LaneUnaware, Car_DeadlockEscape

            [Header("3. Routing Policy")]
            public RoutingMode routingPolicy;   // Random, Static, TrafficAware

            [Header("4. Demand Model & Traffic Signals")]
            public bool useDemandModel;         // TrafficDemandManager toggle[cite: 1]
            public bool enableTrafficLights;    // TrafficLightSystem toggle[cite: 1]
        }

        [Header("Automated Benchmark Configuration")]
        [SerializeField] private List<TestScenario> m_TestMatrix = new List<TestScenario>();
        [SerializeField] private float m_WarmupDuration = 5.0f;   // Seconds to let traffic stabilize
        [SerializeField] private float m_SamplingDuration = 15.0f; // Duration to sample per test
        [SerializeField] private float m_SampleInterval = 1.0f;     // Interval between metric snapshots

        [Header("UI Feedback")]
        [SerializeField] private TMP_Text m_StatusText; // Works with both TextMeshPro and Canvas TMP

        [Header("System Component References")]
        [SerializeField] private MonoBehaviour m_CityViewController;   // Manages visual city LOD
        [SerializeField] private MonoBehaviour m_RoadNetworkManager;    // Manages roads and routing[cite: 1]
        [SerializeField] private MonoBehaviour m_TrafficSpawner;         // Manages vehicle counts & prefabs[cite: 1]
        [SerializeField] private MonoBehaviour m_TrafficDemandManager;   // Manages supply/demand zones[cite: 1]
        [SerializeField] private MonoBehaviour m_TrafficLightSystem;     // Manages procedural signals[cite: 1]

        // Profiler Recorders
        private ProfilerRecorder m_CpuRecorder;
        private ProfilerRecorder m_GpuRecorder;
        private float m_FpsDeltaTime = 0.0f;
        private List<string> m_CsvData = new List<string>();
        private bool m_IsRunning = false;

        void OnEnable()
        {
            try
            {
                m_CpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
                m_GpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Korhonen Runner] Hardware ProfilerRecorder initialization warning: {ex.Message}");
            }
        }

        void OnDisable()
        {
            if (m_CpuRecorder.Valid) m_CpuRecorder.Dispose();
            if (m_GpuRecorder.Valid) m_GpuRecorder.Dispose();
        }

        void Start()
        {
            // Initialize explicit, clean CSV headers
            m_CsvData.Add("Timestamp_s,ScenarioName,RoadFile_Area,LODLevel,VehicleCount,PrefabName,RoutingPolicy,DemandModel,TrafficLights,FPS,FrameTime_ms,CPUTime_ms,GPUTime_ms,AllocatedRAM_MB,ReservedRAM_MB");

            if (m_TestMatrix != null && m_TestMatrix.Count > 0)
            {
                StartCoroutine(RunKorhonenBenchmarkSuite());
            }
            else
            {
                UpdateUI("Error: Benchmark Matrix is empty! Configure scenarios in Inspector.");
                Debug.LogError("[Korhonen Runner] No test scenarios populated in m_TestMatrix.");
            }
        }

        void Update()
        {
            m_FpsDeltaTime += (Time.unscaledDeltaTime - m_FpsDeltaTime) * 0.1f;
        }

        private IEnumerator RunKorhonenBenchmarkSuite()
        {
            m_IsRunning = true;
            Debug.Log($"[Korhonen Runner] Starting automated benchmark suite ({m_TestMatrix.Count} scenarios)...");

            for (int i = 0; i < m_TestMatrix.Count; i++)
            {
                TestScenario scenario = m_TestMatrix[i];
                UpdateUI($"Test [{i + 1}/{m_TestMatrix.Count}]: {scenario.scenarioName}");

                // 1. Apply Scenario Configuration
                ApplyScenarioSettings(scenario);

                // 2. Warmup Phase
                UpdateUI($"[{scenario.scenarioName}] Warming up ({m_WarmupDuration}s)...");
                yield return new WaitForSeconds(m_WarmupDuration);

                // 3. Automated Sampling Phase
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

            m_IsRunning = false;
            UpdateUI("Korhonen Benchmark Suite Complete! Data Saved.");
            Debug.Log("[Korhonen Runner] Suite completed successfully.");
        }

        private void ApplyScenarioSettings(TestScenario config)
        {
            // Set Geometry LOD Level
            if (m_CityViewController != null)
            {
                string lodMethodName = $"SetLOD{config.lodLevel}";
                if (!InvokeMethod(m_CityViewController, lodMethodName))
                {
                    SetMemberValue(m_CityViewController, "m_LOD", config.lodLevel);
                    InvokeMethod(m_CityViewController, "Apply");
                }
            }

            // Set Road File Name & Routing Policy Mode[cite: 1]
            if (m_RoadNetworkManager != null)
            {
                SetMemberValue(m_RoadNetworkManager, "FileName", config.roadFileName);
                SetNestedMemberValue(m_RoadNetworkManager, "RoutingPolicy.Mode", config.routingPolicy);
                InvokeMethod(m_RoadNetworkManager, "ReloadNetwork");
            }

            // Set Vehicle Spawner Controls[cite: 1]
            if (m_TrafficSpawner != null)
            {
                SetMemberValue(m_TrafficSpawner, "VehicleCount", config.vehicleCount);
                SetMemberValue(m_TrafficSpawner, "VehiclePrefab", config.vehiclePrefab);
            }

            // Set Demand Model Policy[cite: 1]
            if (m_TrafficDemandManager != null)
            {
                SetMemberValue(m_TrafficDemandManager, "UseDemandModel", config.useDemandModel);
            }

            // Set Traffic Lights Toggle[cite: 1]
            if (m_TrafficLightSystem != null)
            {
                SetMemberValue(m_TrafficLightSystem, "EnableTrafficLights", config.enableTrafficLights);
            }
        }

        private void RecordPerformanceMetric(TestScenario config)
        {
            float fps = m_FpsDeltaTime > 0.0001f ? (1.0f / m_FpsDeltaTime) : 0.0f;
            float totalFrameTimeMs = m_FpsDeltaTime * 1000f;

            float cpuTimeMs = (m_CpuRecorder.Valid && m_CpuRecorder.Count > 0)
                ? (float)(m_CpuRecorder.LastValue * 1e-6)
                : totalFrameTimeMs;

            float gpuTimeMs = (m_GpuRecorder.Valid && m_GpuRecorder.Count > 0)
                ? (float)(m_GpuRecorder.LastValue * 1e-6)
                : 0f;

            float allocatedRamMB = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
            float reservedRamMB = Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f);

            string prefabName = config.vehiclePrefab != null ? config.vehiclePrefab.name : "None";

            // Enforce CultureInfo.InvariantCulture for decimal dots (.) instead of regional commas (,)
            string row = string.Format(CultureInfo.InvariantCulture,
                "{0:F2},{1},{2},{3},{4},{5},{6},{7},{8},{9:F2},{10:F2},{11:F2},{12:F2},{13:F1},{14:F1}",
                Time.time,
                SanitizeCsvField(config.scenarioName),
                SanitizeCsvField(config.roadFileName),
                config.lodLevel,
                config.vehicleCount,
                SanitizeCsvField(prefabName),
                config.routingPolicy.ToString(),
                config.useDemandModel,
                config.enableTrafficLights,
                Mathf.RoundToInt(fps),
                totalFrameTimeMs,
                cpuTimeMs,
                gpuTimeMs,
                allocatedRamMB,
                reservedRamMB);

            m_CsvData.Add(row);
        }

        public void ExportCsvFile()
        {
            try
            {
                string filePath = Path.Combine(Application.persistentDataPath, "Korhonen_Automated_Performance_Log.csv");
                File.WriteAllLines(filePath, m_CsvData);
                Debug.Log($"[Korhonen Runner] Exported CSV to: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Korhonen Runner] Failed to write CSV file: {ex.Message}");
            }
        }

        private void UpdateUI(string message)
        {
            if (m_StatusText != null) m_StatusText.text = message;
        }

        private string SanitizeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "None";
            return field.Replace(',', '_').Replace('\n', '_').Replace('\r', '_');
        }

        // --- Robust Reflection Utilities ---
        private bool SetMemberValue(object target, string memberName, object value)
        {
            if (target == null) return false;
            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(target, ConvertValue(value, field.FieldType));
                return true;
            }

            PropertyInfo prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, ConvertValue(value, prop.PropertyType), null);
                return true;
            }

            return false;
        }

        private bool SetNestedMemberValue(object target, string path, object value)
        {
            if (target == null) return false;
            string[] parts = path.Split('.');
            object current = target;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
                Type type = current.GetType();

                FieldInfo field = type.GetField(parts[i], flags);
                if (field != null) { current = field.GetValue(current); continue; }

                PropertyInfo prop = type.GetProperty(parts[i], flags);
                if (prop != null) { current = prop.GetValue(current, null); continue; }

                return false;
            }

            return SetMemberValue(current, parts[parts.Length - 1], value);
        }

        private bool InvokeMethod(object target, string methodName)
        {
            if (target == null) return false;
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (method != null && method.GetParameters().Length == 0)
            {
                method.Invoke(target, null);
                return true;
            }
            return false;
        }

        private object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsIsAssignableFrom(value.GetType())) return value;
            if (targetType.IsEnum) return Enum.ToObject(targetType, value);
            return Convert.ChangeType(value, targetType);
        }
    }

    internal static class ReflectionExtensions
    {
        public static bool IsIsAssignableFrom(this Type targetType, Type c) => targetType.IsAssignableFrom(c);
    }
}
