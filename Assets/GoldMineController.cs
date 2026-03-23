using System;
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

    [Header("UI（可选）")]
    [Tooltip("金矿 UI 进度条（fillAmount = current/max）")]
    public Image mineFillImage;
    [Tooltip("金矿文本（示例：3/10）")]
    public TMP_Text mineText;

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

    void Awake()
    {
        maxGold = Mathf.Max(1, maxGold);
        _currentGold = Mathf.Clamp(initialGold, 0, maxGold);
        stealHitsRequired = Mathf.Max(1, stealHitsRequired);
        TryAutoBindUI();
        RefreshUIAndNotify();
    }

    /// <summary>
    /// 野怪尝试偷矿：同一 enemyId 累计命中到阈值后，扣 1 金并返回 true。
    /// 不满阈值或矿已空则返回 false。
    /// </summary>
    public bool TryStealByEnemy(string enemyId)
    {
        if (IsDepleted)
            return false;

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
            return false;

        _currentStealHitCount = 0;
        _currentGold = Mathf.Max(0, _currentGold - 1);
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
            RefreshUIAndNotify();

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
            GameObject fillObj = GameObject.Find("GoldMineFill");
            if (fillObj != null)
                mineFillImage = fillObj.GetComponent<Image>();
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
        if (mineFillImage != null && maxGold > 0)
            mineFillImage.fillAmount = (float)_currentGold / maxGold;

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
}
