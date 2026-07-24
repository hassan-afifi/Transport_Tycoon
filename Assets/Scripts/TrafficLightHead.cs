using UnityEngine;

public enum TrafficLightSignalColor
{
    Red = 0,
    Yellow = 1,
    Green = 2
}

public class TrafficLightHead : MonoBehaviour
{
    [SerializeField] private Light greenLight;
    [SerializeField] private Light yellowLight;
    [SerializeField] private Light redLight;

    private void Awake()
    {
        AutoAssignMissingLights();
    }

    private void OnValidate()
    {
        AutoAssignMissingLights();
    }

    public void AutoAssignMissingLights()
    {
        if (greenLight != null && yellowLight != null && redLight != null)
        {
            return;
        }

        Light[] lights = GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light candidate = lights[i];
            if (candidate == null)
            {
                continue;
            }

            string lightName = candidate.name.ToLowerInvariant();
            if (greenLight == null && lightName.Contains("green"))
            {
                greenLight = candidate;
                continue;
            }

            if (yellowLight == null && lightName.Contains("yellow"))
            {
                yellowLight = candidate;
                continue;
            }

            if (redLight == null && (lightName.Contains("red") || lightName.Contains("read")))
            {
                redLight = candidate;
            }
        }
    }

    public void SetSignal(TrafficLightSignalColor signalColor)
    {
        if (greenLight != null)
        {
            greenLight.enabled = signalColor == TrafficLightSignalColor.Green;
        }

        if (yellowLight != null)
        {
            yellowLight.enabled = signalColor == TrafficLightSignalColor.Yellow;
        }

        if (redLight != null)
        {
            redLight.enabled = signalColor == TrafficLightSignalColor.Red;
        }
    }
}
