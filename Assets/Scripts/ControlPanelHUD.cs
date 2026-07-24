using TMPro;
using UnityEngine;

public class ControlPanelHUD : MonoBehaviour
{
    [SerializeField] private EconomyManager economyManager;
    [SerializeField] private TMP_Text balanceText;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private string balancePrefix = "Balance: $";
    [SerializeField] private string timePrefix = "Time: ";
    [SerializeField] private bool setNormalSpeedOnEnable = true;
    [SerializeField] private float normalSpeed = 1f;
    [SerializeField] private float speed2x = 2f;
    [SerializeField] private float speed4x = 4f;

    private float elapsedSeconds;
    private float baseFixedDeltaTime;

    private void Awake()
    {
        baseFixedDeltaTime = Time.fixedDeltaTime;

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

        if (setNormalSpeedOnEnable)
        {
            SetNormalSpeed();
        }

        elapsedSeconds = 0f;
        RefreshBalanceText();
        RefreshTimeText();
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

    private void Update()
    {
        elapsedSeconds += Time.deltaTime;
        RefreshTimeText();
    }

    private void RefreshBalanceText()
    {
        if (balanceText == null)
        {
            return;
        }

        balanceText.text = balancePrefix + (economyManager != null ? economyManager.CurrentBalance : 0);
    }

    private void RefreshTimeText()
    {
        if (timeText == null)
        {
            return;
        }

        int totalSeconds = Mathf.FloorToInt(elapsedSeconds);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        timeText.text = $"{timePrefix}{minutes:00}:{seconds:00}";
    }

    public void PauseGame()
    {
        SetGameSpeed(0f);
    }

    public void SetNormalSpeed()
    {
        SetGameSpeed(normalSpeed);
    }

    public void Set2xSpeed()
    {
        SetGameSpeed(speed2x);
    }

    public void Set4xSpeed()
    {
        SetGameSpeed(speed4x);
    }

    private void SetGameSpeed(float speed)
    {
        float clampedSpeed = Mathf.Max(0f, speed);
        Time.timeScale = clampedSpeed;
        Time.fixedDeltaTime = clampedSpeed > 0f ? baseFixedDeltaTime * clampedSpeed : baseFixedDeltaTime;
        AudioListener.pause = clampedSpeed <= 0f;
    }
}
