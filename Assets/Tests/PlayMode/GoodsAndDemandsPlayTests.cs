using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class GoodsAndDemandsPlayTests
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

        if (GridMap.HasInstance && GridMap.Instance != null && GridMap.Instance.gameObject != null)
        {
            Object.DestroyImmediate(GridMap.Instance.gameObject);
        }
    }

    [Test]
    public void ApplyBuiltInRecipe_ConfiguresKnownTypeAndIgnoresUnknownType()
    {
        BuildingEconomy steelMill = CreateBuilding("SteelMill", BuildingType.SteelMill);
        List<GoodsEntry> steelStock = GetPrivateField<List<GoodsEntry>>(steelMill, "stock");
        steelStock.Add(new GoodsEntry { cargoType = CargoType.Wood, amount = 5 });
        SetPrivateField(steelMill, "clearStockWhenApplyingRecipe", true);

        steelMill.ApplyBuiltInRecipe();

        Assert.AreEqual("Steel Mill", steelMill.BuildingName);
        Assert.AreEqual(3, FindAmount(steelMill.Production, CargoType.Steel));
        Assert.AreEqual(6, FindAmount(steelMill.Consumption, CargoType.Iron));
        Assert.AreEqual(0, steelMill.Demand.Count);
        Assert.AreEqual(0, steelMill.Stock.Count);

        BuildingEconomy unknown = CreateBuilding("CustomBuilding", BuildingType.None);
        SetPrivateField(unknown, "buildingName", "Custom Building");
        SetPrivateField(unknown, "production", new List<GoodsEntry> { new GoodsEntry { cargoType = CargoType.Wood, amount = 2 } });
        unknown.ApplyBuiltInRecipe();

        Assert.AreEqual("Custom Building", unknown.BuildingName);
        Assert.AreEqual(2, FindAmount(unknown.Production, CargoType.Wood));
    }

    [Test]
    public void Simulate_ProcessesConversionSalesAndPassengerGeneration()
    {
        BuildingEconomy workshop = CreateBuilding("Workshop", BuildingType.Workshop);
        workshop.ApplyBuiltInRecipe();
        List<GoodsEntry> workshopStock = GetPrivateField<List<GoodsEntry>>(workshop, "stock");
        workshop.AddToList(workshopStock, CargoType.Wood, 12);

        workshop.Simulate(60f);

        Assert.AreEqual(6, workshop.GetAmount(workshopStock, CargoType.Wood));
        Assert.AreEqual(2, workshop.GetAmount(workshopStock, CargoType.Furniture));

        workshop.Simulate(0f);
        Assert.AreEqual(6, workshop.GetAmount(workshopStock, CargoType.Wood));
        Assert.AreEqual(2, workshop.GetAmount(workshopStock, CargoType.Furniture));

        BuildingEconomy city = CreateBuilding("City", BuildingType.City);
        city.ApplyBuiltInRecipe();
        city.Simulate(60f);

        Assert.AreEqual(12, city.PassengersWaiting);
    }

    [Test]
    public void TakeWaitingPassengers_ReturnsRequestedOrAvailableAndReducesCount()
    {
        BuildingEconomy city = CreateBuilding("City", BuildingType.City);
        SetPrivateField(city, "passengersWaiting", 5);

        int firstTake = city.TakeWaitingPassengers(3);
        int secondTake = city.TakeWaitingPassengers(10);
        int invalidTake = city.TakeWaitingPassengers(0);

        Assert.AreEqual(3, firstTake);
        Assert.AreEqual(2, secondTake);
        Assert.AreEqual(0, invalidTake);
        Assert.AreEqual(0, city.PassengersWaiting);
    }

    [Test]
    public void CanProvideCargo_ReturnsExpectedForPassengersAndStockedProduction()
    {
        BuildingEconomy forest = CreateBuilding("Forest", BuildingType.Forest);
        forest.ApplyBuiltInRecipe();
        List<GoodsEntry> forestStock = GetPrivateField<List<GoodsEntry>>(forest, "stock");
        forest.AddToList(forestStock, CargoType.Wood, 1);

        BuildingEconomy city = CreateBuilding("City", BuildingType.City);
        city.ApplyBuiltInRecipe();
        SetPrivateField(city, "passengersWaiting", 2);

        Assert.IsFalse(forest.CanProvideCargo(CargoType.None));
        Assert.IsTrue(forest.CanProvideCargo(CargoType.Wood));
        Assert.IsFalse(forest.CanProvideCargo(CargoType.Passengers));
        Assert.IsTrue(city.CanProvideCargo(CargoType.Passengers));
    }

    [Test]
    public void CanReceiveCargo_ReturnsExpectedForConsumptionDemandAndPassengers()
    {
        BuildingEconomy workshop = CreateBuilding("Workshop", BuildingType.Workshop);
        workshop.ApplyBuiltInRecipe();

        BuildingEconomy autoService = CreateBuilding("AutoService", BuildingType.AutoService);
        autoService.ApplyBuiltInRecipe();

        BuildingEconomy city = CreateBuilding("City", BuildingType.City);
        city.ApplyBuiltInRecipe();

        Assert.IsFalse(workshop.CanReceiveCargo(CargoType.None));
        Assert.IsTrue(workshop.CanReceiveCargo(CargoType.Wood));
        Assert.IsTrue(autoService.CanReceiveCargo(CargoType.Steel));
        Assert.IsTrue(city.CanReceiveCargo(CargoType.Passengers));
        Assert.IsFalse(city.CanReceiveCargo(CargoType.Wood));
    }

    [Test]
    public void TakeCargo_HandlesPassengersStockAndInvalidRequests()
    {
        BuildingEconomy city = CreateBuilding("City", BuildingType.City);
        city.ApplyBuiltInRecipe();
        SetPrivateField(city, "passengersWaiting", 5);

        int cityTake = city.TakeCargo(CargoType.Passengers, 2);
        Assert.AreEqual(2, cityTake);
        Assert.AreEqual(3, city.PassengersWaiting);

        BuildingEconomy forest = CreateBuilding("Forest", BuildingType.Forest);
        forest.ApplyBuiltInRecipe();
        List<GoodsEntry> forestStock = GetPrivateField<List<GoodsEntry>>(forest, "stock");
        forest.AddToList(forestStock, CargoType.Wood, 4);

        int firstWoodTake = forest.TakeCargo(CargoType.Wood, 3);
        int secondWoodTake = forest.TakeCargo(CargoType.Wood, 10);
        int invalidTake = forest.TakeCargo(CargoType.None, 3);

        Assert.AreEqual(3, firstWoodTake);
        Assert.AreEqual(1, secondWoodTake);
        Assert.AreEqual(0, invalidTake);
        Assert.AreEqual(0, forest.GetAmount(forestStock, CargoType.Wood));
    }

    [Test]
    public void ReceiveCargo_AddsToStockOrPaysMoneyDependingOnType()
    {
        BuildingEconomy workshop = CreateBuilding("Workshop", BuildingType.Workshop);
        workshop.ApplyBuiltInRecipe();
        List<GoodsEntry> workshopStock = GetPrivateField<List<GoodsEntry>>(workshop, "stock");

        int receivedWood = workshop.ReceiveCargo(CargoType.Wood, 5);
        int rejectedSteel = workshop.ReceiveCargo(CargoType.Steel, 4);

        Assert.AreEqual(5, receivedWood);
        Assert.AreEqual(0, rejectedSteel);
        Assert.AreEqual(5, workshop.GetAmount(workshopStock, CargoType.Wood));

        BuildingEconomy city = CreateBuilding("City", BuildingType.City);
        city.ApplyBuiltInRecipe();

        int passengerPayment = city.ReceiveCargo(CargoType.Passengers, 2);
        Assert.AreEqual(2, passengerPayment);
        Assert.AreEqual(640, city.TotalMoneyEarned);
    }

    [Test]
    public void GetAmount_ReturnsStoredAmountOrZeroWhenMissing()
    {
        BuildingEconomy building = CreateBuilding("Lookup", BuildingType.Forest);
        List<GoodsEntry> list = new()
        {
            new GoodsEntry { cargoType = CargoType.Wood, amount = 7 },
            new GoodsEntry { cargoType = CargoType.Iron, amount = 3 }
        };

        Assert.AreEqual(7, building.GetAmount(list, CargoType.Wood));
        Assert.AreEqual(0, building.GetAmount(list, CargoType.Steel));
        Assert.AreEqual(0, building.GetAmount(list, CargoType.None));
    }

    [Test]
    public void AddToList_AddsUpdatesRemovesAndIgnoresInvalidInput()
    {
        BuildingEconomy building = CreateBuilding("ListOps", BuildingType.Forest);
        List<GoodsEntry> list = new();

        building.AddToList(list, CargoType.Wood, 3);
        Assert.AreEqual(3, building.GetAmount(list, CargoType.Wood));

        building.AddToList(list, CargoType.Wood, -2);
        Assert.AreEqual(1, building.GetAmount(list, CargoType.Wood));

        building.AddToList(list, CargoType.Wood, -1);
        Assert.AreEqual(0, list.Count);

        building.AddToList(list, CargoType.None, 10);
        building.AddToList(list, CargoType.Iron, 0);
        Assert.AreEqual(0, list.Count);
    }

    [Test]
    public void GetInfoText_ShowsProducedInputStockCityPassengersAndMoney()
    {
        BuildingEconomy forest = CreateBuilding("Forest", BuildingType.Forest);
        forest.ApplyBuiltInRecipe();
        List<GoodsEntry> forestStock = GetPrivateField<List<GoodsEntry>>(forest, "stock");
        forest.AddToList(forestStock, CargoType.Wood, 7);

        string forestInfo = forest.GetInfoText();
        Assert.That(forestInfo, Does.Contain("Produced:"));
        Assert.That(forestInfo, Does.Contain("Wood: 7"));

        BuildingEconomy workshop = CreateBuilding("Workshop", BuildingType.Workshop);
        workshop.ApplyBuiltInRecipe();
        List<GoodsEntry> workshopStock = GetPrivateField<List<GoodsEntry>>(workshop, "stock");
        workshop.AddToList(workshopStock, CargoType.Wood, 4);

        string workshopInfo = workshop.GetInfoText();
        Assert.That(workshopInfo, Does.Contain("Input stock:"));
        Assert.That(workshopInfo, Does.Contain("Wood: 4"));

        BuildingEconomy city = CreateBuilding("City", BuildingType.City);
        city.ApplyBuiltInRecipe();
        SetPrivateField(city, "passengersWaiting", 5);
        city.ReceiveCargo(CargoType.Passengers, 2);

        string cityInfo = city.GetInfoText();
        Assert.That(cityInfo, Does.Contain("Passengers waiting: 5"));
        Assert.That(cityInfo, Does.Contain("Money earned: $640"));
    }

    private BuildingEconomy CreateBuilding(string name, BuildingType type)
    {
        GameObject go = CreateGameObject(name);
        go.SetActive(false);
        BuildingEconomy building = go.AddComponent<BuildingEconomy>();
        SetPrivateField(building, "buildingType", type);
        SetPrivateField(building, "buildingName", name);
        SetPrivateField(building, "useBuiltInRecipe", false);
        SetPrivateField(building, "production", new List<GoodsEntry>());
        SetPrivateField(building, "consumption", new List<GoodsEntry>());
        SetPrivateField(building, "demand", new List<GoodsEntry>());
        SetPrivateField(building, "stock", new List<GoodsEntry>());
        return building;
    }

    private static int FindAmount(IReadOnlyList<GoodsEntry> list, CargoType cargoType)
    {
        for (int i = 0; i < list.Count; i++)
        {
            GoodsEntry entry = list[i];
            if (entry != null && entry.cargoType == cargoType)
            {
                return entry.amount;
            }
        }

        return 0;
    }

    private GameObject CreateGameObject(string name)
    {
        GameObject go = new GameObject(name);
        createdObjects.Add(go);
        return go;
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        return (T)field.GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }
}
