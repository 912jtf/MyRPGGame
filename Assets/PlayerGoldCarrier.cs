using System;
using System.Collections;
using TMPro;
using Mirror;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 玩家金块携带与投递：
/// - 触碰 GoldPickup 自动拾取；
/// - 进入矿区（GoldMineController 的 Trigger）自动投递；
/// - 可选按键丢弃 1 金块到脚下。
/// </summary>
public class PlayerGoldCarrier : NetworkBehaviour
{
    static int s_pickupCmdLogCount;
    const int kMaxPickupCmdLogs = 30;

    static int s_dropCmdLogCount;
    const int kMaxDropCmdLogs = 10;

    void LogPickupCmd(string msg)
    {
        if (s_pickupCmdLogCount >= kMaxPickupCmdLogs)
            return;
        s_pickupCmdLogCount++;
        Debug.Log(msg);
    }

    [Header("携带设置")]
    [Min(1)] public int maxCarry = 2;
    [Min(0)] [SyncVar(hook = nameof(OnCarryCountChanged))]
    public int carryCount = 0;

    // 最近一次“拾取成功”的世界坐标（由服务器写入，本地玩家用来播音效/特效）
    [SyncVar] Vector2 _lastPickupWorldPos;

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

    [Header("音效（拖入 AudioClip；留空则静音）")]
    [Tooltip("玩家成功拾取到金块（含吸附拾取）时播放。")]
    public AudioClip pickupGoldSfx;
    [Range(0f, 1f)] public float pickupGoldSfxVolume = 1f;

    [Header("拾取特效（可选）")]
    [Tooltip("拾取瞬间生成的特效 Prefab（例如 Assets/SO/Particle System.prefab）。留空则不生成。")]
    public GameObject pickupVfxPrefab;
    public float pickupVfxOffsetY = 0f;
    public bool pickupVfxFollowPickupDuringMagnet = true;

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
    Mirror.NetworkIdentity _netIdentity;

    void OnCarryCountChanged(int oldValue, int newValue)
    {
        // 仅本地玩家实例更新背包 UI（避免 host/client 之间互相覆盖全局 UI）
        if (!isLocalPlayer)
            return;

        LogPickupCmd($"[OnCarryCountChanged] localCarry {oldValue}->{newValue} max={maxCarry}");

        // 只有“增加”才播放拾取反馈（减少是丢弃/投递）
        if (newValue > oldValue)
            PlayPickupFeedback(_lastPickupWorldPos);

        _bagFillTarget = maxCarry > 0 ? (float)carryCount / maxCarry : 0f;
        _bagFillDisplay = _bagFillTarget;
        RefreshUIAndNotify(); // 让 carryText/bagCapacityText 立刻更新
        UpdateBagFillUI();
        UpdateBagFullFlash();
        UpdateDepositPrompt();
    }

    void PlayPickupFeedback(Vector2 worldPos)
    {
        // 音效：用玩家位置播，避免 3D 衰减导致听不到
        CombatSfxUtil.Play2D(pickupGoldSfx, transform.position, pickupGoldSfxVolume);

        // 特效：在拾取点生成（如果没配 prefab 就跳过）
        if (pickupVfxPrefab == null)
            return;

        Vector3 vfxPos = new Vector3(worldPos.x, worldPos.y, 0f) + new Vector3(0f, pickupVfxOffsetY, 0f);
        GameObject vfxGo = Instantiate(pickupVfxPrefab, vfxPos, Quaternion.identity);

        ParticleSystem[] pss = vfxGo.GetComponentsInChildren<ParticleSystem>(true);
        float maxT = 2f;
        if (pss != null && pss.Length > 0)
        {
            foreach (var ps in pss)
            {
                if (ps == null) continue;
                var main = ps.main;
                float dur = main.duration;
                float startLife = main.startLifetime.mode == ParticleSystemCurveMode.Constant
                    ? main.startLifetime.constant
                    : main.startLifetime.constantMax;
                maxT = Mathf.Max(maxT, dur + startLife);
            }
        }
        Destroy(vfxGo, maxT + 0.25f);
    }

    void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _pickupTriggerCollider = ResolvePickupTriggerCollider();
        _netIdentity = GetComponent<Mirror.NetworkIdentity>();
        maxCarry = Mathf.Max(1, maxCarry);
        if (isServer)
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
        if (!isLocalPlayer)
            return;

        if (Input.GetKeyDown(depositKey))
        {
            int requested = Mathf.Clamp(depositPerPress, 1, carryCount);
            if (_nearbyMine != null && requested > 0)
                CmdTryDepositToMine(requested);
        }

        if (!allowDropByKey || !Input.GetKeyDown(dropKey))
        {
            UpdateBagFillUI();
            UpdateBagFullFlash();
            UpdateDepositPrompt();
            return;
        }

        // 明确防呆：没金块时按 F 不应有任何丢弃行为。
        if (carryCount > 0 && goldPickupPrefab != null)
        {
            if (s_dropCmdLogCount < kMaxDropCmdLogs)
            {
                s_dropCmdLogCount++;
                Debug.Log($"[PlayerGoldCarrier] Local drop pressed: carryCount={carryCount} goldPickupPrefab={(goldPickupPrefab != null ? "OK" : "NULL")} netClientActive={NetworkClient.active}");
            }
            TryDropOneRequestToServer();
        }

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
        // 只让本地玩家发起拾取请求；由服务器进行权威扣除/销毁
        if (!isLocalPlayer)
            return false;

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

        // 玩家触碰到的地面金块统一设为常驻，避免倒计时自动消失（视觉/容错用，服务器也会做销毁）
        pickup.autoDestroyAfter = 0f;

        Mirror.NetworkIdentity pickupNi = pickup.GetComponent<Mirror.NetworkIdentity>();
        if (pickupNi == null)
            return false;

        CmdTryPickup(pickupNi.netId, take);
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
            // 如果动画期间金块变成了“挂敌人携带”，立刻停止本次吸取视觉，避免假吸导致误解。
            if (pickup.IsAttachedToEnemyCarrier)
            {
                pickup.EndPickupAnimationLocal();
                yield break;
            }

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

        if (pickup != null)
            pickup.EndPickupAnimationLocal();
    }

    [Command]
    void CmdTryPickup(uint pickupNetId, int desiredTake)
    {
        if (pickupNetId == 0)
            return;
        if (desiredTake <= 0)
            return;
        if (carryCount >= maxCarry)
            return;

        int before = carryCount;

        if (!NetworkServer.spawned.TryGetValue(pickupNetId, out Mirror.NetworkIdentity ni) || ni == null)
        {
            LogPickupCmd($"[CmdTryPickup FAIL] spawned missing netId={pickupNetId} carry={before}/{maxCarry}");
            return;
        }

        GoldPickup pickup = ni.GetComponent<GoldPickup>();
        if (pickup == null)
        {
            LogPickupCmd($"[CmdTryPickup FAIL] GoldPickup missing for netId={pickupNetId} carry={before}/{maxCarry}");
            return;
        }

        if (pickup.IsCarriedByEnemy)
        {
            LogPickupCmd($"[CmdTryPickup FAIL] pickup carried by enemy netId={pickupNetId} carry={before}/{maxCarry}");
            return;
        }
        if (!pickup.CanBePickedDetailed(out bool timeOk, out bool animOk, out bool carriedOk, out float nowTime, out float spawnTime, out float readyAt))
        {
            LogPickupCmd(
                $"[CmdTryPickup FAIL] cannot pick netId={pickupNetId} carry={before}/{maxCarry} " +
                $"now={nowTime:F2} spawn={spawnTime:F2} delay={pickup.pickupDelay:F2} readyAt={readyAt:F2} " +
                $"timeOk={timeOk} animOk={animOk} carriedOk={carriedOk} isCarried={pickup.IsCarriedByEnemy}"
            );
            return;
        }

        // 在服务器端也锁定拾取，防止同一帧/多帧重复触发造成多次扣减
        pickup.BeginPickupAnimation();

        int room = maxCarry - carryCount;
        int amount = Mathf.Max(1, pickup.amount);
        int take = Mathf.Min(room, amount, desiredTake);
        if (take <= 0)
        {
            LogPickupCmd($"[CmdTryPickup FAIL] computed take<=0 netId={pickupNetId} room={room} amount={pickup.amount} desired={desiredTake} carry={before}/{maxCarry}");
            return;
        }

        // 玩家拾取后该金块永不自动销毁
        pickup.autoDestroyAfter = 0f;

        bool fullConsume = take >= amount;

        // 记录拾取成功位置，供客户端播放音效/特效
        _lastPickupWorldPos = pickup.transform.position;

        // 记录服务器端判定信息：便于确认为什么“丢出来又立刻被捡回”。
        pickup.CanBePickedDetailed(out bool timeOk2, out bool animOk2, out bool carriedOk2, out float nowTime2, out float spawnTime2, out float readyAt2);

        if (fullConsume)
            pickup.Consume();
        else
        {
            pickup.amount = amount - take;
            pickup.EndPickupAnimationForServer(); // 部分拾取后必须解除拾取锁定
        }

        carryCount += take;

        LogPickupCmd($"[CmdTryPickup OK] netId={pickupNetId} take={take} carry {before}->{carryCount} max={maxCarry} pickupDelay={pickup.pickupDelay:F2} spawnTime={spawnTime2:F2} now={nowTime2:F2} readyAt={readyAt2:F2} timeOk={timeOk2} animOk={animOk2} carriedOk={carriedOk2}");
    }

    // 之前用 Rpc 来触发音效/特效，但在某些情况下（尤其 host）不稳定。
    // 现在改为：以 carryCount SyncVar hook 为准，保证只要“真正捡到”就一定播放反馈。

    [Command]
    void CmdTryDepositToMine(int requestedCount)
    {
        if (requestedCount <= 0)
            return;
        if (carryCount <= 0)
            return;

        requestedCount = Mathf.Min(requestedCount, carryCount);
        if (requestedCount <= 0)
            return;

        GoldMineController mine = FindObjectOfType<GoldMineController>();
        if (mine == null)
            return;

        int deposited = mine.DepositFromPlayer(requestedCount);
        if (deposited <= 0)
            return;

        carryCount = Mathf.Max(0, carryCount - deposited);
    }

    void TryDropOneRequestToServer()
    {
        // 兜底：若未在 Inspector 配置，尝试从场景中找一个 GoldPickup 作为运行时模板。
        EnsureDropPrefabBound();
        if (goldPickupPrefab == null)
            return;

        // 需求：按 F 丢弃必须落在“玩家原地”
        Vector2 spawnPos = (Vector2)transform.position;

        Debug.Log($"[TryDropOneRequestToServer] carryCount={carryCount} maxCarry={maxCarry} prefab={(goldPickupPrefab != null ? "OK" : "NULL")} spawnPos=({spawnPos.x:F2},{spawnPos.y:F2}) isLocalPlayer={isLocalPlayer} netActive={NetworkClient.active}");
        CmdTryDropGold(spawnPos);
    }

    [Command]
    void CmdTryDropGold(Vector2 spawnPos)
    {
        int before = carryCount;
        Debug.Log($"[CmdTryDropGold] spawnPos=({spawnPos.x:F2},{spawnPos.y:F2}) carry {before}->{before-1} prefab={(goldPickupPrefab != null ? "OK" : "NULL")} isServer={isServer}");

        if (carryCount <= 0)
            return;

        // 确保掉落模板在 server 端可用
        EnsureDropPrefabBound();
        if (goldPickupPrefab == null)
            return;

        // 服务器生成并网络同步
        GameObject go = Instantiate(goldPickupPrefab, spawnPos, Quaternion.identity);
        GoldPickup pickup = go.GetComponent<GoldPickup>();
        if (pickup != null)
        {
            pickup.amount = 1;
            pickup.autoDestroyAfter = 0f;
            // 掉落后延迟允许拾取，避免玩家立刻捡回（否则会出现“背包 -1 又 +1”）
            pickup.pickupDelay = 2.0f;
            pickup.ServerResetSpawnTimeNow();
            Debug.Log($"[CmdTryDropGold] dropped pickupDelay={pickup.pickupDelay:F2}");
        }

        EnsureNetworkedGoldPickup(go);
        NetworkServer.Spawn(go);

        NetworkIdentity droppedNi = go.GetComponent<NetworkIdentity>();
        if (droppedNi != null)
            Debug.Log($"[CmdTryDropGold] spawned droppedGold netId={droppedNi.netId}");

        carryCount = Mathf.Max(0, carryCount - 1);
    }

    void EnsureNetworkedGoldPickup(GameObject go)
    {
        if (go == null)
            return;

        if (go.GetComponent<Mirror.NetworkIdentity>() == null)
            go.AddComponent<Mirror.NetworkIdentity>();

        if (go.GetComponent<NetworkTransformReliable>() == null)
        {
            var nt = go.AddComponent<NetworkTransformReliable>();
            // 必须显式设置 target，避免 NetworkTransformReliable 在 LateUpdate 中访问 null
            nt.target = go.transform;
            nt.syncPosition = true;
            nt.syncRotation = true;
            nt.syncScale = false;
            nt.coordinateSpace = Mirror.CoordinateSpace.World;
            nt.updateMethod = Mirror.UpdateMethod.FixedUpdate;
        }
        else
        {
            var nt = go.GetComponent<NetworkTransformReliable>();
            if (nt != null && nt.target == null)
                nt.target = go.transform;
            if (nt != null)
                nt.coordinateSpace = Mirror.CoordinateSpace.World;
        }
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
            // 掉落后延迟允许拾取，避免玩家立刻把自己丢的金块吸回背包
            pickup.pickupDelay = 2.0f;
            pickup.ServerResetSpawnTimeNow();
            Debug.Log($"[CmdTryDropGold] pickupDelay={pickup.pickupDelay:F2} after reset spawnTimeNow");
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
