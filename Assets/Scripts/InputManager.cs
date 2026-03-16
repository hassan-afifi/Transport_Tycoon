using UnityEngine;
using UnityEngine.InputSystem; 
using System;
using UnityEngine.EventSystems;
public class InputManager : MonoBehaviour
{
    [SerializeField]
    private Camera sceneCamera;

    private Vector3 lastPosition;

    [SerializeField]
    private LayerMask placementLayerMask;

    public event Action onClicked, onExit, onRotate;
    private void Update()
    {
        if(Mouse.current.leftButton.wasPressedThisFrame)
            onClicked?.Invoke();
        if(Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            onExit?.Invoke();
        if(Keyboard.current.rKey.wasPressedThisFrame)
            onRotate?.Invoke();
    }
// 
    public bool IsPointerOverUI()
        => EventSystem.current.IsPointerOverGameObject();


    public Vector3 GetSelectedMapPosition()
    {
        if (Mouse.current == null) return lastPosition;

        Vector2 mousePos = Mouse.current.position.ReadValue();

        Ray ray = sceneCamera.ScreenPointToRay(mousePos);
        
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100, placementLayerMask))
        {
            lastPosition = hit.point;
        }

        return lastPosition;
    }  
}