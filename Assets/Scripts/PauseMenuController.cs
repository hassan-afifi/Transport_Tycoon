using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PauseMenuController : MonoBehaviour
{
    [SerializeField] private GameObject pauseMenuRoot;
    [SerializeField] private OptionsMenuController optionsMenu;
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private bool hidePauseMenuOnStart = true;
    [SerializeField] private bool ignoreEscapeWhenGameOver = true;

    private float baseFixedDeltaTime;
    private float resumeTimeScale = 1f;
    private bool isPaused;

    private void Awake()
    {
        baseFixedDeltaTime = Time.fixedDeltaTime;

        if (hidePauseMenuOnStart && pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(false);
        }

        if (optionsMenu == null)
        {
            optionsMenu = FindFirstObjectByType<OptionsMenuController>();
        }

        if (optionsMenu == null)
        {
            OptionsMenuController[] allMenus = Resources.FindObjectsOfTypeAll<OptionsMenuController>();
            for (int i = 0; i < allMenus.Length; i++)
            {
                if (allMenus[i] != null && allMenus[i].gameObject.scene.IsValid() && allMenus[i].gameObject.scene.isLoaded)
                {
                    optionsMenu = allMenus[i];
                    break;
                }
            }
        }
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (!Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        if (optionsMenu != null && optionsMenu.IsOpen)
        {
            return;
        }

        if (ignoreEscapeWhenGameOver && EconomyManager.HasInstance && EconomyManager.Instance.IsGameOver)
        {
            return;
        }

        if (isPaused)
        {
            ContinueGame();
        }
        else
        {
            PauseGame();
        }
    }

    private void OnDisable()
    {
        if (isPaused)
        {
            ApplyResume();
            isPaused = false;
        }
    }

    public void PauseGame()
    {
        if (isPaused)
        {
            return;
        }

        resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        isPaused = true;

        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(true);
        }

        Time.timeScale = 0f;
        Time.fixedDeltaTime = baseFixedDeltaTime;
        AudioListener.pause = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void ContinueGame()
    {
        if (!isPaused)
        {
            return;
        }

        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(false);
        }

        ApplyResume();
        isPaused = false;
    }

    public void OpenOptions()
    {
        if (optionsMenu != null)
        {
            optionsMenu.OpenMenu();
        }
    }

    public void GoToMainMenu()
    {
        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(false);
        }

        isPaused = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = baseFixedDeltaTime;
        AudioListener.pause = false;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void QuitGame()
    {
        CoreUtility.Quit();
    }

    private void ApplyResume()
    {
        float restoredScale = Mathf.Max(0.01f, resumeTimeScale);
        Time.timeScale = restoredScale;
        Time.fixedDeltaTime = baseFixedDeltaTime * restoredScale;
        AudioListener.pause = false;
    }
}
