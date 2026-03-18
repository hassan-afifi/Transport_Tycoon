using System.Collections.Generic;
using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;

    [SerializeField] private bool persistAcrossScenes;

    private readonly HashSet<BuildingEconomy> buildings = new();
    public IReadOnlyCollection<BuildingEconomy> Buildings => buildings;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Register(BuildingEconomy building)
    {
        if (building != null)
        {
            buildings.Add(building);
        }
    }

    public void Unregister(BuildingEconomy building)
    {
        if (building != null)
        {
            buildings.Remove(building);
        }
    }
}
