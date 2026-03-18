using UnityEngine;

public enum StopRoadAxis
{
    None = 0,
    NorthSouth = 1,
    EastWest = 2
}

public class StopNode : MonoBehaviour
{
    [SerializeField] private int stopId;
    [SerializeField] private string stopName;
    [SerializeField] private Vector3Int gridCell;
    [SerializeField] private StopRoadAxis roadAxis;
    [SerializeField] private bool isLockedInPlace;

    public int StopId => stopId;
    public string StopName => stopName;
    public Vector3Int GridCell => gridCell;
    public StopRoadAxis RoadAxis => roadAxis;
    public bool IsLockedInPlace => isLockedInPlace;

    public void Initialize(
        int id,
        Vector3Int cell,
        string displayName,
        StopRoadAxis axis = StopRoadAxis.None,
        bool lockedInPlace = false)
    {
        stopId = id;
        gridCell = cell;
        stopName = displayName;
        roadAxis = axis;
        isLockedInPlace = lockedInPlace;
    }
}
