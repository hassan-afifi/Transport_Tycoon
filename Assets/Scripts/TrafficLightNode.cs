using UnityEngine;

public enum TrafficLightLayoutMode
{
    Unsupported = 0,
    FourWay = 1,
    ThreeWay = 2
}

public class TrafficLightNode : MonoBehaviour
{
    [SerializeField] private int lightId;
    [SerializeField] private string lightName;
    [SerializeField] private Vector3Int gridCell;
    [SerializeField] private bool isLockedInPlace;
    [SerializeField] private RoadDirectionMask allowedDirections =
        RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.South | RoadDirectionMask.West;
    [SerializeField] private TrafficLightLayoutMode layoutMode = TrafficLightLayoutMode.FourWay;
    [SerializeField] private RoadDirectionMask primaryDirections = RoadDirectionMask.North | RoadDirectionMask.South;
    [SerializeField] private RoadDirectionMask secondaryDirections = RoadDirectionMask.East | RoadDirectionMask.West;
    [SerializeField, Min(1f)] private float primaryGreenDurationSeconds = 8f;
    [SerializeField, Min(1f)] private float secondaryGreenDurationSeconds = 8f;
    [SerializeField, Min(0f)] private float yellowDurationSeconds = 2f;
    [SerializeField] private bool primaryPhaseActive = true;
    [SerializeField] private bool yellowPhase;
    [SerializeField] private float phaseElapsedSeconds;

    private static readonly RoadDirectionMask CardinalMask =
        RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.South | RoadDirectionMask.West;

    private TrafficLightHead[] heads;

    public int LightId => lightId;
    public string LightName => lightName;
    public Vector3Int GridCell => gridCell;
    public bool IsLockedInPlace => isLockedInPlace;
    public RoadDirectionMask AllowedDirections => allowedDirections;
    public TrafficLightLayoutMode LayoutMode => layoutMode;
    public bool IsYellowPhase => yellowPhase;
    public bool IsPrimaryPhaseActive => primaryPhaseActive;

    private void Awake()
    {
        CacheHeads();
        RebuildLayoutFromAllowedDirections();
        ApplySignalLights();
    }

    private void OnEnable()
    {
        ApplySignalLights();
    }

    public void Initialize(int id, Vector3Int cell, string displayName, bool lockedInPlace = false)
    {
        lightId = id;
        gridCell = cell;
        lightName = displayName;
        isLockedInPlace = lockedInPlace;
        phaseElapsedSeconds = 0f;
        primaryPhaseActive = true;
        yellowPhase = false;
        CacheHeads();
        RebuildLayoutFromAllowedDirections();
        ApplySignalLights();
    }

    public void ConfigureAllowedDirections(RoadDirectionMask directions)
    {
        allowedDirections = directions & CardinalMask;
        phaseElapsedSeconds = 0f;
        primaryPhaseActive = true;
        yellowPhase = false;
        RebuildLayoutFromAllowedDirections();
        ApplySignalLights();
    }

    public bool SupportsDirection(RoadDirectionMask direction)
    {
        return HasDirection(allowedDirections, direction);
    }

    public float GetPrimaryGreenDurationSeconds()
    {
        return Mathf.Max(1f, primaryGreenDurationSeconds);
    }

    public float GetSecondaryGreenDurationSeconds()
    {
        return Mathf.Max(1f, secondaryGreenDurationSeconds);
    }

    public string GetPrimaryDurationLabel()
    {
        if (layoutMode == TrafficLightLayoutMode.FourWay)
        {
            return "N/S";
        }

        return "Main";
    }

    public string GetSecondaryDurationLabel()
    {
        if (layoutMode == TrafficLightLayoutMode.FourWay)
        {
            return "E/W";
        }

        return "Side";
    }

    public void SetPrimaryGreenDurationSeconds(float durationSeconds)
    {
        primaryGreenDurationSeconds = Mathf.Max(1f, durationSeconds);
        if (!yellowPhase && primaryPhaseActive)
        {
            float currentDuration = GetCurrentPhaseDurationSeconds();
            if (phaseElapsedSeconds > currentDuration)
            {
                phaseElapsedSeconds = Mathf.Repeat(phaseElapsedSeconds, currentDuration);
            }
        }
    }

    public void SetSecondaryGreenDurationSeconds(float durationSeconds)
    {
        secondaryGreenDurationSeconds = Mathf.Max(1f, durationSeconds);
        if (!yellowPhase && !primaryPhaseActive)
        {
            float currentDuration = GetCurrentPhaseDurationSeconds();
            if (phaseElapsedSeconds > currentDuration)
            {
                phaseElapsedSeconds = Mathf.Repeat(phaseElapsedSeconds, currentDuration);
            }
        }
    }

    public bool IsDirectionGreen(RoadDirectionMask incomingDirection)
    {
        if (yellowPhase)
        {
            return false;
        }

        RoadDirectionMask activeMask = primaryPhaseActive ? primaryDirections : secondaryDirections;
        return HasDirection(activeMask, incomingDirection);
    }

    public string GetActivePhaseLabel()
    {
        if (layoutMode == TrafficLightLayoutMode.Unsupported)
        {
            return "No Active Direction";
        }

        string groupLabel = primaryPhaseActive ? GetPrimaryDurationLabel() : GetSecondaryDurationLabel();
        return yellowPhase ? $"{groupLabel} Yellow" : $"{groupLabel} Green";
    }

    private void Update()
    {
        if (layoutMode == TrafficLightLayoutMode.Unsupported || primaryDirections == RoadDirectionMask.None || secondaryDirections == RoadDirectionMask.None)
        {
            return;
        }

        float dt = Time.unscaledDeltaTime * Mathf.Max(0f, Time.timeScale);
        if (dt <= 0f)
        {
            return;
        }

        phaseElapsedSeconds += dt;
        float currentPhaseDuration = GetCurrentPhaseDurationSeconds();
        while (phaseElapsedSeconds >= currentPhaseDuration)
        {
            phaseElapsedSeconds -= currentPhaseDuration;
            AdvancePhase();
            currentPhaseDuration = GetCurrentPhaseDurationSeconds();
            ApplySignalLights();
        }
    }

    private void OnTransformChildrenChanged()
    {
        CacheHeads();
        RebuildLayoutFromAllowedDirections();
        ApplySignalLights();
    }

    private void CacheHeads()
    {
        heads = GetComponentsInChildren<TrafficLightHead>(true);
    }

    private void RebuildLayoutFromAllowedDirections()
    {
        RoadDirectionMask mask = allowedDirections & CardinalMask;
        int connected = CountDirections(mask);
        if (connected < 3)
        {
            layoutMode = TrafficLightLayoutMode.Unsupported;
            primaryDirections = RoadDirectionMask.None;
            secondaryDirections = RoadDirectionMask.None;
            primaryPhaseActive = true;
            yellowPhase = false;
            return;
        }

        bool hasNorthSouthPair = HasDirection(mask, RoadDirectionMask.North) && HasDirection(mask, RoadDirectionMask.South);
        bool hasEastWestPair = HasDirection(mask, RoadDirectionMask.East) && HasDirection(mask, RoadDirectionMask.West);

        if (connected >= 4)
        {
            layoutMode = TrafficLightLayoutMode.FourWay;
            primaryDirections = RoadDirectionMask.North | RoadDirectionMask.South;
            secondaryDirections = RoadDirectionMask.East | RoadDirectionMask.West;
            return;
        }

        layoutMode = TrafficLightLayoutMode.ThreeWay;
        if (hasNorthSouthPair)
        {
            primaryDirections = RoadDirectionMask.North | RoadDirectionMask.South;
            secondaryDirections = mask & ~primaryDirections;
            return;
        }

        if (hasEastWestPair)
        {
            primaryDirections = RoadDirectionMask.East | RoadDirectionMask.West;
            secondaryDirections = mask & ~primaryDirections;
            return;
        }

        primaryDirections = GetFallbackPrimary(mask);
        secondaryDirections = mask & ~primaryDirections;
    }

    private void AdvancePhase()
    {
        if (!yellowPhase)
        {
            yellowPhase = true;
            return;
        }

        yellowPhase = false;
        primaryPhaseActive = !primaryPhaseActive;
    }

    private void ApplySignalLights()
    {
        if (heads == null || heads.Length == 0)
        {
            return;
        }

        for (int i = 0; i < heads.Length; i++)
        {
            TrafficLightHead head = heads[i];
            if (head == null)
            {
                continue;
            }

            RoadDirectionMask headDirection = RoadUtility.GetClosestCardinalDirection(head.transform.forward);
            RoadDirectionMask activeMask = primaryPhaseActive ? primaryDirections : secondaryDirections;
            TrafficLightSignalColor signalColor = TrafficLightSignalColor.Red;
            if (headDirection != RoadDirectionMask.None
                && HasDirection(allowedDirections, headDirection)
                && HasDirection(activeMask, headDirection))
            {
                signalColor = yellowPhase ? TrafficLightSignalColor.Yellow : TrafficLightSignalColor.Green;
            }

            head.SetSignal(signalColor);
        }
    }

    private static bool HasDirection(RoadDirectionMask mask, RoadDirectionMask direction)
    {
        return RoadUtility.HasDirection(mask, direction);
    }

    private float GetCurrentPhaseDurationSeconds()
    {
        if (yellowPhase)
        {
            return Mathf.Max(0.1f, yellowDurationSeconds);
        }

        return primaryPhaseActive
            ? Mathf.Max(1f, primaryGreenDurationSeconds)
            : Mathf.Max(1f, secondaryGreenDurationSeconds);
    }

    private static int CountDirections(RoadDirectionMask mask)
    {
        return RoadUtility.CountConnectedDirections(mask);
    }

    private static RoadDirectionMask GetFallbackPrimary(RoadDirectionMask mask)
    {
        if (HasDirection(mask, RoadDirectionMask.North) && HasDirection(mask, RoadDirectionMask.East))
        {
            return RoadDirectionMask.North | RoadDirectionMask.East;
        }

        if (HasDirection(mask, RoadDirectionMask.East) && HasDirection(mask, RoadDirectionMask.South))
        {
            return RoadDirectionMask.East | RoadDirectionMask.South;
        }

        if (HasDirection(mask, RoadDirectionMask.South) && HasDirection(mask, RoadDirectionMask.West))
        {
            return RoadDirectionMask.South | RoadDirectionMask.West;
        }

        if (HasDirection(mask, RoadDirectionMask.West) && HasDirection(mask, RoadDirectionMask.North))
        {
            return RoadDirectionMask.West | RoadDirectionMask.North;
        }

        return RoadDirectionMask.None;
    }
}
