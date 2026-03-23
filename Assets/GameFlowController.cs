using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Phase 1 单机流程控制：
/// - 倒计时；
/// - 失败：玩家死亡 / 金矿归零 / 时间耗尽且矿未满；
/// - 胜利：金矿达到上限。
/// </summary>
public class GameFlowController : MonoBehaviour
{
    public enum EndReason
    {
        None,
        PlayerDied,
        MineDepleted,
        TimeOut,
        MineFilled
    }

    [Header("核心引用（可选，留空会自动查找）")]
    public GoldMineController goldMine;
    public PlayerHealth playerHealth;

    [Header("时间设置")]
    [Min(1f)] public float roundDuration = 60f;
    public bool stopTimeScaleOnEnd = false;

    [Header("UI（可选）")]
    public TMP_Text timerText;
    public TMP_Text resultText;

    [Header("事件（可选）")]
    public UnityEvent onGameWin;
    public UnityEvent onGameLose;
    public UnityEvent<string> onGameEnded;

    public bool IsEnded => _isEnded;
    public float RemainingTime => _remainingTime;
    public EndReason Reason => _reason;

    float _remainingTime;
    bool _isEnded;
    EndReason _reason = EndReason.None;

    void Awake()
    {
        TryResolveReferences();
        TryAutoBindUI();
        _remainingTime = Mathf.Max(1f, roundDuration);
        RefreshTimerUI();
        if (resultText != null)
            resultText.text = string.Empty;
    }

    void OnEnable()
    {
        if (playerHealth != null)
            playerHealth.Died += OnPlayerDied;
        if (goldMine != null)
            goldMine.onMineDepleted?.AddListener(OnMineDepleted);
    }

    void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.Died -= OnPlayerDied;
        if (goldMine != null)
            goldMine.onMineDepleted?.RemoveListener(OnMineDepleted);
    }

    void Update()
    {
        if (_isEnded)
            return;

        if (goldMine != null && goldMine.CurrentGold >= goldMine.maxGold)
        {
            EndGame(true, EndReason.MineFilled, "Victory: Mine Filled");
            return;
        }

        _remainingTime -= Time.deltaTime;
        if (_remainingTime < 0f)
            _remainingTime = 0f;
        RefreshTimerUI();

        if (_remainingTime <= 0f)
        {
            bool mineFilled = goldMine != null && goldMine.CurrentGold >= goldMine.maxGold;
            if (mineFilled)
                EndGame(true, EndReason.MineFilled, "Victory: Mine Filled");
            else
                EndGame(false, EndReason.TimeOut, "Defeat: Time Out");
        }
    }

    public void RestartRoundTimer(float seconds)
    {
        _remainingTime = Mathf.Max(1f, seconds);
        _reason = EndReason.None;
        _isEnded = false;
        RefreshTimerUI();
        if (resultText != null)
            resultText.text = string.Empty;
    }

    void OnPlayerDied()
    {
        EndGame(false, EndReason.PlayerDied, "Defeat: Player Died");
    }

    void OnMineDepleted()
    {
        EndGame(false, EndReason.MineDepleted, "Defeat: Mine Depleted");
    }

    void EndGame(bool win, EndReason reason, string message)
    {
        if (_isEnded)
            return;

        _isEnded = true;
        _reason = reason;

        if (resultText != null)
            resultText.text = message;

        if (win) onGameWin?.Invoke();
        else onGameLose?.Invoke();
        onGameEnded?.Invoke(message);

        if (stopTimeScaleOnEnd)
            Time.timeScale = 0f;

        Debug.Log($"[GameFlow] {message}");
    }

    void TryResolveReferences()
    {
        if (goldMine == null)
            goldMine = FindObjectOfType<GoldMineController>();

        if (playerHealth == null)
            playerHealth = FindObjectOfType<PlayerHealth>();
    }

    void TryAutoBindUI()
    {
        if (timerText == null)
        {
            GameObject timerObj = GameObject.Find("TimerText");
            if (timerObj != null)
                timerText = timerObj.GetComponent<TMP_Text>();
        }

        if (resultText == null)
        {
            GameObject resultObj = GameObject.Find("ResultText");
            if (resultObj != null)
                resultText = resultObj.GetComponent<TMP_Text>();
        }
    }

    void RefreshTimerUI()
    {
        if (timerText == null)
            return;
        int sec = Mathf.CeilToInt(_remainingTime);
        timerText.text = $"{sec}s";
    }
}
