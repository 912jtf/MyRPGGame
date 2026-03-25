using UnityEngine;

/// <summary>
/// 简单 BGM 播放器：挂在场景任意物体上即可。
/// - 可选跨场景保留
/// - 自动循环播放
/// - 可在 Inspector 调整音量
/// </summary>
public class BGMPlayer : MonoBehaviour
{
    [Header("BGM 设置")]
    public AudioClip bgmClip;
    [Range(0f, 1f)] public float volume = 0.65f;
    public bool playOnStart = true;
    public bool loop = true;
    [Tooltip("勾选后该 BGM 物体跨场景保留。")]
    public bool dontDestroyOnLoad = true;
    [Tooltip("勾选后场景中仅保留一个 BGMPlayer，避免重复叠播。")]
    public bool singleton = true;

    AudioSource _audioSource;
    static BGMPlayer _instance;

    void Awake()
    {
        if (singleton)
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _audioSource.playOnAwake = false;
        _audioSource.loop = loop;
        _audioSource.volume = volume;
        _audioSource.spatialBlend = 0f; // 2D BGM

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (playOnStart)
            Play();
    }

    void OnValidate()
    {
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            return;
        _audioSource.volume = Mathf.Clamp01(volume);
        _audioSource.loop = loop;
        _audioSource.spatialBlend = 0f;
    }

    public void Play()
    {
        if (_audioSource == null || bgmClip == null)
            return;
        _audioSource.clip = bgmClip;
        if (!_audioSource.isPlaying)
            _audioSource.Play();
    }

    public void Stop()
    {
        if (_audioSource == null)
            return;
        _audioSource.Stop();
    }
}
