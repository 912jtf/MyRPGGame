using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
    public KeyCode dropKey = KeyCode.F;
    public GameObject goldPickupPrefab;
    public float dropForwardDistance = 0f;
    [Tooltip("丢弃时在玩家脚下的随机散开半径（0 表示不散开）。")]
    public float dropScatterRadius = 0.1f;
    [Header("投递到金矿（按键）")]
    [Tooltip("进入金矿触发范围后，按该键投递 1 块金子。")]
    public KeyCode depositKey = KeyCode.G;
    [Tooltip("每次按键投递的数量。")]
    [Min(1)] public int depositPerPress = 1;
    [Header("投递提示（矿点上方菱形）")]
    public bool showDepositPrompt = true;
    [Tooltip("金矿下用于提示的子物体名称（你现在用的是 GoldIsometric Diamond）。")]
    public string depositPromptChildName = "GoldIsometric Diamond";
    public Color depositPromptColor = new Color(1f, 0.95f, 0.3f, 1f);
    public float depositPromptPulseSpeed = 6f;

    [Header("拾取动画（吸向玩家）")]
    [Tooltip("整堆拾取时先飞向玩家再入账；部分拾取（背包不够装整堆）时仍瞬间拾取。")]
    public bool animatePickupMagnet = true;
    public float pickupMagnetSpeed = 16f;
    public float pickupMagnetMaxDuration = 0.85f;
    public float pickupArriveDistance = 0.12f;
    public Vector3 pickupMagnetTargetOffset = new Vector3(0f, 0.2f, 0f);
    [Tooltip("靠近玩家时缩小到该比例，增强吸入感；设为 1 关闭缩放。")]
    [Range(0.05f, 1f)] public float pickupMagnetEndScale = 0.3f;

    [Header("UI（可选）")]
    public TMP_Text carryText;
    [Tooltip("背包容量进度条（fillAmount = carryCount / maxCarry）。留空会自动找 BagCapacityBar。")]
    public Image bagCapacityFillImage;
    [Tooltip("背包容量文本（示例：1/2）。留空会自动找 BagCapacityText。")]
    public TMP_Text bagCapacityText;
    [Tooltip("勾选后进度条平滑变化。")]
    public bool animateBagCapacityFill = true;
    [Tooltip("进度条跟随速度，越大越快。")]
    public float bagCapacityFillLerpSpeed = 12f;
    [Header("满容量提示（可选）")]
    [Tooltip("背包满时，每隔 fullFlashInterval 秒闪一下。")]
    public bool flashWhenBagFull = true;
    [Tooltip("只有附近存在可拾取金块时才闪烁提示。")]
    public bool flashOnlyWhenPickupNearby = true;
    public float fullFlashInterval = 3f;
    public float fullFlashDuration = 0.15f;
    public Color fullFlashColor = new Color(1f, 0.4f, 0.1f, 1f);

    [Header("事件（可选）")]
    public UnityEvent<int, int> onCarryChanged; // current, max
    public UnityEvent<int> onDepositToMine;     // deposited amount

    public event Action<int, int> CarryChanged;

    SpriteRenderer _spriteRenderer;
    Collider2D _pickupTriggerCollider;
    float _bagFillDisplay;
    float _bagFillTarget;
    float _nextFullFlashTime;
    Coroutine _fullFlashRoutine;
    Color _bagFillBaseColor = Color.white;
    Color _bagTextBaseColor = Color.white;
    bool _hasCachedBagBaseColors;
    bool _warnedMissingDropPrefab;
    GoldMineController _nearbyMine;
    Transform _depositPromptTransform;
    SpriteRenderer _depositPromptRenderer;
    Color _depositPromptBaseColor = Color.white;
    Vector3 _depositPromptBaseScale = Vector3.one;

    void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _pickupTriggerCollider = ResolvePickupTriggerCollider();
        maxCarry = Mathf.Max(1, maxCarry);
        carryCount = Mathf.Clamp(carryCount, 0, maxCarry);
        TryAutoBindUI();
        _bagFillTarget = maxCarry > 0 ? (float)carryCount / maxCarry : 0f;
        _bagFillDisplay = _bagFillTarget;
        RefreshUIAndNotify();
    }

    void Start()
    {
        // 防止 UI 比玩家晚初始化导致 Awake 时自动绑定失败
        TryAutoBindUI();
        HideAllDepositPromptChildrenInScene();
        CacheBagUIBaseColors();
        RefreshUIAndNotify();
    }

    void Update()
    {
        if (Input.GetKeyDown(depositKey))
            TryDepositOneToNearbyMine();

        if (!allowDropByKey || !Input.GetKeyDown(dropKey))
        {
            UpdateBagFillUI();
            UpdateBagFullFlash();
            UpdateDepositPrompt();
            return;
        }

        // 明确防呆：没金块时按 F 不应有任何丢弃行为。
        if (carryCount > 0)
            TryDropOne();

        UpdateBagFillUI();
        UpdateBagFullFlash();
        UpdateDepositPrompt();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryPickup(other);

        GoldMineController mine = GetMineFromCollider(other);
        if (mine != null)
            _nearbyMine = mine;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        GoldMineController mine = GetMineFromCollider(other);
        if (mine != null)
            _nearbyMine = mine;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        GoldMineController mine = GetMineFromCollider(other);
        if (mine != null && mine == _nearbyMine)
            _nearbyMine = null;
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

        // 玩家触碰到的地面金块统一设为常驻，避免倒计时自动消失。
        pickup.autoDestroyAfter = 0f;

        bool fullConsume = take >= amount;
        if (animatePickupMagnet && fullConsume)
        {
            pickup.BeginPickupAnimation();
            StartCoroutine(CoMagnetPickupThenApply(pickup, take));
            return true;
        }

        carryCount += take;
        if (take >= amount)
            pickup.Consume();
        else
            pickup.amount = amount - take;

        RefreshUIAndNotify();
        return true;
    }

    IEnumerator CoMagnetPickupThenApply(GoldPickup pickup, int take)
    {
        if (pickup == null)
            yield break;

        Vector3 startScale = pickup.transform.localScale;
        Rigidbody2D rb = pickup.GetComponent<Rigidbody2D>();
        float elapsed = 0f;
        bool shrink = pickupMagnetEndScale > 0.001f && pickupMagnetEndScale < 0.999f;

        while (pickup != null && elapsed < pickupMagnetMaxDuration)
        {
            elapsed += Time.deltaTime;
            Vector3 target = transform.position + pickupMagnetTargetOffset;
            Vector3 pos = rb != null ? (Vector3)rb.position : pickup.transform.position;
            float step = pickupMagnetSpeed * Time.deltaTime;
            Vector3 newPos = Vector3.MoveTowards(pos, target, step);

            if (rb != null)
            {
                if (rb.bodyType == RigidbodyType2D.Kinematic)
                    rb.MovePosition(newPos);
                else
                    rb.position = newPos;
            }
            else
                pickup.transform.position = newPos;

            if (shrink)
            {
                float u = Mathf.Clamp01(elapsed / pickupMagnetMaxDuration);
                float s = Mathf.Lerp(1f, pickupMagnetEndScale, u * u);
                pickup.transform.localScale = startScale * s;
            }

            pos = rb != null ? (Vector3)rb.position : pickup.transform.position;
            if (Vector3.Distance(pos, target) <= pickupArriveDistance)
                break;

            yield return null;
        }

        if (pickup == null)
            yield break;

        carryCount += take;
        pickup.Consume();
        RefreshUIAndNotify();
    }

    bool TryDepositOneToNearbyMine()
    {
        if (carryCount <= 0 || _nearbyMine == null)
            return false;

        if (_nearbyMine.CurrentGold >= _nearbyMine.maxGold)
            return false;

        int tryCount = Mathf.Clamp(depositPerPress, 1, carryCount);
        int deposited = _nearbyMine.DepositFromPlayer(tryCount);
        if (deposited <= 0)
            return false;

        carryCount = Mathf.Max(0, carryCount - deposited);
        RefreshUIAndNotify();
        onDepositToMine?.Invoke(deposited);
        return true;
    }

    GoldMineController GetMineFromCollider(Collider2D other)
    {
        if (other == null)
            return null;
        GoldMineController mine = other.GetComponent<GoldMineController>();
        if (mine == null)
            mine = other.GetComponentInParent<GoldMineController>();
        return mine;
    }

    public bool TryDropOne()
    {
        if (carryCount <= 0)
            return false;

        EnsureDropPrefabBound();
        if (goldPickupPrefab == null)
        {
            if (!_warnedMissingDropPrefab)
            {
                _warnedMissingDropPrefab = true;
                Debug.LogWarning("[PlayerGoldCarrier] 无法丢金块：goldPickupPrefab 未配置，且未找到可用 GoldPickup 模板。");
            }
            return false;
        }

        Vector2 spawnPos = (Vector2)transform.position + GetForwardDir() * Mathf.Max(0f, dropForwardDistance);
        if (dropScatterRadius > 0f)
            spawnPos += UnityEngine.Random.insideUnitCircle * dropScatterRadius;
        GameObject go = Instantiate(goldPickupPrefab, spawnPos, Quaternion.identity);
        GoldPickup pickup = go.GetComponent<GoldPickup>();
        if (pickup != null)
        {
            pickup.amount = 1;
            pickup.autoDestroyAfter = 0f;
        }

        carryCount -= 1;
        RefreshUIAndNotify();
        return true;
    }

    void EnsureDropPrefabBound()
    {
        if (goldPickupPrefab != null)
            return;

        // 兜底：若未在 Inspector 配置，尝试从场景中找一个 GoldPickup 作为运行时模板。
        GoldPickup[] all = FindObjectsOfType<GoldPickup>(true);
        for (int i = 0; i < all.Length; i++)
        {
            GoldPickup p = all[i];
            if (p == null) continue;
            if (!p.gameObject.scene.IsValid()) continue;
            goldPickupPrefab = p.gameObject;
            return;
        }
    }

    void TryAutoBindUI()
    {
        if (carryText == null)
        {
            GameObject textObj = GameObject.Find("PlayerGoldText");
            if (textObj != null)
                carryText = textObj.GetComponent<TMP_Text>();
        }

        if (bagCapacityText == null)
        {
            GameObject textObj = GameObject.Find("BagCapacityText");
            if (textObj != null)
                bagCapacityText = textObj.GetComponent<TMP_Text>();
        }

        if (bagCapacityFillImage == null)
        {
            GameObject fillObj = GameObject.Find("BagCapacityBar");
            if (fillObj != null)
                bagCapacityFillImage = fillObj.GetComponent<Image>();
        }

        CacheBagUIBaseColors();
    }

    Vector2 GetForwardDir()
    {
        if (_spriteRenderer != null && _spriteRenderer.flipX)
            return Vector2.left;
        return Vector2.right;
    }

    void RefreshUIAndNotify()
    {
        _bagFillTarget = maxCarry > 0 ? (float)carryCount / maxCarry : 0f;
        if (!animateBagCapacityFill)
            _bagFillDisplay = _bagFillTarget;

        if (carryText != null)
            carryText.text = $"{carryCount}/{maxCarry}";
        if (bagCapacityText != null)
            bagCapacityText.text = $"{carryCount}/{maxCarry}";
        if (bagCapacityFillImage != null && !animateBagCapacityFill)
            bagCapacityFillImage.fillAmount = _bagFillTarget;

        if (carryCount >= maxCarry)
            _nextFullFlashTime = Mathf.Min(_nextFullFlashTime, Time.time);
        else
            ResetBagFlashVisual();

        CarryChanged?.Invoke(carryCount, maxCarry);
        onCarryChanged?.Invoke(carryCount, maxCarry);
    }

    void UpdateBagFillUI()
    {
        if (bagCapacityFillImage == null || !animateBagCapacityFill)
            return;

        if (Mathf.Abs(_bagFillDisplay - _bagFillTarget) < 0.0001f)
        {
            _bagFillDisplay = _bagFillTarget;
            bagCapacityFillImage.fillAmount = _bagFillDisplay;
            return;
        }

        _bagFillDisplay = Mathf.Lerp(_bagFillDisplay, _bagFillTarget, Mathf.Clamp01(Time.deltaTime * bagCapacityFillLerpSpeed));
        bagCapacityFillImage.fillAmount = _bagFillDisplay;
    }

    void UpdateBagFullFlash()
    {
        if (!flashWhenBagFull || maxCarry <= 0)
            return;
        if (carryCount < maxCarry)
            return;
        if (flashOnlyWhenPickupNearby && !HasNearbyPickableGold())
            return;

        if (_nextFullFlashTime <= 0f)
            _nextFullFlashTime = Time.time + Mathf.Max(0.1f, fullFlashInterval);

        if (Time.time < _nextFullFlashTime)
            return;

        _nextFullFlashTime = Time.time + Mathf.Max(0.1f, fullFlashInterval);
        if (_fullFlashRoutine != null)
            StopCoroutine(_fullFlashRoutine);
        _fullFlashRoutine = StartCoroutine(CoFlashBagFullOnce());
    }

    bool HasNearbyPickableGold()
    {
        if (_pickupTriggerCollider == null)
            _pickupTriggerCollider = ResolvePickupTriggerCollider();
        float r = GetPickupCheckRadiusFromCollider(_pickupTriggerCollider);
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, r);
        if (hits == null || hits.Length == 0)
            return false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D c = hits[i];
            if (c == null) continue;
            GoldPickup p = c.GetComponent<GoldPickup>();
            if (p == null) p = c.GetComponentInParent<GoldPickup>();
            if (p == null) continue;
            if (!p.CanBePicked) continue;
            return true;
        }

        return false;
    }

    Collider2D ResolvePickupTriggerCollider()
    {
        Collider2D[] cols = GetComponents<Collider2D>();
        if (cols == null || cols.Length == 0)
            return null;

        Collider2D best = null;
        float bestRadius = 0f;
        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D c = cols[i];
            if (c == null || !c.enabled || !c.isTrigger)
                continue;
            float r = GetPickupCheckRadiusFromCollider(c);
            if (best == null || r > bestRadius)
            {
                best = c;
                bestRadius = r;
            }
        }
        return best;
    }

    float GetPickupCheckRadiusFromCollider(Collider2D c)
    {
        if (c == null)
            return 0.6f;

        // 复用玩家触发拾取的真实范围，避免双参数。
        Bounds b = c.bounds;
        float ex = b.extents.x;
        float ey = b.extents.y;
        float r = Mathf.Sqrt(ex * ex + ey * ey);
        return Mathf.Max(0.05f, r);
    }

    IEnumerator CoFlashBagFullOnce()
    {
        CacheBagUIBaseColors();
        float dur = Mathf.Max(0.05f, fullFlashDuration);

        if (bagCapacityFillImage != null)
            bagCapacityFillImage.color = fullFlashColor;
        if (bagCapacityText != null)
            bagCapacityText.color = fullFlashColor;

        yield return new WaitForSeconds(dur);

        if (bagCapacityFillImage != null)
            bagCapacityFillImage.color = _bagFillBaseColor;
        if (bagCapacityText != null)
            bagCapacityText.color = _bagTextBaseColor;

        _fullFlashRoutine = null;
    }

    void CacheBagUIBaseColors()
    {
        if (_hasCachedBagBaseColors)
            return;
        if (bagCapacityFillImage != null)
            _bagFillBaseColor = bagCapacityFillImage.color;
        if (bagCapacityText != null)
            _bagTextBaseColor = bagCapacityText.color;
        if (bagCapacityFillImage != null || bagCapacityText != null)
            _hasCachedBagBaseColors = true;
    }

    void ResetBagFlashVisual()
    {
        _nextFullFlashTime = 0f;
        if (_fullFlashRoutine != null)
        {
            StopCoroutine(_fullFlashRoutine);
            _fullFlashRoutine = null;
        }
        if (bagCapacityFillImage != null)
            bagCapacityFillImage.color = _bagFillBaseColor;
        if (bagCapacityText != null)
            bagCapacityText.color = _bagTextBaseColor;
    }

    void UpdateDepositPrompt()
    {
        bool canPrompt = showDepositPrompt
            && _nearbyMine != null
            && carryCount > 0
            && _nearbyMine.CurrentGold < _nearbyMine.maxGold;

        if (!canPrompt)
        {
            HideDepositPrompt();
            return;
        }

        EnsureDepositPromptFromMine();
        if (_depositPromptTransform == null || _depositPromptRenderer == null)
            return;

        _depositPromptTransform.gameObject.SetActive(true);

        float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Max(0.1f, depositPromptPulseSpeed)));
        _depositPromptTransform.localScale = _depositPromptBaseScale * (0.9f + 0.2f * pulse);
        Color c = _depositPromptBaseColor;
        c.a *= pulse;
        _depositPromptRenderer.color = c;
    }

    void EnsureDepositPromptFromMine()
    {
        if (_nearbyMine == null)
            return;
        if (_depositPromptTransform != null && _depositPromptRenderer != null && _depositPromptTransform.gameObject.activeInHierarchy)
            return;

        Transform t = _nearbyMine.transform.Find(depositPromptChildName);
        if (t == null)
            return;

        SpriteRenderer sr = t.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = t.GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null)
            return;

        _depositPromptTransform = t;
        _depositPromptRenderer = sr;
        _depositPromptBaseColor = sr.color;
        _depositPromptBaseScale = t.localScale;
    }

    void OnDisable()
    {
        HideDepositPrompt();
    }

    void OnDestroy()
    {
        HideDepositPrompt();
    }

    void HideDepositPrompt()
    {
        if (_depositPromptTransform == null)
            return;
        _depositPromptTransform.gameObject.SetActive(false);
        _depositPromptTransform.localScale = _depositPromptBaseScale;
        if (_depositPromptRenderer != null)
            _depositPromptRenderer.color = _depositPromptBaseColor;
    }

    void HideAllDepositPromptChildrenInScene()
    {
        GoldMineController[] mines = FindObjectsOfType<GoldMineController>(true);
        if (mines == null || mines.Length == 0)
            return;

        for (int i = 0; i < mines.Length; i++)
        {
            GoldMineController mine = mines[i];
            if (mine == null) continue;
            Transform t = mine.transform.Find(depositPromptChildName);
            if (t == null) continue;
            t.gameObject.SetActive(false);
        }
    }
}
