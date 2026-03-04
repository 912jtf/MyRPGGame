using UnityEngine;

[CreateAssetMenu(fileName = "DashSkill_", menuName = "SO/Skill/Dash")]
public class DashSkillSO : SkillSO
{
    [Header("冲刺设置")]
    [Tooltip("冲刺距离")]
    public float dashDistance = 3f;

    [Tooltip("冲刺持续时间（秒），0 表示瞬移")]
    public float dashDuration = 0.1f;

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

        // 0 或负数时，保持原来的“瞬移”感
        if (dashDuration <= 0f)
        {
            Vector3 deltaInstant = (Vector3)(dir.normalized * dashDistance);
            t.position += deltaInstant;
            return;
        }

        // 否则使用协程，在 dashDuration 内平滑移动
        owner.StartCoroutine(DashRoutine(owner.gameObject, dir.normalized));
    }

    private System.Collections.IEnumerator DashRoutine(GameObject target, Vector2 dirNormalized)
    {
        if (target == null) yield break;

        Transform t = target.transform;
        if (t == null) yield break;

        Rigidbody2D rb = target.GetComponent<Rigidbody2D>();

        Vector3 startPos = t.position;
        Vector3 endPos = startPos + (Vector3)(dirNormalized * dashDistance);

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

