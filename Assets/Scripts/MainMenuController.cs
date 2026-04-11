using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private OptionsMenuController optionsMenu;

    private void Awake()
    {
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

    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    public void OpenOptions()
    {
        if (optionsMenu != null)
        {
            optionsMenu.OpenMenu();
        }
    }

    public void QuitGame()
    {
        CoreUtility.Quit();
    }
}
