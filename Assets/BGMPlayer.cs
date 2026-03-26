using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>
/// 简单 BGM 播放器：挂在场景任意物体上即可。
/// - 可选跨场景保留
/// - 自动循环播放
/// - 可在 Inspector 调整音量
/// - 联机时：BGM 只在「本机有游戏窗口的进程」播放（Host/纯客户端/单机），纯服务器进程不播
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

    [Header("联机（Mirror）")]
    [Tooltip("纯服务器（NetworkServer 开启且本机无 NetworkClient）不播放 BGM。每个客户端/Host 进程各自只听自己的 BGM，不会通过网络发给对方。")]
    public bool muteOnDedicatedServer = true;

    [Tooltip("将本物体挂到 MainCamera 下（仅当未勾选「跨场景保留」时使用；否则换场景可能随相机被销毁）。")]
    public bool attachToMainCamera = false;

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

    IEnumerator Start()
    {
        // 等一帧：Camera.main / NetworkManager 就绪
        yield return null;

        if (attachToMainCamera && !dontDestroyOnLoad && Camera.main != null)
            transform.SetParent(Camera.main.transform, false);

        if (muteOnDedicatedServer && IsDedicatedServerProcess())
            yield break;

        if (playOnStart)
            Play();
    }

    void Update()
    {
        if (!muteOnDedicatedServer || _audioSource == null || !_audioSource.isPlaying)
            return;
        if (!IsDedicatedServerProcess())
            return;
        Stop();
    }

    /// <summary>本进程只有服务端、没有本地客户端（独立 Server 可执行文件等）。</summary>
    static bool IsDedicatedServerProcess()
    {
        return NetworkServer.active && !NetworkClient.active;
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
