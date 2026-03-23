using UnityEngine;

/// <summary>
/// 敌人生成器：定时在玩家周围四面八方随机生成敌人。
/// 挂在场景中的一个空物体上（例如命名为 EnemySpawner）。
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("生成设置")]
    [Tooltip("要生成的敌人预制体数组（可拖入多个敌人 Prefab，会随机生成其中一个）")]
    public GameObject[] enemyPrefabs;

    [Tooltip("玩家 Transform（可留空，运行时自动按 Tag=Player 查找）")]
    public Transform player;

    [Tooltip("生成时间间隔（秒）")]
    public float spawnInterval = 3f;

    [Tooltip("生成时与玩家的大致距离")]
    public float spawnDistance = 8f;

    [Header("地图边界（可选）")]
    [Tooltip("是否限制敌人生成在一个矩形边界内（例如你的地图范围）")]
    public bool useBounds = false;

    [Tooltip("地图左下角世界坐标")]
    public Vector2 minPosition;

    [Tooltip("地图右上角世界坐标")]
    public Vector2 maxPosition;

    [Tooltip("同一时间场景中允许存在的敌人最大数量（0 或负数表示不限制）")]
    public int maxEnemies = 0;

    [Header("出生点检测（避免卡墙 / 与其它野怪重叠）")]
    [Tooltip("会阻挡走路的层（如建筑、墙、障碍），生成时避开这些碰撞体")]
    public LayerMask obstacleLayers;
    [Tooltip("野怪所在层（如 Enemy）。若勾选，则生成点不能与已有野怪身体重叠")]
    public LayerMask enemyLayers;
    [Tooltip("检测半径，略大于敌人碰撞体即可")]
    public float spawnCheckRadius = 0.5f;
    [Tooltip("最多尝试几次随机位置，都无效则本帧不生成")]
    public int maxSpawnAttempts = 10;

    private float _timer;
    ContactFilter2D _enemySpawnFilter;
    readonly Collider2D[] _enemyOverlapBuffer = new Collider2D[16];

    private void Start()
    {
        // 未在 Inspector 勾选时，默认检测 Enemy 层，避免生成点与已有野怪重叠
        if (enemyLayers.value == 0)
            enemyLayers = LayerMask.GetMask("Enemy");

        _enemySpawnFilter = new ContactFilter2D
        {
            useLayerMask = true,
            useTriggers = false // 必须忽略索敌用大圆 Trigger，否则会误判整片区域被占用
        };
        _enemySpawnFilter.SetLayerMask(enemyLayers);

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
            {
                player = p.transform;
            }
        }
    }

    private void Update()
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;

        // 联机模式下 PlayerNet 是在点击 Host / Client 后由 NetworkManager 动态生成的，
        // 因此 Start 时场景里还没有 Tag=Player 的物体。
        // 这里在每帧都尝试一次查找，直到找到为止。
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
            {
                player = p.transform;
            }
            if (player == null) return;
        }

        _timer += Time.deltaTime;
        if (_timer < spawnInterval) return;
        _timer = 0f;

        if (maxEnemies > 0)
        {
            int current = CountAliveEnemies();
            if (current >= maxEnemies)
            {
                return;
            }
        }

        SpawnOne();
    }

    /// <summary>
    /// 在玩家周围随机生成一个敌人；若出生点与障碍重叠则重试，只出生在可走动区域。
    /// </summary>
    private void SpawnOne()
    {
        if (player == null || enemyPrefabs == null || enemyPrefabs.Length == 0) return;

        Vector3 playerPos = player.position;
        int attempts = Mathf.Max(1, maxSpawnAttempts);

        // 从敌人预制体数组中随机选择一个
        GameObject selectedPrefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
        if (selectedPrefab == null) return;

        for (int i = 0; i < attempts; i++)
        {
            Vector2 finalPos = PickRandomSpawnPosition(playerPos);

            // 若未勾选障碍层，不检测障碍，但仍可检测与其它野怪重叠
            bool blockedByObstacle = obstacleLayers != 0 &&
                Physics2D.OverlapCircle(finalPos, spawnCheckRadius, obstacleLayers);
            int enemyHits = enemyLayers.value != 0
                ? Physics2D.OverlapCircle(finalPos, spawnCheckRadius, _enemySpawnFilter, _enemyOverlapBuffer)
                : 0;
            bool blockedByEnemy = enemyHits > 0;

            if (!blockedByObstacle && !blockedByEnemy)
            {
                Instantiate(selectedPrefab, finalPos, Quaternion.identity);
                return;
            }
        }
        // 多次尝试仍无空地，本帧不生成
    }

    private Vector2 PickRandomSpawnPosition(Vector3 playerPos)
    {
        int side = Random.Range(0, 4);
        Vector2 dir;
        switch (side)
        {
            case 0: dir = Vector2.up; break;
            case 1: dir = Vector2.down; break;
            case 2: dir = Vector2.left; break;
            default: dir = Vector2.right; break;
        }

        float distance = spawnDistance + Random.Range(-1f, 1f);
        Vector2 basePos = (Vector2)playerPos + dir * distance;

        Vector2 perpendicular = new Vector2(-dir.y, dir.x);
        float scatter = Random.Range(-2f, 2f);
        Vector2 finalPos = basePos + perpendicular * scatter;

        if (useBounds)
        {
            finalPos.x = Mathf.Clamp(finalPos.x, minPosition.x, maxPosition.x);
            finalPos.y = Mathf.Clamp(finalPos.y, minPosition.y, maxPosition.y);
        }

        return finalPos;
    }

    /// <summary>
    /// 计算当前场景中“靠近玩家”的敌人数目。
    /// 说明：
    /// - 若有些敌人被你风筝到很远的地方，但仍然存活，
    ///   就不再把它们算进来，避免刷怪器以为数量已满而停止刷新。
    /// </summary>
    private int CountAliveEnemies()
    {
        EnemyHealth[] enemies = FindObjectsOfType<EnemyHealth>();
        if (player == null)
        {
            return enemies.Length;
        }

        int count = 0;
        Vector3 playerPos = player.position;
        float maxDistance = spawnDistance * 2f; // 只统计离玩家不太远的敌人
        float maxSqr = maxDistance * maxDistance;

        foreach (EnemyHealth e in enemies)
        {
            if (e == null) continue;
            Vector3 pos = e.transform.position;
            if ((pos - playerPos).sqrMagnitude <= maxSqr)
            {
                count++;
            }
        }

        return count;
    }
}

