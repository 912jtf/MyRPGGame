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
                CmdActivateSkill(i, transform.position);
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
    void CmdActivateSkill(int skillIndex, Vector3 castWorldPos)
    {
        if (skills == null || skillIndex < 0 || skillIndex >= skills.Count)
            return;

        SkillSO skill = skills[skillIndex];
        if (skill == null) return;
        if (skill.key == KeyCode.None) return;

        // 服务器冷却也要校验（真正权威）
        if (!IsCooldownReady(skill))
            return;

        // 保存施放快照，让 SkillSO 在服务器端用“客户端那一刻的位置”
        LastCastWorldPosition = castWorldPos;

        skill.Activate(this);
        StartCooldown(skill);
    }
}

