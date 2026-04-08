using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    [SerializeField] private Camera sceneCamera;
    [SerializeField] private LayerMask placementLayerMask;
    [SerializeField, Min(1f)] private float maxRaycastDistance = 5000f;

    public event Action onClicked;
    public event Action onExit;
    public event Action onRotate;

    private void Awake()
    {
        if (sceneCamera == null)
        {
            sceneCamera = Camera.main;
        }

        if (placementLayerMask.value == 0)
        {
            placementLayerMask = LayerMask.GetMask("Placement");
        }
    }

    private void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            onClicked?.Invoke();
        }

        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            onExit?.Invoke();
        }

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            onRotate?.Invoke();
        }
    }

    public bool IsPointerOverUI()
    {
        return CoreUtility.IsPointerOverUI();
    }

    public bool TryGetSelectedMapPosition(out Vector3 position)
    {
        position = default;

        if (sceneCamera == null || Mouse.current == null)
        {
            return false;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = sceneCamera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, placementLayerMask, QueryTriggerInteraction.Ignore))
        {
            position = hit.point;
            return true;
        }

        return false;
    }
}
