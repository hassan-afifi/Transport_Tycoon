using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class GoodsEntry
{
    [FormerlySerializedAs("goodsType")]
    public CargoType cargoType;

    [FormerlySerializedAs("amount"), Min(0)]
    public int amount = 1;
}

public class BuildingEconomy : MonoBehaviour
{
    [Header("Building Type")]
    [SerializeField] private BuildingType buildingType = BuildingType.None;
    [SerializeField] private bool useBuiltInRecipe = true;
    [SerializeField] private bool applyBuiltInRecipeInEditor = true;
    [SerializeField] private bool clearStockWhenApplyingRecipe = true;

    [Header("Identity")]
    [SerializeField] private string buildingName;

    [Header("Rates (Units/Minute)")]
    [SerializeField] private List<GoodsEntry> production = new();
    [SerializeField] private List<GoodsEntry> consumption = new();
    [SerializeField] private List<GoodsEntry> demand = new();

    [Header("Current stock (Units)")]
    [SerializeField] private List<GoodsEntry> stock = new();

    [Header("Simulation")]
    [SerializeField, Min(0.1f)] private float simulationStepSeconds = 1f;

    [Header("Demand fluctuation (optional)")]
    [SerializeField] private bool dynamicDemand;
    [SerializeField, Min(0f)] private float demandChangeSpeed = 0.25f;
    [SerializeField, Min(0f)] private float demandVariation = 2f;
    [SerializeField, Min(5f)] private float demandUpdateInterval = 30f;

    [Header("City")]
    [SerializeField, Min(0)] private int passengerSpawnPerMinute = 12;

    private float simulationAccumulator;
    private float demandTimer;
    private float passengerSpawnProgress;

    private int totalMoneyEarned;
    private int passengersWaiting;

    private readonly Dictionary<CargoType, float> productionProgress = new();
    private readonly Dictionary<CargoType, float> consumptionProgress = new();
    private readonly Dictionary<CargoType, float> demandProgress = new();

    public string BuildingName => string.IsNullOrWhiteSpace(buildingName) ? gameObject.name : buildingName;
    public BuildingType BuildingType => buildingType;
    public IReadOnlyList<GoodsEntry> Production => production;
    public IReadOnlyList<GoodsEntry> Consumption => consumption;
    public IReadOnlyList<GoodsEntry> Demand => demand;
    public IReadOnlyList<GoodsEntry> Stock => stock;
    public int TotalMoneyEarned => totalMoneyEarned;
    public int PassengersWaiting => passengersWaiting;

    private void OnEnable()
    {
        if (EconomyManager.HasInstance)
        {
            EconomyManager.Instance.Register(this);
        }
    }

    private void OnDisable()
    {
        if (EconomyManager.HasInstance)
        {
            EconomyManager.Instance.Unregister(this);
        }
    }

    private void Start()
    {
        if (useBuiltInRecipe)
        {
            ApplyBuiltInRecipe();
        }
        else if (string.IsNullOrWhiteSpace(buildingName))
        {
            buildingName = gameObject.name;
        }

        NormalizeDataLists();
        ResetRuntimeProgress();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying && useBuiltInRecipe && applyBuiltInRecipeInEditor)
        {
            ApplyBuiltInRecipe();
        }

        NormalizeDataLists();
    }

    [ContextMenu("Apply Built-In Recipe")]
    public void ApplyBuiltInRecipe()
    {
        if (!TryCreateRecipeForType(
                buildingType,
                out string recipeName,
                out List<GoodsEntry> recipeProduction,
                out List<GoodsEntry> recipeConsumption,
                out List<GoodsEntry> recipeDemand))
        {
            return;
        }

        buildingName = recipeName;
        production = recipeProduction;
        consumption = recipeConsumption;
        demand = recipeDemand;

        if (clearStockWhenApplyingRecipe)
        {
            stock.Clear();
        }
    }

    private void Update()
    {
        simulationAccumulator += Time.deltaTime;

        while (simulationAccumulator >= simulationStepSeconds)
        {
            Simulate(simulationStepSeconds);
            simulationAccumulator -= simulationStepSeconds;
        }
    }

    public void Simulate(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }

        float productionFactor = ConsumeForThisStep(dt);
        ProduceForThisStep(dt, productionFactor);
        SellToDemand(dt);
        UpdateCityPassengers(dt);

        if (dynamicDemand)
        {
            UpdateDemand(dt);
        }
    }

    public int TakeWaitingPassengers(int requestedAmount)
    {
        if (requestedAmount <= 0 || passengersWaiting <= 0)
        {
            return 0;
        }

        int taken = Mathf.Min(requestedAmount, passengersWaiting);
        passengersWaiting -= taken;
        return taken;
    }

    private float ConsumeForThisStep(float dt)
    {
        if (!HasValidRateEntries(consumption))
        {
            return 1f;
        }

        List<PendingUnits> plannedConsumption = new();
        float possibleFactor = 1f;

        for (int i = 0; i < consumption.Count; i++)
        {
            GoodsEntry entry = consumption[i];
            int desiredUnits = AccumulateAndGetDesiredUnits(entry, dt, consumptionProgress);
            if (desiredUnits <= 0)
            {
                continue;
            }

            plannedConsumption.Add(new PendingUnits(entry.cargoType, desiredUnits));

            int availableUnits = GetAmount(stock, entry.cargoType);
            float ratio = availableUnits / (float)desiredUnits;
            possibleFactor = Mathf.Min(possibleFactor, ratio);
        }

        if (plannedConsumption.Count == 0)
        {
            return 0f;
        }

        possibleFactor = Mathf.Clamp01(possibleFactor);

        for (int i = 0; i < plannedConsumption.Count; i++)
        {
            PendingUnits pending = plannedConsumption[i];
            int consumeUnits = Mathf.FloorToInt(pending.units * possibleFactor);
            if (consumeUnits > 0)
            {
                AddToList(stock, pending.cargoType, -consumeUnits);
            }

            ReduceProgress(pending.cargoType, pending.units, consumptionProgress);
        }

        return possibleFactor;
    }

    private void ProduceForThisStep(float dt, float productionFactor)
    {
        if (productionFactor <= 0f)
        {
            return;
        }

        for (int i = 0; i < production.Count; i++)
        {
            GoodsEntry entry = production[i];
            int desiredUnits = AccumulateAndGetDesiredUnits(entry, dt, productionProgress);
            if (desiredUnits <= 0)
            {
                continue;
            }

            int producedUnits = productionFactor >= 1f
                ? desiredUnits
                : Mathf.FloorToInt(desiredUnits * productionFactor);

            if (producedUnits > 0)
            {
                AddToList(stock, entry.cargoType, producedUnits);
            }

            ReduceProgress(entry.cargoType, desiredUnits, productionProgress);
        }
    }

    private void SellToDemand(float dt)
    {
        if (!CanEarnMoneyFromSales())
        {
            return;
        }

        for (int i = 0; i < demand.Count; i++)
        {
            GoodsEntry entry = demand[i];
            int desiredSellUnits = AccumulateAndGetDesiredUnits(entry, dt, demandProgress);
            if (desiredSellUnits <= 0)
            {
                continue;
            }

            int inStock = GetAmount(stock, entry.cargoType);
            int soldUnits = Mathf.Min(inStock, desiredSellUnits);
            if (soldUnits <= 0)
            {
                ReduceProgress(entry.cargoType, desiredSellUnits, demandProgress);
                continue;
            }

            AddToList(stock, entry.cargoType, -soldUnits);
            totalMoneyEarned += soldUnits * GetSellPrice(entry.cargoType);
            ReduceProgress(entry.cargoType, desiredSellUnits, demandProgress);
        }
    }

    private void UpdateCityPassengers(float dt)
    {
        if (buildingType != BuildingType.City || passengerSpawnPerMinute <= 0)
        {
            return;
        }

        passengerSpawnProgress += passengerSpawnPerMinute * dt / 60f;
        int spawnedPassengers = Mathf.FloorToInt(passengerSpawnProgress);
        if (spawnedPassengers > 0)
        {
            passengerSpawnProgress -= spawnedPassengers;
            passengersWaiting += spawnedPassengers;
        }
    }

    private void UpdateDemand(float dt)
    {
        demandTimer += dt;
        if (demandTimer < demandUpdateInterval)
        {
            return;
        }

        demandTimer = 0f;

        for (int i = 0; i < demand.Count; i++)
        {
            GoodsEntry entry = demand[i];
            if (entry == null || entry.cargoType == CargoType.None || entry.amount <= 0)
            {
                continue;
            }

            int delta = Mathf.RoundToInt(UnityEngine.Random.Range(-demandVariation, demandVariation) * demandChangeSpeed);
            entry.amount = Mathf.Max(0, entry.amount + delta);
        }

        NormalizeConfigList(demand, removeZeroAmounts: true);
    }

    public int GetAmount(List<GoodsEntry> list, CargoType type)
    {
        if (type == CargoType.None)
        {
            return 0;
        }

        for (int i = 0; i < list.Count; i++)
        {
            GoodsEntry entry = list[i];
            if (entry != null && entry.cargoType == type)
            {
                return entry.amount;
            }
        }

        return 0;
    }

    public void AddToList(List<GoodsEntry> list, CargoType type, int value)
    {
        if (type == CargoType.None || value == 0)
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            GoodsEntry entry = list[i];
            if (entry == null || entry.cargoType != type)
            {
                continue;
            }

            entry.amount = Mathf.Max(0, entry.amount + value);
            if (entry.amount <= 0)
            {
                list.RemoveAt(i);
            }

            return;
        }

        if (value > 0)
        {
            list.Add(new GoodsEntry { cargoType = type, amount = value });
        }
    }

    public string GetInfoText()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(BuildingName);

        AppendProducedStatus(builder);
        AppendConverterInputStock(builder);

        if (buildingType == BuildingType.City)
        {
            builder.AppendLine();
            builder.Append("Passengers waiting: ").Append(passengersWaiting);

            builder.AppendLine();
            builder.AppendLine();
            builder.Append("Money earned: $").Append(totalMoneyEarned);
        }
        else if (CanEarnMoneyFromSales())
        {
            builder.AppendLine();
            builder.Append("Money earned: $").Append(totalMoneyEarned);
        }

        return builder.ToString();
    }

    private bool AppendProducedStatus(StringBuilder builder)
    {
        bool wroteHeader = false;
        HashSet<CargoType> handledTypes = new();

        for (int i = 0; i < production.Count; i++)
        {
            GoodsEntry entry = production[i];
            if (!IsValidRateEntry(entry) || !handledTypes.Add(entry.cargoType))
            {
                continue;
            }

            if (!wroteHeader)
            {
                wroteHeader = true;
                builder.AppendLine();
                builder.Append("Produced").AppendLine(":");
            }

            int inStockAmount = GetAmount(stock, entry.cargoType);
            builder.Append("- ")
                .Append(entry.cargoType)
                .Append(": ")
                .Append(inStockAmount)
                .AppendLine();
        }

        return wroteHeader;
    }

    private bool AppendConverterInputStock(StringBuilder builder)
    {
        if (!IsConverterBuilding())
        {
            return false;
        }

        bool wroteHeader = false;
        HashSet<CargoType> handledTypes = new();

        for (int i = 0; i < consumption.Count; i++)
        {
            GoodsEntry entry = consumption[i];
            if (!IsValidRateEntry(entry) || !handledTypes.Add(entry.cargoType))
            {
                continue;
            }

            if (!wroteHeader)
            {
                wroteHeader = true;
                builder.AppendLine();
                builder.Append("Input stock").AppendLine(":");
            }

            int amountInStock = GetAmount(stock, entry.cargoType);
            builder.Append("- ")
                .Append(entry.cargoType)
                .Append(": ")
                .Append(amountInStock)
                .AppendLine();
        }

        return wroteHeader;
    }

    private bool IsConverterBuilding()
    {
        return HasValidRateEntries(production) && HasValidRateEntries(consumption);
    }

    private bool CanEarnMoneyFromSales()
    {
        return buildingType == BuildingType.City
            || buildingType == BuildingType.BooksShop
            || buildingType == BuildingType.AutoService;
    }

    private int AccumulateAndGetDesiredUnits(GoodsEntry entry, float dt, Dictionary<CargoType, float> progressStore)
    {
        if (!IsValidRateEntry(entry))
        {
            return 0;
        }

        float progress = 0f;
        progressStore.TryGetValue(entry.cargoType, out progress);

        progress += entry.amount * dt / 60f;
        progressStore[entry.cargoType] = progress;
        return Mathf.Max(0, Mathf.FloorToInt(progress));
    }

    private static void ReduceProgress(CargoType cargoType, int processedUnits, Dictionary<CargoType, float> progressStore)
    {
        if (cargoType == CargoType.None || processedUnits <= 0)
        {
            return;
        }

        if (!progressStore.TryGetValue(cargoType, out float progress))
        {
            return;
        }

        progressStore[cargoType] = Mathf.Max(0f, progress - processedUnits);
    }

    private void NormalizeDataLists()
    {
        NormalizeConfigList(production, removeZeroAmounts: true);
        NormalizeConfigList(consumption, removeZeroAmounts: true);
        NormalizeConfigList(demand, removeZeroAmounts: true);
        NormalizeConfigList(stock, removeZeroAmounts: true);
    }

    private static void NormalizeConfigList(List<GoodsEntry> list, bool removeZeroAmounts)
    {
        if (list == null)
        {
            return;
        }

        Dictionary<CargoType, int> merged = new();

        for (int i = 0; i < list.Count; i++)
        {
            GoodsEntry entry = list[i];
            if (entry == null || entry.cargoType == CargoType.None)
            {
                continue;
            }

            int amount = Mathf.Max(0, entry.amount);
            if (removeZeroAmounts && amount <= 0)
            {
                continue;
            }

            if (merged.TryGetValue(entry.cargoType, out int currentAmount))
            {
                merged[entry.cargoType] = currentAmount + amount;
            }
            else
            {
                merged[entry.cargoType] = amount;
            }
        }

        list.Clear();

        foreach (KeyValuePair<CargoType, int> pair in merged)
        {
            list.Add(new GoodsEntry
            {
                cargoType = pair.Key,
                amount = pair.Value
            });
        }
    }

    private static bool HasValidRateEntries(List<GoodsEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsValidRateEntry(entries[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidRateEntry(GoodsEntry entry)
    {
        return entry != null && entry.cargoType != CargoType.None && entry.amount > 0;
    }

    private void ResetRuntimeProgress()
    {
        simulationAccumulator = 0f;
        demandTimer = 0f;
        passengerSpawnProgress = 0f;
        totalMoneyEarned = 0;
        passengersWaiting = 0;

        productionProgress.Clear();
        consumptionProgress.Clear();
        demandProgress.Clear();
    }

    private static bool TryCreateRecipeForType(
        BuildingType type,
        out string recipeName,
        out List<GoodsEntry> recipeProduction,
        out List<GoodsEntry> recipeConsumption,
        out List<GoodsEntry> recipeDemand)
    {
        recipeProduction = new List<GoodsEntry>();
        recipeConsumption = new List<GoodsEntry>();
        recipeDemand = new List<GoodsEntry>();
        recipeName = type.ToString();

        switch (type)
        {
            case BuildingType.City:
                recipeName = "City";
                recipeDemand.Add(Entry(CargoType.Furniture, 4));
                return true;

            case BuildingType.SteelMill:
                recipeName = "Steel Mill";
                recipeConsumption.Add(Entry(CargoType.Iron, 6));
                recipeProduction.Add(Entry(CargoType.Steel, 3));
                return true;

            case BuildingType.Factory:
                recipeName = "Factory";
                recipeConsumption.Add(Entry(CargoType.Wood, 4));
                recipeProduction.Add(Entry(CargoType.Paper, 8));
                return true;

            case BuildingType.AutoService:
                recipeName = "Auto Service";
                recipeDemand.Add(Entry(CargoType.Steel, 4));
                return true;

            case BuildingType.BooksShop:
                recipeName = "Books Shop";
                recipeDemand.Add(Entry(CargoType.Paper, 6));
                return true;

            case BuildingType.Workshop:
                recipeName = "Workshop";
                recipeConsumption.Add(Entry(CargoType.Wood, 6));
                recipeProduction.Add(Entry(CargoType.Furniture, 2));
                return true;

            case BuildingType.Forest:
                recipeName = "Forest";
                recipeProduction.Add(Entry(CargoType.Wood, 16));
                return true;

            case BuildingType.Mine:
                recipeName = "Mine";
                recipeProduction.Add(Entry(CargoType.Iron, 10));
                return true;

            default:
                recipeName = "Building";
                return false;
        }
    }

    private static GoodsEntry Entry(CargoType cargoType, int amount)
    {
        return new GoodsEntry
        {
            cargoType = cargoType,
            amount = amount
        };
    }

    private static int GetSellPrice(CargoType cargoType)
    {
        switch (cargoType)
        {
            case CargoType.Furniture:
                return 90;
            case CargoType.Steel:
                return 55;
            case CargoType.Paper:
                return 35;
            case CargoType.Wood:
                return 15;
            case CargoType.Iron:
                return 18;
            case CargoType.Passengers:
                return 8;
            default:
                return 1;
        }
    }

    private readonly struct PendingUnits
    {
        public readonly CargoType cargoType;
        public readonly int units;

        public PendingUnits(CargoType cargoType, int units)
        {
            this.cargoType = cargoType;
            this.units = units;
        }
    }
}
