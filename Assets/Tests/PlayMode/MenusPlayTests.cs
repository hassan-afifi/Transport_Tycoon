using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class MenusPlayTests
{
    private const string CameraFovKey = "options.cameraFov";
    private const string CameraSensitivityKey = "options.cameraSensitivity";
    private const string VolumeKey = "options.volume";

    private readonly List<GameObject> createdObjects = new();
    private float originalTimeScale;
    private float originalFixedDeltaTime;
    private bool originalAudioPause;
    private float originalAudioVolume;

    [SetUp]
    public void SetUp()
    {
        originalTimeScale = Time.timeScale;
        originalFixedDeltaTime = Time.fixedDeltaTime;
        originalAudioPause = AudioListener.pause;
        originalAudioVolume = AudioListener.volume;
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = originalTimeScale;
        Time.fixedDeltaTime = originalFixedDeltaTime;
        AudioListener.pause = originalAudioPause;
        AudioListener.volume = originalAudioVolume;

        PlayerPrefs.DeleteKey(CameraFovKey);
        PlayerPrefs.DeleteKey(CameraSensitivityKey);
        PlayerPrefs.DeleteKey(VolumeKey);

        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }

        createdObjects.Clear();

        if (EconomyManager.HasInstance && EconomyManager.Instance != null && EconomyManager.Instance.gameObject != null)
        {
            UnityEngine.Object.DestroyImmediate(EconomyManager.Instance.gameObject);
        }
    }

    [Test]
    public void OptionsMenu_OpenSaveCancelAndClose_UpdatesStatePrefsAndCameraValues()
    {
        CameraController cameraController = CreateCameraController();
        OptionsMenuController optionsMenu = CreateOptionsMenu(
            out GameObject panelRoot,
            out Slider fovSlider,
            out Slider sensitivitySlider,
            out Slider volumeSlider);

        Assert.IsFalse(optionsMenu.IsOpen);
        Assert.IsFalse(panelRoot.activeSelf);

        optionsMenu.OpenMenu();
        Assert.IsTrue(optionsMenu.IsOpen);
        Assert.IsTrue(panelRoot.activeSelf);

        fovSlider.value = 95f;
        sensitivitySlider.value = 80f;
        volumeSlider.value = 35f;
        ExecuteIgnoringFailingMessages(() => optionsMenu.SaveAndClose());

        Assert.IsFalse(optionsMenu.IsOpen);
        Assert.IsFalse(panelRoot.activeSelf);
        Assert.AreEqual(95f, cameraController.CameraFov, 0.001f);
        Assert.AreEqual(0.8f, cameraController.OrbitSensitivity, 0.001f);
        Assert.AreEqual(0.35f, AudioListener.volume, 0.001f);
        Assert.AreEqual(95f, PlayerPrefs.GetFloat(CameraFovKey), 0.001f);
        Assert.AreEqual(80f, PlayerPrefs.GetFloat(CameraSensitivityKey), 0.001f);
        Assert.AreEqual(35f, PlayerPrefs.GetFloat(VolumeKey), 0.001f);

        optionsMenu.OpenMenu();
        fovSlider.value = 70f;
        sensitivitySlider.value = 10f;
        volumeSlider.value = 5f;
        optionsMenu.CancelAndClose();

        Assert.IsFalse(optionsMenu.IsOpen);
        Assert.AreEqual(95f, fovSlider.value, 0.001f);
        Assert.AreEqual(80f, sensitivitySlider.value, 0.001f);
        Assert.AreEqual(35f, volumeSlider.value, 0.001f);

        optionsMenu.OpenMenu();
        optionsMenu.CloseMenu();
        Assert.IsFalse(optionsMenu.IsOpen);
    }

    [Test]
    public void PauseMenu_PauseContinueAndOpenOptions_UpdateRuntimeState()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        OptionsMenuController optionsMenu = CreateOptionsMenu(
            out GameObject optionsPanelRoot,
            out _,
            out _,
            out _);
        GameObject pauseRoot = Track(new GameObject("PauseMenuRoot"));
        pauseRoot.SetActive(false);

        PauseMenuController pauseController = Track(new GameObject("PauseMenuController")).AddComponent<PauseMenuController>();
        SetPrivateField(pauseController, "pauseMenuRoot", pauseRoot);
        SetPrivateField(pauseController, "optionsMenu", optionsMenu);
        SetPrivateField(pauseController, "hidePauseMenuOnStart", true);
        InvokePrivateMethodIfExists(pauseController, "Awake");

        pauseController.PauseGame();
        Assert.IsTrue(pauseRoot.activeSelf);
        Assert.AreEqual(0f, Time.timeScale, 0.0001f);
        Assert.IsTrue(AudioListener.pause);

        pauseController.OpenOptions();
        Assert.IsTrue(optionsPanelRoot.activeSelf);

        pauseController.ContinueGame();
        Assert.IsFalse(pauseRoot.activeSelf);
        Assert.AreEqual(1f, Time.timeScale, 0.0001f);
        Assert.IsFalse(AudioListener.pause);
    }

    [Test]
    public void PauseMenu_OnDisableWhilePaused_ResumesRuntime()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        PauseMenuController pauseController = Track(new GameObject("PauseMenuController")).AddComponent<PauseMenuController>();
        SetPrivateField(pauseController, "pauseMenuRoot", Track(new GameObject("PauseRoot")));
        SetPrivateField(pauseController, "hidePauseMenuOnStart", false);
        InvokePrivateMethodIfExists(pauseController, "Awake");

        pauseController.PauseGame();
        Assert.AreEqual(0f, Time.timeScale, 0.0001f);

        InvokePrivateMethodIfExists(pauseController, "OnDisable");

        Assert.AreEqual(1f, Time.timeScale, 0.0001f);
        Assert.IsFalse(AudioListener.pause);
    }

    [Test]
    public void ControlPanelSpeedMethods_SetExpectedTimeScaleAndAudioPause()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        ControlPanelHUD hud = Track(new GameObject("ControlPanelHUD")).AddComponent<ControlPanelHUD>();
        SetPrivateField(hud, "setNormalSpeedOnEnable", false);
        SetPrivateField(hud, "normalSpeed", 1f);
        SetPrivateField(hud, "speed2x", 2f);
        SetPrivateField(hud, "speed4x", 4f);
        InvokePrivateMethodIfExists(hud, "Awake");

        float baseFixed = Time.fixedDeltaTime;

        hud.PauseGame();
        Assert.AreEqual(0f, Time.timeScale, 0.0001f);
        Assert.AreEqual(baseFixed, Time.fixedDeltaTime, 0.0001f);
        Assert.IsTrue(AudioListener.pause);

        hud.Set2xSpeed();
        Assert.AreEqual(2f, Time.timeScale, 0.0001f);
        Assert.AreEqual(baseFixed * 2f, Time.fixedDeltaTime, 0.0001f);
        Assert.IsFalse(AudioListener.pause);

        hud.Set4xSpeed();
        Assert.AreEqual(4f, Time.timeScale, 0.0001f);
        Assert.AreEqual(baseFixed * 4f, Time.fixedDeltaTime, 0.0001f);

        hud.SetNormalSpeed();
        Assert.AreEqual(1f, Time.timeScale, 0.0001f);
        Assert.AreEqual(baseFixed, Time.fixedDeltaTime, 0.0001f);
    }

    [Test]
    public void MainMenu_OpenOptions_ActivatesOptionsMenu()
    {
        OptionsMenuController optionsMenu = CreateOptionsMenu(
            out GameObject panelRoot,
            out _,
            out _,
            out _);
        MainMenuController mainMenu = Track(new GameObject("MainMenuController")).AddComponent<MainMenuController>();
        SetPrivateField(mainMenu, "optionsMenu", optionsMenu);
        InvokePrivateMethodIfExists(mainMenu, "Awake");

        Assert.IsFalse(panelRoot.activeSelf);

        mainMenu.OpenOptions();
        Assert.IsTrue(panelRoot.activeSelf);

    }

    [Test]
    public void MinimapController_PointerHandlers_HandleMissingRuntimeDependenciesSafely()
    {
        EventSystem eventSystem = Track(new GameObject("EventSystem", typeof(EventSystem))).GetComponent<EventSystem>();
        MinimapController minimapController = Track(new GameObject("MinimapController")).AddComponent<MinimapController>();

        PointerEventData rightClick = new(eventSystem)
        {
            button = PointerEventData.InputButton.Right,
            pointerId = 1,
            position = new Vector2(100f, 100f)
        };

        Assert.DoesNotThrow(() => minimapController.OnPointerDown(rightClick));
        Assert.DoesNotThrow(() => minimapController.OnDrag(rightClick));
        Assert.DoesNotThrow(() => minimapController.OnPointerUp(rightClick));

        PointerEventData leftClick = new(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            pointerId = 2,
            position = new Vector2(50f, 50f)
        };

        Assert.DoesNotThrow(() => minimapController.OnPointerDown(leftClick));
        Assert.DoesNotThrow(() => minimapController.OnPointerUp(leftClick));
    }

    [Test]
    public void GameResultPanel_ShowsOnWinAndLose()
    {
        EconomyManager winEconomy = CreateEconomyManager(startingBalance: 100, targetBalance: 150, roadCost: 250);
        GameResultPanel winPanel = CreateGameResultPanel(winEconomy, out GameObject winPanelRoot);

        InvokePrivateMethodIfExists(winPanel, "OnEnable");
        Assert.IsFalse(winPanelRoot.activeSelf);

        winEconomy.AddRevenue(60);
        Assert.IsTrue(winPanelRoot.activeSelf);
        Assert.AreEqual(0f, Time.timeScale, 0.0001f);
        Assert.IsTrue(AudioListener.pause);
        Assert.DoesNotThrow(() => winPanel.GoToMainMenu());
        InvokePrivateMethodIfExists(winPanel, "OnDisable");

        if (EconomyManager.HasInstance && EconomyManager.Instance != null && EconomyManager.Instance.gameObject != null)
        {
            UnityEngine.Object.DestroyImmediate(EconomyManager.Instance.gameObject);
        }

        Time.timeScale = 1f;
        AudioListener.pause = false;

        EconomyManager loseEconomy = CreateEconomyManager(startingBalance: 100, targetBalance: 1000, roadCost: 250);
        GameResultPanel losePanel = CreateGameResultPanel(loseEconomy, out GameObject losePanelRoot);

        InvokePrivateMethodIfExists(losePanel, "OnEnable");
        Assert.IsTrue(loseEconomy.TrySpendForRoadPlacement(0));
        Assert.IsTrue(losePanelRoot.activeSelf);
        Assert.IsTrue(loseEconomy.IsBankrupt);
        InvokePrivateMethodIfExists(losePanel, "OnDisable");
    }

    private OptionsMenuController CreateOptionsMenu(
        out GameObject panelRoot,
        out Slider fovSlider,
        out Slider sensitivitySlider,
        out Slider volumeSlider)
    {
        panelRoot = Track(new GameObject("OptionsPanelRoot"));
        panelRoot.SetActive(false);

        fovSlider = Track(new GameObject("FovSlider", typeof(Slider))).GetComponent<Slider>();
        sensitivitySlider = Track(new GameObject("SensitivitySlider", typeof(Slider))).GetComponent<Slider>();
        volumeSlider = Track(new GameObject("VolumeSlider", typeof(Slider))).GetComponent<Slider>();

        OptionsMenuController menu = Track(new GameObject("OptionsMenuController")).AddComponent<OptionsMenuController>();
        SetPrivateField(menu, "panelRoot", panelRoot);
        SetPrivateField(menu, "displayModeDropdown", null);
        SetPrivateField(menu, "resolutionDropdown", null);
        SetPrivateField(menu, "fovSlider", fovSlider);
        SetPrivateField(menu, "sensitivitySlider", sensitivitySlider);
        SetPrivateField(menu, "volumeSlider", volumeSlider);
        ExecuteIgnoringFailingMessages(() => InvokePrivateMethodIfExists(menu, "Awake"));
        return menu;
    }

    private CameraController CreateCameraController()
    {
        GameObject go = Track(new GameObject("CameraController", typeof(Camera), typeof(CameraController)));
        CameraController controller = go.GetComponent<CameraController>();
        InvokePrivateMethodIfExists(controller, "Awake");
        return controller;
    }

    private GameResultPanel CreateGameResultPanel(EconomyManager economyManager, out GameObject panelRoot)
    {
        panelRoot = Track(new GameObject("GameResultPanelRoot"));
        panelRoot.SetActive(false);

        GameResultPanel panel = Track(new GameObject("GameResultPanel")).AddComponent<GameResultPanel>();
        SetPrivateField(panel, "economyManager", economyManager);
        SetPrivateField(panel, "panelRoot", panelRoot);
        SetPrivateField(panel, "resultText", null);
        SetPrivateField(panel, "mainMenuSceneName", " ");
        SetPrivateField(panel, "pauseGameOnResult", true);
        InvokePrivateMethodIfExists(panel, "Awake");
        return panel;
    }

    private EconomyManager CreateEconomyManager(
        int startingBalance,
        int targetBalance,
        int roadCost)
    {
        EconomyManager manager = Track(new GameObject("EconomyManager")).AddComponent<EconomyManager>();
        SetPrivateField(manager, "startingBalance", startingBalance);
        SetPrivateField(manager, "targetBalanceToWin", targetBalance);
        SetPrivateField(manager, "roadPlacementCost", roadCost);
        SetPrivateField(manager, "blockTransactionsAfterGameOver", true);
        SetPrivateField(manager, "refundRate", 1f);
        InvokePrivateMethodIfExists(manager, "OnValidate");
        InvokePrivateMethodIfExists(manager, "Awake");
        manager.ResetEconomyState();
        return manager;
    }

    private GameObject Track(GameObject gameObject)
    {
        createdObjects.Add(gameObject);
        return gameObject;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivateMethodIfExists(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }

    private static void ExecuteIgnoringFailingMessages(Action action)
    {
        bool previousIgnore = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            action?.Invoke();
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnore;
        }
    }
}
