using System.Collections;
using System.IO;
using UnityEngine;

//What it does: The master brain. It talks to CityViewController to set building counts, LODs, and vehicle numbers, starts the logger, triggers the camera movement, and repeats across all test conditions automatically.

namespace Unity.VRTemplate
{
    public class BenchmarkRunner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] CityViewController m_CityView; // Your updated controller
        [SerializeField] BenchmarkCameraPath m_CameraPath;
        [SerializeField] PerformanceLogger m_Logger;

        [Header("Matrix Parameters (108 Conditions)")]
        [SerializeField] int[] m_LODs = new int[] { 1, 2, 3 };
        [SerializeField] int[] m_BuildingComplexities = new int[] { 1000, 2000, 5000, 10000, 15000, -1 };
        [SerializeField] int[] m_VehicleCounts = new int[] { 500, 1000, 1500, 2000, 2500, 3000 }; 
        [SerializeField] int m_RepetitionsPerCondition = 3;

        [Header("Timing")]
        [SerializeField] float m_WarmupDuration = 3.0f;
        [SerializeField] float m_ThermalCooldownDuration = 4.0f;

        [Header("Safety / Pilot Testing")]
        [Tooltip("Check this to test only 2 runs first to make sure everything works.")]
        [SerializeField] bool m_PilotTestOnly = true;

        [ContextMenu("Start Benchmark Runs")]

        void Start()
        {
            Debug.Log("[BenchmarkRunner] Starting Automated Matrix Run...");
            RunBenchmarkMatrix();
        }

        public void RunBenchmarkMatrix()
        {
            StartCoroutine(MatrixExecutionRoutine());
        }

        IEnumerator MatrixExecutionRoutine()
        {
            int runCounter = 0;

            foreach (int lod in m_LODs)
            {
                foreach (int bldComp in m_BuildingComplexities)
                {
                    foreach (int vehCount in m_VehicleCounts)
                    {
                        for (int rep = 1; rep <= m_RepetitionsPerCondition; rep++)
                        {
                            runCounter++;

                            // 1. Change scene settings using CityViewController[cite: 26]
                            ApplyLOD(lod);
                            ApplyBuildingComplexity(bldComp);
                            ApplyVehicleCount(vehCount);

                            // 2. Wait for physics and spawner to settle
                            yield return new WaitForSeconds(m_WarmupDuration);

                            // 3. Start Recording
                            string bldLabel = (bldComp == -1) ? "All" : bldComp.ToString();
                            string metadataHeader = $"Run_{runCounter},LOD_{lod},Bld_{bldLabel},Veh_{vehCount},Rep_{rep}";
                            m_Logger.StartLogging(metadataHeader);

                            // 4. Move Camera along Waypoints
                            bool pathFinished = false;
                            StartCoroutine(m_CameraPath.PlayPathRoutine(() => pathFinished = true));

                            yield return new WaitUntil(() => pathFinished);

                            // 5. Save directly to persistentDataPath
                            m_Logger.SaveToPersistentDataPath();

                            // 6. Cooldown delay
                            yield return new WaitForSeconds(m_ThermalCooldownDuration);

                            // Pilot stop guard
                            if (m_PilotTestOnly && runCounter >= 2)
                            {
                                Debug.Log("[BenchmarkRunner] Pilot test complete! Check persistentDataPath for the Benchmark_Results_*.csv file from this run.");
                                yield break;
                            }
                        }
                    }
                }
            }

            Debug.Log("[BenchmarkRunner] All benchmark runs complete!");
        }

        void ApplyLOD(int level)
        {
            if (level == 1) m_CityView.SetLOD1();
            else if (level == 2) m_CityView.SetLOD2();
            else if (level == 3) m_CityView.SetLOD3();
        }

        void ApplyBuildingComplexity(int count)
        {
            switch (count)
            {
                case 1000: m_CityView.SetComplexity1000(); break;
                case 2000: m_CityView.SetComplexity2000(); break;
                case 5000: m_CityView.SetComplexity5000(); break;
                case 10000: m_CityView.SetComplexity10000(); break;
                case 15000: m_CityView.SetComplexity15000(); break;
                case -1: m_CityView.SetComplexityAll(); break;
            }
        }

        void ApplyVehicleCount(int count)
        {
            switch (count)
            {
                case 500: m_CityView.SetVehicleCount500(); break; 
                case 1000: m_CityView.SetVehicleCount1000(); break; 
                case 1500: m_CityView.SetVehicleCount1500(); break; 
                case 2000: m_CityView.SetVehicleCount2000(); break; 
                case 2500: m_CityView.SetVehicleCount2500(); break; 
                case 3000: m_CityView.SetVehicleCount3000(); break; 
            }
        }
    }
}
