using UnityEngine;

[CreateAssetMenu(fileName = "Skill_", menuName = "SO/Skill/Base Skill")]
public abstract class SkillSO : ScriptableObject
{
    [Header("通用设置")]
    public string skillName;

    [Tooltip("释放该技能的按键")]
    public KeyCode key = KeyCode.None;

    [Tooltip("技能冷却时间（秒）")]
    public float cooldown = 1f;

    /// <summary>
    /// 技能释放逻辑，由具体技能实现。
    /// </summary>
    /// <param name="owner">释放技能的实体（通常是玩家）</param>
    public abstract void Activate(PlayerSkills owner);
}

