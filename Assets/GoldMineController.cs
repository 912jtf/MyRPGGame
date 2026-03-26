using System;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 金矿核心控制：
/// 1) 管理当前金量与容量上限；
/// 2) 处理野怪按次数偷矿；
/// 3) 提供 UI 更新接口（可自动找 UI，也可外部订阅事件）。
/// </summary>
public class GoldMineController : MonoBehaviour
{
    [Header("金矿容量")]
    [Min(0)] public int initialGold = 3;
    [Min(1)] public int maxGold = 10;

    [Header("被偷规则")]
    [Tooltip("同一只野怪连续攻击多少次，才会成功偷走 1 金。")]
    [Min(1)] public int stealHitsRequired = 5;

    [Header("调试（可选）")]
    [Tooltip("输出金矿初始化与偷矿明细日志。")]
    public bool debugMineLog = true;

    [Header("UI（可选）")]
    [Tooltip("金矿 UI 进度条（fillAmount = current/max）；留空则运行时查找 GoldHealthBar，再退回 GoldMineFill。")]
    public Image mineFillImage;
    [Tooltip("金矿文本（示例：3/10）；留空则运行时查找 GoldHealthText，再退回 GoldMineText。")]
    public TMP_Text mineText;
    [Tooltip("勾选后进度条用插值跟随目标比例，偷矿时会有缩短动画。")]
    public bool animateGoldBarFill = true;
    [Tooltip("越大越快贴近目标 fill（仅 animateGoldBarFill 时生效）。")]
    public float goldBarFillLerpSpeed = 12f;

    [Header("音效（拖入 AudioClip；留空则静音）")]
    [Tooltip("野怪成功偷走 1 金时播放。")]
    public AudioClip mineHitByEnemySfx;
    [Range(0f, 1f)] public float mineHitByEnemySfxVolume = 1f;
    [Tooltip("玩家成功投递金块到金矿时播放。")]
    public AudioClip mineDepositSfx;
    [Range(0f, 1f)] public float mineDepositSfxVolume = 1f;

    [Header("事件回调（可选）")]
    public UnityEvent<int, int> onGoldChanged; // 参数：current, max
    public UnityEvent onMineDepleted;

    public int CurrentGold => _currentGold;
    public bool IsDepleted => _currentGold <= 0;

    /// <summary>
    /// 供代码直接订阅的事件接口（参数：current, max）。
    /// </summary>
    public event Action<int, int> GoldChanged;

    int _currentGold;
    string _currentThiefEnemyId;
    int _currentStealHitCount;
    bool _depletedEventSent;
    float _fillDisplay;
    float _fillTarget;

    void Awake()
    {
        maxGold = Mathf.Max(1, maxGold);
        _currentGold = Mathf.Clamp(initialGold, 0, maxGold);
        stealHitsRequired = Mathf.Max(1, stealHitsRequired);
        if (debugMineLog)
        {
            Debug.Log($"[GoldMine:{name}#{GetInstanceID()}] Awake initial={initialGold}, current={_currentGold}, max={maxGold}, stealHitsRequired={stealHitsRequired}");
        }
        TryAutoBindUI();
        _fillTarget = maxGold > 0 ? (float)_currentGold / maxGold : 0f;
        _fillDisplay = _fillTarget;
        RefreshUIAndNotify();
    }

    void Start()
    {
        // Canvas 可能比金矿晚 Awake，这里再绑一次 GoldHealthBar / GoldHealthText。
        TryAutoBindUI();
        _fillTarget = maxGold > 0 ? (float)_currentGold / maxGold : 0f;
        if (!animateGoldBarFill)
            _fillDisplay = _fillTarget;
        RefreshUIAndNotify();
    }

    void Update()
    {
        if (mineFillImage == null)
            return;
        if (!animateGoldBarFill)
            return;
        if (Mathf.Approximately(_fillDisplay, _fillTarget))
        {
            _fillDisplay = _fillTarget;
            mineFillImage.fillAmount = _fillDisplay;
            return;
        }

        _fillDisplay = Mathf.Lerp(_fillDisplay, _fillTarget, Mathf.Clamp01(Time.deltaTime * goldBarFillLerpSpeed));
        mineFillImage.fillAmount = _fillDisplay;
    }

    /// <summary>
    /// 野怪尝试偷矿：同一 enemyId 累计命中到阈值后，扣 1 金并返回 true。
    /// 不满阈值或矿已空则返回 false。
    /// </summary>
    public bool TryStealByEnemy(string enemyId)
    {
        if (IsDepleted)
        {
            if (debugMineLog)
                Debug.Log($"[GoldMine:{name}#{GetInstanceID()}] steal ignored (depleted), enemyId={enemyId}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(enemyId))
            enemyId = "UnknownEnemy";

        if (_currentThiefEnemyId == enemyId)
            _currentStealHitCount++;
        else
        {
            _currentThiefEnemyId = enemyId;
            _currentStealHitCount = 1;
        }

        if (_currentStealHitCount < stealHitsRequired)
        {
            if (debugMineLog)
                Debug.Log($"[GoldMine:{name}#{GetInstanceID()}] steal hit enemyId={enemyId}, combo={_currentStealHitCount}/{stealHitsRequired}, current={_currentGold}");
            return false;
        }

        _currentStealHitCount = 0;
        _currentGold = Mathf.Max(0, _currentGold - 1);
        // 偷矿为共同目标：与投递一致，由 PlayerHealth ClientRpc 让所有客户端（含 Host）播放
        if (NetworkServer.active)
            PlayerHealth.ServerBroadcastMineStealSfx(transform.position);
        else
            CombatSfxUtil.Play2D(mineHitByEnemySfx, transform.position, mineHitByEnemySfxVolume);
        if (debugMineLog)
            Debug.Log($"[GoldMine:{name}#{GetInstanceID()}] STEAL SUCCESS enemyId={enemyId}, current={_currentGold}/{maxGold}");
        RefreshUIAndNotify();
        return true;
    }

    /// <summary>
    /// 玩家投递金块。返回本次实际增加值（会自动夹到 maxGold）。
    /// </summary>
    public int DepositFromPlayer(int count)
    {
        if (count <= 0 || _currentGold >= maxGold)
            return 0;

        int before = _currentGold;
        _currentGold = Mathf.Clamp(_currentGold + count, 0, maxGold);
        int added = _currentGold - before;

        if (added > 0)
        {
            // 投递音效由 PlayerGoldCarrier 的 ClientRpc 在所有客户端播放（共同目标，Host/Client 都听见）。
            RefreshUIAndNotify();
        }

        return added;
    }

    /// <summary>
    /// 获取指定野怪当前累计偷矿命中次数（仅当前连击中的野怪有效）。
    /// </summary>
    public int GetCurrentStealHitCount(string enemyId)
    {
        if (string.IsNullOrWhiteSpace(enemyId))
            return 0;
        return _currentThiefEnemyId == enemyId ? _currentStealHitCount : 0;
    }

    /// <summary>
    /// 手动刷新 UI 和事件，供外部状态同步后调用。
    /// </summary>
    public void ForceRefreshUI()
    {
        RefreshUIAndNotify();
    }

    void TryAutoBindUI()
    {
        if (mineFillImage == null)
        {
            GameObject fillObj = GameObject.Find("GoldHealthBar");
            if (fillObj != null)
                mineFillImage = fillObj.GetComponent<Image>();
        }

        if (mineFillImage == null)
        {
            GameObject fillObj = GameObject.Find("GoldMineFill");
            if (fillObj != null)
                mineFillImage = fillObj.GetComponent<Image>();
        }

        if (mineText == null)
        {
            GameObject textObj = GameObject.Find("GoldHealthText");
            if (textObj != null)
                mineText = textObj.GetComponent<TMP_Text>();
        }

        if (mineText == null)
        {
            GameObject textObj = GameObject.Find("GoldMineText");
            if (textObj != null)
                mineText = textObj.GetComponent<TMP_Text>();
        }
    }

    void RefreshUIAndNotify()
    {
        _fillTarget = maxGold > 0 ? (float)_currentGold / maxGold : 0f;
        if (!animateGoldBarFill)
        {
            _fillDisplay = _fillTarget;
            if (mineFillImage != null)
                mineFillImage.fillAmount = _fillTarget;
        }
        else if (mineFillImage != null && Mathf.Abs(_fillDisplay - _fillTarget) < 0.0001f)
        {
            // 已与目标一致：立即刷到 Image（首帧绑定 UI、或数值未变时）
            _fillDisplay = _fillTarget;
            mineFillImage.fillAmount = _fillTarget;
        }
        // animate 且正在过渡时：fill 由 Update 每帧插值

        if (mineText != null)
            mineText.text = $"{_currentGold}/{maxGold}";

        GoldChanged?.Invoke(_currentGold, maxGold);
        onGoldChanged?.Invoke(_currentGold, maxGold);

        if (!_depletedEventSent && IsDepleted)
        {
            _depletedEventSent = true;
            onMineDepleted?.Invoke();
        }
    }

    /// <summary>
    /// 网络同步用：服务端把最新金量广播到客户端，用于让客户端 UI 也跟随变化。
    /// </summary>
    public void SetGoldNetwork(int current, int max)
    {
        maxGold = Mathf.Max(1, max);
        _currentGold = Mathf.Clamp(current, 0, maxGold);

        _depletedEventSent = false;
        _fillTarget = maxGold > 0 ? (float)_currentGold / maxGold : 0f;
        _fillDisplay = _fillTarget;

        RefreshUIAndNotify();
    }
}
