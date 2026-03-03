using UnityEngine;
using Mirror;

// 单机时控制移动；联机时只有“网络玩家”才允许控制，场景里的纯单机 Player 会禁用
public class PlayerMovement : MonoBehaviour
{
    [Header("移动速度")]
    public float speed = 5f;   // 在 Inspector 中调整玩家移动速度

    [Tooltip("小于此值的输入视为 0，避免摇杆/键盘漂移导致偶发单向移动")]
    public float inputDeadZone = 0.2f;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Vector2 moveInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        // 联机模式下，没有 NetworkIdentity 的是场景里的“单机 Player”：不控制并直接隐藏，只保留网络生成的两人
        if ((NetworkClient.active || NetworkServer.active) && GetComponent<NetworkIdentity>() == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // 获取玩家输入（WASD / 方向键），加死区避免漂移
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(moveX) < inputDeadZone) moveX = 0f;
        if (Mathf.Abs(moveY) < inputDeadZone) moveY = 0f;

        // 归一化，防止斜向移动更快
        moveInput = new Vector2(moveX, moveY).normalized;

        // 左右方向时翻转角色朝向
        if (spriteRenderer != null)
        {
            if (moveX > inputDeadZone)
            {
                // 朝右
                spriteRenderer.flipX = false;
            }
            else if (moveX < -inputDeadZone)
            {
                // 朝左
                spriteRenderer.flipX = true;
            }
        }

        // 把输入的绝对值传给 Animator
        if (animator != null)
        {
            float absH = Mathf.Abs(moveX);
            float absV = Mathf.Abs(moveY);

            animator.SetFloat("h", absH);  // 水平方向绝对值
            animator.SetFloat("s", absV);  // 垂直方向绝对值
        }
    }

    void FixedUpdate()
    {
        if (rb == null) return;
        if (moveInput.sqrMagnitude < 0.001f)
            rb.velocity = Vector2.zero;
        else
            rb.velocity = moveInput * speed;
    }

    void LateUpdate()
    {
        // 联机时己方无输入则再次清零速度，减少同步出去的位移漂移
        if (rb == null) return;
        if ((NetworkClient.active || NetworkServer.active) && GetComponent<NetworkIdentity>() != null && moveInput.sqrMagnitude < 0.001f)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
}
