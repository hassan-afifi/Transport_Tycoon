using UnityEngine;
using UnityEngine.EventSystems;

public class MinimapController : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private Camera minimapCamera;
    [SerializeField] private Transform gameplayCamera;
    [SerializeField] private RectTransform minimapRect;
    [SerializeField] private LayerMask navigationMask = ~0;
    [SerializeField] private float maxRayDistance = 5000f;
    [SerializeField] private float fallbackPlaneY = 0f;
    [SerializeField] private Collider mapBounds;
    [SerializeField] private float boundsPadding = 0f;
    [SerializeField] private Transform markerTransform;
    [SerializeField] private bool autoFindMainCamera = true;
    [SerializeField] private float markerY = 280f;

    private int activePointerId = int.MinValue;
    private bool pointerHeld;
    private float markerFixedX;
    private float markerFixedY;

    private void Awake()
    {
        if (minimapRect == null)
        {
            minimapRect = transform as RectTransform;
        }

        if (gameplayCamera == null && Camera.main != null)
        {
            gameplayCamera = Camera.main.transform;
        }

        CacheMarkerFixedAngles();
    }

    private void LateUpdate()
    {
        EnsureGameplayCamera();
        UpdateMarker();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (!TryNavigate(eventData))
        {
            return;
        }

        activePointerId = eventData.pointerId;
        pointerHeld = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!pointerHeld || eventData.pointerId != activePointerId)
        {
            return;
        }

        TryNavigate(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId)
        {
            return;
        }

        pointerHeld = false;
        activePointerId = int.MinValue;
    }

    private void EnsureGameplayCamera()
    {
        if (gameplayCamera == null && autoFindMainCamera && Camera.main != null)
        {
            gameplayCamera = Camera.main.transform;
        }
    }

    private bool TryNavigate(PointerEventData eventData)
    {
        if (minimapCamera == null || gameplayCamera == null || minimapRect == null)
        {
            return false;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                minimapRect,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return false;
        }

        Rect rect = minimapRect.rect;
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return false;
        }

        float u = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float v = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);
        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            return false;
        }

        Ray ray = minimapCamera.ViewportPointToRay(new Vector3(u, v, 0f));
        Vector3 worldPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, navigationMask, QueryTriggerInteraction.Ignore))
        {
            worldPoint = hit.point;
        }
        else
        {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, fallbackPlaneY, 0f));
            if (!plane.Raycast(ray, out float distance))
            {
                return false;
            }

            worldPoint = ray.GetPoint(distance);
        }

        MoveGameplayCamera(worldPoint);
        return true;
    }

    private void MoveGameplayCamera(Vector3 worldPoint)
    {
        Vector3 next = gameplayCamera.position;
        next.x = worldPoint.x;
        next.z = worldPoint.z;

        if (mapBounds != null)
        {
            Bounds b = mapBounds.bounds;
            float minX = b.min.x + boundsPadding;
            float maxX = b.max.x - boundsPadding;
            float minZ = b.min.z + boundsPadding;
            float maxZ = b.max.z - boundsPadding;

            if (minX <= maxX && minZ <= maxZ)
            {
                next.x = Mathf.Clamp(next.x, minX, maxX);
                next.z = Mathf.Clamp(next.z, minZ, maxZ);
            }
        }

        gameplayCamera.position = next;
    }

    private void CacheMarkerFixedAngles()
    {
        if (markerTransform == null)
        {
            return;
        }

        Vector3 euler = markerTransform.eulerAngles;
        markerFixedX = euler.x;
        markerFixedY = euler.y;
    }

    private void UpdateMarker()
    {
        if (markerTransform == null || gameplayCamera == null)
        {
            return;
        }

        markerTransform.position = new Vector3(gameplayCamera.position.x, markerY, gameplayCamera.position.z);
        markerTransform.eulerAngles = new Vector3(markerFixedX, markerFixedY, -gameplayCamera.eulerAngles.y);
    }
}
