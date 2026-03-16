using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
}