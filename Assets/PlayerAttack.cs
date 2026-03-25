using UnityEngine;
using UnityEngine.Serialization;
using Mirror;

/// <summary>
/// J：L1→L2→重击。中途按键只排队，不提前打断当前段；每段播完（OnAttackEnd）再衔接下一段。
/// </summary>
public class PlayerAttack : MonoBehaviour
{
    [Header("攻击设置")]
    [Tooltip("无 attackarea 或未使用触发器时，用圆形 Overlap 判定的半径")]
    public float attackRange = 0.5f;
    public Vector2 attackOffset = new Vector2(0.5f, 0f);
    public LayerMask enemyLayer;
    public float attackDamage = 1f;

    [Header("轻攻击伤害倍率（第 1、2 段，相对 attackDamage）")]
    public float[] comboDamageMultipliers = { 1f, 1.05f };

    [Header("第三段（重击终结）")]
    public float heavyDamageMultiplier = 2f;
    public float maxHeavyAttackTime = 1.2f;

    [Header("attackarea 触发器")]
    [Tooltip("子物体 attackarea 上的 PlayerAttackHitbox；留空则自动 GetComponentInChildren")]
    public PlayerAttackHitbox hitbox;
    [Tooltip("朝右时 attackarea 相对角色的水平偏移（朝左时自动取反）。flipX 只翻转贴图，不会移动子物体，必须由此镜像判定区。")]
    public float attackAreaOffsetX = 0.55f;
    [Tooltip("若已挂 Hitbox，默认不在 OnAttackHit 里再做圆形判定（避免重复伤害）")]
    public bool circleDamageOnlyWhenNoHitbox = true;

    [Header("输入")]
    [FormerlySerializedAs("lightAttackKey")]
    public KeyCode attackKey = KeyCode.J;

    [Header("预输入")]
    [Tooltip("站立时：整套攻击结束后在此时间内仍算预输入，可立刻起下一套 L1")]
    public float inputBufferTime = 0.18f;

    [Header("安全保护")]
    public float maxAttackTime = 1f;

    [Header("音效（拖入 AudioClip；留空则静音）")]
    [Tooltip("轻击第 1、2 段挥砍")]
    public AudioClip lightAttackSfx;
    [Tooltip("重击挥砍")]
    public AudioClip heavyAttackSfx;
    [Range(0f, 1f)] public float lightAttackSfxVolume = 1f;
    [Range(0f, 1f)] public float heavyAttackSfxVolume = 1f;

    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private NetworkIdentity _netIdentity;

    private bool isAttacking;
    private float attackTimer;
    private bool _currentAttackIsHeavy;

    private float _bufferExpireTime;
    private bool _bufferWantsLight;

    /// <summary>在 L1 期间按过 J：本段结束后接 L2（不提前打断 L1）。</summary>
    private bool _queueL2AfterL1End;
    /// <summary>在 L2 期间按过 J：本段结束后接重击（不提前打断 L2）。</summary>
    private bool _queueHeavyAfterL2End;

    private static readonly int HashAttackL1 = Animator.StringToHash("Attack_L1");
    private static readonly int HashAttackL2 = Animator.StringToHash("Attack_L2");

    private Transform _attackAreaTransform;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        _netIdentity = GetComponent<NetworkIdentity>();
        if (hitbox == null)
            hitbox = GetComponentInChildren<PlayerAttackHitbox>();
        _attackAreaTransform = hitbox != null ? hitbox.transform : transform.Find("attackarea");
    }

    private void LateUpdate()
    {
        SyncAttackAreaFacing();
    }

    /// <summary>
    /// flipX 只翻转 Sprite，子物体 attackarea 默认一直在身体右侧，需按朝向平移才能打到左侧敌人。
    /// </summary>
    private void SyncAttackAreaFacing()
    {
        if (_attackAreaTransform == null || spriteRenderer == null)
            return;

        float sign = spriteRenderer.flipX ? -1f : 1f;
        Vector3 lp = _attackAreaTransform.localPosition;
        lp.x = sign * Mathf.Abs(attackAreaOffsetX);
        _attackAreaTransform.localPosition = lp;
    }

    private bool IsControlledLocally()
    {
        if (_netIdentity != null && !_netIdentity.isLocalPlayer)
            return false;
        return true;
    }

    private void Update()
    {
        if (!IsControlledLocally())
            return;

        if (isAttacking)
        {
            attackTimer += Time.deltaTime;
            float limit = _currentAttackIsHeavy ? maxHeavyAttackTime : maxAttackTime;
            if (attackTimer >= limit)
                ForceEndAttack();
        }

        if (Input.GetKeyDown(attackKey))
        {
            _bufferExpireTime = Time.time + inputBufferTime;
            if (!isAttacking)
            {
                StartLightCombo(1);
                _bufferWantsLight = false;
                _bufferExpireTime = 0f;
            }
            else if (_currentAttackIsHeavy)
            {
                // 预输入窗口必须覆盖整段重击，否则 OnAttackEnd 时 buffer 早已过期，会感觉“重击后按 J 僵直”。
                _bufferWantsLight = true;
                _bufferExpireTime = Time.time + inputBufferTime + maxHeavyAttackTime;
            }
            else if (animator != null)
            {
                AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
                if (st.shortNameHash == HashAttackL1)
                    _queueL2AfterL1End = true;
                else if (st.shortNameHash == HashAttackL2)
                    _queueHeavyAfterL2End = true;
            }
        }

        if (!isAttacking && _bufferWantsLight && Time.time <= _bufferExpireTime)
        {
            StartLightCombo(1);
            _bufferWantsLight = false;
            _bufferExpireTime = 0f;
        }
    }

    private void StartLightCombo(int comboStep)
    {
        isAttacking = true;
        attackTimer = 0f;
        _currentAttackIsHeavy = false;
        _bufferWantsLight = false;
        _queueL2AfterL1End = false;
        _queueHeavyAfterL2End = false;

        if (animator != null)
        {
            if (HasAnimatorBool("isHeavyAttack"))
                animator.SetBool("isHeavyAttack", false);
            animator.SetInteger("ComboStep", Mathf.Clamp(comboStep, 1, 2));
            animator.SetBool("isattacking", true);
            // 与 L2 / 重击一致：强制从当前状态（含重击收招尾帧）立刻切到 L1，避免先 Exit→Idle 再排队进 L1 造成的僵直感。
            if (Mathf.Clamp(comboStep, 1, 2) == 1)
                animator.Play("Attack_L1", 0, 0f);
        }

        if (Mathf.Clamp(comboStep, 1, 2) == 1)
            PlayLightAttackSfx();
    }

    void PlayLightAttackSfx()
    {
        if (!ShouldPlayLocalPlayerSfx())
            return;
        CombatSfxUtil.Play2D(lightAttackSfx, transform.position, lightAttackSfxVolume);
    }

    void PlayHeavyAttackSfx()
    {
        if (!ShouldPlayLocalPlayerSfx())
            return;
        CombatSfxUtil.Play2D(heavyAttackSfx, transform.position, heavyAttackSfxVolume);
    }

    bool ShouldPlayLocalPlayerSfx()
    {
        if (_netIdentity == null)
            return true;
        return _netIdentity.isLocalPlayer;
    }

    private void ForceEndAttack()
    {
        OnAttackEnd();
    }

    /// <summary>
    /// 由 attackarea 的 Trigger 调用：对单个碰撞体尝试结算一次伤害。
    /// </summary>
    public bool TryApplyHitFromHitbox(Collider2D other)
    {
        if (other == null || hitbox == null || !hitbox.DamageWindowActive)
            return false;

        if (((1 << other.gameObject.layer) & enemyLayer) == 0)
            return false;

        float dir = (spriteRenderer != null && spriteRenderer.flipX) ? -1f : 1f;
        Vector2 forward = new Vector2(dir, 0f);
        Vector2 toEnemy = (Vector2)other.transform.position - (Vector2)transform.position;
        if (toEnemy.sqrMagnitude > 0.0001f && Vector2.Dot(toEnemy.normalized, forward) <= 0f)
            return false;

        if (other.isTrigger)
            return false;

        EnemyHealth enemyHealth = other.GetComponent<EnemyHealth>();
        if (enemyHealth == null)
            return false;

        float dmg = ComputeDamage();
        enemyHealth.TakeDamage(dmg, transform.position);
        return true;
    }

    private float ComputeDamage()
    {
        if (_currentAttackIsHeavy)
            return attackDamage * heavyDamageMultiplier;

        int step = 1;
        if (animator != null)
            step = Mathf.Clamp(animator.GetInteger("ComboStep"), 1, 2);

        float m = 1f;
        if (comboDamageMultipliers != null && comboDamageMultipliers.Length > 0)
        {
            int idx = Mathf.Clamp(step - 1, 0, comboDamageMultipliers.Length - 1);
            m = comboDamageMultipliers[idx];
        }
        return attackDamage * m;
    }

    /// <summary>
    /// 动画事件：无 Hitbox 或需要额外判定时，用圆形 Overlap 打一刀（与旧逻辑兼容）。
    /// </summary>
    public void OnAttackHit()
    {
        if (circleDamageOnlyWhenNoHitbox && hitbox != null)
            return;
        if (attackRange <= 0f)
            return;

        Vector2 attackCenter = GetAttackCenter();
        float dir = (spriteRenderer != null && spriteRenderer.flipX) ? -1f : 1f;
        Vector2 forward = new Vector2(dir, 0f);

        Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(attackCenter, attackRange, enemyLayer);
        foreach (Collider2D enemyCollider in hitEnemies)
        {
            if (enemyCollider.isTrigger)
                continue;
            Vector2 toEnemy = (Vector2)enemyCollider.transform.position - (Vector2)transform.position;
            if (Vector2.Dot(toEnemy.normalized, forward) <= 0f)
                continue;

            EnemyHealth enemyHealth = enemyCollider.GetComponent<EnemyHealth>();
            if (enemyHealth != null)
                enemyHealth.TakeDamage(ComputeDamage(), transform.position);
        }
    }

    private Vector2 GetAttackCenter()
    {
        float dir = (spriteRenderer != null && spriteRenderer.flipX) ? -1f : 1f;
        Vector2 offset = new Vector2(attackOffset.x * dir, attackOffset.y);
        return (Vector2)transform.position + offset;
    }

    /// <summary>动画事件：一招结束（每段轻攻击末尾或重攻击末尾）。</summary>
    public void OnAttackEnd()
    {
        if (animator != null)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            int hash = st.shortNameHash;

            // 本段完整播完后再接段，不中途打断
            if (hash == HashAttackL1 && _queueL2AfterL1End)
            {
                _queueL2AfterL1End = false;
                isAttacking = true;
                attackTimer = 0f;
                _currentAttackIsHeavy = false;
                animator.SetInteger("ComboStep", 2);
                if (HasAnimatorBool("isHeavyAttack"))
                    animator.SetBool("isHeavyAttack", false);
                animator.SetBool("isattacking", true);
                animator.Play("Attack_L2", 0, 0f);
                PlayLightAttackSfx();
                if (hitbox != null)
                {
                    hitbox.HitboxEnd();
                    hitbox.SwingStart();
                }
                return;
            }

            if (hash == HashAttackL2 && _queueHeavyAfterL2End)
            {
                _queueHeavyAfterL2End = false;
                isAttacking = true;
                attackTimer = 0f;
                _currentAttackIsHeavy = true;
                if (HasAnimatorBool("isHeavyAttack"))
                    animator.SetBool("isHeavyAttack", true);
                animator.SetInteger("ComboStep", 3);
                animator.SetBool("isattacking", true);
                animator.Play("Attack_heavy", 0, 0f);
                PlayHeavyAttackSfx();
                if (hitbox != null)
                {
                    hitbox.HitboxEnd();
                    hitbox.SwingStart();
                }
                return;
            }
        }

        isAttacking = false;
        _currentAttackIsHeavy = false;
        _queueL2AfterL1End = false;
        _queueHeavyAfterL2End = false;

        if (animator != null)
        {
            animator.SetBool("isattacking", false);
            if (HasAnimatorBool("isHeavyAttack"))
                animator.SetBool("isHeavyAttack", false);
            animator.SetInteger("ComboStep", 0);
        }

        if (hitbox != null)
        {
            hitbox.HitboxEnd();
            hitbox.SwingStart();
        }

        if (_bufferWantsLight && Time.time <= _bufferExpireTime)
        {
            StartLightCombo(1);
            _bufferWantsLight = false;
            _bufferExpireTime = 0f;
        }
    }

    /// <summary>兼容导入动画里仍叫 AttackOver 的事件。</summary>
    public void AttackOver()
    {
        OnAttackEnd();
    }

    /// <summary>动画事件（挂在 Player 根物体）：委托给子物体 attackarea 上的触发窗口。</summary>
    public void SwingStart()
    {
        if (hitbox != null)
            hitbox.SwingStart();
    }

    public void HitboxBegin()
    {
        if (hitbox != null)
            hitbox.HitboxBegin();
    }

    public void HitboxEnd()
    {
        if (hitbox != null)
            hitbox.HitboxEnd();
    }

    private static bool HasAnimatorBool(Animator a, string name)
    {
        if (a == null) return false;
        foreach (AnimatorControllerParameter p in a.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == name)
                return true;
        }
        return false;
    }

    private bool HasAnimatorBool(string name) => HasAnimatorBool(animator, name);

    private void OnDrawGizmosSelected()
    {
        if (attackRange <= 0f)
            return;
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        float dir = (sr != null && sr.flipX) ? -1f : 1f;
        Vector2 offset = new Vector2(attackOffset.x * dir, attackOffset.y);
        Vector2 center = (Vector2)transform.position + offset;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, attackRange);
    }
}
