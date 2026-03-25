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
        // Reliable 快照的时间差会让 centerOffset 出现“沿移动方向偏移”的错觉。
        // 现在把轨道中心“强绑定到角色本体”，由 Fire.cs 客户端本地公式来跟随角色中心。
        Vector3 centerOffset = Vector3.zero;

        if (fireCount <= 0) fireCount = 3;

        CombatSfxUtil.Play2D(castSfx, castPos, castSfxVolume);

        float deltaAngle = 360f / fireCount;

        for (int i = 0; i < fireCount; i++)
        {
            float startAngle = i * deltaAngle;
            Vector3 spawnCenterPos = serverOwnerPos;
            GameObject fireObj = Object.Instantiate(firePrefab, spawnCenterPos, Quaternion.identity);
            Fire fire = fireObj.GetComponent<Fire>();
            if (fire != null)
            {
                // 必须在 Spawn 之前调用，确保 SyncVar 初始值能随首包同步到客户端
                fire.Init(centerTransform, startAngle, radius, rotateSpeed, duration, damage, centerOffset);
            }

            if (NetworkServer.active && fireObj != null)
                NetworkServer.Spawn(fireObj);
        }
    }
}

