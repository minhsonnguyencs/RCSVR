using UnityEngine;
using UnityEngine.UI;

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
        [SerializeField] Color m_ActiveColor   = new Color(0.18f, 0.56f, 1.00f);
        [SerializeField] Color m_InactiveColor = new Color(0.13f, 0.13f, 0.16f);

        // --- Performance Monitoring Additions -----------------------------------------
        [Header("Performance Profiling")]
        [Tooltip("Optional Text component to show FPS live on your VR hand menu")]
        [SerializeField] Text m_FpsCounterText;

        private float m_FpsDeltaTime = 0.0f;

        // --- Initial State ------------------------------------------------------------

        int m_LOD        = 1;
        int m_Complexity = 10;
        int m_VehicleCount = 500;

        // Flat lookup table: [complexityIndex][lodIndex]
        GameObject[,] m_Objects;

        void Start()
        {
            m_Objects = new GameObject[6, 3]
            {
                { m_10_LOD1,  m_10_LOD2,  m_10_LOD3  },
                { m_20_LOD1,  m_20_LOD2,  m_20_LOD3  },
                { m_50_LOD1,  m_50_LOD2,  m_50_LOD3  },
                { m_150_LOD1, m_150_LOD2, m_150_LOD3 },
                { m_250_LOD1, m_250_LOD2, m_250_LOD3 },
                { m_All_LOD1, m_All_LOD2, m_All_LOD3 },
            };

            Apply();
            HighlightLOD(m_LOD);
            HighlightComplexity(m_Complexity);
            HighlightVehicleCount(m_VehicleCount);
        }

        // --- --------------------------------------------
        void Update()
        {
            // Continuously calculate frame rendering times
            m_FpsDeltaTime += (Time.unscaledDeltaTime - m_FpsDeltaTime) * 0.1f;

            if (m_FpsCounterText != null)
            {
                float fps = 1.0f / m_FpsDeltaTime;
                m_FpsCounterText.text = $"FPS: {Mathf.RoundToInt(fps)} | LOD: {m_LOD} | Count: {(m_Complexity == -1 ? "All" : m_Complexity)}";
            }
        }

        // --- Btn LOD -------------------------------------------------------------------

        public void SetLOD1() { m_LOD = 1; Apply(); HighlightLOD(1); }
        public void SetLOD2() { m_LOD = 2; Apply(); HighlightLOD(2); }
        public void SetLOD3() { m_LOD = 3; Apply(); HighlightLOD(3); }

        // --- Btn Complexity ------------------------------------------------------------

        public void SetComplexity10()  { m_Complexity = 10;  Apply(); HighlightComplexity(10);  }
        public void SetComplexity20()  { m_Complexity = 20;  Apply(); HighlightComplexity(20);  }
        public void SetComplexity50()  { m_Complexity = 50;  Apply(); HighlightComplexity(50);  }
        public void SetComplexity150() { m_Complexity = 150; Apply(); HighlightComplexity(150); }
        public void SetComplexity250() { m_Complexity = 250; Apply(); HighlightComplexity(250); }
        public void SetComplexityAll() { m_Complexity = -1;  Apply(); HighlightComplexity(-1);  }

        // --- Btn Vehicle Count -----------------------------------------------------------

        public void SetVehicleCount500()  { SetVehicleCount(500);  }
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

        // --- Internal Render Toggling ------------------------------------------------------------

        void Apply()
        {
            int ci = ComplexityIndex(m_Complexity);
            int li = m_LOD - 1; // LOD1→0, LOD2→1, LOD3→2

            for (int c = 0; c < 6; c++)
                for (int l = 0; l < 3; l++)
                    if (m_Objects[c, l] != null)
                        m_Objects[c, l].SetActive(c == ci && l == li);

            // Print the rendering changes straight to the Unity log for profiling
            float currentFps = 1.0f / m_FpsDeltaTime;
            Debug.Log($"[PERF LOG] Configuration Changed! Active Complexity: {(m_Complexity == -1 ? "All" : m_Complexity)} buildings at LOD {m_LOD}. Immediate Baseline FPS: {Mathf.RoundToInt(currentFps)}");
        
        }

        static int ComplexityIndex(int count) => count switch
        {
            10  => 0,
            20  => 1,
            50  => 2,
            150 => 3,
            250 => 4,
            -1  => 5,
            _   => 0,
        };

        void HighlightLOD(int level)
        {
            Highlight(m_BtnLOD1, level == 1);
            Highlight(m_BtnLOD2, level == 2);
            Highlight(m_BtnLOD3, level == 3);
        }

        void HighlightComplexity(int count)
        {
            Highlight(m_Btn10,  count == 10);
            Highlight(m_Btn20,  count == 20);
            Highlight(m_Btn50,  count == 50);
            Highlight(m_Btn150, count == 150);
            Highlight(m_Btn250, count == 250);
            Highlight(m_BtnAll, count == -1);
        }

        void HighlightVehicleCount(int count)
        {
            Highlight(m_Btn500,  count == 500);
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
