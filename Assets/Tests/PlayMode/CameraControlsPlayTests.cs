using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CameraControlsPlayTests
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
    public void SetOrbitSensitivity_UpdatesSensitivityWhenValueIsPositive()
    {
        CameraController controller = CreateController(false);

        controller.SetOrbitSensitivity(0.35f);

        Assert.AreEqual(0.35f, controller.OrbitSensitivity, 0.0001f);
    }

    [Test]
    public void SetOrbitSensitivity_ClampsToMinimumWhenValueIsTooLow()
    {
        CameraController controller = CreateController(false);

        controller.SetOrbitSensitivity(0f);

        Assert.AreEqual(0.001f, controller.OrbitSensitivity, 0.0001f);
    }

    [Test]
    public void CameraFov_ReturnsFallbackWhenNoCameraIsAssigned()
    {
        CameraController controller = CreateController(false);

        Assert.AreEqual(60f, controller.CameraFov, 0.0001f);
    }

    [Test]
    public void SetCameraFov_UsesAttachedCameraAndClampsIntoValidRange()
    {
        CameraController controller = CreateController(true);
        Camera localCamera = controller.GetComponent<Camera>();
        Assert.IsNotNull(localCamera);

        controller.SetCameraFov(999f);
        Assert.AreEqual(179f, localCamera.fieldOfView, 0.0001f);

        controller.SetCameraFov(-20f);
        Assert.AreEqual(1f, localCamera.fieldOfView, 0.0001f);
    }

    [Test]
    public void SetCameraFov_UsesMainCameraWhenNoLocalCameraExists()
    {
        CameraController controller = CreateController(false);
        Camera mainCamera = EnsureMainCameraExists();

        controller.SetCameraFov(72f);

        Assert.AreEqual(72f, mainCamera.fieldOfView, 0.0001f);
    }

    [Test]
    public void SelectionChanged_FiresOnlyWhenSelectionActuallyChanges()
    {
        CameraController controller = CreateController(false);
        GameObject a = Track(new GameObject("A"));
        GameObject b = Track(new GameObject("B"));

        int eventCount = 0;
        GameObject lastSelection = a;
        controller.SelectionChanged += selected =>
        {
            eventCount++;
            lastSelection = selected;
        };

        InvokeSetSelectedObject(controller, a);
        InvokeSetSelectedObject(controller, a);
        InvokeSetSelectedObject(controller, b);
        InvokeSetSelectedObject(controller, null);

        Assert.AreEqual(3, eventCount);
        Assert.IsNull(lastSelection);
        Assert.IsNull(controller.SelectedObject);
    }

    private CameraController CreateController(bool withLocalCamera)
    {
        GameObject go = Track(new GameObject("CameraController"));
        if (withLocalCamera)
        {
            go.AddComponent<Camera>();
        }

        return go.AddComponent<CameraController>();
    }

    private static void InvokeSetSelectedObject(CameraController controller, GameObject selection)
    {
        MethodInfo setSelectedObject = typeof(CameraController).GetMethod(
            "SetSelectedObject",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(setSelectedObject);
        setSelectedObject.Invoke(controller, new object[] { selection });
    }

    private Camera EnsureMainCameraExists()
    {
        if (Camera.main != null)
        {
            return Camera.main;
        }

        GameObject mainGo = Track(new GameObject("MainCamera"));
        mainGo.tag = "MainCamera";
        return mainGo.AddComponent<Camera>();
    }

    private GameObject Track(GameObject go)
    {
        createdObjects.Add(go);
        return go;
    }
}
