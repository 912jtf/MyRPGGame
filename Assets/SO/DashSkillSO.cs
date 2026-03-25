using UnityEngine;

[CreateAssetMenu(fileName = "DashSkill_", menuName = "SO/Skill/Dash")]
public class DashSkillSO : SkillSO
{
    [Header("冲刺设置")]
    [Tooltip("冲刺距离")]
    public float dashDistance = 3f;

    [Tooltip("冲刺持续时间（秒），0 表示瞬移")]
    public float dashDuration = 0.1f;

    [Header("碰撞设置")]
    [Tooltip("冲刺时会被这些 Layer 的碰撞体阻挡（例如：默认地面/建筑层）")]
    public LayerMask obstacleLayers;

    [Tooltip("与碰撞体保持的最小间隔，避免精度误差卡进墙里")]
    public float skin = 0.02f;
    readonly RaycastHit2D[] _dashCastHits = new RaycastHit2D[16];

    [Header("音效（可选）")]
    public AudioClip castSfx;
    [Range(0f, 1f)] public float castSfxVolume = 1f;

    public override void Activate(PlayerSkills owner)
    {
        if (owner == null) return;

        Transform t = owner.transform;
        if (t == null) return;

        Rigidbody2D rb = owner.GetComponent<Rigidbody2D>();
        SpriteRenderer sr = owner.GetComponent<SpriteRenderer>();

        Vector2 dir = Vector2.zero;

        // 1. 有移动速度时，沿当前移动方向冲刺
        if (rb != null && rb.velocity.sqrMagnitude > 0.001f)
        {
            dir = rb.velocity.normalized;
        }
        else if (sr != null)
        {
            // 2. 静止时，根据朝向决定“向后”方向
            // flipX == false：脸朝右；flipX == true：脸朝左
            float faceX = sr.flipX ? -1f : 1f;
            // 向后 = 朝向的反方向
            dir = new Vector2(-faceX, 0f);
        }
        else
        {
            // 3. 兜底：向左闪避
            dir = Vector2.left;
        }

        if (dir.sqrMagnitude < 0.001f) return;

        CombatSfxUtil.Play2D(castSfx, t.position, castSfxVolume);

        // 先根据碰撞体修正终点，防止穿墙
        Vector3 startPos = t.position;
        Collider2D ownerCol = owner.GetComponent<Collider2D>();
        Vector3 endPos = ComputeDashEndPosition(startPos, dir.normalized, ownerCol);

        // 0 或负数时，保持原来的“瞬移”感
        if (dashDuration <= 0f)
        {
            t.position = endPos;
            return;
        }

        // 否则使用协程，在 dashDuration 内平滑移动
        owner.StartCoroutine(DashRoutine(owner.gameObject, startPos, endPos));
    }

    /// <summary>
    /// 从 startPos 朝 dirNormalized 方向尝试 dashDistance 距离，
    /// 如果遇到 obstacleLayers 上的碰撞体，则终点停在碰撞点前一点。
    /// </summary>
    private Vector3 ComputeDashEndPosition(Vector3 startPos, Vector2 dirNormalized, Collider2D ownerCol)
    {
        float dist = Mathf.Max(0f, dashDistance);
        if (dist <= 0f)
            return startPos;

        // 1) 优先用玩家自身碰撞体做 Cast（比 Raycast 点检测更不容易穿墙）。
        if (ownerCol != null)
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true
            };

            // 若未配置 obstacleLayers，默认检测所有碰撞层。
            int mask = obstacleLayers.value != 0 ? obstacleLayers.value : Physics2D.DefaultRaycastLayers;
            filter.SetLayerMask(mask);

            int count = ownerCol.Cast(dirNormalized, filter, _dashCastHits, dist + Mathf.Max(0f, skin));
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D h = _dashCastHits[i];
                if (h.collider == null) continue;
                if (h.distance <= 0.0001f) continue;
                if (h.distance < nearest) nearest = h.distance;
            }

            if (nearest < float.PositiveInfinity)
            {
                float safe = Mathf.Max(0f, nearest - Mathf.Max(0f, skin));
                return startPos + (Vector3)(dirNormalized * safe);
            }
        }

        // 2) 兜底：没有碰撞体时用 Raycast。
        int rayMask = obstacleLayers.value != 0 ? obstacleLayers.value : Physics2D.DefaultRaycastLayers;
        RaycastHit2D hit = Physics2D.Raycast(startPos, dirNormalized, dist, rayMask);
        if (hit.collider != null)
            return hit.point - dirNormalized * Mathf.Max(0f, skin);

        return startPos + (Vector3)(dirNormalized * dist);
    }

    private System.Collections.IEnumerator DashRoutine(GameObject target, Vector3 startPos, Vector3 endPos)
    {
        if (target == null) yield break;

        Transform t = target.transform;
        if (t == null) yield break;

        Rigidbody2D rb = target.GetComponent<Rigidbody2D>();

        float elapsed = 0f;

        // 冲刺过程中可选地把刚体速度清零，避免抖动
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }

        while (elapsed < dashDuration)
        {
            elapsed += Time.deltaTime;
            float t01 = Mathf.Clamp01(elapsed / dashDuration);

            // 这里用线性插值（如需更柔和可以改成缓入缓出曲线）
            t.position = Vector3.Lerp(startPos, endPos, t01);

            yield return null;
        }

        // 结束时确保精确落在目标点
        t.position = endPos;
    }
}

