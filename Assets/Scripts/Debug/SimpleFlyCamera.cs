using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleFlyCamera : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 20f;
    public float sprintSpeed = 200f;

    [Header("Mouse Look")]
    public float lookSpeed = 0.15f;

    [Header("Traffic Debug Controls")]
    [Tooltip("RoadNetworkManager used by the runtime control panel. If left empty, the script tries to find one at Start().")]
    public RoadNetworkManager roadNetworkManager;

    [Tooltip("TrafficSpawner used by the runtime control panel. If left empty, the script tries to find one at Start().")]
    public TrafficSpawner trafficSpawner;

    [Header("Traffic Prefabs")]
    public GameObject carSimplePrefab;
    public GameObject carLaneUnawarePrefab;
    public GameObject carLaneAwarePrefab;
    public GameObject carDeadlockEscapePrefab;

    [Header("Debug Panel")]
    public Key panelToggleKey = Key.F1;
    public bool showControlPanel = false;

    float yaw;
    float pitch;

    private string roadFileInput = "";
    private string vehicleCountInput = "";

    private Rect controlWindowRect =
        new Rect(20f, 20f, 360f, 390f);

    private GUIStyle selectedButtonStyle;


    void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;

        if (roadNetworkManager == null)
            roadNetworkManager =
                FindObjectOfType<RoadNetworkManager>();

        if (trafficSpawner == null)
            trafficSpawner =
                FindObjectOfType<TrafficSpawner>();

        if (roadNetworkManager != null)
            roadFileInput =
                roadNetworkManager.fileName;

        if (trafficSpawner != null)
            vehicleCountInput =
                trafficSpawner.vehicleCount.ToString();

        SetCursorForCurrentMode();
    }


    void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current[panelToggleKey].wasPressedThisFrame)
        {
            showControlPanel = !showControlPanel;
            SetCursorForCurrentMode();
        }

        /*
         * Escape leaves mouse-look mode.
         * When the panel is open the cursor remains free.
         */
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!showControlPanel &&
            Mouse.current != null &&
            Mouse.current.rightButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /*
         * Do not steer the camera while using the controls.
         * This also prevents WASD/E/Q input typed around the UI
         * from moving the camera.
         */
        if (showControlPanel)
            return;

        if (Cursor.lockState == CursorLockMode.Locked &&
            Mouse.current != null)
        {
            Vector2 mouseDelta =
                Mouse.current.delta.ReadValue();

            yaw +=
                mouseDelta.x * lookSpeed;

            pitch -=
                mouseDelta.y * lookSpeed;

            pitch =
                Mathf.Clamp(
                    pitch,
                    -89f,
                    89f
                );

            transform.rotation =
                Quaternion.Euler(
                    pitch,
                    yaw,
                    0f
                );
        }

        Vector3 move =
            Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
            move += Vector3.forward;

        if (Keyboard.current.sKey.isPressed)
            move += Vector3.back;

        if (Keyboard.current.aKey.isPressed)
            move += Vector3.left;

        if (Keyboard.current.dKey.isPressed)
            move += Vector3.right;

        if (Keyboard.current.eKey.isPressed)
            move += Vector3.up;

        if (Keyboard.current.qKey.isPressed)
            move += Vector3.down;

        bool sprint =
            Keyboard.current.leftShiftKey.isPressed ||
            Keyboard.current.rightShiftKey.isPressed;

        float speed =
            sprint
                ? sprintSpeed
                : walkSpeed;

        transform.Translate(
            move.normalized
            * speed
            * Time.deltaTime,
            Space.Self
        );
    }


    private void SetCursorForCurrentMode()
    {
        if (showControlPanel)
        {
            Cursor.lockState =
                CursorLockMode.None;

            Cursor.visible =
                true;
        }
        else
        {
            Cursor.lockState =
                CursorLockMode.Locked;

            Cursor.visible =
                false;
        }
    }


    void OnGUI()
    {
        if (!showControlPanel)
            return;

        EnsureStyles();

        controlWindowRect =
            GUI.Window(
                GetInstanceID(),
                controlWindowRect,
                DrawControlWindow,
                "Traffic / Road Controls"
            );
    }


    private void EnsureStyles()
    {
        if (selectedButtonStyle != null)
            return;

        selectedButtonStyle =
            new GUIStyle(GUI.skin.button);

        selectedButtonStyle.fontStyle =
            FontStyle.Bold;
    }


    private void DrawControlWindow(int windowId)
    {
        GUILayout.Space(4f);

        DrawRoadNetworkControls();

        GUILayout.Space(10f);

        DrawVehicleCountControls();

        GUILayout.Space(10f);

        DrawPrefabControls();

        GUILayout.Space(12f);

        GUILayout.Label(
            "F1: close panel    |    Right mouse: recapture camera"
        );

        GUI.DragWindow(
            new Rect(
                0f,
                0f,
                10000f,
                24f
            )
        );
    }


    private void DrawRoadNetworkControls()
    {
        GUILayout.Label("Road network JSON");

        roadFileInput =
            GUILayout.TextField(
                roadFileInput
            );

        GUI.enabled =
            roadNetworkManager != null;

        if (GUILayout.Button(
                "Reload road network"))
        {
            roadNetworkManager
                .ReloadRoadNetworkFromString(
                    roadFileInput
                );

            /*
             * RoadNetworkManager already coordinates
             * the vehicle respawn after a successful reload.
             */
            roadFileInput =
                roadNetworkManager.fileName;

            if (trafficSpawner != null)
            {
                vehicleCountInput =
                    trafficSpawner
                        .vehicleCount
                        .ToString();
            }
        }

        GUI.enabled = true;

        if (roadNetworkManager == null)
        {
            GUILayout.Label(
                "RoadNetworkManager not assigned/found."
            );
        }
    }


    private void DrawVehicleCountControls()
    {
        GUILayout.Label("Number of vehicles");

        vehicleCountInput =
            GUILayout.TextField(
                vehicleCountInput
            );

        GUI.enabled =
            trafficSpawner != null;

        if (GUILayout.Button(
                "Respawn vehicles"))
        {
            trafficSpawner
                .RespawnVehiclesFromString(
                    vehicleCountInput
                );

            vehicleCountInput =
                trafficSpawner
                    .vehicleCount
                    .ToString();
        }

        GUI.enabled = true;

        if (trafficSpawner == null)
        {
            GUILayout.Label(
                "TrafficSpawner not assigned/found."
            );
        }
        else
        {
            GUILayout.Label(
                "Currently spawned: "
                + trafficSpawner
                    .CurrentSpawnedVehicleCount
            );
        }
    }


    private void DrawPrefabControls()
    {
        GUILayout.Label("Vehicle behavior prefab");

        if (trafficSpawner == null)
        {
            GUI.enabled = false;
        }

        DrawPrefabButton(
            "Simple",
            carSimplePrefab
        );

        DrawPrefabButton(
            "Lane unaware",
            carLaneUnawarePrefab
        );

        DrawPrefabButton(
            "Lane aware",
            carLaneAwarePrefab
        );

        DrawPrefabButton(
            "Deadlock escape",
            carDeadlockEscapePrefab
        );

        GUI.enabled = true;

        if (trafficSpawner != null &&
            trafficSpawner.vehiclePrefab != null)
        {
            GUILayout.Label(
                "Active: "
                + trafficSpawner
                    .vehiclePrefab
                    .name
            );
        }
    }


    private void DrawPrefabButton(
        string label,
        GameObject prefab)
    {
        bool isSelected =
            trafficSpawner != null &&
            trafficSpawner.vehiclePrefab == prefab &&
            prefab != null;

        GUI.enabled =
            trafficSpawner != null &&
            prefab != null;

        GUIStyle style =
            isSelected
                ? selectedButtonStyle
                : GUI.skin.button;

        string buttonLabel =
            isSelected
                ? "✓ " + label
                : label;

        if (GUILayout.Button(
                buttonLabel,
                style))
        {
            SetVehiclePrefabAndRespawn(
                prefab
            );
        }

        GUI.enabled =
            trafficSpawner != null;
    }


    private void SetVehiclePrefabAndRespawn(
        GameObject prefab)
    {
        if (trafficSpawner == null ||
            prefab == null)
        {
            return;
        }

        trafficSpawner.vehiclePrefab =
            prefab;

        trafficSpawner
            .RespawnVehicles();

        vehicleCountInput =
            trafficSpawner
                .vehicleCount
                .ToString();

        Debug.Log(
            "Traffic prefab changed to "
            + prefab.name
            + " and traffic respawned."
        );
    }
}
