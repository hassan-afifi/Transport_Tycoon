using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameResultPanel : MonoBehaviour
{
    [SerializeField] private EconomyManager economyManager;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private string winMessage = "YOU WON!";
    [SerializeField] private string loseMessage = "YOU LOST!";
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private bool pauseGameOnResult = true;

    private bool shown;

    private void Awake()
    {
        if (economyManager == null)
        {
            economyManager = FindFirstObjectByType<EconomyManager>();
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (economyManager != null)
        {
            economyManager.GameWon += HandleGameWon;
            economyManager.GameLost += HandleGameLost;
        }

        TryShowCurrentState();
    }

    private void OnDisable()
    {
        if (economyManager != null)
        {
            economyManager.GameWon -= HandleGameWon;
            economyManager.GameLost -= HandleGameLost;
        }
    }

    private void HandleGameWon()
    {
        ShowResult(winMessage);
    }

    private void HandleGameLost()
    {
        ShowResult(loseMessage);
    }

    private void TryShowCurrentState()
    {
        if (economyManager == null || shown)
        {
            return;
        }

        if (economyManager.HasWon)
        {
            ShowResult(winMessage);
        }
        else if (economyManager.IsBankrupt)
        {
            ShowResult(loseMessage);
        }
    }

    private void ShowResult(string message)
    {
        if (shown)
        {
            return;
        }

        shown = true;
        if (resultText != null)
        {
            resultText.text = message;
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }

        if (pauseGameOnResult)
        {
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }
    }

    public void RestartGame()
    {
        ResumeRuntimeAndLoad(SceneManager.GetActiveScene().name);
    }

    public void GoToMainMenu()
    {
        ResumeRuntimeAndLoad(mainMenuSceneName);
    }

    public void QuitGame()
    {
        ApplicationQuitUtility.Quit();
    }

    private static void ResumeRuntimeAndLoad(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(sceneName);
    }
}
