using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct CargoCostEntry
{
    public CargoType cargoType;
    [Min(0)] public int cost;
}

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }
    public static bool HasInstance => Instance != null;
    [SerializeField] private bool persistAcrossScenes;
    [SerializeField, Min(0)] private int startingBalance = 100000;
    [SerializeField, Min(1)] private int targetBalanceToWin = 1000000;
    [SerializeField] private bool blockTransactionsAfterGameOver = true;
    [SerializeField, Min(0)] private int roadPlacementCost = 800;
    [SerializeField, Min(0)] private int stopPlacementCost = 10000;
    [SerializeField, Min(0f)] private float refundRate = 1f;
    [SerializeField] private List<CargoCostEntry> vehiclePurchaseCosts = new()
    {
        new CargoCostEntry { cargoType = CargoType.Passengers, cost = 34000 },
        new CargoCostEntry { cargoType = CargoType.Iron, cost = 28000 },
        new CargoCostEntry { cargoType = CargoType.Steel, cost = 30000 },
        new CargoCostEntry { cargoType = CargoType.Wood, cost = 28000 },
        new CargoCostEntry { cargoType = CargoType.Paper, cost = 28000 },
        new CargoCostEntry { cargoType = CargoType.Furniture, cost = 31200 }
    };

    private readonly HashSet<BuildingEconomy> buildings = new();
    private readonly Dictionary<CargoType, int> vehicleCostLookup = new();
    private int currentBalance;
    private bool isBankrupt;
    private bool hasWon;

    public IReadOnlyCollection<BuildingEconomy> Buildings => buildings;
    public int CurrentBalance => currentBalance;
    public int StartingBalance => startingBalance;
    public int TargetBalanceToWin => targetBalanceToWin;
    public bool IsBankrupt => isBankrupt;
    public bool HasWon => hasWon;
    public bool IsGameOver => isBankrupt || hasWon;

    public event Action<int, int> BalanceChanged;
    public event Action GameWon;
    public event Action GameLost;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        RebuildVehicleCostLookup();
        ResetEconomyState();

        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnValidate()
    {
        refundRate = Mathf.Clamp01(refundRate);
        RebuildVehicleCostLookup();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [ContextMenu("Reset Economy State")]
    public void ResetEconomyState()
    {
        currentBalance = Mathf.Max(0, startingBalance);
        isBankrupt = false;
        hasWon = false;
        BalanceChanged?.Invoke(currentBalance, 0);
        EvaluateEndStates();
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

    public bool TrySpendForRoadPlacement(int roadObjectId)
    {
        return TrySpend(roadPlacementCost);
    }

    public int GetRoadPlacementCost(int roadObjectId)
    {
        return roadPlacementCost;
    }

    public int RefundForRoadRemoval(int roadObjectId)
    {
        return AddRefund(roadPlacementCost);
    }

    public bool TrySpendForStopPlacement()
    {
        return TrySpend(stopPlacementCost);
    }

    public int GetStopPlacementCost()
    {
        return stopPlacementCost;
    }

    public int RefundForStopRemoval()
    {
        return AddRefund(stopPlacementCost);
    }

    public bool TrySpendForVehicle(CargoType cargoType)
    {
        if (!vehicleCostLookup.TryGetValue(cargoType, out int cost))
        {
            return false;
        }

        return TrySpend(cost);
    }

    public int GetVehiclePurchaseCost(CargoType cargoType)
    {
        return vehicleCostLookup.TryGetValue(cargoType, out int cost) ? cost : -1;
    }

    public int RefundForVehicle(CargoType cargoType)
    {
        if (!vehicleCostLookup.TryGetValue(cargoType, out int cost))
        {
            return 0;
        }

        return AddRefund(cost);
    }

    public int AddRevenue(int amount)
    {
        if (amount <= 0)
        {
            return currentBalance;
        }

        if (blockTransactionsAfterGameOver && IsGameOver)
        {
            return currentBalance;
        }

        ApplyBalanceDelta(amount);
        return currentBalance;
    }

    private bool TrySpend(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (blockTransactionsAfterGameOver && IsGameOver)
        {
            return false;
        }

        ApplyBalanceDelta(-amount);
        return true;
    }

    private int AddRefund(int originalCost)
    {
        if (originalCost <= 0)
        {
            return currentBalance;
        }

        if (blockTransactionsAfterGameOver && IsGameOver)
        {
            return currentBalance;
        }

        int refundAmount = Mathf.RoundToInt(originalCost * refundRate);
        if (refundAmount <= 0)
        {
            return currentBalance;
        }

        ApplyBalanceDelta(refundAmount);
        return currentBalance;
    }

    private void ApplyBalanceDelta(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        currentBalance += delta;
        BalanceChanged?.Invoke(currentBalance, delta);
        EvaluateEndStates();
    }

    private void EvaluateEndStates()
    {
        if (!isBankrupt && currentBalance < 0)
        {
            isBankrupt = true;
            GameLost?.Invoke();
        }

        if (!hasWon && currentBalance >= targetBalanceToWin)
        {
            hasWon = true;
            GameWon?.Invoke();
        }
    }

    private void RebuildVehicleCostLookup()
    {
        vehicleCostLookup.Clear();
        for (int i = 0; i < vehiclePurchaseCosts.Count; i++)
        {
            CargoCostEntry entry = vehiclePurchaseCosts[i];
            if (entry.cargoType == CargoType.None || entry.cost < 0)
            {
                continue;
            }

            vehicleCostLookup[entry.cargoType] = entry.cost;
        }
    }
}
