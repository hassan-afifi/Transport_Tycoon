using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class GoodsEntry
{
    public GoodsType goodsType;
    public float amount;
}

public class BuildingEconomy : MonoBehaviour
{
    [Header("Identity")]
    public string buildingName;

    [Header("Production per second")]
    public List<GoodsEntry> production = new List<GoodsEntry>();

    [Header("Consumption per second")]
    public List<GoodsEntry> consumption = new List<GoodsEntry>();

    [Header("Demand target")]
    public List<GoodsEntry> demand = new List<GoodsEntry>();

    [Header("Current stock")]
    public List<GoodsEntry> stock = new List<GoodsEntry>();

    [Header("Demand change settings")]
    public bool dynamicDemand = false;
    public float demandChangeSpeed = 0.2f;
    public float demandVariation = 2f;

    private float demandTimer = 0f;

    private void Start()
    {
        if (string.IsNullOrWhiteSpace(buildingName))
        {
            buildingName = gameObject.name;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        Produce(dt);
        Consume(dt);

        if (dynamicDemand)
        {
            UpdateDemand(dt);
        }
    }

    private void Produce(float dt)
    {
        foreach (var entry in production)
        {
            AddToList(stock, entry.goodsType, entry.amount * dt);
        }
    }

    private void Consume(float dt)
    {
        foreach (var entry in consumption)
        {
            float current = GetAmount(stock, entry.goodsType);
            float needed = entry.amount * dt;

            if (current >= needed)
            {
                AddToList(stock, entry.goodsType, -needed);
            }
        }
    }

    private void UpdateDemand(float dt)
    {
        demandTimer += dt;

        if (demandTimer >= 3f)
        {
            demandTimer = 0f;

            for (int i = 0; i < demand.Count; i++)
            {
                float delta = UnityEngine.Random.Range(-demandVariation, demandVariation) * demandChangeSpeed;
                demand[i].amount = Mathf.Max(0f, demand[i].amount + delta);
            }
        }
    }

    public float GetAmount(List<GoodsEntry> list, GoodsType type)
    {
        foreach (var entry in list)
        {
            if (entry.goodsType == type)
            {
                return entry.amount;
            }
        }
        return 0f;
    }

    public void AddToList(List<GoodsEntry> list, GoodsType type, float value)
    {
        foreach (var entry in list)
        {
            if (entry.goodsType == type)
            {
                entry.amount += value;
                return;
            }
        }

        list.Add(new GoodsEntry { goodsType = type, amount = value });
    }

    public string GetInfoText()
    {
        string result = $"{buildingName}\n\n";

        result += "Production:\n";
        if (production.Count == 0) result += "- None\n";
        foreach (var item in production)
            result += $"- {item.goodsType}: {item.amount:F1}/s\n";

        result += "\nConsumption:\n";
        if (consumption.Count == 0) result += "- None\n";
        foreach (var item in consumption)
            result += $"- {item.goodsType}: {item.amount:F1}/s\n";

        result += "\nDemand:\n";
        if (demand.Count == 0) result += "- None\n";
        foreach (var item in demand)
            result += $"- {item.goodsType}: {item.amount:F1}\n";

        result += "\nStock:\n";
        if (stock.Count == 0) result += "- None\n";
        foreach (var item in stock)
            result += $"- {item.goodsType}: {item.amount:F1}\n";

        return result;
    }
}