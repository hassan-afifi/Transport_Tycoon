using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Collider mapBounds;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 60f;
    [SerializeField] private float dragPanSpeed = 0.2f;
    [SerializeField] private float verticalSpeed = 60f;
    [SerializeField] private float sprintMultiplier = 2f;

    [Header("View")]
    [SerializeField] private float orbitSpeed = 0.2f;
    [SerializeField] private float zoomSpeed = 0.08f;
    [SerializeField] private float minPitch = 35f;
    [SerializeField] private float maxPitch = 85f;
    [SerializeField] private float minY = 20f;
    [SerializeField] private float maxY = 300f;

    [Header("Selection")]
    [SerializeField] private LayerMask selectableLayers = ~0;
    [SerializeField] private float clickDragThreshold = 6f;
    [SerializeField] private float clickMaxDuration = 0.25f;

    [Header("Bounds")]
    [SerializeField] private float boundsPadding = 10f;

    public GameObject SelectedObject { get; private set; }

    private InputAction move;
    private InputAction look;
    private InputAction leftClick;
    private InputAction rightClick;
    private InputAction scroll;
    private InputAction sprint;
    private InputAction jump;
    private InputAction crouch;

    private bool leftHeld;
    private bool leftDragged;
    private bool orbiting;
    private Vector2 pressPos;
    private float pressTime;
    private float yaw;
    private float pitch;

    private bool savedCursorVisible;
    private CursorLockMode savedCursorLock;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (inputActions == null)
        {
            PlayerInput playerInput = FindFirstObjectByType<PlayerInput>();
            if (playerInput != null)
            {
                inputActions = playerInput.actions;
            }
        }

        move = FindAction("Player/Move");
        look = FindAction("Player/Look");
        leftClick = FindAction("UI/Click");
        rightClick = FindAction("UI/RightClick");
        scroll = FindAction("UI/ScrollWheel");
        sprint = FindAction("Player/Sprint");
        jump = FindAction("Player/Jump");
        crouch = FindAction("Player/Crouch");

        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = Mathf.Clamp(NormalizeAngle(euler.x), minPitch, maxPitch);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void OnEnable()
    {
        ToggleActions(true);
    }

    private void OnDisable()
    {
        ToggleActions(false);
        SetOrbiting(false);
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            return;
        }

        HandleKeyboardPan();
        HandleVertical();
        HandleZoom();
        HandleMouse();
        ClampToBounds();
    }

    private void HandleKeyboardPan()
    {
        Vector2 input = ReadVector2(move);
        if (input.sqrMagnitude <= 0f)
        {
            return;
        }

        float speed = moveSpeed * (IsPressed(sprint) ? sprintMultiplier : 1f) * Time.deltaTime;
        Vector3 forward = Planar(transform.forward, Vector3.forward);
        Vector3 right = Planar(transform.right, Vector3.right);
        transform.position += (forward * input.y + right * input.x) * speed;
    }

    private void HandleVertical()
    {
        float yInput = (IsPressed(jump) ? 1f : 0f) - (IsPressed(crouch) ? 1f : 0f);
        if (Mathf.Approximately(yInput, 0f))
        {
            return;
        }

        float speed = verticalSpeed * (IsPressed(sprint) ? sprintMultiplier : 1f) * Time.deltaTime;
        Vector3 pos = transform.position;
        pos.y = Mathf.Clamp(pos.y + yInput * speed, minY, maxY);
        transform.position = pos;
    }

    private void HandleZoom()
    {
        float wheel = ReadVector2(scroll).y;
        if (Mathf.Abs(wheel) <= 0.001f)
        {
            return;
        }

        Vector3 pos = transform.position;
        pos.y = Mathf.Clamp(pos.y - wheel * zoomSpeed, minY, maxY);
        transform.position = pos;
    }

    private void HandleMouse()
    {
        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Vector2 delta = ReadVector2(look);

        if (WasPressedThisFrame(leftClick) && !IsPointerOverUI())
        {
            leftHeld = true;
            leftDragged = false;
            pressPos = mousePos;
            pressTime = Time.time;
        }

        if (leftHeld && IsPressed(leftClick))
        {
            if (!leftDragged && Vector2.Distance(mousePos, pressPos) >= clickDragThreshold)
            {
                leftDragged = true;
            }

            if (leftDragged)
            {
                DragPan(delta);
            }
        }

        if (leftHeld && WasReleasedThisFrame(leftClick))
        {
            bool click = !leftDragged && Time.time - pressTime <= clickMaxDuration;
            if (click && !IsPointerOverUI())
            {
                Select(mousePos);
            }

            leftHeld = false;
            leftDragged = false;
        }

        SetOrbiting(IsPressed(rightClick) && !IsPointerOverUI());
        if (orbiting)
        {
            Orbit(delta);
        }
    }

    private void DragPan(Vector2 delta)
    {
        float heightFactor = Mathf.Max(1f, transform.position.y * 0.02f);
        Vector3 forward = Planar(transform.forward, Vector3.forward);
        Vector3 right = Planar(transform.right, Vector3.right);
        transform.position += (-right * delta.x - forward * delta.y) * (dragPanSpeed * heightFactor);
    }

    private void Orbit(Vector2 delta)
    {
        yaw += delta.x * orbitSpeed;
        pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, minPitch, maxPitch);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void Select(Vector2 screenPos)
    {
        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, selectableLayers))
        {
            SelectedObject = hit.collider.transform.root.gameObject;
        }
        else
        {
            SelectedObject = null;
        }
    }

    private void ClampToBounds()
    {
        if (mapBounds == null)
        {
            return;
        }

        Bounds b = mapBounds.bounds;
        float minX = b.min.x + boundsPadding;
        float maxX = b.max.x - boundsPadding;
        float minZ = b.min.z + boundsPadding;
        float maxZ = b.max.z - boundsPadding;

        if (minX > maxX || minZ > maxZ)
        {
            return;
        }

        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
        transform.position = pos;
    }

    private void SetOrbiting(bool value)
    {
        if (orbiting == value)
        {
            return;
        }

        orbiting = value;
        if (orbiting)
        {
            savedCursorVisible = Cursor.visible;
            savedCursorLock = Cursor.lockState;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            Cursor.visible = savedCursorVisible;
            Cursor.lockState = savedCursorLock;
        }
    }

    private InputAction FindAction(string path)
    {
        return inputActions != null ? inputActions.FindAction(path, false) : null;
    }

    private void ToggleActions(bool enabled)
    {
        Toggle(move, enabled);
        Toggle(look, enabled);
        Toggle(leftClick, enabled);
        Toggle(rightClick, enabled);
        Toggle(scroll, enabled);
        Toggle(sprint, enabled);
        Toggle(jump, enabled);
        Toggle(crouch, enabled);
    }

    private static void Toggle(InputAction action, bool enabled)
    {
        if (action == null)
        {
            return;
        }

        if (enabled && !action.enabled)
        {
            action.Enable();
        }

        if (!enabled && action.enabled)
        {
            action.Disable();
        }
    }

    private static Vector2 ReadVector2(InputAction action)
    {
        return action != null ? action.ReadValue<Vector2>() : Vector2.zero;
    }

    private static bool IsPressed(InputAction action)
    {
        return action != null && action.IsPressed();
    }

    private static bool WasPressedThisFrame(InputAction action)
    {
        return action != null && action.WasPressedThisFrame();
    }

    private static bool WasReleasedThisFrame(InputAction action)
    {
        return action != null && action.WasReleasedThisFrame();
    }

    private static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private static Vector3 Planar(Vector3 value, Vector3 fallback)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.0001f ? value.normalized : fallback;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f)
        {
            angle += 360f;
        }

        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }
}
