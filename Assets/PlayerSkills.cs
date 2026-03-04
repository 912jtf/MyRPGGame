using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂在玩家身上的技能管理器：负责检测输入与冷却，并调用各个 SkillSO。
/// </summary>
public class PlayerSkills : MonoBehaviour
{
    [Header("玩家拥有的技能")]
    public List<SkillSO> skills = new List<SkillSO>();

    // 运行时冷却结束时间表：SkillSO → 下次可用时间
    private readonly Dictionary<SkillSO, float> _cooldownReadyTime = new Dictionary<SkillSO, float>();

    private void Update()
    {
        if (skills == null || skills.Count == 0) return;

        foreach (SkillSO skill in skills)
        {
            if (skill == null) continue;
            if (skill.key == KeyCode.None) continue;

            // 按键按下且冷却结束
            if (Input.GetKeyDown(skill.key) && IsCooldownReady(skill))
            {
                skill.Activate(this);
                StartCooldown(skill);
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
}

