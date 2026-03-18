using TMPro;
using UnityEngine;

public class BuildingInfoPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CameraController cameraController;
    [SerializeField] private TMP_Text infoText;

    [Header("Text")]
    [SerializeField] private string emptySelectionText = "Select a building";
    [SerializeField] private string noEconomyText = "No economy data for this object";

    [Header("Refresh")]
    [SerializeField] private bool liveUpdateWhileSelected = true;
    [SerializeField, Min(0.05f)] private float liveUpdateInterval = 0.25f;

    private BuildingEconomy currentBuildingEconomy;
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
        if (!liveUpdateWhileSelected || currentBuildingEconomy == null)
        {
            return;
        }

        if (Time.unscaledTime < nextLiveUpdateTime)
        {
            return;
        }

        nextLiveUpdateTime = Time.unscaledTime + liveUpdateInterval;
        SetText(currentBuildingEconomy.GetInfoText());
    }

    private void HandleSelectionChanged(GameObject selectedObject)
    {
        currentBuildingEconomy = FindBuildingEconomy(selectedObject);

        if (selectedObject == null)
        {
            SetText(emptySelectionText);
            return;
        }

        if (currentBuildingEconomy == null)
        {
            SetText($"{selectedObject.name}\n\n{noEconomyText}");
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

    private void SetText(string value)
    {
        if (infoText != null)
        {
            infoText.text = value;
        }
    }
}
