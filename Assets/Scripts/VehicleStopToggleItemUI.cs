using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VehicleStopToggleItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Toggle toggle;

    private VehicleStopAssignPanel owner;
    private bool suppressCallback;

    public int StopId { get; private set; }

    private void Awake()
    {
        if (toggle == null)
        {
            toggle = GetComponentInChildren<Toggle>(true);
        }
    }

    private void OnEnable()
    {
        if (toggle != null)
        {
            toggle.onValueChanged.AddListener(HandleToggleChanged);
        }
    }

    private void OnDisable()
    {
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveListener(HandleToggleChanged);
        }
    }

    public void Setup(VehicleStopAssignPanel panel, int stopId, string label, bool isOn)
    {
        owner = panel;
        StopId = stopId;
        if (labelText != null)
        {
            labelText.text = label;
        }

        SetIsOnWithoutNotify(isOn);
    }

    public void SetIsOnWithoutNotify(bool isOn)
    {
        if (toggle == null)
        {
            return;
        }

        suppressCallback = true;
        toggle.isOn = isOn;
        suppressCallback = false;
    }

    private void HandleToggleChanged(bool isOn)
    {
        if (suppressCallback || owner == null)
        {
            return;
        }

        owner.HandleStopToggleChanged(StopId, isOn);
    }
}
