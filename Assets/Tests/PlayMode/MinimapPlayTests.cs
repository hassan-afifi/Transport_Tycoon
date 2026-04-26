using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

public class MinimapPlayTests
{
    private readonly List<GameObject> createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void Awake_AssignsMissingRectAndGameplayCamera_AndCachesMarkerAngles()
    {
        Camera mainCamera = CreateMainCamera();

        RectTransform minimapRect = Track(new GameObject("MinimapRoot")).AddComponent<RectTransform>();
        MinimapController controller = minimapRect.gameObject.AddComponent<MinimapController>();

        Transform marker = Track(new GameObject("Marker")).transform;
        marker.eulerAngles = new Vector3(25f, 35f, 45f);

        SetPrivateField(controller, "markerTransform", marker);
        SetPrivateField(controller, "minimapRect", null);
        SetPrivateField(controller, "gameplayCamera", null);

        InvokePrivateMethod(controller, "Awake");

        Assert.AreSame(minimapRect, GetPrivateField<RectTransform>(controller, "minimapRect"));
        Transform gameplayCamera = GetPrivateField<Transform>(controller, "gameplayCamera");
        Assert.IsNotNull(gameplayCamera);
        Assert.AreSame(mainCamera.transform, gameplayCamera);
        Assert.AreEqual(25f, GetPrivateField<float>(controller, "markerFixedX"), 0.0001f);
        Assert.AreEqual(35f, GetPrivateField<float>(controller, "markerFixedY"), 0.0001f);
    }

    [Test]
    public void EnsureGameplayCamera_AutoFindsMainCamera_WhenEnabled()
    {
        CreateMainCamera();

        MinimapController controller = Track(new GameObject("Minimap")).AddComponent<MinimapController>();
        SetPrivateField(controller, "gameplayCamera", null);
        SetPrivateField(controller, "autoFindMainCamera", true);

        InvokePrivateMethod(controller, "EnsureGameplayCamera");

        Transform gameplayCamera = GetPrivateField<Transform>(controller, "gameplayCamera");
        Assert.IsNotNull(gameplayCamera);
        Assert.AreSame(Camera.main.transform, gameplayCamera);

        SetPrivateField(controller, "gameplayCamera", null);
        SetPrivateField(controller, "autoFindMainCamera", false);
        InvokePrivateMethod(controller, "EnsureGameplayCamera");

        Assert.IsNull(GetPrivateField<Transform>(controller, "gameplayCamera"));
    }

    [Test]
    public void MoveGameplayCamera_UpdatesXZWithoutBounds()
    {
        MinimapController controller = Track(new GameObject("Minimap")).AddComponent<MinimapController>();
        Transform gameplayCamera = Track(new GameObject("GameplayCamera")).transform;
        gameplayCamera.position = new Vector3(1f, 22f, 3f);

        SetPrivateField(controller, "gameplayCamera", gameplayCamera);
        SetPrivateField(controller, "mapBounds", null);

        InvokePrivateMethod(controller, "MoveGameplayCamera", new Vector3(12f, 0f, -9f));

        Assert.AreEqual(12f, gameplayCamera.position.x, 0.0001f);
        Assert.AreEqual(22f, gameplayCamera.position.y, 0.0001f);
        Assert.AreEqual(-9f, gameplayCamera.position.z, 0.0001f);
    }

    [Test]
    public void MoveGameplayCamera_ClampsToBoundsWithPadding()
    {
        MinimapController controller = Track(new GameObject("Minimap")).AddComponent<MinimapController>();
        Transform gameplayCamera = Track(new GameObject("GameplayCamera")).transform;
        gameplayCamera.position = new Vector3(0f, 15f, 0f);

        BoxCollider boundsCollider = Track(new GameObject("Bounds")).AddComponent<BoxCollider>();
        boundsCollider.center = Vector3.zero;
        boundsCollider.size = new Vector3(10f, 2f, 10f);

        SetPrivateField(controller, "gameplayCamera", gameplayCamera);
        SetPrivateField(controller, "mapBounds", boundsCollider);
        SetPrivateField(controller, "boundsPadding", 1f);

        InvokePrivateMethod(controller, "MoveGameplayCamera", new Vector3(99f, 0f, -99f));

        Assert.AreEqual(4f, gameplayCamera.position.x, 0.0001f);
        Assert.AreEqual(15f, gameplayCamera.position.y, 0.0001f);
        Assert.AreEqual(-4f, gameplayCamera.position.z, 0.0001f);
    }

    [Test]
    public void UpdateMarker_CopiesGameplayXZAndUsesNegativeGameplayYawOnZ()
    {
        MinimapController controller = Track(new GameObject("Minimap")).AddComponent<MinimapController>();
        Transform gameplayCamera = Track(new GameObject("GameplayCamera")).transform;
        gameplayCamera.position = new Vector3(7f, 10f, -3f);
        gameplayCamera.eulerAngles = new Vector3(0f, 135f, 0f);

        Transform marker = Track(new GameObject("Marker")).transform;
        marker.eulerAngles = new Vector3(11f, 22f, 33f);

        SetPrivateField(controller, "gameplayCamera", gameplayCamera);
        SetPrivateField(controller, "markerTransform", marker);
        SetPrivateField(controller, "markerY", 280f);

        InvokePrivateMethod(controller, "CacheMarkerFixedAngles");
        InvokePrivateMethod(controller, "UpdateMarker");

        Assert.AreEqual(7f, marker.position.x, 0.0001f);
        Assert.AreEqual(280f, marker.position.y, 0.0001f);
        Assert.AreEqual(-3f, marker.position.z, 0.0001f);
        Assert.AreEqual(0f, Mathf.Abs(Mathf.DeltaAngle(11f, marker.eulerAngles.x)), 0.0001f);
        Assert.AreEqual(0f, Mathf.Abs(Mathf.DeltaAngle(22f, marker.eulerAngles.y)), 0.0001f);
        Assert.AreEqual(0f, Mathf.Abs(Mathf.DeltaAngle(-135f, marker.eulerAngles.z)), 0.0001f);
    }

    [Test]
    public void TryNavigate_ReturnsFalse_WhenDependenciesAreMissing()
    {
        EventSystem eventSystem = Track(new GameObject("EventSystem", typeof(EventSystem))).GetComponent<EventSystem>();
        MinimapController controller = Track(new GameObject("Minimap")).AddComponent<MinimapController>();
        PointerEventData pointerEvent = new(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            pointerId = 1,
            position = new Vector2(50f, 50f)
        };

        bool navigated = InvokePrivateMethod<bool>(controller, "TryNavigate", pointerEvent);

        Assert.IsFalse(navigated);
    }

    [Test]
    public void TryNavigate_UsesFallbackPlaneAndMovesGameplayCameraXZ()
    {
        EventSystem eventSystem = Track(new GameObject("EventSystem", typeof(EventSystem))).GetComponent<EventSystem>();
        Canvas canvas = Track(new GameObject("Canvas")).AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        RectTransform minimapRect = Track(new GameObject("MinimapRect")).AddComponent<RectTransform>();
        minimapRect.SetParent(canvas.transform, false);
        minimapRect.anchorMin = new Vector2(0.5f, 0.5f);
        minimapRect.anchorMax = new Vector2(0.5f, 0.5f);
        minimapRect.pivot = new Vector2(0.5f, 0.5f);
        minimapRect.anchoredPosition = Vector2.zero;
        minimapRect.sizeDelta = new Vector2(300f, 300f);

        Camera minimapCamera = Track(new GameObject("MinimapCamera")).AddComponent<Camera>();
        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = 25f;
        minimapCamera.transform.position = new Vector3(0f, 120f, 0f);
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Transform gameplayCamera = Track(new GameObject("GameplayCamera")).transform;
        gameplayCamera.position = new Vector3(10f, 30f, 10f);

        MinimapController controller = Track(new GameObject("MinimapController")).AddComponent<MinimapController>();
        SetPrivateField(controller, "minimapCamera", minimapCamera);
        SetPrivateField(controller, "gameplayCamera", gameplayCamera);
        SetPrivateField(controller, "minimapRect", minimapRect);
        SetPrivateField(controller, "navigationMask", (LayerMask)0);
        SetPrivateField(controller, "fallbackPlaneY", 5f);

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, minimapRect.TransformPoint(minimapRect.rect.center));
        PointerEventData pointerEvent = new(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            pointerId = 3,
            position = screenPoint
        };

        bool navigated = InvokePrivateMethod<bool>(controller, "TryNavigate", pointerEvent);

        Assert.IsTrue(navigated);
        Assert.AreEqual(0f, gameplayCamera.position.x, 0.001f);
        Assert.AreEqual(30f, gameplayCamera.position.y, 0.001f);
        Assert.AreEqual(0f, gameplayCamera.position.z, 0.001f);
    }

    [Test]
    public void PointerHandlers_LeftClickSuccessPath_CaptureDragAndReleasePointer()
    {
        EventSystem eventSystem = Track(new GameObject("EventSystem", typeof(EventSystem))).GetComponent<EventSystem>();
        Canvas canvas = Track(new GameObject("Canvas")).AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        RectTransform minimapRect = Track(new GameObject("MinimapRect")).AddComponent<RectTransform>();
        minimapRect.SetParent(canvas.transform, false);
        minimapRect.anchorMin = new Vector2(0.5f, 0.5f);
        minimapRect.anchorMax = new Vector2(0.5f, 0.5f);
        minimapRect.pivot = new Vector2(0.5f, 0.5f);
        minimapRect.anchoredPosition = Vector2.zero;
        minimapRect.sizeDelta = new Vector2(300f, 300f);

        Camera minimapCamera = Track(new GameObject("MinimapCamera")).AddComponent<Camera>();
        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = 25f;
        minimapCamera.transform.position = new Vector3(0f, 120f, 0f);
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Transform gameplayCamera = Track(new GameObject("GameplayCamera")).transform;
        gameplayCamera.position = new Vector3(10f, 30f, 10f);

        MinimapController controller = Track(new GameObject("MinimapController")).AddComponent<MinimapController>();
        SetPrivateField(controller, "minimapCamera", minimapCamera);
        SetPrivateField(controller, "gameplayCamera", gameplayCamera);
        SetPrivateField(controller, "minimapRect", minimapRect);
        SetPrivateField(controller, "navigationMask", (LayerMask)0);
        SetPrivateField(controller, "fallbackPlaneY", 5f);

        Vector2 centerScreen = RectTransformUtility.WorldToScreenPoint(null, minimapRect.TransformPoint(minimapRect.rect.center));
        PointerEventData down = new(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            pointerId = 91,
            position = centerScreen
        };

        controller.OnPointerDown(down);

        Assert.IsTrue(GetPrivateField<bool>(controller, "pointerHeld"));
        Assert.AreEqual(91, GetPrivateField<int>(controller, "activePointerId"));
        Assert.AreEqual(0f, gameplayCamera.position.x, 0.001f);
        Assert.AreEqual(30f, gameplayCamera.position.y, 0.001f);
        Assert.AreEqual(0f, gameplayCamera.position.z, 0.001f);

        Vector3 localDragPoint = new Vector3(minimapRect.rect.xMax - 1f, minimapRect.rect.yMax - 1f, 0f);
        Vector2 dragScreen = RectTransformUtility.WorldToScreenPoint(null, minimapRect.TransformPoint(localDragPoint));
        PointerEventData drag = new(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            pointerId = 91,
            position = dragScreen
        };

        controller.OnDrag(drag);
        Assert.Greater(gameplayCamera.position.x, 0f);
        Assert.Greater(gameplayCamera.position.z, 0f);

        PointerEventData up = new(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            pointerId = 91,
            position = dragScreen
        };

        controller.OnPointerUp(up);
        Assert.IsFalse(GetPrivateField<bool>(controller, "pointerHeld"));
        Assert.AreEqual(int.MinValue, GetPrivateField<int>(controller, "activePointerId"));
    }

    [Test]
    public void PointerHandlers_CaptureAndReleaseOnlyMatchingPointerId()
    {
        EventSystem eventSystem = Track(new GameObject("EventSystem", typeof(EventSystem))).GetComponent<EventSystem>();
        MinimapController controller = Track(new GameObject("Minimap")).AddComponent<MinimapController>();

        PointerEventData rightDown = new(eventSystem)
        {
            button = PointerEventData.InputButton.Right,
            pointerId = 11,
            position = Vector2.zero
        };

        controller.OnPointerDown(rightDown);
        Assert.IsFalse(GetPrivateField<bool>(controller, "pointerHeld"));
        Assert.AreEqual(int.MinValue, GetPrivateField<int>(controller, "activePointerId"));

        SetPrivateField(controller, "pointerHeld", true);
        SetPrivateField(controller, "activePointerId", 77);

        PointerEventData wrongDrag = new(eventSystem)
        {
            pointerId = 76,
            button = PointerEventData.InputButton.Left,
            position = new Vector2(100f, 100f)
        };
        controller.OnDrag(wrongDrag);
        Assert.IsTrue(GetPrivateField<bool>(controller, "pointerHeld"));
        Assert.AreEqual(77, GetPrivateField<int>(controller, "activePointerId"));

        PointerEventData wrongUp = new(eventSystem)
        {
            pointerId = 76,
            button = PointerEventData.InputButton.Left
        };

        controller.OnPointerUp(wrongUp);
        Assert.IsTrue(GetPrivateField<bool>(controller, "pointerHeld"));
        Assert.AreEqual(77, GetPrivateField<int>(controller, "activePointerId"));

        PointerEventData matchingUp = new(eventSystem)
        {
            pointerId = 77,
            button = PointerEventData.InputButton.Left
        };

        PointerEventData matchingDrag = new(eventSystem)
        {
            pointerId = 77,
            button = PointerEventData.InputButton.Left,
            position = new Vector2(120f, 120f)
        };
        controller.OnDrag(matchingDrag);

        controller.OnPointerUp(matchingUp);
        Assert.IsFalse(GetPrivateField<bool>(controller, "pointerHeld"));
        Assert.AreEqual(int.MinValue, GetPrivateField<int>(controller, "activePointerId"));
    }

    [Test]
    public void LateUpdate_AutoFindsGameplayCameraAndUpdatesMarker()
    {
        Camera mainCamera = CreateMainCamera();
        mainCamera.transform.position = new Vector3(4f, 15f, -2f);
        mainCamera.transform.eulerAngles = new Vector3(0f, 90f, 0f);

        MinimapController controller = Track(new GameObject("Minimap")).AddComponent<MinimapController>();
        Transform marker = Track(new GameObject("Marker")).transform;
        marker.eulerAngles = new Vector3(17f, 29f, 41f);

        SetPrivateField(controller, "gameplayCamera", null);
        SetPrivateField(controller, "autoFindMainCamera", true);
        SetPrivateField(controller, "markerTransform", marker);
        SetPrivateField(controller, "markerY", 220f);

        InvokePrivateMethod(controller, "CacheMarkerFixedAngles");
        InvokePrivateMethod(controller, "LateUpdate");

        Assert.AreSame(mainCamera.transform, GetPrivateField<Transform>(controller, "gameplayCamera"));
        Assert.AreEqual(4f, marker.position.x, 0.001f);
        Assert.AreEqual(220f, marker.position.y, 0.001f);
        Assert.AreEqual(-2f, marker.position.z, 0.001f);
        Assert.AreEqual(0f, Mathf.Abs(Mathf.DeltaAngle(-90f, marker.eulerAngles.z)), 0.001f);
    }

    private Camera CreateMainCamera()
    {
        if (Camera.main != null)
        {
            return Camera.main;
        }

        GameObject cameraObject = Track(new GameObject("MainCamera"));
        cameraObject.tag = "MainCamera";
        return cameraObject.AddComponent<Camera>();
    }

    private GameObject Track(GameObject go)
    {
        createdObjects.Add(go);
        return go;
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        return (T)field.GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivateMethod(object target, string methodName, params object[] parameters)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
        method.Invoke(target, parameters);
    }

    private static T InvokePrivateMethod<T>(object target, string methodName, params object[] parameters)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
        object result = method.Invoke(target, parameters);
        return result is T typed ? typed : default(T);
    }
}
