using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.XR;

namespace Unity.VRTemplate
{
    public class PerformanceLogger : MonoBehaviour
    {
        ProfilerRecorder m_CpuFrameTimeRecorder;
        ProfilerRecorder m_GpuFrameTimeRecorder;
        ProfilerRecorder m_TotalAllocatedMemoryRecorder;

        List<XRDisplaySubsystem> m_DisplaySubsystems = new();
        XRDisplaySubsystem m_DisplaySubsystem;

        StringBuilder m_CsvBuffer = new StringBuilder();
        bool m_IsLogging;
        int m_FrameIndex;
        string m_CurrentMetadataHeader;

        void OnEnable()
        {
            m_CpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread Machine Frame Time");
            m_GpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GpuFrameTime");
            m_TotalAllocatedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Allocated Memory");

            SubsystemManager.GetSubsystems(m_DisplaySubsystems);
            if (m_DisplaySubsystems.Count > 0)
                m_DisplaySubsystem = m_DisplaySubsystems[0];
        }

        void OnDisable()
        {
            m_CpuFrameTimeRecorder.Dispose();
            m_GpuFrameTimeRecorder.Dispose();
            m_TotalAllocatedMemoryRecorder.Dispose();
        }

        public void StartLogging(string metadataHeader)
        {
            m_CurrentMetadataHeader = metadataHeader;
            m_CsvBuffer.Clear();
            m_FrameIndex = 0;
            m_IsLogging = true;
        }

        public void SaveToPersistentDataPath()
        {
            m_IsLogging = false;

            try
            {
                // Forces saving to standard persistentDataPath
                string filePath = Path.Combine(Application.persistentDataPath, "Master_Benchmark_Results.csv");
                bool fileExists = File.Exists(filePath);

                using (StreamWriter writer = new StreamWriter(filePath, append: true))
                {
                    if (!fileExists)
                    {
                        writer.WriteLine("RunMetadata,FrameIndex,Timestamp,DeltaTimeMS,FPS,CpuTimeMS,GpuTimeMS,AllocatedRAM_MB,ThermalTempC,IsReprojected");
                    }

                    string[] lines = m_CsvBuffer.ToString().Split('\n');
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            writer.WriteLine(line);
                    }
                }

                // Explicit log output showing exact file location
                Debug.Log($"[PerformanceLogger] FILE SAVED SUCCESSFULLY AT: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PerformanceLogger] ERROR SAVING CSV: {ex.Message}");
            }
        }

        void Update()
        {
            if (!m_IsLogging) return;

            m_FrameIndex++;
            float deltaTimeMs = Time.unscaledDeltaTime * 1000f;
            float fps = 1f / Time.unscaledDeltaTime;

            float cpuTimeMs = m_CpuFrameTimeRecorder.Valid ? m_CpuFrameTimeRecorder.LastValue * 1e-6f : -1f;
            float gpuTimeMs = m_GpuFrameTimeRecorder.Valid ? m_GpuFrameTimeRecorder.LastValue * 1e-6f : -1f;
            float ramMb = m_TotalAllocatedMemoryRecorder.Valid ? m_TotalAllocatedMemoryRecorder.LastValue / (1024f * 1024f) : -1f;

            float tempC = GetDeviceTemperature();
            bool isReprojected = CheckIfReprojected();

            m_CsvBuffer.AppendLine($"{m_CurrentMetadataHeader},{m_FrameIndex},{Time.time:F3},{deltaTimeMs:F2},{fps:F1},{cpuTimeMs:F2},{gpuTimeMs:F2},{ramMb:F1},{tempC:F1},{isReprojected}");
        }

        bool CheckIfReprojected()
        {
            if (m_DisplaySubsystem != null && m_DisplaySubsystem.TryGetDroppedFrameCount(out int droppedFrames))
            {
                return droppedFrames > 0;
            }
            return Time.unscaledDeltaTime > (1f / 72f);
        }

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
    }
}