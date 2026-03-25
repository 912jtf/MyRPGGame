using Mirror;
using UnityEngine;

public class Fire : MonoBehaviour
{
    private Transform center;
    private Vector3 centerOffset; // 修正轨道中心，消除“施放命令到服务器有一帧延迟”导致的落后
    private float angle;        // 当前角度（度）
    private float radius;
    private float rotateSpeed;  // 每秒旋转角度（度）
    private float duration;
    private float timer;
    private int damage;

    /// <summary>
    /// 由技能在生成时调用，初始化火焰轨道参数。
    /// </summary>
    public void Init(Transform center, float startAngle, float radius, float rotateSpeed, float duration, int damage)
    {
        this.center = center;
        this.angle = startAngle;
        this.radius = radius;
        this.rotateSpeed = rotateSpeed;
        this.duration = duration;
        this.damage = damage;
    }

    /// <summary>
    /// 兼容旧调用：centerOffset 默认 (0,0,0)
    /// </summary>
    public void Init(Transform center, float startAngle, float radius, float rotateSpeed, float duration, int damage, Vector3 centerOffset)
    {
        this.center = center;
        this.centerOffset = centerOffset;
        this.angle = startAngle;
        this.radius = radius;
        this.rotateSpeed = rotateSpeed;
        this.duration = duration;
        this.damage = damage;
    }

    private void Update()
    {
        // 由网络同步驱动：只在服务器更新弹丸位置/销毁；客户端依赖 NetworkTransform
        if (!NetworkServer.active)
            return;

        if (center == null)
        {
            Destroy(gameObject);
            return;
        }

        timer += Time.deltaTime;
        if (timer >= duration)
        {
            Destroy(gameObject);
            return;
        }

        angle += rotateSpeed * Time.deltaTime;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
        // 由中心 Transform + offset 修正，保证轨道从客户端看到的位置开始
        transform.position = (center.position + centerOffset) + offset;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!NetworkServer.active)
            return;

        // 只对“身体”碰撞体造成伤害，忽略敌人的索敌用 Trigger，避免调大索敌半径时火球伤害范围跟着变大
        if (other.isTrigger) return;

        EnemyHealth enemyHealth = other.GetComponent<EnemyHealth>();
        if (enemyHealth != null && damage > 0)
        {
            enemyHealth.TakeDamage(damage);
        }
    }
}

