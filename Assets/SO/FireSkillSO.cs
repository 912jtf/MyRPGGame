using Mirror;
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
        // 技能由 PlayerSkills.CmdActivateSkill 在服务器触发，客户端不应在本地实例化战斗物体。
        if (!NetworkServer.active)
            return;

        if (firePrefab == null || owner == null) return;

        Transform centerTransform = owner.transform;
        if (centerTransform == null) return;

        Vector3 castPos = owner.LastCastWorldPosition;
        Vector3 serverOwnerPos = centerTransform.position;
        // “客户端施放那一刻的位置” - “服务器那一刻的玩家位置”
        // 用这个 offset 修正轨道中心，消除视觉上一帧延迟
        Vector3 centerOffset = castPos - serverOwnerPos;

        if (fireCount <= 0) fireCount = 3;

        CombatSfxUtil.Play2D(castSfx, castPos, castSfxVolume);

        float deltaAngle = 360f / fireCount;

        for (int i = 0; i < fireCount; i++)
        {
            float startAngle = i * deltaAngle;
            Vector3 spawnCenterPos = serverOwnerPos + centerOffset;
            GameObject fireObj = Object.Instantiate(firePrefab, spawnCenterPos, Quaternion.identity);
            if (NetworkServer.active)
            {
                // 需要你给 Fire.prefab 添加 NetworkIdentity（并建议加 NetworkTransform）。
                if (fireObj != null)
                    NetworkServer.Spawn(fireObj);
            }
            Fire fire = fireObj.GetComponent<Fire>();
            if (fire != null)
            {
                fire.Init(centerTransform, startAngle, radius, rotateSpeed, duration, damage, centerOffset);
            }
        }
    }
}

