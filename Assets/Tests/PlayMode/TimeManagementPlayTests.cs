using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class TimeManagementPlayTests
{
    private readonly List<GameObject> createdObjects = new();
    private float originalTimeScale;
    private float originalFixedDeltaTime;
    private bool originalAudioPause;

    [SetUp]
    public void SetUp()
    {
        originalTimeScale = Time.timeScale;
        originalFixedDeltaTime = Time.fixedDeltaTime;
        originalAudioPause = AudioListener.pause;
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = originalTimeScale;
        Time.fixedDeltaTime = originalFixedDeltaTime;
        AudioListener.pause = originalAudioPause;

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
    public void PauseGame_SetsTimeScaleToZeroAndPausesAudio()
    {
        Time.fixedDeltaTime = 0.02f;
        Time.timeScale = 3f;
        AudioListener.pause = false;

        ControlPanelHUD hud = CreateHud(normalSpeed: 1f, speed2x: 2f, speed4x: 4f);

        hud.PauseGame();

        Assert.AreEqual(0f, Time.timeScale, 0.0001f);
        Assert.AreEqual(0.02f, Time.fixedDeltaTime, 0.0001f);
        Assert.IsTrue(AudioListener.pause);
    }

    [Test]
    public void SetNormalSpeed_UsesConfiguredValueAndClampsNegativeToZero()
    {
        Time.fixedDeltaTime = 0.02f;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        ControlPanelHUD hud = CreateHud(normalSpeed: 1.5f, speed2x: 2f, speed4x: 4f);

        hud.SetNormalSpeed();
        Assert.AreEqual(1.5f, Time.timeScale, 0.0001f);
        Assert.AreEqual(0.03f, Time.fixedDeltaTime, 0.0001f);
        Assert.IsFalse(AudioListener.pause);

        SetPrivateField(hud, "normalSpeed", -5f);
        hud.SetNormalSpeed();
        Assert.AreEqual(0f, Time.timeScale, 0.0001f);
        Assert.AreEqual(0.02f, Time.fixedDeltaTime, 0.0001f);
        Assert.IsTrue(AudioListener.pause);
    }

    [Test]
    public void Set2xSpeed_UsesConfigured2xValue()
    {
        Time.fixedDeltaTime = 0.02f;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        ControlPanelHUD hud = CreateHud(normalSpeed: 1f, speed2x: 2.75f, speed4x: 4f);

        hud.Set2xSpeed();

        Assert.AreEqual(2.75f, Time.timeScale, 0.0001f);
        Assert.AreEqual(0.055f, Time.fixedDeltaTime, 0.0001f);
        Assert.IsFalse(AudioListener.pause);
    }

    [Test]
    public void Set4xSpeed_UsesConfigured4xValue()
    {
        Time.fixedDeltaTime = 0.02f;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        ControlPanelHUD hud = CreateHud(normalSpeed: 1f, speed2x: 2f, speed4x: 4.5f);

        hud.Set4xSpeed();

        Assert.AreEqual(4.5f, Time.timeScale, 0.0001f);
        Assert.AreEqual(0.09f, Time.fixedDeltaTime, 0.0001f);
        Assert.IsFalse(AudioListener.pause);
    }

    [Test]
    public void EnableRunInBackground_SetsApplicationFlag()
    {
        MethodInfo method = typeof(CoreUtility).GetMethod("EnableRunInBackground", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        bool originalRunInBackground = Application.runInBackground;
        try
        {
            Application.runInBackground = false;
            method.Invoke(null, null);
            Assert.IsTrue(Application.runInBackground);
        }
        finally
        {
            Application.runInBackground = originalRunInBackground;
        }
    }

    private ControlPanelHUD CreateHud(float normalSpeed, float speed2x, float speed4x)
    {
        ControlPanelHUD hud = Track(new GameObject("ControlPanelHUD")).AddComponent<ControlPanelHUD>();
        SetPrivateField(hud, "setNormalSpeedOnEnable", false);
        SetPrivateField(hud, "normalSpeed", normalSpeed);
        SetPrivateField(hud, "speed2x", speed2x);
        SetPrivateField(hud, "speed4x", speed4x);
        InvokePrivateMethod(hud, "Awake");
        return hud;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivateMethod(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
        method.Invoke(target, null);
    }

    private GameObject Track(GameObject go)
    {
        createdObjects.Add(go);
        return go;
    }
}

