using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class EconomyPlayTests
{
    private readonly List<GameObject> createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
        }

        createdObjects.Clear();

        if (EconomyManager.HasInstance && EconomyManager.Instance != null && EconomyManager.Instance.gameObject != null)
        {
            Object.DestroyImmediate(EconomyManager.Instance.gameObject);
        }

        if (GridMap.HasInstance && GridMap.Instance != null && GridMap.Instance.gameObject != null)
        {
            Object.DestroyImmediate(GridMap.Instance.gameObject);
        }
    }

    [Test]
    public void SingletonAndResetEconomyState_ExposeExpectedInitialState()
    {
        EconomyManager manager = CreateEconomyManager(
            startingBalance: 7500,
            targetBalance: 9000,
            blockAfterGameOver: true);

        int balanceChangedCalls = 0;
        int lastBalance = int.MinValue;
        int lastDelta = int.MinValue;
        manager.BalanceChanged += (balance, delta) =>
        {
            balanceChangedCalls++;
            lastBalance = balance;
            lastDelta = delta;
        };

        manager.ResetEconomyState();

        Assert.AreSame(manager, EconomyManager.Instance);
        Assert.IsTrue(EconomyManager.HasInstance);
        Assert.AreEqual(7500, manager.CurrentBalance);
        Assert.AreEqual(7500, manager.StartingBalance);
        Assert.AreEqual(9000, manager.TargetBalanceToWin);
        Assert.IsFalse(manager.IsBankrupt);
        Assert.IsFalse(manager.HasWon);
        Assert.IsFalse(manager.IsGameOver);
        Assert.AreEqual(1, balanceChangedCalls);
        Assert.AreEqual(7500, lastBalance);
        Assert.AreEqual(0, lastDelta);
    }

    [Test]
    public void RegisterAndUnregister_TrackBuildingSetAndIgnoreNulls()
    {
        EconomyManager manager = CreateEconomyManager();
        BuildingEconomy first = CreateInactiveBuilding("BuildingA");
        BuildingEconomy second = CreateInactiveBuilding("BuildingB");

        manager.Register(null);
        manager.Register(first);
        manager.Register(first);
        manager.Register(second);

        Assert.AreEqual(2, manager.Buildings.Count);
        Assert.IsTrue(ContainsBuilding(manager.Buildings, first));
        Assert.IsTrue(ContainsBuilding(manager.Buildings, second));

        manager.Unregister(null);
        manager.Unregister(first);
        manager.Unregister(first);

        Assert.AreEqual(1, manager.Buildings.Count);
        Assert.IsFalse(ContainsBuilding(manager.Buildings, first));
        Assert.IsTrue(ContainsBuilding(manager.Buildings, second));
    }

    [Test]
    public void RoadPlacementCostAndRefund_UseConfiguredBaseAndAdditionalClearCost()
    {
        EconomyManager manager = CreateEconomyManager(
            startingBalance: 5000,
            roadCost: 300,
            refundRate: 1f);

        Assert.AreEqual(300, manager.GetRoadPlacementCost(0));
        Assert.AreEqual(380, manager.GetRoadPlacementCost(0, 80));
        Assert.AreEqual(300, manager.GetRoadPlacementCost(0, -50));

        bool spent = manager.TrySpendForRoadPlacement(0, 80);
        int afterSpendBalance = manager.CurrentBalance;
        int refundedBalance = manager.RefundForRoadRemoval(0);

        Assert.IsTrue(spent);
        Assert.AreEqual(4620, afterSpendBalance);
        Assert.AreEqual(4920, refundedBalance);
        Assert.AreEqual(4920, manager.CurrentBalance);
    }

    [Test]
    public void StopAndTrafficLightCostsAndRefunds_RespectRefundRate()
    {
        EconomyManager manager = CreateEconomyManager(
            startingBalance: 10000,
            stopCost: 2000,
            trafficLightCost: 1500,
            refundRate: 0.5f);

        Assert.AreEqual(2000, manager.GetStopPlacementCost());
        Assert.AreEqual(1500, manager.GetTrafficLightPlacementCost());
        Assert.IsTrue(manager.TrySpendForStopPlacement());
        Assert.IsTrue(manager.TrySpendForTrafficLightPlacement());
        Assert.AreEqual(6500, manager.CurrentBalance);

        int afterStopRefund = manager.RefundForStopRemoval();
        int afterLightRefund = manager.RefundForTrafficLightRemoval();

        Assert.AreEqual(7500, afterStopRefund);
        Assert.AreEqual(8250, afterLightRefund);
        Assert.AreEqual(8250, manager.CurrentBalance);
    }

    [Test]
    public void VehicleCostLookupAndTransactions_HandleConfiguredAndMissingTypes()
    {
        EconomyManager manager = CreateEconomyManager(
            startingBalance: 12000,
            refundRate: 1f,
            vehicleCosts: new List<CargoCostEntry>
            {
                new CargoCostEntry { cargoType = CargoType.Wood, cost = 3000 },
                new CargoCostEntry { cargoType = CargoType.Steel, cost = 2500 },
                new CargoCostEntry { cargoType = CargoType.None, cost = 9999 },
                new CargoCostEntry { cargoType = CargoType.Paper, cost = -1 }
            });

        Assert.AreEqual(3000, manager.GetVehiclePurchaseCost(CargoType.Wood));
        Assert.AreEqual(2500, manager.GetVehiclePurchaseCost(CargoType.Steel));
        Assert.AreEqual(-1, manager.GetVehiclePurchaseCost(CargoType.Passengers));
        Assert.IsTrue(manager.TrySpendForVehicle(CargoType.Wood));
        Assert.IsFalse(manager.TrySpendForVehicle(CargoType.Passengers));
        Assert.AreEqual(9000, manager.CurrentBalance);

        int woodRefundBalance = manager.RefundForVehicle(CargoType.Wood);
        int missingRefundBalance = manager.RefundForVehicle(CargoType.Passengers);

        Assert.AreEqual(12000, woodRefundBalance);
        Assert.AreEqual(0, missingRefundBalance);
        Assert.AreEqual(12000, manager.CurrentBalance);
    }

    [Test]
    public void GameLostBlocksTransactions_WhenConfiguredToBlockAfterGameOver()
    {
        EconomyManager manager = CreateEconomyManager(
            startingBalance: 100,
            targetBalance: 500,
            blockAfterGameOver: true,
            roadCost: 250,
            stopCost: 2000,
            refundRate: 1f);

        int lostCalls = 0;
        int wonCalls = 0;
        manager.GameLost += () => lostCalls++;
        manager.GameWon += () => wonCalls++;

        bool firstSpend = manager.TrySpendForRoadPlacement(0);
        bool blockedSpend = manager.TrySpendForStopPlacement();
        int blockedRevenueBalance = manager.AddRevenue(300);
        int blockedRefundBalance = manager.RefundForRoadRemoval(0);

        Assert.IsTrue(firstSpend);
        Assert.AreEqual(-150, manager.CurrentBalance);
        Assert.IsTrue(manager.IsBankrupt);
        Assert.IsFalse(manager.HasWon);
        Assert.IsTrue(manager.IsGameOver);
        Assert.AreEqual(1, lostCalls);
        Assert.AreEqual(0, wonCalls);
        Assert.IsFalse(blockedSpend);
        Assert.AreEqual(-150, blockedRevenueBalance);
        Assert.AreEqual(-150, blockedRefundBalance);
    }

    [Test]
    public void RevenueFlow_TriggersWinAndIgnoresNonPositiveRevenue()
    {
        EconomyManager manager = CreateEconomyManager(
            startingBalance: 100,
            targetBalance: 150,
            blockAfterGameOver: true);

        int wonCalls = 0;
        manager.GameWon += () => wonCalls++;

        int unchangedZero = manager.AddRevenue(0);
        int unchangedNegative = manager.AddRevenue(-5);
        int afterWinningRevenue = manager.AddRevenue(60);
        int blockedAfterWin = manager.AddRevenue(100);

        Assert.AreEqual(100, unchangedZero);
        Assert.AreEqual(100, unchangedNegative);
        Assert.AreEqual(160, afterWinningRevenue);
        Assert.AreEqual(160, blockedAfterWin);
        Assert.IsTrue(manager.HasWon);
        Assert.IsFalse(manager.IsBankrupt);
        Assert.AreEqual(1, wonCalls);
    }

    [Test]
    public void TransactionsContinueAfterGameOver_WhenBlockingDisabled()
    {
        EconomyManager manager = CreateEconomyManager(
            startingBalance: 100,
            targetBalance: 150,
            blockAfterGameOver: false,
            roadCost: 250,
            refundRate: 1f);

        Assert.AreEqual(160, manager.AddRevenue(60));
        Assert.IsTrue(manager.HasWon);
        Assert.IsTrue(manager.IsGameOver);

        Assert.IsTrue(manager.TrySpendForRoadPlacement(0));
        Assert.AreEqual(-90, manager.CurrentBalance);
        Assert.AreEqual(160, manager.RefundForRoadRemoval(0));
        Assert.AreEqual(360, manager.AddRevenue(200));
    }

    private EconomyManager CreateEconomyManager(
        int startingBalance = 100000,
        int targetBalance = 1000000,
        bool blockAfterGameOver = true,
        int roadCost = 250,
        int stopCost = 2000,
        int trafficLightCost = 2000,
        float refundRate = 1f,
        List<CargoCostEntry> vehicleCosts = null)
    {
        if (EconomyManager.HasInstance && EconomyManager.Instance != null && EconomyManager.Instance.gameObject != null)
        {
            Object.DestroyImmediate(EconomyManager.Instance.gameObject);
        }

        EconomyManager manager = Track(new GameObject("EconomyManager")).AddComponent<EconomyManager>();
        SetPrivateField(manager, "startingBalance", startingBalance);
        SetPrivateField(manager, "targetBalanceToWin", targetBalance);
        SetPrivateField(manager, "blockTransactionsAfterGameOver", blockAfterGameOver);
        SetPrivateField(manager, "roadPlacementCost", roadCost);
        SetPrivateField(manager, "stopPlacementCost", stopCost);
        SetPrivateField(manager, "trafficLightPlacementCost", trafficLightCost);
        SetPrivateField(manager, "refundRate", refundRate);
        if (vehicleCosts != null)
        {
            SetPrivateField(manager, "vehiclePurchaseCosts", vehicleCosts);
        }

        InvokePrivateMethodIfExists(manager, "OnValidate");
        InvokePrivateMethodIfExists(manager, "Awake");
        manager.ResetEconomyState();
        return manager;
    }

    private BuildingEconomy CreateInactiveBuilding(string name)
    {
        GameObject go = Track(new GameObject(name));
        go.SetActive(false);
        return go.AddComponent<BuildingEconomy>();
    }

    private GameObject Track(GameObject gameObject)
    {
        createdObjects.Add(gameObject);
        return gameObject;
    }

    private static bool ContainsBuilding(IReadOnlyCollection<BuildingEconomy> collection, BuildingEconomy target)
    {
        foreach (BuildingEconomy building in collection)
        {
            if (building == target)
            {
                return true;
            }
        }

        return false;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivateMethodIfExists(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }
}
