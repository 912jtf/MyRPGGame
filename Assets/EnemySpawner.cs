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

    [Header("地图边界（可选）")]
    [Tooltip("是否限制敌人生成在一个矩形边界内（例如你的地图范围）")]
    public bool useBounds = false;

    [Tooltip("地图左下角世界坐标")]
    public Vector2 minPosition;

    [Tooltip("地图右上角世界坐标")]
    public Vector2 maxPosition;

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

        // 如果开启了边界限制，把生成位置限制在矩形范围内
        if (useBounds)
        {
            finalPos.x = Mathf.Clamp(finalPos.x, minPosition.x, maxPosition.x);
            finalPos.y = Mathf.Clamp(finalPos.y, minPosition.y, maxPosition.y);
        }

        Instantiate(enemyPrefab, finalPos, Quaternion.identity);
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

