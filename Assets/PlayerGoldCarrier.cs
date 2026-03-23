using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 玩家金块携带与投递：
/// - 触碰 GoldPickup 自动拾取；
/// - 进入矿区（GoldMineController 的 Trigger）自动投递；
/// - 可选按键丢弃 1 金块到脚下。
/// </summary>
public class PlayerGoldCarrier : MonoBehaviour
{
    [Header("携带设置")]
    [Min(1)] public int maxCarry = 2;
    [Min(0)] public int carryCount = 0;

    [Header("丢弃设置（可选）")]
    public bool allowDropByKey = true;
    public KeyCode dropKey = KeyCode.G;
    public GameObject goldPickupPrefab;
    public float dropForwardDistance = 0.45f;

    [Header("UI（可选）")]
    public TMP_Text carryText;

    [Header("事件（可选）")]
    public UnityEvent<int, int> onCarryChanged; // current, max
    public UnityEvent<int> onDepositToMine;     // deposited amount

    public event Action<int, int> CarryChanged;

    SpriteRenderer _spriteRenderer;

    void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        maxCarry = Mathf.Max(1, maxCarry);
        carryCount = Mathf.Clamp(carryCount, 0, maxCarry);
        TryAutoBindUI();
        RefreshUIAndNotify();
    }

    void Update()
    {
        if (!allowDropByKey || !Input.GetKeyDown(dropKey))
            return;
        TryDropOne();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryPickup(other);
        TryDeposit(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        // 留在矿区时持续尝试自动投递，避免只在 Enter 一帧错过。
        TryDeposit(other);
    }

    bool TryPickup(Collider2D other)
    {
        if (carryCount >= maxCarry || other == null)
            return false;

        GoldPickup pickup = other.GetComponent<GoldPickup>();
        if (pickup == null)
            pickup = other.GetComponentInParent<GoldPickup>();
        if (pickup == null || !pickup.CanBePicked)
            return false;

        int room = maxCarry - carryCount;
        int amount = Mathf.Max(1, pickup.amount);
        int take = Mathf.Min(room, amount);
        if (take <= 0)
            return false;

        carryCount += take;
        if (take >= amount)
        {
            pickup.Consume();
        }
        else
        {
            pickup.amount = amount - take;
        }

        RefreshUIAndNotify();
        return true;
    }

    bool TryDeposit(Collider2D other)
    {
        if (carryCount <= 0 || other == null)
            return false;

        GoldMineController mine = other.GetComponent<GoldMineController>();
        if (mine == null)
            mine = other.GetComponentInParent<GoldMineController>();
        if (mine == null)
            return false;

        int deposited = mine.DepositFromPlayer(carryCount);
        if (deposited <= 0)
            return false;

        carryCount = Mathf.Max(0, carryCount - deposited);
        RefreshUIAndNotify();
        onDepositToMine?.Invoke(deposited);
        return true;
    }

    public bool TryDropOne()
    {
        if (carryCount <= 0 || goldPickupPrefab == null)
            return false;

        Vector2 spawnPos = (Vector2)transform.position + GetForwardDir() * dropForwardDistance;
        GameObject go = Instantiate(goldPickupPrefab, spawnPos, Quaternion.identity);
        GoldPickup pickup = go.GetComponent<GoldPickup>();
        if (pickup != null)
            pickup.amount = 1;

        carryCount -= 1;
        RefreshUIAndNotify();
        return true;
    }

    void TryAutoBindUI()
    {
        if (carryText == null)
        {
            GameObject textObj = GameObject.Find("PlayerGoldText");
            if (textObj != null)
                carryText = textObj.GetComponent<TMP_Text>();
        }
    }

    Vector2 GetForwardDir()
    {
        if (_spriteRenderer != null && _spriteRenderer.flipX)
            return Vector2.left;
        return Vector2.right;
    }

    void RefreshUIAndNotify()
    {
        if (carryText != null)
            carryText.text = $"{carryCount}/{maxCarry}";
        CarryChanged?.Invoke(carryCount, maxCarry);
        onCarryChanged?.Invoke(carryCount, maxCarry);
    }
}
