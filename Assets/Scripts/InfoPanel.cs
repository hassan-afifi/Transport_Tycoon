using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BuildingInfoPanel : MonoBehaviour
{
    [SerializeField] private CameraController cameraController;
    [SerializeField] private VehicleStopAssignPanel vehicleStopAssignPanel;
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private Button assignStopsButton;
    [SerializeField] private GameObject trafficLightControlsRoot;
    [SerializeField] private TMP_Text primaryPhaseLabelText;
    [SerializeField] private TMP_Text secondaryPhaseLabelText;
    [SerializeField] private TMP_InputField primaryPhaseDurationInput;
    [SerializeField] private TMP_InputField secondaryPhaseDurationInput;
    [SerializeField, Min(1f)] private float minTrafficLightPhaseDuration = 1f;
    [SerializeField, Min(1f)] private float maxTrafficLightPhaseDuration = 120f;
    [SerializeField] private string emptySelectionText = "Select a building";
    [SerializeField] private string noEconomyText = "No economy data for this object";
    [SerializeField] private string noVehicleText = "No vehicle data for this object";
    [SerializeField] private string noTrafficLightText = "No traffic light data for this object";
    [SerializeField] private bool liveUpdateWhileSelected = true;
    [SerializeField, Min(0.05f)] private float liveUpdateInterval = 0.25f;

    private BuildingEconomy currentBuildingEconomy;
    private VehicleAgent currentVehicleAgent;
    private TrafficLightNode currentTrafficLightNode;
    private bool isUpdatingTrafficLightInput;
    private float nextLiveUpdateTime;

    private void Awake()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<CameraController>();
        }

        SetTrafficLightControlsVisible(false);
        SetVehicleControlsVisible(false);
    }

    private void OnEnable()
    {
        BindTrafficLightInputs(true);

        if (cameraController != null)
        {
            cameraController.SelectionChanged += HandleSelectionChanged;
            HandleSelectionChanged(cameraController.SelectedObject);
            return;
        }

        SetText(emptySelectionText);
    }

    private void OnDisable()
    {
        BindTrafficLightInputs(false);

        if (cameraController != null)
        {
            cameraController.SelectionChanged -= HandleSelectionChanged;
        }
    }

    private void Update()
    {
        if (!liveUpdateWhileSelected)
        {
            return;
        }

        if (Time.unscaledTime < nextLiveUpdateTime)
        {
            return;
        }

        nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;

        if (currentBuildingEconomy != null)
        {
            SetText(currentBuildingEconomy.GetInfoText());
            return;
        }

        if (currentVehicleAgent != null)
        {
            SetText(GetVehicleInfoText(currentVehicleAgent));
            return;
        }

        if (currentTrafficLightNode != null)
        {
            SetText(GetTrafficLightInfoText(currentTrafficLightNode));
        }
    }

    private void HandleSelectionChanged(GameObject selectedObject)
    {
        currentBuildingEconomy = FindBuildingEconomy(selectedObject);
        currentVehicleAgent = FindVehicleAgent(selectedObject);
        currentTrafficLightNode = FindTrafficLightNode(selectedObject);

        SetTrafficLightControlsVisible(currentTrafficLightNode != null);
        SetVehicleControlsVisible(currentVehicleAgent != null);
        SyncTrafficLightControls();

        if (selectedObject == null)
        {
            SetText(emptySelectionText);
            return;
        }

        if (currentBuildingEconomy != null)
        {
            nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;
            SetText(currentBuildingEconomy.GetInfoText());
            return;
        }

        if (currentVehicleAgent != null)
        {
            nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;
            SetText(GetVehicleInfoText(currentVehicleAgent));
            return;
        }

        if (currentTrafficLightNode != null)
        {
            nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;
            SetText(GetTrafficLightInfoText(currentTrafficLightNode));
            return;
        }

        SetText($"{selectedObject.name}\n\n{noEconomyText}\n{noVehicleText}\n{noTrafficLightText}");
    }

    private void BindTrafficLightInputs(bool bind)
    {
        if (primaryPhaseDurationInput != null)
        {
            if (bind)
            {
                primaryPhaseDurationInput.onEndEdit.AddListener(HandlePrimaryPhaseDurationChanged);
            }
            else
            {
                primaryPhaseDurationInput.onEndEdit.RemoveListener(HandlePrimaryPhaseDurationChanged);
            }
        }

        if (secondaryPhaseDurationInput != null)
        {
            if (bind)
            {
                secondaryPhaseDurationInput.onEndEdit.AddListener(HandleSecondaryPhaseDurationChanged);
            }
            else
            {
                secondaryPhaseDurationInput.onEndEdit.RemoveListener(HandleSecondaryPhaseDurationChanged);
            }
        }
    }

    private void HandlePrimaryPhaseDurationChanged(string value)
    {
        if (isUpdatingTrafficLightInput || currentTrafficLightNode == null)
        {
            return;
        }

        if (!TryParseDuration(value, out float parsedSeconds))
        {
            SyncTrafficLightControls();
            return;
        }

        float clampedSeconds = Mathf.Clamp(parsedSeconds, minTrafficLightPhaseDuration, maxTrafficLightPhaseDuration);
        currentTrafficLightNode.SetPrimaryGreenDurationSeconds(clampedSeconds);
        SyncTrafficLightControls();
        nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;
        SetText(GetTrafficLightInfoText(currentTrafficLightNode));
    }

    private void HandleSecondaryPhaseDurationChanged(string value)
    {
        if (isUpdatingTrafficLightInput || currentTrafficLightNode == null)
        {
            return;
        }

        if (!TryParseDuration(value, out float parsedSeconds))
        {
            SyncTrafficLightControls();
            return;
        }

        float clampedSeconds = Mathf.Clamp(parsedSeconds, minTrafficLightPhaseDuration, maxTrafficLightPhaseDuration);
        currentTrafficLightNode.SetSecondaryGreenDurationSeconds(clampedSeconds);
        SyncTrafficLightControls();
        nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;
        SetText(GetTrafficLightInfoText(currentTrafficLightNode));
    }

    private static bool TryParseDuration(string value, out float parsedSeconds)
    {
        parsedSeconds = 0f;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().Replace(',', '.');
        return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedSeconds);
    }

    private void SyncTrafficLightControls()
    {
        isUpdatingTrafficLightInput = true;

        if (primaryPhaseLabelText != null)
        {
            primaryPhaseLabelText.text = currentTrafficLightNode != null
                ? currentTrafficLightNode.GetPrimaryDurationLabel()
                : "Primary";
        }

        if (secondaryPhaseLabelText != null)
        {
            secondaryPhaseLabelText.text = currentTrafficLightNode != null
                ? currentTrafficLightNode.GetSecondaryDurationLabel()
                : "Secondary";
        }

        if (primaryPhaseDurationInput != null)
        {
            primaryPhaseDurationInput.interactable = currentTrafficLightNode != null;
            primaryPhaseDurationInput.SetTextWithoutNotify(
                currentTrafficLightNode != null
                    ? currentTrafficLightNode.GetPrimaryGreenDurationSeconds().ToString("0.##")
                    : string.Empty);
        }

        if (secondaryPhaseDurationInput != null)
        {
            secondaryPhaseDurationInput.interactable = currentTrafficLightNode != null;
            secondaryPhaseDurationInput.SetTextWithoutNotify(
                currentTrafficLightNode != null
                    ? currentTrafficLightNode.GetSecondaryGreenDurationSeconds().ToString("0.##")
                    : string.Empty);
        }

        isUpdatingTrafficLightInput = false;
    }

    private static string GetTrafficLightInfoText(TrafficLightNode trafficLight)
    {
        if (trafficLight == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine(trafficLight.LightName);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine();
        builder.Append("Active phase: ").Append(trafficLight.GetActivePhaseLabel());
        return builder.ToString();
    }

    private static string GetVehicleInfoText(VehicleAgent vehicle)
    {
        if (vehicle == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        builder.Append("Cargo type: ").AppendLine(vehicle.CargoType.ToString());
        builder.AppendLine();
        builder.Append("Cargo: ").Append(vehicle.CargoAmount).Append(" / ").Append(vehicle.CargoCapacity);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine();
        return builder.ToString();
    }

    private static BuildingEconomy FindBuildingEconomy(GameObject selectedObject)
    {
        if (selectedObject == null)
        {
            return null;
        }

        BuildingEconomy onObject = selectedObject.GetComponent<BuildingEconomy>();
        if (onObject != null)
        {
            return onObject;
        }

        BuildingEconomy inParent = selectedObject.GetComponentInParent<BuildingEconomy>();
        if (inParent != null)
        {
            return inParent;
        }

        return selectedObject.GetComponentInChildren<BuildingEconomy>(true);
    }

    private static VehicleAgent FindVehicleAgent(GameObject selectedObject)
    {
        if (selectedObject == null)
        {
            return null;
        }

        VehicleAgent onObject = selectedObject.GetComponent<VehicleAgent>();
        if (onObject != null)
        {
            return onObject;
        }

        VehicleAgent inParent = selectedObject.GetComponentInParent<VehicleAgent>();
        if (inParent != null)
        {
            return inParent;
        }

        return selectedObject.GetComponentInChildren<VehicleAgent>(true);
    }

    private static TrafficLightNode FindTrafficLightNode(GameObject selectedObject)
    {
        if (selectedObject == null)
        {
            return null;
        }

        TrafficLightNode onObject = selectedObject.GetComponent<TrafficLightNode>();
        if (onObject != null)
        {
            return onObject;
        }

        TrafficLightNode inParent = selectedObject.GetComponentInParent<TrafficLightNode>();
        if (inParent != null)
        {
            return inParent;
        }

        return selectedObject.GetComponentInChildren<TrafficLightNode>(true);
    }

    private void SetTrafficLightControlsVisible(bool visible)
    {
        if (trafficLightControlsRoot != null)
        {
            trafficLightControlsRoot.SetActive(visible);
        }
    }

    private void SetVehicleControlsVisible(bool visible)
    {
        if (assignStopsButton != null)
        {
            assignStopsButton.gameObject.SetActive(visible);
            assignStopsButton.interactable = visible;
        }
    }

    public void OpenAssignedStopsPanelForSelectedVehicle()
    {
        if (currentVehicleAgent == null)
        {
            return;
        }

        if (vehicleStopAssignPanel == null)
        {
            vehicleStopAssignPanel = FindFirstObjectByType<VehicleStopAssignPanel>();
        }

        if (vehicleStopAssignPanel == null)
        {
            return;
        }

        vehicleStopAssignPanel.OpenForVehicle(currentVehicleAgent, false);
    }

    private void SetText(string value)
    {
        if (infoText != null)
        {
            infoText.text = value;
        }
    }
}
