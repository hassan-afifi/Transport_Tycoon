using TMPro;
using UnityEngine;

public class EconomyHUD : MonoBehaviour
{
    [SerializeField] private EconomyManager economyManager;
    [SerializeField] private TMP_Text balanceText;
    [SerializeField] private string balancePrefix = "Balance: $";

    private void Awake()
    {
        if (economyManager == null)
        {
            economyManager = FindFirstObjectByType<EconomyManager>();
        }
    }

    private void OnEnable()
    {
        if (economyManager != null)
        {
            economyManager.BalanceChanged += HandleBalanceChanged;
        }

        RefreshBalanceText();
    }

    private void OnDisable()
    {
        if (economyManager != null)
        {
            economyManager.BalanceChanged -= HandleBalanceChanged;
        }
    }

    private void HandleBalanceChanged(int balance, int delta)
    {
        RefreshBalanceText();
    }

    private void RefreshBalanceText()
    {
        if (balanceText == null)
        {
            return;
        }

        balanceText.text = balancePrefix + (economyManager != null ? economyManager.CurrentBalance : 0);
    }
}
