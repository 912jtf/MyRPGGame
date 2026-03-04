using UnityEngine;

/// <summary>
/// 敌人生成器：定时在玩家周围四面八方随机生成敌人。
/// 挂在场景中的一个空物体上（例如命名为 EnemySpawner）。
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("生成设置")]
    [Tooltip("要生成的敌人预制体（拖入 enemy Prefab）")]
    public GameObject enemyPrefab;

    [Tooltip("玩家 Transform（可留空，运行时自动按 Tag=Player 查找）")]
    public Transform player;

    [Tooltip("生成时间间隔（秒）")]
    public float spawnInterval = 3f;

    [Tooltip("生成时与玩家的大致距离")]
    public float spawnDistance = 8f;

    [Tooltip("同一时间场景中允许存在的敌人最大数量（0 或负数表示不限制）")]
    public int maxEnemies = 0;

    private float _timer;

    private void Start()
    {
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
        if (enemyPrefab == null) return;
        if (player == null) return;

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
    /// 在玩家周围四个方向之一随机生成一个敌人。
    /// </summary>
    private void SpawnOne()
    {
        if (player == null) return;

        Vector3 playerPos = player.position;

        // 随机选择一个方向：上、下、左、右
        int side = Random.Range(0, 4);
        Vector2 dir;
        switch (side)
        {
            case 0: dir = Vector2.up; break;
            case 1: dir = Vector2.down; break;
            case 2: dir = Vector2.left; break;
            default: dir = Vector2.right; break;
        }

        // 在该方向上一个区间内随机距离，并增加少量左右/上下偏移，使生成位置更自然
        float distance = spawnDistance + Random.Range(-1f, 1f);
        Vector2 basePos = (Vector2)playerPos + dir * distance;

        // 垂直于 dir 的随机偏移
        Vector2 perpendicular = new Vector2(-dir.y, dir.x);
        float scatter = Random.Range(-2f, 2f);
        Vector2 finalPos = basePos + perpendicular * scatter;

        Instantiate(enemyPrefab, finalPos, Quaternion.identity);
    }

    /// <summary>
    /// 计算当前场景中存活的敌人数目（根据 EnemyHealth 组件统计）。
    /// </summary>
    private int CountAliveEnemies()
    {
        EnemyHealth[] enemies = FindObjectsOfType<EnemyHealth>();
        return enemies.Length;
    }
}

