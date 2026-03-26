using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 挂在玩家身上的技能管理器：负责检测输入与冷却，并调用各个 SkillSO。
/// </summary>
public class PlayerSkills : NetworkBehaviour
{
    [Header("玩家拥有的技能")]
    public List<SkillSO> skills = new List<SkillSO>();

    // 运行时冷却结束时间表：SkillSO → 下次可用时间
    private readonly Dictionary<SkillSO, float> _cooldownReadyTime = new Dictionary<SkillSO, float>();

    // 给各 SkillSO 在服务器侧使用的“投放/施放快照”
    // 目的是修复：客户端走路时命令到服务器有一帧延迟，导致特效/弹丸从落后位置生成。
    public Vector3 LastCastWorldPosition { get; private set; }
    public Vector2 LastCastVelocity { get; private set; }

    /// <summary>由 CmdActivateSkill 在调用 SkillSO.Activate 前写入，供技能在服务器侧触发“仅施法者客户端”的音效。</summary>
    public int LastActivatedSkillIndex { get; private set; }

    [Header("冲刺（DashSkillSO）客户端音效")]
    [Tooltip("与 DashSkillSO 的 castSfx 一致即可；留空则仅位移无音效。")]
    [SerializeField] AudioClip _dashSfx;
    [SerializeField] [Range(0f, 1f)] float _dashSfxVolume = 1f;

    void Awake()
    {
        // keep empty
    }

    private void Update()
    {
        // 只让“本地玩家”发送技能请求到服务器
        if (!isLocalPlayer)
            return;

        if (skills == null || skills.Count == 0) return;

        for (int i = 0; i < skills.Count; i++)
        {
            SkillSO skill = skills[i];
            if (skill == null) continue;
            if (skill.key == KeyCode.None) continue;

            // 按键按下且冷却结束
            if (Input.GetKeyDown(skill.key) && IsCooldownReady(skill))
            {
                // 交给服务器执行（保证火球/治疗/冲刺在两边一致）
                Vector3 castPos = transform.position;
                Vector2 castVel = Vector2.zero;
                Rigidbody2D rb = GetComponent<Rigidbody2D>();
                if (rb != null)
                    castVel = rb.velocity;

                // 用服务器时间戳做预测：减少“命令延迟导致火球中心落后”的观感
                double castServerTime = NetworkTime.time;
                CmdActivateSkill(i, castPos, castVel, castServerTime);
            }
        }
    }

    private bool IsCooldownReady(SkillSO skill)
    {
        if (!_cooldownReadyTime.TryGetValue(skill, out float readyTime))
        {
            return true;
        }

        return Time.time >= readyTime;
    }

    private void StartCooldown(SkillSO skill)
    {
        float cd = Mathf.Max(0f, skill.cooldown);
        _cooldownReadyTime[skill] = Time.time + cd;
    }

    [Command]
    void CmdActivateSkill(int skillIndex, Vector3 castWorldPos, Vector2 castVelocity, double castServerTime)
    {
        if (skills == null || skillIndex < 0 || skillIndex >= skills.Count)
            return;

        SkillSO skill = skills[skillIndex];
        if (skill == null) return;
        if (skill.key == KeyCode.None) return;

        // 服务器冷却也要校验（真正权威）
        if (!IsCooldownReady(skill))
            return;

        // 预测：把“客户端施放那一刻的位置”推进到“服务器执行那一刻”
        // dt 可能会受网络影响，这里做一个安全夹紧避免异常大 dt
        double dtDouble = NetworkTime.time - castServerTime;
        float dt = Mathf.Clamp((float)dtDouble, 0f, 0.5f);

        LastCastVelocity = castVelocity;
        LastCastWorldPosition = castWorldPos + (Vector3)(castVelocity * dt);

        LastActivatedSkillIndex = skillIndex;
        skill.Activate(this);
        StartCooldown(skill);
    }

    /// <summary>由 FireSkillSO / HealSkillSO 等在服务器上调用：施法音效只在拥有者本机播放。</summary>
    public void ServerNotifySkillCastSfxLocal(Vector3 worldPos)
    {
        if (!NetworkServer.active)
            return;
        TargetSkillCastSfx(LastActivatedSkillIndex, worldPos);
    }

    [TargetRpc]
    void TargetSkillCastSfx(int skillIndex, Vector3 worldPos)
    {
        if (skills == null || skillIndex < 0 || skillIndex >= skills.Count)
            return;
        SkillSO s = skills[skillIndex];
        if (s is FireSkillSO fire)
            CombatSfxUtil.Play2D(fire.castSfx, worldPos, fire.castSfxVolume);
        else if (s is HealSkillSO heal)
            CombatSfxUtil.Play2D(heal.castSfx, worldPos, heal.castSfxVolume);
    }

    /// <summary>由 EnemyHealth 在服务器上调用：野怪受击音只在“造成伤害的玩家”本机播放。</summary>
    [TargetRpc]
    internal void TargetPlayEnemyHurtSfxAtOwnerClient(uint enemyNetId)
    {
        if (!NetworkClient.spawned.TryGetValue(enemyNetId, out NetworkIdentity ni) || ni == null)
            return;
        EnemyHealth eh = ni.GetComponent<EnemyHealth>();
        if (eh == null)
            return;
        CombatSfxUtil.Play2D(eh.hurtSfx, ni.transform.position, eh.hurtSfxVolume);
    }

    /// <summary>由 DashSkillSO 在服务器上调用：把冲刺位移下发到拥有者客户端（与 Client→Server 的 NetworkTransform 一致）。</summary>
    public void ServerNotifyDash(Vector3 endPos, float duration)
    {
        if (!NetworkServer.active)
            return;
        TargetApplyDash(endPos, duration);
    }

    [TargetRpc]
    void TargetApplyDash(Vector3 endPos, float duration)
    {
        if (_dashSfx != null)
            CombatSfxUtil.Play2D(_dashSfx, transform.position, _dashSfxVolume);
        else if (skills != null)
        {
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i] is DashSkillSO dash)
                {
                    CombatSfxUtil.Play2D(dash.castSfx, transform.position, dash.castSfxVolume);
                    break;
                }
            }
        }
        StartCoroutine(CoDashLocal(endPos, duration));
    }

    IEnumerator CoDashLocal(Vector3 endPos, float duration)
    {
        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm != null)
            pm.SetDashSuppressMove(true);

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        Transform tr = transform;
        Vector3 startPos = tr.position;

        if (duration <= 0f)
        {
            tr.position = endPos;
            if (rb != null)
            {
                rb.velocity = Vector2.zero;
                rb.MovePosition(endPos);
            }
            if (pm != null)
                pm.SetDashSuppressMove(false);
            yield break;
        }

        if (rb != null)
            rb.velocity = Vector2.zero;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t01 = Mathf.Clamp01(elapsed / duration);
            Vector3 p = Vector3.Lerp(startPos, endPos, t01);
            tr.position = p;
            if (rb != null)
                rb.MovePosition(p);
            yield return null;
        }

        tr.position = endPos;
        if (rb != null)
        {
            rb.MovePosition(endPos);
            rb.velocity = Vector2.zero;
        }
        if (pm != null)
            pm.SetDashSuppressMove(false);
    }
}

