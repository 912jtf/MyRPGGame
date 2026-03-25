using UnityEngine;

[CreateAssetMenu(fileName = "FireSkill_", menuName = "SO/Skill/Fire Orbit")]
public class FireSkillSO : SkillSO
{
    [Header("火焰设置")]
    public GameObject firePrefab;
    public int fireCount = 3;
    public float radius = 1.2f;
    public float rotateSpeed = 180f;   // 每秒旋转角度（度）
    public float duration = 3f;
    public int damage = 1;

    [Header("音效（可选）")]
    public AudioClip castSfx;
    [Range(0f, 1f)] public float castSfxVolume = 1f;

    public override void Activate(PlayerSkills owner)
    {
        if (firePrefab == null || owner == null) return;

        Transform center = owner.transform;
        if (center == null) return;

        if (fireCount <= 0) fireCount = 3;

        CombatSfxUtil.Play2D(castSfx, center.position, castSfxVolume);

        float deltaAngle = 360f / fireCount;

        for (int i = 0; i < fireCount; i++)
        {
            float startAngle = i * deltaAngle;
            GameObject fireObj = Object.Instantiate(firePrefab, center.position, Quaternion.identity);
            Fire fire = fireObj.GetComponent<Fire>();
            if (fire != null)
            {
                fire.Init(center, startAngle, radius, rotateSpeed, duration, damage);
            }
        }
    }
}

