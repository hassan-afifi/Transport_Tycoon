using TMPro;
using UnityEngine;
using System.Text;

public class BuildingInfoPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CameraController cameraController;
    [SerializeField] private TMP_Text infoText;

    [Header("Text")]
    [SerializeField] private string emptySelectionText = "Select a building";
    [SerializeField] private string noEconomyText = "No economy data for this object";
    [SerializeField] private string noVehicleText = "No vehicle data for this object";

    [Header("Refresh")]
    [SerializeField] private bool liveUpdateWhileSelected = true;
    [SerializeField, Min(0.05f)] private float liveUpdateInterval = 0.25f;

    private BuildingEconomy currentBuildingEconomy;
    private VehicleAgent currentVehicleAgent;
    private float nextLiveUpdateTime;

    private void Awake()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<CameraController>();
        }
    }

    private void OnEnable()
    {
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
        }
    }

    private void HandleSelectionChanged(GameObject selectedObject)
    {
        currentBuildingEconomy = FindBuildingEconomy(selectedObject);
        currentVehicleAgent = FindVehicleAgent(selectedObject);

        if (selectedObject == null)
        {
            SetText(emptySelectionText);
            return;
        }

        if (currentBuildingEconomy == null)
        {
            if (currentVehicleAgent == null)
            {
                SetText($"{selectedObject.name}\n\n{noEconomyText}\n{noVehicleText}");
                return;
            }

            nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;
            SetText(GetVehicleInfoText(currentVehicleAgent));
            return;
        }

        nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;
        SetText(currentBuildingEconomy.GetInfoText());
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

    private static string GetVehicleInfoText(VehicleAgent vehicle)
    {
        if (vehicle == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        builder.Append("Cargo type: ").AppendLine(vehicle.CargoType.ToString());
        builder.Append("Cargo: ").Append(vehicle.CargoAmount).Append(" / ").Append(vehicle.CargoCapacity);
        return builder.ToString();
    }

    private void SetText(string value)
    {
        if (infoText != null)
        {
            infoText.text = value;
        }
    }
}
