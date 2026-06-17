using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace Unity.VRTemplate
{
    public class HandMenuActivator : MonoBehaviour
    {
        [Tooltip("The World-Space Canvas panel to show/hide.")]
        [SerializeField] GameObject m_MenuPanel;

        [Tooltip("Dot-product threshold: how much the palm must face the camera (0=perpendicular, 1=direct).")]
        [SerializeField, Range(0f, 1f)] float m_PalmFacingThreshold = 0.5f;

        [Tooltip("Minimum wrist height relative to camera (prevents triggers when arms hang at sides).")]
        [SerializeField] float m_MinWristHeightOffset = -0.5f;

        [Tooltip("World-space offset applied in wrist-local space.")]
        [SerializeField] Vector3 m_WristOffset = new Vector3(0f, 0.06f, 0.04f);

        [Tooltip("Lerp speed for menu position tracking.")]
        [SerializeField] float m_TrackingSpeed = 18f;

        [Header("Button Toggle")]
        [Tooltip("Input action for Y-button toggle (HandMenu/Toggle in New Actions).")]
        [SerializeField] InputActionReference m_ToggleAction;

        [Tooltip("Left controller transform to attach the menu to when toggled via button.")]
        [SerializeField] Transform m_LeftController;

        static readonly List<XRHandSubsystem> s_Subsystems = new();
        XRHandSubsystem m_HandSubsystem;
        Transform m_CameraTransform;
        bool m_IsVisible;
        bool m_ButtonToggled;

        void OnEnable()
        {
            AcquireHandSubsystem();
            if (m_ToggleAction != null)
            {
                m_ToggleAction.action.performed += OnTogglePressed;
                m_ToggleAction.action.Enable();
            }
        }

        void OnDisable()
        {
            if (m_ToggleAction != null)
                m_ToggleAction.action.performed -= OnTogglePressed;
        }

        void OnTogglePressed(InputAction.CallbackContext ctx)
        {
            m_ButtonToggled = !m_ButtonToggled;
            if (!m_ButtonToggled)
                SetVisible(false, immediate: true);
        }

        void Start()
        {
            m_CameraTransform = Camera.main?.transform;
            SetVisible(false, immediate: true);
        }

        void Update()
        {
            if (m_MenuPanel == null) return;

            // Lazily acquire camera (XR camera may not be ready at Start).
            if (m_CameraTransform == null)
                m_CameraTransform = Camera.main?.transform;

            // Y-button toggled: attach menu to left controller.
            if (m_ButtonToggled)
            {
                SetVisible(true);
                Transform anchor = m_LeftController != null ? m_LeftController : m_CameraTransform;
                if (anchor != null)
                {
                    Vector3 btnPos = anchor.position + anchor.rotation * m_WristOffset;
                    m_MenuPanel.transform.position = Vector3.Lerp(
                        m_MenuPanel.transform.position, btnPos, Time.deltaTime * m_TrackingSpeed);
                    if (m_CameraTransform != null)
                    {
                        Vector3 btnToCam = m_CameraTransform.position - m_MenuPanel.transform.position;
                        if (btnToCam.sqrMagnitude > 0.0001f)
                            m_MenuPanel.transform.rotation = Quaternion.LookRotation(-btnToCam);
                    }
                }
                return;
            }

            // Lazily acquire subsystem (may not be running at OnEnable on device).
            if (m_HandSubsystem == null || !m_HandSubsystem.running)
                AcquireHandSubsystem();

            if (m_HandSubsystem == null || !m_HandSubsystem.running)
            {
                SetVisible(false);
                return;
            }

            var leftHand = m_HandSubsystem.leftHand;
            if (!leftHand.isTracked)
            {
                SetVisible(false);
                return;
            }

            bool gotWrist = leftHand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wristPose);
            bool gotPalm  = leftHand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palmPose);

            if (!gotWrist || !gotPalm || m_CameraTransform == null)
            {
                SetVisible(false);
                return;
            }

            // In XR Hands the palm joint's +Y points toward the BACK of the hand.
            // The palm surface normal is therefore -palmPose.up.
            Vector3 palmNormal  = -palmPose.up;
            Vector3 toHead      = (m_CameraTransform.position - palmPose.position).normalized;
            bool palmFacingHead = Vector3.Dot(palmNormal, toHead) > m_PalmFacingThreshold;

            // Require wrist to be raised (avoids accidental triggers at hip level).
            bool wristRaised = wristPose.position.y > (m_CameraTransform.position.y + m_MinWristHeightOffset);

            SetVisible(palmFacingHead && wristRaised);

            if (!m_IsVisible) return;

            // Position the panel above the wrist in local-wrist space.
            Vector3 targetPos = wristPose.position + wristPose.rotation * m_WristOffset;
            m_MenuPanel.transform.position = Vector3.Lerp(
                m_MenuPanel.transform.position, targetPos, Time.deltaTime * m_TrackingSpeed);

            // Billboard: rotate to face the camera.
            Vector3 toCam = m_CameraTransform.position - m_MenuPanel.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                m_MenuPanel.transform.rotation = Quaternion.LookRotation(-toCam);
        }

        void SetVisible(bool visible, bool immediate = false)
        {
            if (!immediate && m_IsVisible == visible) return;
            m_IsVisible = visible;
            if (m_MenuPanel != null)
                m_MenuPanel.SetActive(visible);
        }

        void AcquireHandSubsystem()
        {
            SubsystemManager.GetSubsystems(s_Subsystems);
            m_HandSubsystem = null;
            foreach (var sub in s_Subsystems)
            {
                if (sub.running) { m_HandSubsystem = sub; return; }
            }
            if (s_Subsystems.Count > 0)
                m_HandSubsystem = s_Subsystems[0];
        }
    }
}
