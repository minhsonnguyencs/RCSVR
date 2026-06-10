using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Unity.VRTemplate
{
    public class HandMenuActivator : MonoBehaviour
    {
        [SerializeField] GameObject m_MenuPanel;

        [Tooltip("Local offset from controller origin in normal view (Layout A: localPosition, Layout B: world offset).")]
        [SerializeField] Vector3 m_WristOffset = new Vector3(0.12f, 0.04f, 0.04f);

        [Tooltip("Lerp speed for world-space position tracking (Layout B only).")]
        [SerializeField] float m_TrackingSpeed = 18f;

        [Header("Controller Tracking")]
        [Tooltip("Drag 'Near-Far Interactor Left' from the XR Origin hierarchy.")]
        [SerializeField] Transform m_LeftControllerTransform;

        [Header("Bird-Eye")]
        [Tooltip("Auto-found on the same GameObject if left empty.")]
        [SerializeField] ViewpointController m_ViewpointController;

        [Tooltip("Local offset applied to the controller in bird-eye mode (grip button position).")]
        [SerializeField] Vector3 m_GripOffset = new Vector3(0f, 0.06f, 0.04f);

        Transform   m_CameraTransform;
        Canvas      m_Canvas;
        bool        m_IsVisible;
        bool        m_NeedsSnap;

        InputDevice m_LeftController;
        bool        m_XButtonPrev;
        bool        m_ButtonOverride;

        readonly List<InputDevice> m_DeviceBuf = new(1);

        void Start()
        {
            m_CameraTransform = Camera.main?.transform;
            if (m_ViewpointController == null)
                m_ViewpointController = GetComponent<ViewpointController>();
            if (m_MenuPanel != null)
                m_Canvas = m_MenuPanel.GetComponent<Canvas>();
            EnsureEventCamera();
            SetVisible(false, immediate: true);
        }

        void Update()
        {
            if (m_MenuPanel == null) return;

            // X button toggle
            if (!m_LeftController.isValid)
            {
                InputDevices.GetDevicesWithCharacteristics(
                    InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, m_DeviceBuf);
                if (m_DeviceBuf.Count > 0) m_LeftController = m_DeviceBuf[0];
            }
            if (m_LeftController.isValid)
            {
                m_LeftController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool xNow);
                if (xNow && !m_XButtonPrev) m_ButtonOverride = !m_ButtonOverride;
                m_XButtonPrev = xNow;
            }

            SetVisible(m_ButtonOverride);
            if (!m_IsVisible) return;

            // Position
            bool inBirdEye  = m_ViewpointController != null && m_ViewpointController.InBirdEye;
            bool isParented = m_MenuPanel.transform.parent != null;

            if (isParented)
            {
                // Layout A: parented to controller — set localPosition only, hierarchy does the rest.
                m_MenuPanel.transform.localPosition = inBirdEye ? m_GripOffset : m_WristOffset;
            }
            else
            {
                // Layout B: world-space root — lerp toward computed world target.
                Vector3 offset = inBirdEye ? m_GripOffset : m_WristOffset;
                Vector3 targetPos = m_LeftControllerTransform != null
                    ? m_LeftControllerTransform.position + m_LeftControllerTransform.rotation * offset
                    : m_CameraTransform.position + m_CameraTransform.forward * 0.5f - Vector3.up * 0.15f;

                if (m_NeedsSnap) { m_MenuPanel.transform.position = targetPos; m_NeedsSnap = false; }
                else m_MenuPanel.transform.position = Vector3.Lerp(
                    m_MenuPanel.transform.position, targetPos, Time.deltaTime * m_TrackingSpeed);
            }

            // Billboard — face the camera every frame.
            Vector3 toCam = m_CameraTransform.position - m_MenuPanel.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                m_MenuPanel.transform.rotation = Quaternion.LookRotation(-toCam);
        }

        void SetVisible(bool visible, bool immediate = false)
        {
            if (!immediate && m_IsVisible == visible) return;
            if (visible && !m_IsVisible) { m_NeedsSnap = true; EnsureEventCamera(); }
            m_IsVisible = visible;
            if (m_MenuPanel != null) m_MenuPanel.SetActive(visible);
        }

        void EnsureEventCamera()
        {
            if (m_Canvas == null || m_Canvas.renderMode != RenderMode.WorldSpace) return;
            if (m_Canvas.worldCamera != null) return;
            if (m_CameraTransform == null) m_CameraTransform = Camera.main?.transform;
            if (m_CameraTransform != null)
                m_Canvas.worldCamera = m_CameraTransform.GetComponent<Camera>();
        }
    }
}
