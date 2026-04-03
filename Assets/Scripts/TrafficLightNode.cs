using UnityEngine;

public class TrafficLightNode : MonoBehaviour
{
    [SerializeField] private int lightId;
    [SerializeField] private string lightName;
    [SerializeField] private Vector3Int gridCell;
    [SerializeField] private bool isLockedInPlace;

    public int LightId => lightId;
    public string LightName => lightName;
    public Vector3Int GridCell => gridCell;
    public bool IsLockedInPlace => isLockedInPlace;

    public void Initialize(int id, Vector3Int cell, string displayName, bool lockedInPlace = false)
    {
        lightId = id;
        gridCell = cell;
        lightName = displayName;
        isLockedInPlace = lockedInPlace;
    }
}
