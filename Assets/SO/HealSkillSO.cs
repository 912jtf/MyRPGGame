using Mirror;
using UnityEngine;

[CreateAssetMenu(fileName = "HealSkill_", menuName = "SO/Skill/Heal")]
public class HealSkillSO : SkillSO
{
    [Header("治疗设置")]
    public int healAmount = 1;

    [Header("特效")]
    public GameObject healEffectPrefab;
    public Vector3 effectOffset = Vector3.zero;

    [Header("音效（可选）")]
    public AudioClip castSfx;
    [Range(0f, 1f)] public float castSfxVolume = 1f;

    public override void Activate(PlayerSkills owner)
    {
        // 技能由 PlayerSkills.CmdActivateSkill 在服务器触发
        if (!NetworkServer.active)
            return;

        if (owner == null) return;

        Vector3 castPos = owner.LastCastWorldPosition;
        CombatSfxUtil.Play2D(castSfx, castPos, castSfxVolume);

        PlayerHealth playerHealth = owner.GetComponent<PlayerHealth>();
        if (playerHealth != null && healAmount > 0)
        {
            playerHealth.Heal(healAmount);
        }

        if (healEffectPrefab != null)
        {
            Vector3 spawnPos = owner.LastCastWorldPosition + effectOffset;
            GameObject eff = Object.Instantiate(healEffectPrefab, spawnPos, Quaternion.identity);
            if (eff != null)
                NetworkServer.Spawn(eff);
        }
    }
}

