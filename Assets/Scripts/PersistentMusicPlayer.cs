using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class PersistentMusicPlayer : MonoBehaviour
{
    [SerializeField] private AudioClip musicClip;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool playWhilePaused = true;

    private static PersistentMusicPlayer instance;
    private AudioSource audioSource;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
        audioSource.ignoreListenerPause = playWhilePaused;
        audioSource.volume = volume;
        audioSource.clip = musicClip;
    }

    private void Start()
    {
        if (playOnStart && audioSource != null && audioSource.clip != null && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    private void OnValidate()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null)
        {
            return;
        }

        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
        audioSource.ignoreListenerPause = playWhilePaused;
        audioSource.volume = volume;
        audioSource.clip = musicClip;
    }
}
