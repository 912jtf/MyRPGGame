using UnityEngine;

public class PlayerAttack : MonoBehaviour
{
    [Header("攻击设置")]
    public float attackRange = 0.5f;                 // 攻击半径
    public Vector2 attackOffset = new Vector2(0.5f, 0f); // 面向右时，相对玩家的位置偏移
    public LayerMask enemyLayer;                     // 只勾选 Enemy 层
    public int attackDamage = 1;                     // 伤害值（敌人一下死，这里给 1 即可）

    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private bool isAttacking = false;  // 防止攻击打断
    private float attackTimer = 0f;

    [Header("安全保护")]
    public float maxAttackTime = 1f;   // 超过这个时间还没结束攻击，则强制重置

    private void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        if (isAttacking)
        {
            attackTimer += Time.deltaTime;
            if (attackTimer >= maxAttackTime)
            {
                // 防止动画事件丢失导致一直卡在攻击状态
                OnAttackEnd();
            }
        }

        // 按下 J 开始攻击
        if (!isAttacking && Input.GetKeyDown(KeyCode.J))
        {
            StartAttack();
        }
    }

    private void StartAttack()
    {
        isAttacking = true;
        attackTimer = 0f;
        if (animator != null)
        {
            animator.SetBool("isattacking", true);
        }
        // 在攻击动画中通过 Animation Event 调用 OnAttackHit 和 OnAttackEnd
    }

    /// <summary>
    /// 根据朝向计算当前攻击中心
    /// </summary>
    private Vector2 GetAttackCenter()
    {
        // 使用 SpriteRenderer.flipX 判断左右朝向：
        // flipX == false：朝右；flipX == true：朝左
        float dir = (spriteRenderer != null && spriteRenderer.flipX) ? -1f : 1f;
        Vector2 offset = new Vector2(attackOffset.x * dir, attackOffset.y);
        return (Vector2)transform.position + offset;
    }

    /// <summary>
    /// 动画事件 1：在出招的关键帧调用，用于伤害判定
    /// </summary>
    public void OnAttackHit()
    {
        // 攻击半径 <= 0 时，直接认为没有攻击范围，不做判定
        if (attackRange <= 0f)
        {
            return;
        }

        // 使用 OverlapCircleAll 进行范围判定（2D），中心根据朝向偏移
        Vector2 attackCenter = GetAttackCenter();
        // 计算当前朝向向量（右或左）
        float dir = (spriteRenderer != null && spriteRenderer.flipX) ? -1f : 1f;
        Vector2 forward = new Vector2(dir, 0f);

        Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(
            attackCenter,
            attackRange,
            enemyLayer
        );

        foreach (Collider2D enemyCollider in hitEnemies)
        {
            // 忽略用于索敌的 Trigger，只对实体碰撞体造成伤害
            if (enemyCollider.isTrigger)
            {
                continue;
            }

            // 只打在自己“前半圆”的目标（身后不受伤害）
            Vector2 toEnemy = (Vector2)enemyCollider.transform.position - (Vector2)transform.position;
            if (Vector2.Dot(toEnemy.normalized, forward) <= 0f)
            {
                continue;
            }

            EnemyHealth enemyHealth = enemyCollider.GetComponent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.TakeDamage(attackDamage);
            }
        }
    }

    /// <summary>
    /// 动画事件 2：在攻击动作最后一帧调用，用于结束攻击状态
    /// </summary>
    public void OnAttackEnd()
    {
        isAttacking = false;
        if (animator != null)
        {
            animator.SetBool("isattacking", false);
        }
    }

    /// <summary>
    /// 在 Scene 视图中显示攻击范围
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (attackRange <= 0f)
        {
            return;
        }

        // 编辑器模式下也根据当前 flipX 大致显示攻击圈
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        float dir = (sr != null && sr.flipX) ? -1f : 1f;
        Vector2 offset = new Vector2(attackOffset.x * dir, attackOffset.y);
        Vector2 center = (Vector2)transform.position + offset;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, attackRange);
    }
}
