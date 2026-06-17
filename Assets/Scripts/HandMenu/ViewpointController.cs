using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Unity.VRTemplate
{
    public class ViewpointController : MonoBehaviour
    {
        [Tooltip("The XR Origin (XR Rig) root transform.")]
        [SerializeField] Transform m_XROriginTransform;

        [Header("Preset Viewpoints")]
        [Tooltip("Empty GameObjects at stand positions; Y-rotation = desired facing direction.")]
        [SerializeField] Transform m_Viewpoint1;
        [SerializeField] Transform m_Viewpoint2;
        [SerializeField] Transform m_Viewpoint3;

        [Header("Bird-Eye")]
        [Tooltip("Starting height (metres) when entering bird-eye view.")]
        [SerializeField] float m_BirdEyeHeight = 150f;
        [Tooltip("XZ entry point for bird-eye view.")]
        [SerializeField] Vector3 m_SceneCenter = Vector3.zero;

        [Header("Bird-Eye Flight")]
        [Tooltip("Horizontal fly speed in m/s (left thumbstick).")]
        [SerializeField] float m_FlySpeed = 20f;
        [Tooltip("Vertical fly speed in m/s (right thumbstick Y).")]
        [SerializeField] float m_FlyVerticalSpeed = 10f;
        [Tooltip("Thumbstick dead-zone — inputs smaller than this are ignored.")]
        [SerializeField, Range(0f, 0.5f)] float m_StickDeadzone = 0.15f;

        [Header("Transition")]
        [SerializeField] float m_TransitionDuration = 0.8f;

        [Header("Scene Objects")]
        [Tooltip("Ground object to hide in bird-eye view.")]
        [SerializeField] GameObject m_CityGround;

        // -- State --------------------------------------------------
        public bool InBirdEye => m_InBirdEye;

        bool                m_InBirdEye;
        float               m_BirdEyeTargetY;  
        Vector3             m_SavedPosition;
        Coroutine           m_ActiveMove;

        CharacterController m_CC;
        Rigidbody           m_Rb;
        Transform           m_CameraTransform;

        InputDevice m_LeftController;
        InputDevice m_RightController;

        // -- Unity callbacks --------------------------------------------------

        void Awake()
        {
            if (m_XROriginTransform == null) return;
            m_CC = m_XROriginTransform.GetComponent<CharacterController>();
            m_Rb = m_XROriginTransform.GetComponent<Rigidbody>();
        }

        void Update()
        {
            // Flight only runs in bird-eye and only after the entry transition finishes.
            if (!m_InBirdEye || m_ActiveMove != null) return;

            PollControllers();

            float dx = 0f, dz = 0f, dy = 0f;

            if (m_LeftController.isValid)
            {
                m_LeftController.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 ls);
                if (Mathf.Abs(ls.x) > m_StickDeadzone) dx = ls.x;
                if (Mathf.Abs(ls.y) > m_StickDeadzone) dz = ls.y;
            }
            if (m_RightController.isValid)
            {
                m_RightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 rs);
                if (Mathf.Abs(rs.y) > m_StickDeadzone) dy = rs.y;
            }

            if (dx == 0f && dz == 0f && dy == 0f) return;

            if (m_CameraTransform == null) m_CameraTransform = Camera.main?.transform;
            Vector3 forward = m_CameraTransform != null
                ? Vector3.ProjectOnPlane(m_CameraTransform.forward, Vector3.up).normalized
                : Vector3.forward;
            Vector3 right = m_CameraTransform != null
                ? Vector3.ProjectOnPlane(m_CameraTransform.right, Vector3.up).normalized
                : Vector3.right;

            // Horizontal movement applied to transform.
            Vector3 pos = m_XROriginTransform.position;
            pos += (forward * dz + right * dx) * (m_FlySpeed * Time.deltaTime);

            // Vertical movement tracked in m_BirdEyeTargetY — never read back from
            // transform.position.y, so GravityProvider drift cannot accumulate.
            m_BirdEyeTargetY += dy * m_FlyVerticalSpeed * Time.deltaTime;
            pos.y = m_BirdEyeTargetY;

            m_XROriginTransform.position = pos;
        }

        void LateUpdate()
        {
            // Enforce altitude after all Update() calls (including XRI GravityProvider).
            // LateUpdate always runs last, so this cancels any gravity applied this frame.
            if (!m_InBirdEye || m_ActiveMove != null) return;

            Vector3 pos = m_XROriginTransform.position;
            pos.y = m_BirdEyeTargetY;
            m_XROriginTransform.position = pos;
        }

        // -- Public API --------------------------------------------------

        public void SetViewpoint1() => TeleportTo(m_Viewpoint1);
        public void SetViewpoint2() => TeleportTo(m_Viewpoint2);
        public void SetViewpoint3() => TeleportTo(m_Viewpoint3);

        public void ToggleBirdEye()
        {
            if (m_InBirdEye) ExitBirdEye();
            else             EnterBirdEye();
        }

        // -- Internal --------------------------------------------------

        void TeleportTo(Transform target)
        {
            if (m_XROriginTransform == null) return;
            if (target == null)
            {
                Debug.LogWarning("[ViewpointController] Viewpoint transform is not assigned.", this);
                return;
            }

            m_InBirdEye = false;
            if (m_CityGround != null) m_CityGround.SetActive(true);
            // Yaw-only snap — instant, not lerped, to avoid spinning-world artefact.
            m_XROriginTransform.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            BeginMove(target.position, groundLevel: true);
        }

        void EnterBirdEye()
        {
            if (m_XROriginTransform == null) return;
            m_SavedPosition = m_XROriginTransform.position;
            var birdPos = new Vector3(m_SceneCenter.x, m_BirdEyeHeight, m_SceneCenter.z);
            BeginMove(birdPos, groundLevel: false);
            m_InBirdEye = true;
            if (m_CityGround != null) m_CityGround.SetActive(false);
        }

        void ExitBirdEye()
        {
            m_InBirdEye = false;   // set before BeginMove so LateUpdate stops enforcing Y
            BeginMove(m_SavedPosition, groundLevel: true);
            if (m_CityGround != null) m_CityGround.SetActive(true);
        }

        void BeginMove(Vector3 targetPos, bool groundLevel)
        {
            if (m_ActiveMove != null) StopCoroutine(m_ActiveMove);
            m_ActiveMove = StartCoroutine(MoveRoutine(targetPos, groundLevel));
        }

        IEnumerator MoveRoutine(Vector3 targetPos, bool groundLevel)
        {
            SuspendPhysics();
            yield return null;              // one frame for deactivation to take effect

            Vector3 startPos = m_XROriginTransform.position;
            float elapsed = 0f;

            while (elapsed < m_TransitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / m_TransitionDuration));
                m_XROriginTransform.position = Vector3.Lerp(startPos, targetPos, t);
                yield return null;
            }

            m_XROriginTransform.position = targetPos;
            Physics.SyncTransforms();

            if (!groundLevel)
            {
                // Record the authoritative altitude BEFORE releasing m_ActiveMove, so
                // LateUpdate's Y-lock is already correct on the very first frame of flight.
                m_BirdEyeTargetY = targetPos.y;
            }

            if (groundLevel)
            {
                yield return null;          // one frame for transform to settle
                ResumePhysics();
            }
            // Bird-eye: physics stays suspended; LateUpdate handles altitude each frame.

            m_ActiveMove = null;
        }

        // -- Helpers --------------------------------------------------

        void SuspendPhysics()
        {
            if (m_CC != null) m_CC.enabled = false;
            if (m_Rb != null) { m_Rb.isKinematic = true; m_Rb.linearVelocity = Vector3.zero; }
        }

        void ResumePhysics()
        {
            if (m_CC != null) m_CC.enabled = true;
            if (m_Rb != null) m_Rb.isKinematic = false;
        }

        void PollControllers()
        {
            if (!m_LeftController.isValid)
            {
                var buf = new List<InputDevice>();
                InputDevices.GetDevicesWithCharacteristics(
                    InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, buf);
                if (buf.Count > 0) m_LeftController = buf[0];
            }
            if (!m_RightController.isValid)
            {
                var buf = new List<InputDevice>();
                InputDevices.GetDevicesWithCharacteristics(
                    InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, buf);
                if (buf.Count > 0) m_RightController = buf[0];
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            DrawViewpointGizmo(m_Viewpoint1, new Color(0.2f, 1f, 0.2f), "1");
            DrawViewpointGizmo(m_Viewpoint2, new Color(1f, 0.8f, 0.1f), "2");
            DrawViewpointGizmo(m_Viewpoint3, new Color(0.2f, 0.8f, 1f), "3");

            // Bird-eye entry point
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.5f);
            var birdPos = new Vector3(m_SceneCenter.x, m_BirdEyeHeight, m_SceneCenter.z);
            Gizmos.DrawWireSphere(birdPos, 5f);
            UnityEditor.Handles.Label(birdPos + Vector3.up * 6f, "Bird-Eye");
        }

        static void DrawViewpointGizmo(Transform vp, Color color, string label)
        {
            if (vp == null) return;
            Gizmos.color = color;
            Gizmos.DrawSphere(vp.position, 0.4f);
            // Facing direction arrow (2 m long)
            Gizmos.DrawRay(vp.position + Vector3.up * 0.4f,
                           Quaternion.Euler(0f, vp.eulerAngles.y, 0f) * Vector3.forward * 2f);
            UnityEditor.Handles.Label(vp.position + Vector3.up * 1f,
                                      $"VP {label}  {vp.position}");
        }
#endif
    }
}
