using System;
using System.Collections;
using UnityEngine;

//What it does: Moves your camera smoothly through a list of target points so every single test run sees the exact same camera path.

namespace Unity.VRTemplate
{
    public class BenchmarkCameraPath : MonoBehaviour
    {
        [Header("Path Setup")]
        [SerializeField] Transform[] m_Waypoints;
        [SerializeField] float m_MoveSpeed = 5f;

        public bool IsPlaying { get; private set; }

        public IEnumerator PlayPathRoutine(Action onComplete)
        {
            if (m_Waypoints == null || m_Waypoints.Length < 2)
            {
                Debug.LogError("[BenchmarkCameraPath] You must assign at least 2 Waypoints in the Inspector!");
                yield break;
            }

            IsPlaying = true;

            for (int i = 0; i < m_Waypoints.Length - 1; i++)
            {
                Vector3 startPos = m_Waypoints[i].position;
                Vector3 endPos = m_Waypoints[i + 1].position;
                float distance = Vector3.Distance(startPos, endPos);
                float duration = distance / m_MoveSpeed;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    transform.position = Vector3.Lerp(startPos, endPos, t);

                    Vector3 dir = (endPos - startPos).normalized;
                    if (dir.sqrMagnitude > 0.001f)
                        transform.rotation = Quaternion.LookRotation(dir);

                    yield return null;
                }
            }

            IsPlaying = false;
            onComplete?.Invoke();
        }
    }
}
