using Mirror;
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
        if (!NetworkServer.active)
            return;

        Transform t = owner.transform;
        if (t == null) return;

        Rigidbody2D rb = owner.GetComponent<Rigidbody2D>();
        SpriteRenderer sr = owner.GetComponent<SpriteRenderer>();

        // 优先用 Cmd 里记录的 LastCastVelocity（客户端按键瞬间的移动方向），避免服务器上 rb.velocity 与朝向不同步导致“倒着冲”。
        Vector2 dir = Vector2.zero;
        if (owner.LastCastVelocity.sqrMagnitude > 0.001f)
            dir = owner.LastCastVelocity.normalized;
        else if (rb != null && rb.velocity.sqrMagnitude > 0.001f)
            dir = rb.velocity.normalized;
        else if (sr != null)
        {
            // flipX=true 时贴图朝左，冲刺方向为 (-1,0)；flipX=false 朝右，为 (1,0)。此前用 -faceX 会左右反了。
            dir = new Vector2(sr.flipX ? -1f : 1f, 0f);
        }
        else
            dir = Vector2.left;

        if (dir.sqrMagnitude < 0.001f) return;

        Vector3 startPos = t.position;
        Collider2D ownerCol = owner.GetComponent<Collider2D>();
        Vector3 endPos = ComputeDashEndPosition(startPos, dir.normalized, ownerCol);

        // 玩家 NetworkTransform 为 Client→Server 时，服务器上改 transform 会被客户端下一帧位置覆盖。
        // 冲刺位移必须在「拥有者客户端」执行，由 NetworkTransform 同步到服务器与其它客户端。
        float dur = Mathf.Max(0f, dashDuration);
        owner.ServerNotifyDash(endPos, dur);
    }

    Vector3 ComputeDashEndPosition(Vector3 startPos, Vector2 dirNormalized, Collider2D ownerCol)
    {
        float dist = Mathf.Max(0f, dashDistance);
        if (dist <= 0f)
            return startPos;

        if (ownerCol != null)
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true
            };

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

        int rayMask = obstacleLayers.value != 0 ? obstacleLayers.value : Physics2D.DefaultRaycastLayers;
        RaycastHit2D hit = Physics2D.Raycast(startPos, dirNormalized, dist, rayMask);
        if (hit.collider != null)
            return hit.point - dirNormalized * Mathf.Max(0f, skin);

        return startPos + (Vector3)(dirNormalized * dist);
    }
}
