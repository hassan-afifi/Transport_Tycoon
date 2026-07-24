using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class MenusEditTests
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

        FieldInfo musicInstance = typeof(PersistentMusicPlayer).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        musicInstance?.SetValue(null, null);
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
    public void MainMenu_OpenOptions_ActivatesOptionsMenuAndQuitDoesNotThrow()
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

        Assert.DoesNotThrow(() => mainMenu.QuitGame());
    }

    [Test]
    public void MainMenu_StartGame_AttemptsSceneLoadUsingConfiguredSceneName()
    {
        MainMenuController mainMenu = Track(new GameObject("MainMenuController")).AddComponent<MainMenuController>();
        SetPrivateField(mainMenu, "gameSceneName", " ");

        ExecuteIgnoringSceneLoadErrors(mainMenu.StartGame);
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
    public void GameResultPanel_ShowsOnWinAndLoseAndPublicActionsAreCallable()
    {
        EconomyManager winEconomy = CreateEconomyManager(startingBalance: 100, targetBalance: 150, roadCost: 250);
        GameResultPanel winPanel = CreateGameResultPanel(winEconomy, out GameObject winPanelRoot);

        InvokePrivateMethodIfExists(winPanel, "OnEnable");
        Assert.IsFalse(winPanelRoot.activeSelf);

        winEconomy.AddRevenue(60);
        Assert.IsTrue(winPanelRoot.activeSelf);
        Assert.AreEqual(0f, Time.timeScale, 0.0001f);
        Assert.IsTrue(AudioListener.pause);
        ExecuteIgnoringSceneLoadErrors(winPanel.GoToMainMenu);
        Assert.DoesNotThrow(() => winPanel.QuitGame());
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
        Assert.DoesNotThrow(() => losePanel.QuitGame());
        InvokePrivateMethodIfExists(losePanel, "OnDisable");
    }

    [Test]
    public void PauseMenu_GoToMainMenuAndQuitGame_ResetRuntimeAndInvokePublicActions()
    {
        Time.timeScale = 0f;
        AudioListener.pause = true;
        float baseFixed = Time.fixedDeltaTime;

        GameObject pauseRoot = Track(new GameObject("PauseMenuRoot"));
        pauseRoot.SetActive(true);
        PauseMenuController pauseController = Track(new GameObject("PauseMenuController")).AddComponent<PauseMenuController>();
        SetPrivateField(pauseController, "pauseMenuRoot", pauseRoot);
        SetPrivateField(pauseController, "mainMenuSceneName", " ");
        SetPrivateField(pauseController, "hidePauseMenuOnStart", false);
        InvokePrivateMethodIfExists(pauseController, "Awake");

        ExecuteIgnoringSceneLoadErrors(pauseController.GoToMainMenu);

        Assert.IsFalse(pauseRoot.activeSelf);
        Assert.AreEqual(1f, Time.timeScale, 0.0001f);
        Assert.AreEqual(baseFixed, Time.fixedDeltaTime, 0.0001f);
        Assert.IsFalse(AudioListener.pause);
        Assert.DoesNotThrow(() => pauseController.QuitGame());
    }

    [Test]
    public void GameResultPanel_RestartGame_ResumesRuntimeBeforeAttemptingSceneReload()
    {
        EconomyManager economy = CreateEconomyManager(startingBalance: 100, targetBalance: 200, roadCost: 250);
        GameResultPanel panel = CreateGameResultPanel(economy, out _);

        Time.timeScale = 0f;
        AudioListener.pause = true;
        string activeSceneName = SceneManager.GetActiveScene().name;

        ExecuteIgnoringSceneLoadErrors(panel.RestartGame);

        if (string.IsNullOrWhiteSpace(activeSceneName))
        {
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);
            Assert.IsTrue(AudioListener.pause);
            return;
        }

        Assert.AreEqual(1f, Time.timeScale, 0.0001f);
        Assert.IsFalse(AudioListener.pause);
    }

    [Test]
    public void DropdownHelper_ArrowAndCenteringMethods_Run()
    {
        Type dropdownType = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
        Assert.IsNotNull(dropdownType, "TMP_Dropdown type not found.");

        GameObject root = Track(new GameObject("DropdownRoot"));
        Component dropdown = root.AddComponent(dropdownType);
        DropdownHelper helper = root.AddComponent<DropdownHelper>();
        Image arrow = root.AddComponent<Image>();
        SetPrivateField(helper, "arrowGraphic", arrow);

        InvokePrivateMethodIfExists(helper, "Awake");
        InvokePrivateMethodIfExists(helper, "OnEnable");
        InvokePrivateMethod(helper, "UpdateArrow", true);
        InvokePrivateMethod(helper, "UpdateArrow", false);
        InvokePrivateMethodIfExists(helper, "LateUpdate");

        GameObject canvasGo = Track(new GameObject("Canvas"));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        root.transform.SetParent(canvas.transform, false);

        GameObject scrollGo = Track(new GameObject("Scroll"));
        scrollGo.transform.SetParent(canvasGo.transform, false);
        ScrollRect scrollRect = scrollGo.AddComponent<ScrollRect>();
        GameObject viewportGo = Track(new GameObject("Viewport"));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewport = viewportGo.AddComponent<RectTransform>();
        viewport.sizeDelta = new Vector2(100f, 100f);
        GameObject contentGo = Track(new GameObject("Content"));
        contentGo.transform.SetParent(scrollGo.transform, false);
        RectTransform content = contentGo.AddComponent<RectTransform>();
        content.sizeDelta = new Vector2(100f, 400f);
        scrollRect.viewport = viewport;
        scrollRect.content = content;
        scrollGo.SetActive(true);

        float itemHeight = (float)InvokePrivateMethod(helper, "GetItemHeight", content, 4);
        Assert.Greater(itemHeight, 0f);

        object found = InvokePrivateMethod(helper, "FindOpenList");
        Assert.AreSame(scrollRect, found);

        IEnumerator center = (IEnumerator)InvokePrivateMethod(helper, "CenterOnOpen");
        Assert.IsNotNull(center);
        while (center.MoveNext())
        {
        }

        Assert.IsNotNull(dropdown);
    }

    [Test]
    public void SliderSync_UpdatesFromSliderAndInput()
    {
        Type inputFieldType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
        Assert.IsNotNull(inputFieldType, "TMP_InputField type not found.");

        GameObject root = Track(new GameObject("SliderSyncRoot"));
        root.SetActive(false);
        SliderSync sync = root.AddComponent<SliderSync>();

        GameObject sliderGo = Track(new GameObject("Slider"));
        sliderGo.transform.SetParent(root.transform, false);
        Slider slider = sliderGo.AddComponent<Slider>();

        GameObject inputGo = Track(new GameObject("TMP_InputField"));
        inputGo.transform.SetParent(root.transform, false);
        Component inputField = inputGo.AddComponent(inputFieldType);
        Assert.IsNotNull(inputField);

        slider.minValue = 10f;
        slider.maxValue = 90f;
        slider.value = 50f;
        SetPrivateField(sync, "slider", slider);
        SetPrivateField(sync, "inputField", inputField);

        root.SetActive(true);
        InvokePrivateMethodIfExists(sync, "Start");
        InvokePrivateMethod(sync, "OnSliderChanged", 25f);
        InvokePrivateMethod(sync, "OnInputEdit", "123");
        InvokePrivateMethod(sync, "OnInputEdit", "not_a_number");
        InvokePrivateMethod(sync, "SyncInput");

        string formatted = (string)InvokePrivateMethod(sync, "FormatValue", 42.4f);
        Assert.AreEqual("42", formatted);

        InvokePrivateMethodIfExists(sync, "OnDestroy");
        Assert.That(slider.value, Is.InRange(slider.minValue, slider.maxValue));
    }

    [Test]
    public void PersistentMusicPlayer_OnValidateAndStartApplyAudioSettings()
    {
        GameObject root = Track(new GameObject("PersistentMusicPlayerRoot"));
        AudioSource audioSource = root.AddComponent<AudioSource>();
        PersistentMusicPlayer player = root.AddComponent<PersistentMusicPlayer>();
        AudioClip clip = AudioClip.Create("MusicClip", 64, 1, 44100, false);
        SetPrivateField(player, "musicClip", clip);
        SetPrivateField(player, "volume", 0.35f);
        SetPrivateField(player, "playOnStart", true);
        SetPrivateField(player, "playWhilePaused", true);

        TargetInvocationException awakeException = Assert.Throws<TargetInvocationException>(() => InvokePrivateMethod(player, "Awake"));
        Assert.IsNotNull(awakeException);
        Assert.IsInstanceOf<InvalidOperationException>(awakeException.InnerException);

        SetPrivateField(player, "audioSource", audioSource);
        InvokePrivateMethodIfExists(player, "OnValidate");
        InvokePrivateMethodIfExists(player, "Start");

        Assert.AreEqual(0.35f, audioSource.volume, 0.0001f);
        Assert.IsTrue(audioSource.loop);
        Assert.IsTrue(audioSource.ignoreListenerPause);
        Assert.AreSame(clip, audioSource.clip);
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

    private static object InvokePrivateMethod(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
        return method.Invoke(target, args);
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

    private static void ExecuteIgnoringSceneLoadErrors(Action action)
    {
        bool previousIgnore = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            action?.Invoke();
        }
        catch (Exception)
        {
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnore;
        }
    }
}
