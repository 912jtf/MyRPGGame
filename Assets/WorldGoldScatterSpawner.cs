using UnityEngine;
using Mirror;

/// <summary>
/// 开局在场景内生成散落金块，数量为 <see cref="GoldMineController.maxGold"/> 减去
/// <see cref="GoldMineController.initialGold"/>（与「矿里还差多少金才能满」一致）。
/// 金块预制体需挂 <see cref="GoldPickup"/>（例如 G_Idle）。
/// </summary>
public class WorldGoldScatterSpawner : MonoBehaviour
{
    [Header("金矿（必须）")]
    [Tooltip("留空则自动查找场景中的 GoldMineController。")]
    public GoldMineController goldMine;

    [Header("金块预制体")]
    [Tooltip("需挂 GoldPickup，例如 G_Idle。")]
    public GameObject goldPickupPrefab;

    [Header("散布区域（世界坐标矩形）")]
    [Tooltip("左下角与右上角，与 EnemySpawner 的 minPosition / maxPosition 填同一组数即可。")]
    public Vector2 worldRectMin = new Vector2(-13.34f, -4.39f);
    [Tooltip("右上角世界坐标。")]
    public Vector2 worldRectMax = new Vector2(13.34f, 4.39f);
    [Tooltip("相对边界向内缩进，避免贴边生成。")]
    public float edgePadding = 0.2f;

    [Header("生成质量（避免刷在墙里）")]
    [Tooltip("若勾选，则避开 obstacleLayers 上的碰撞体。")]
    public bool useObstacleCheck = true;
    public LayerMask obstacleLayers;
    public float spawnCheckRadius = 0.35f;
    [Min(1)] public int maxAttemptsPerPickup = 40;

    [Header("杂项")]
    [Tooltip("开局是否自动生成；若关闭可在外部调用 SpawnWorldGold()。")]
    public bool spawnOnStart = true;
    [Tooltip(">=0 时固定随机种子，便于复现同一布局。")]
    public int randomSeed = -1;
    [Tooltip("生成实例的父物体；留空则自动创建名为 WorldGoldPickups 的空物体。")]
    public Transform spawnParent;

    [Header("调试")]
    public bool debugSpawnLogs = true;

    bool _spawnedOnce;

    void Start()
    {
        if (_spawnedOnce)
            return;
        if (!spawnOnStart)
            return;

        // NetworkServer.active 可能在 Start() 执行时尚未变为 true（尤其是 Host）。
        // 为避免“跳过后永远不刷”的问题，这里最多等待 2 秒。
        StartCoroutine(CoWaitAndSpawn());
    }

    System.Collections.IEnumerator CoWaitAndSpawn()
    {
        // Host 启动时 NetworkServer.active 可能有延迟，这里给更长兜底。
        float timeout = 10f;
        while (timeout > 0f && !NetworkServer.active)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (NetworkServer.active)
        {
            if (_spawnedOnce)
                yield break;
            if (debugSpawnLogs)
                Debug.Log($"[WorldGoldScatterSpawner] server active, start SpawnWorldGold. time={Time.time:F2}");
            SpawnWorldGold();
            _spawnedOnce = true;
        }
        else
        {
            if (debugSpawnLogs)
                Debug.LogWarning($"[WorldGoldScatterSpawner] server not active after timeout, skip spawn. time={Time.time:F2}");
        }
    }

    /// <summary>生成数量 = maxGold - initialGold。</summary>
    public void SpawnWorldGold()
    {
        if (goldMine == null)
            goldMine = FindObjectOfType<GoldMineController>();
        if (goldPickupPrefab == null)
        {
            // 兜底：如果 Inspector 引用丢失，尝试从场景中找一个 GoldPickup 作为运行时模板。
            var anyPickup = FindObjectsOfType<GoldPickup>(true);
            if (anyPickup != null && anyPickup.Length > 0 && anyPickup[0] != null)
                goldPickupPrefab = anyPickup[0].gameObject;
        }

        if (goldMine == null || goldPickupPrefab == null)
        {
            Debug.LogWarning($"[WorldGoldScatterSpawner] 缺少引用，跳过生成。 goldMine={(goldMine != null ? "OK" : "NULL")} goldPickupPrefab={(goldPickupPrefab != null ? "OK" : "NULL")}");
            return;
        }

        if (randomSeed >= 0)
            Random.InitState(randomSeed);

        Transform parent = spawnParent;
        if (parent == null)
        {
            GameObject folder = new GameObject("WorldGoldPickups");
            parent = folder.transform;
        }

        int count = Mathf.Max(0, goldMine.maxGold - goldMine.initialGold);
        if (debugSpawnLogs)
            Debug.Log($"[WorldGoldScatterSpawner] spawn count={count} mine={goldMine.name} current={goldMine.CurrentGold}/{goldMine.maxGold}");

        if (count <= 0)
            return;
        for (int i = 0; i < count; i++)
        {
            if (!TryGetRandomSpawnPoint(out Vector2 pos))
                continue;

            GameObject go = Instantiate(goldPickupPrefab, pos, Quaternion.identity, parent);

            EnsureNetworkedGoldPickup(go);

            GoldPickup gp = go.GetComponent<GoldPickup>();
            if (gp != null)
            {
                gp.autoDestroyAfter = 0f;
                // 保留一个较短拾取延迟，避免开局玩家刚生成就立刻把一堆金块捡满，导致“F 看起来丢不出去”。
                gp.pickupDelay = Mathf.Max(gp.pickupDelay, 0.2f);
                gp.ServerResetSpawnTimeNow();
            }

            if (NetworkServer.active)
                NetworkServer.Spawn(go);
        }
    }

    void EnsureNetworkedGoldPickup(GameObject go)
    {
        if (go == null)
            return;

        // 仅当没有 NetworkIdentity 时才动态添加
        if (go.GetComponent<NetworkIdentity>() == null)
            go.AddComponent<NetworkIdentity>();

        // 确保位置可同步（否则客户端只会看到预制体默认位置）
        if (go.GetComponent<NetworkTransformReliable>() == null)
        {
            var nt = go.AddComponent<NetworkTransformReliable>();
            // 必须显式设置 target，避免 LateUpdate 访问 null
            nt.target = go.transform;
            nt.syncPosition = true;
            nt.syncRotation = true;
            nt.syncScale = false;
            nt.coordinateSpace = Mirror.CoordinateSpace.World;
            nt.interpolatePosition = true;
            nt.interpolateRotation = true;
            nt.updateMethod = Mirror.UpdateMethod.FixedUpdate;
        }
        else
        {
            var nt = go.GetComponent<NetworkTransformReliable>();
            if (nt != null && nt.target == null)
                nt.target = go.transform;
        }
    }

    bool TryGetRandomSpawnPoint(out Vector2 pos)
    {
        for (int a = 0; a < maxAttemptsPerPickup; a++)
        {
            pos = SampleRandomPointInArea();
            if (!useObstacleCheck || obstacleLayers.value == 0)
                return true;
            if (!IsBlockedByObstacle(pos))
                return true;
        }

        pos = SampleRandomPointInArea();
        return true;
    }

    bool IsBlockedByObstacle(Vector2 pos)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, spawnCheckRadius, obstacleLayers);
        if (hits == null || hits.Length == 0)
            return false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D c = hits[i];
            if (c == null || !c.enabled || c.isTrigger)
                continue;

            // 仅允许刷在高台层；低台层仍按障碍处理。
            string n = c.gameObject.name;
            if (!string.IsNullOrEmpty(n))
            {
                string lower = n.ToLowerInvariant();
                if (lower.Contains("collision-high"))
                    continue;
            }

            return true;
        }

        return false;
    }

    Vector2 SampleRandomPointInArea()
    {
        float minX = Mathf.Min(worldRectMin.x, worldRectMax.x) + edgePadding;
        float maxX = Mathf.Max(worldRectMin.x, worldRectMax.x) - edgePadding;
        float minY = Mathf.Min(worldRectMin.y, worldRectMax.y) + edgePadding;
        float maxY = Mathf.Max(worldRectMin.y, worldRectMax.y) - edgePadding;

        if (maxX <= minX) maxX = minX + 0.01f;
        if (maxY <= minY) maxY = minY + 0.01f;
        return new Vector2(Random.Range(minX, maxX), Random.Range(minY, maxY));
    }
}
