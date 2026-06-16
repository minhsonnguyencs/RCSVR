using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Unity.VRTemplate
{
    public class RunController : MonoBehaviour
    {
        [SerializeField] InputActionReference m_RunAction;
        [SerializeField] ContinuousMoveProvider m_MoveProvider;
        [SerializeField] float m_RunSpeed = 3f;

        float m_WalkSpeed;

        void Awake()
        {
            if (m_MoveProvider == null)
                m_MoveProvider = FindFirstObjectByType<DynamicMoveProvider>();

            if (m_MoveProvider != null)
                m_WalkSpeed = m_MoveProvider.moveSpeed;
        }

        void OnEnable()
        {
            if (m_RunAction == null) return;
            m_RunAction.action.performed += OnRunPressed;
            m_RunAction.action.canceled += OnRunReleased;
            m_RunAction.action.Enable();
        }

        void OnDisable()
        {
            if (m_RunAction == null) return;
            m_RunAction.action.performed -= OnRunPressed;
            m_RunAction.action.canceled -= OnRunReleased;
        }

        void OnRunPressed(InputAction.CallbackContext _)
        {
            if (m_MoveProvider != null)
                m_MoveProvider.moveSpeed = m_RunSpeed;
        }

        void OnRunReleased(InputAction.CallbackContext _)
        {
            if (m_MoveProvider != null)
                m_MoveProvider.moveSpeed = m_WalkSpeed;
        }
    }
}
