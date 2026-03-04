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

        // 先根据碰撞体修正终点，防止穿墙
        Vector3 startPos = t.position;
        Vector3 endPos = ComputeDashEndPosition(startPos, dir.normalized);

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
    private Vector3 ComputeDashEndPosition(Vector3 startPos, Vector2 dirNormalized)
    {
        // 没配置 Layer 时，保持原来的“直线冲刺”行为
        if (obstacleLayers == 0)
        {
            return startPos + (Vector3)(dirNormalized * dashDistance);
        }

        RaycastHit2D hit = Physics2D.Raycast(startPos, dirNormalized, dashDistance, obstacleLayers);
        if (hit.collider != null)
        {
            // 命中障碍：停在碰撞点稍微外面一点，避免卡进墙
            Vector3 hitPoint = hit.point;
            return hitPoint - (Vector3)(dirNormalized * skin);
        }

        // 没有命中障碍，正常冲刺到最大距离
        return startPos + (Vector3)(dirNormalized * dashDistance);
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

