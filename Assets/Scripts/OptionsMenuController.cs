using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OptionsMenuController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Dropdown displayModeDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Slider fovSlider;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private Slider volumeSlider;

    private const string DisplayModeKey = "options.displayMode";
    private const string ResolutionIndexKey = "options.resolutionIndex";
    private const string ResolutionWidthKey = "options.resolutionWidth";
    private const string ResolutionHeightKey = "options.resolutionHeight";
    private const string CameraFovKey = "options.cameraFov";
    private const string CameraSensitivityKey = "options.cameraSensitivity";
    private const string VolumeKey = "options.volume";

    private const int ModeWindowed = 0;
    private const int ModeBorderless = 1;
    private const int ModeFullscreen = 2;

    private const int DefaultDisplayModeIndex = ModeBorderless;
    private const int DefaultResolutionIndex = 0;
    private const float FovMin = 60f;
    private const float FovMax = 100f;
    private const float DefaultFov = 80f;
    private const float SensitivityPercentMin = 1f;
    private const float SensitivityPercentMax = 100f;
    private const float SensitivityPercentStep = 1f;
    private const float DefaultSensitivityPercent = 50f;
    private const float SensitivityNormalizedMin = 0.01f;
    private const float SensitivityNormalizedMax = 1f;
    private const float VolumePercentMin = 0f;
    private const float VolumePercentMax = 100f;
    private const float VolumePercentStep = 1f;
    private const float DefaultVolumePercent = 100f;
    private const float AspectRatioTolerance = 0.02f;

    private readonly List<Vector2Int> allResolutions = new();
    private readonly List<Vector2Int> filteredResolutions = new();

    private int appliedDisplayModeIndex;
    private Vector2Int appliedResolution;
    private float appliedFov;
    private float appliedSensitivityPercent;
    private float appliedVolumePercent;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

        BuildDisplayModeDropdown();
        BuildResolutionCatalog();
        ConfigureSliders();
        LoadAndApplySavedSettings();
        RefreshUiFromAppliedValues();
    }

    private void OnDestroy()
    {
        if (displayModeDropdown != null)
        {
            displayModeDropdown.onValueChanged.RemoveListener(HandleDisplayModeDropdownChanged);
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.onValueChanged.RemoveListener(HandleSensitivitySliderChanged);
        }
    }

    public void OpenMenu()
    {
        RefreshUiFromAppliedValues();

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
    }

    public void SaveAndClose()
    {
        ReadAppliedValuesFromUi();
        ApplyAppliedValues();
        SaveAppliedValues();
        CloseMenu();
    }

    public void CancelAndClose()
    {
        RefreshUiFromAppliedValues();
        CloseMenu();
    }

    public void CloseMenu()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void BuildDisplayModeDropdown()
    {
        if (displayModeDropdown == null)
        {
            return;
        }

        displayModeDropdown.ClearOptions();
        displayModeDropdown.AddOptions(new List<string>
        {
            "Windowed",
            "Borderless Windowed",
            "Fullscreen"
        });
    }

    private void BuildResolutionCatalog()
    {
        allResolutions.Clear();

        Resolution[] detectedResolutions = Screen.resolutions;
        for (int i = 0; i < detectedResolutions.Length; i++)
        {
            Vector2Int size = new(detectedResolutions[i].width, detectedResolutions[i].height);
            if (!allResolutions.Contains(size))
            {
                allResolutions.Add(size);
            }
        }

        if (allResolutions.Count == 0)
        {
            Vector2Int fallback = GetCurrentDisplayResolution();
            allResolutions.Add(fallback);
        }

        allResolutions.Sort((a, b) =>
        {
            int areaCompare = (b.x * b.y).CompareTo(a.x * a.y);
            return areaCompare != 0 ? areaCompare : b.x.CompareTo(a.x);
        });
    }

    private void ConfigureSliders()
    {
        if (displayModeDropdown != null)
        {
            displayModeDropdown.onValueChanged.RemoveListener(HandleDisplayModeDropdownChanged);
            displayModeDropdown.onValueChanged.AddListener(HandleDisplayModeDropdownChanged);
        }

        if (fovSlider != null)
        {
            fovSlider.minValue = FovMin;
            fovSlider.maxValue = FovMax;
            fovSlider.wholeNumbers = true;
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = SensitivityPercentMin;
            sensitivitySlider.maxValue = SensitivityPercentMax;
            sensitivitySlider.wholeNumbers = true;
            sensitivitySlider.onValueChanged.RemoveListener(HandleSensitivitySliderChanged);
            sensitivitySlider.onValueChanged.AddListener(HandleSensitivitySliderChanged);
        }

        if (volumeSlider != null)
        {
            volumeSlider.minValue = VolumePercentMin;
            volumeSlider.maxValue = VolumePercentMax;
            volumeSlider.wholeNumbers = true;
        }
    }

    private void HandleDisplayModeDropdownChanged(int modeIndex)
    {
        Vector2Int preferred = appliedResolution;
        if (resolutionDropdown != null && filteredResolutions.Count > 0)
        {
            int currentIndex = Mathf.Clamp(resolutionDropdown.value, 0, filteredResolutions.Count - 1);
            preferred = filteredResolutions[currentIndex];
        }

        RebuildResolutionDropdown(Mathf.Clamp(modeIndex, ModeWindowed, ModeFullscreen), preferred);
    }

    private void HandleSensitivitySliderChanged(float value)
    {
        if (sensitivitySlider == null)
        {
            return;
        }

        float snapped = Quantize(value, SensitivityPercentMin, SensitivityPercentMax, SensitivityPercentStep);
        if (!Mathf.Approximately(value, snapped))
        {
            sensitivitySlider.SetValueWithoutNotify(snapped);
        }
    }

    private void LoadAndApplySavedSettings()
    {
        appliedDisplayModeIndex = Mathf.Clamp(PlayerPrefs.GetInt(DisplayModeKey, DefaultDisplayModeIndex), ModeWindowed, ModeFullscreen);
        appliedResolution = LoadSavedResolution();

        if (appliedDisplayModeIndex == ModeBorderless)
        {
            appliedResolution = GetBestFitResolution(allResolutions);
        }
        else if (!allResolutions.Contains(appliedResolution))
        {
            appliedResolution = allResolutions[Mathf.Clamp(DefaultResolutionIndex, 0, allResolutions.Count - 1)];
        }

        appliedFov = Quantize(PlayerPrefs.GetFloat(CameraFovKey, DefaultFov), FovMin, FovMax, 1f);

        float storedSensitivity = PlayerPrefs.GetFloat(CameraSensitivityKey, DefaultSensitivityPercent);
        if (storedSensitivity <= 1f)
        {
            storedSensitivity *= 100f;
        }

        appliedSensitivityPercent = Quantize(
            storedSensitivity,
            SensitivityPercentMin,
            SensitivityPercentMax,
            SensitivityPercentStep);

        float storedVolume = PlayerPrefs.GetFloat(VolumeKey, DefaultVolumePercent);
        if (storedVolume <= 1f)
        {
            storedVolume *= 100f;
        }

        appliedVolumePercent = Quantize(
            storedVolume,
            VolumePercentMin,
            VolumePercentMax,
            VolumePercentStep);

        ApplyAppliedValues();
    }

    private Vector2Int LoadSavedResolution()
    {
        if (PlayerPrefs.HasKey(ResolutionWidthKey) && PlayerPrefs.HasKey(ResolutionHeightKey))
        {
            return new Vector2Int(
                Mathf.Max(1, PlayerPrefs.GetInt(ResolutionWidthKey)),
                Mathf.Max(1, PlayerPrefs.GetInt(ResolutionHeightKey)));
        }

        int legacyIndex = Mathf.Clamp(
            PlayerPrefs.GetInt(ResolutionIndexKey, DefaultResolutionIndex),
            0,
            Mathf.Max(0, allResolutions.Count - 1));

        if (allResolutions.Count > 0)
        {
            return allResolutions[legacyIndex];
        }

        return GetCurrentDisplayResolution();
    }

    private void SaveAppliedValues()
    {
        PlayerPrefs.SetInt(DisplayModeKey, appliedDisplayModeIndex);
        PlayerPrefs.SetInt(ResolutionIndexKey, FindClosestResolutionIndex(allResolutions, appliedResolution));
        PlayerPrefs.SetInt(ResolutionWidthKey, appliedResolution.x);
        PlayerPrefs.SetInt(ResolutionHeightKey, appliedResolution.y);
        PlayerPrefs.SetFloat(CameraFovKey, appliedFov);
        PlayerPrefs.SetFloat(CameraSensitivityKey, appliedSensitivityPercent);
        PlayerPrefs.SetFloat(VolumeKey, appliedVolumePercent);
        PlayerPrefs.Save();
    }

    private void ReadAppliedValuesFromUi()
    {
        if (displayModeDropdown != null)
        {
            appliedDisplayModeIndex = Mathf.Clamp(displayModeDropdown.value, ModeWindowed, ModeFullscreen);
        }

        if (appliedDisplayModeIndex == ModeBorderless)
        {
            appliedResolution = GetBestFitResolution(allResolutions);
        }
        else if (resolutionDropdown != null && filteredResolutions.Count > 0)
        {
            int index = Mathf.Clamp(resolutionDropdown.value, 0, filteredResolutions.Count - 1);
            appliedResolution = filteredResolutions[index];
        }

        if (fovSlider != null)
        {
            appliedFov = Quantize(fovSlider.value, FovMin, FovMax, 1f);
        }

        if (sensitivitySlider != null)
        {
            appliedSensitivityPercent = Quantize(
                sensitivitySlider.value,
                SensitivityPercentMin,
                SensitivityPercentMax,
                SensitivityPercentStep);
        }

        if (volumeSlider != null)
        {
            appliedVolumePercent = Quantize(
                volumeSlider.value,
                VolumePercentMin,
                VolumePercentMax,
                VolumePercentStep);
        }
    }

    private void RefreshUiFromAppliedValues()
    {
        if (displayModeDropdown != null)
        {
            displayModeDropdown.SetValueWithoutNotify(Mathf.Clamp(appliedDisplayModeIndex, ModeWindowed, ModeFullscreen));
        }

        RebuildResolutionDropdown(appliedDisplayModeIndex, appliedResolution);

        if (fovSlider != null)
        {
            fovSlider.SetValueWithoutNotify(ClampToSliderRange(fovSlider, appliedFov));
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.SetValueWithoutNotify(ClampToSliderRange(sensitivitySlider, appliedSensitivityPercent));
        }

        if (volumeSlider != null)
        {
            volumeSlider.SetValueWithoutNotify(ClampToSliderRange(volumeSlider, appliedVolumePercent));
        }
    }

    private void RebuildResolutionDropdown(int displayModeIndex, Vector2Int preferredResolution)
    {
        filteredResolutions.Clear();
        filteredResolutions.AddRange(GetResolutionsForMode(displayModeIndex));

        if (filteredResolutions.Count == 0)
        {
            filteredResolutions.AddRange(allResolutions);
        }

        if (filteredResolutions.Count == 0)
        {
            filteredResolutions.Add(GetCurrentDisplayResolution());
        }

        if (displayModeIndex == ModeBorderless)
        {
            preferredResolution = GetBestFitResolution(filteredResolutions);
            appliedResolution = preferredResolution;
        }

        if (resolutionDropdown == null)
        {
            return;
        }

        resolutionDropdown.ClearOptions();
        List<string> labels = new(filteredResolutions.Count);
        for (int i = 0; i < filteredResolutions.Count; i++)
        {
            Vector2Int size = filteredResolutions[i];
            labels.Add($"{size.x} x {size.y}");
        }

        resolutionDropdown.AddOptions(labels);
        int selectedIndex = FindClosestResolutionIndex(filteredResolutions, preferredResolution);
        resolutionDropdown.SetValueWithoutNotify(selectedIndex);
        resolutionDropdown.interactable = displayModeIndex != ModeBorderless;
    }

    private List<Vector2Int> GetResolutionsForMode(int displayModeIndex)
    {
        if (displayModeIndex != ModeFullscreen)
        {
            return allResolutions;
        }

        float displayAspect = GetCurrentDisplayAspectRatio();
        List<Vector2Int> matches = new();
        for (int i = 0; i < allResolutions.Count; i++)
        {
            Vector2Int size = allResolutions[i];
            if (size.y <= 0)
            {
                continue;
            }

            float ratio = (float)size.x / size.y;
            if (Mathf.Abs(ratio - displayAspect) <= AspectRatioTolerance)
            {
                matches.Add(size);
            }
        }

        return matches.Count > 0 ? matches : allResolutions;
    }

    private Vector2Int GetBestFitResolution(List<Vector2Int> source)
    {
        if (source == null || source.Count == 0)
        {
            return GetCurrentDisplayResolution();
        }

        Vector2Int display = GetCurrentDisplayResolution();
        int bestIndex = FindClosestResolutionIndex(source, display);
        return source[Mathf.Clamp(bestIndex, 0, source.Count - 1)];
    }

    private static int FindClosestResolutionIndex(List<Vector2Int> source, Vector2Int target)
    {
        if (source == null || source.Count == 0)
        {
            return 0;
        }

        int bestIndex = 0;
        long bestScore = long.MaxValue;
        for (int i = 0; i < source.Count; i++)
        {
            Vector2Int size = source[i];
            long score = Mathf.Abs(size.x - target.x) + Mathf.Abs(size.y - target.y);
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static Vector2Int GetCurrentDisplayResolution()
    {
        Resolution current = Screen.currentResolution;
        int width = current.width > 0 ? current.width : Screen.width;
        int height = current.height > 0 ? current.height : Screen.height;
        return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
    }

    private static float GetCurrentDisplayAspectRatio()
    {
        Vector2Int display = GetCurrentDisplayResolution();
        return display.y > 0 ? (float)display.x / display.y : (16f / 9f);
    }

    private void ApplyAppliedValues()
    {
        RebuildResolutionDropdown(appliedDisplayModeIndex, appliedResolution);

        Vector2Int resolutionToApply = appliedDisplayModeIndex == ModeBorderless
            ? GetBestFitResolution(filteredResolutions)
            : appliedResolution;

        Screen.SetResolution(
            resolutionToApply.x,
            resolutionToApply.y,
            IndexToDisplayMode(appliedDisplayModeIndex));

        appliedResolution = resolutionToApply;
        SetCameraFov(appliedFov);
        SetCameraSensitivity(appliedSensitivityPercent * 0.01f);
        AudioListener.volume = Mathf.Clamp01(appliedVolumePercent / 100f);
    }

    private static float ClampToSliderRange(Slider slider, float value)
    {
        return Mathf.Clamp(value, slider.minValue, slider.maxValue);
    }

    private static float Quantize(float value, float min, float max, float step)
    {
        float clamped = Mathf.Clamp(value, min, max);
        if (step <= 0f)
        {
            return clamped;
        }

        float steps = Mathf.Round((clamped - min) / step);
        return min + steps * step;
    }

    private void SetCameraFov(float value)
    {
        float clamped = Mathf.Clamp(value, FovMin, FovMax);
        CameraController controller = FindFirstObjectByType<CameraController>();
        if (controller != null)
        {
            controller.SetCameraFov(clamped);
            return;
        }

        if (Camera.main != null)
        {
            Camera.main.fieldOfView = clamped;
        }
    }

    private void SetCameraSensitivity(float value)
    {
        CameraController controller = FindFirstObjectByType<CameraController>();
        if (controller != null)
        {
            controller.SetOrbitSensitivity(Mathf.Clamp(value, SensitivityNormalizedMin, SensitivityNormalizedMax));
        }
    }

    private static FullScreenMode IndexToDisplayMode(int index)
    {
        return index switch
        {
            ModeWindowed => FullScreenMode.Windowed,
            ModeBorderless => FullScreenMode.FullScreenWindow,
            ModeFullscreen => FullScreenMode.ExclusiveFullScreen,
            _ => FullScreenMode.FullScreenWindow
        };
    }
}
