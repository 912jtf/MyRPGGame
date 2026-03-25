using UnityEngine;
using Mirror;

// 单机时控制移动；联机时只有“网络玩家”才允许控制，场景里的纯单机 Player 会禁用
public class PlayerMovement : MonoBehaviour
{
    [Header("移动速度")]
    public float speed = 5f;   // 在 Inspector 中调整玩家移动速度

    [Tooltip("小于此值的输入视为 0，避免摇杆/键盘漂移导致偶发单向移动")]
    public float inputDeadZone = 0.2f;

    [Header("地图边界（空气墙）")]
    [Tooltip("勾选后限制角色不走出该矩形范围")]
    public bool useMapBounds = true;
    [Tooltip("边界左下角 X")]
    public float mapMinX = -20f;
    [Tooltip("边界右上角 X")]
    public float mapMaxX = 20f;
    [Tooltip("边界左下角 Y")]
    public float mapMinY = -20f;
    [Tooltip("边界右上角 Y")]
    public float mapMaxY = 20f;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Vector2 moveInput;
    private NetworkIdentity _netIdentity;
    private Vector2 _lastPos;

    [Header("Net Debug (client jitter)")]
    public bool debugRemoteJitterLog = true;
    [Tooltip("每帧如果位移超过该阈值，认为发生了可见的突变（用于判断瞬移/掉帧）")]
    public float remoteFrameJumpThreshold = 0.25f;
    [Tooltip("同一玩家在短时间内最多打印一次，避免刷屏")]
    public float remoteFrameLogCooldownSeconds = 0.5f;
    public float remoteLogDurationSeconds = 15f;

    private float _remoteLogUntilTime;
    private float _remoteFrameNextLogTime;
    private Vector2 _remotePrevFramePos;
    private bool _remotePrevFramePosInit;

    [Header("Net Anim (remote)")]
    [Tooltip("远端速度小于该阈值时认为静止，避免抖动导致反复走/停")]
    public float remoteStopVelocity = 0.15f;
    [Tooltip("远端移动方向平滑系数（越大越跟手）。0 表示不平滑。")]
    public float remoteMoveInputSmooth = 18f;
    private Vector2 _remoteMoveInputSmoothed;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        _netIdentity = GetComponent<NetworkIdentity>();
        _lastPos = transform.position;

        _remotePrevFramePos = transform.position;
        _remotePrevFramePosInit = false;
        _remoteFrameNextLogTime = 0f;
        _remoteLogUntilTime = Time.time + remoteLogDurationSeconds;

        _remoteMoveInputSmoothed = Vector2.zero;
    }

    private void ApplyNetRolePhysicsEveryFrame()
    {
        if (rb == null)
            return;
        if (_netIdentity == null)
            return;
        if (!NetworkClient.active && !NetworkServer.active)
            return;

        // 关键：
        // - Server 上必须让 Rigidbody 为 Dynamic，确保敌人的 Trigger/碰撞判定在 server 端能正确触发
        // - Client 上：本地玩家 Dynamic；远端玩家 Kinematic（不让客户端物理反推位置）
        bool isServer = NetworkServer.active;
        bool isLocal = _netIdentity.isLocalPlayer;

        if (!rb.simulated)
            rb.simulated = true;

#if UNITY_6000_0_OR_NEWER
        if (isServer)
            rb.bodyType = RigidbodyType2D.Dynamic;
        else
            rb.bodyType = isLocal ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
#else
        if (isServer)
            rb.isKinematic = false;
        else
            rb.isKinematic = !isLocal;
#endif

        if (!isLocal)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void Update()
    {
        // 联机模式下，没有 NetworkIdentity 的是场景里的“单机 Player”：不控制并直接隐藏，只保留网络生成的两人
        if ((NetworkClient.active || NetworkServer.active) && _netIdentity == null)
        {
            gameObject.SetActive(false);
            return;
        }

        ApplyNetRolePhysicsEveryFrame();

        bool local = (_netIdentity == null) || _netIdentity.isLocalPlayer;

        float moveX;
        float moveY;

        if (local)
        {
            // 获取玩家输入（WASD / 方向键），加死区避免漂移
            moveX = Input.GetAxisRaw("Horizontal");
            moveY = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(moveX) < inputDeadZone) moveX = 0f;
            if (Mathf.Abs(moveY) < inputDeadZone) moveY = 0f;

            // 归一化，防止斜向移动更快
            moveInput = new Vector2(moveX, moveY).normalized;

            // 左右方向时翻转角色朝向
            if (spriteRenderer != null)
            {
                if (moveX > inputDeadZone) spriteRenderer.flipX = false;
                else if (moveX < -inputDeadZone) spriteRenderer.flipX = true;
            }
        }
        else
        {
            // Remote object: position is updated by NetworkTransform.
            // Detect visible "jumps" by checking frame-to-frame delta.
            if (debugRemoteJitterLog && Time.time <= _remoteLogUntilTime)
            {
                Vector2 curForLog = transform.position;
                if (_remotePrevFramePosInit)
                {
                    float deltaFrame = Vector2.Distance(curForLog, _remotePrevFramePos);
                    if (deltaFrame >= remoteFrameJumpThreshold && Time.time >= _remoteFrameNextLogTime)
                    {
                        _remoteFrameNextLogTime = Time.time + remoteFrameLogCooldownSeconds;
                        float dtForLog = Mathf.Max(0.0001f, Time.deltaTime);
                        float approxSpeed = deltaFrame / dtForLog;
                        Debug.Log($"[NetJitter] RemotePlayer frameJump={deltaFrame:F3} approxSpeed={approxSpeed:F3} pos={curForLog} rb.simulated={(rb != null ? rb.simulated : false)} t={Time.time:F2}");
                    }
                }

                _remotePrevFramePos = curForLog;
                _remotePrevFramePosInit = true;
            }

            // 非本地玩家：不读取输入；用位置变化估算“速度大小”，保证客户端至少看到走路/奔跑动画
            Vector2 cur = transform.position;
            Vector2 delta = (cur - _lastPos);
            _lastPos = cur;
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            Vector2 vel = delta / dt;

            // 只用于 Animator，不驱动刚体
            // 关键：你的 Animator 的 h/s 通常用输入轴 [-1,1] 的语义来驱动，
            // 这里应使用“方向归一化后的分量”，而不是直接用速度 vel.x/vel.y（单位 m/s）。
            Vector2 desiredMoveInput = Vector2.zero;
            if (vel.sqrMagnitude > remoteStopVelocity * remoteStopVelocity)
                desiredMoveInput = vel.normalized;

            if (remoteMoveInputSmooth > 0f)
            {
                // 指数平滑：在不同帧率下手感更一致
                float t = 1f - Mathf.Exp(-remoteMoveInputSmooth * Time.deltaTime);
                _remoteMoveInputSmoothed = Vector2.Lerp(_remoteMoveInputSmoothed, desiredMoveInput, t);
            }
            else
            {
                _remoteMoveInputSmoothed = desiredMoveInput;
            }

            moveInput = _remoteMoveInputSmoothed;
            moveX = moveInput.x;
            moveY = moveInput.y;

            if (spriteRenderer != null)
            {
                if (moveX > inputDeadZone) spriteRenderer.flipX = false;
                else if (moveX < -inputDeadZone) spriteRenderer.flipX = true;
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
        // 非本地玩家不驱动刚体速度（由 NetworkTransform 同步位置）
        if (_netIdentity != null && !_netIdentity.isLocalPlayer)
        {
            rb.velocity = Vector2.zero;
            return;
        }
        if (moveInput.sqrMagnitude < 0.001f)
            rb.velocity = Vector2.zero;
        else
            rb.velocity = moveInput * speed;
    }

    void LateUpdate()
    {
        if (rb == null) return;

        // 本地玩家做地图边界裁剪，非本地交给网络同步避免互相打架
        if (_netIdentity != null && !_netIdentity.isLocalPlayer)
        {
            return;
        }

        // 地图边界：把位置限制在矩形内，像空气墙
        if (useMapBounds)
        {
            Vector2 pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, mapMinX, mapMaxX);
            pos.y = Mathf.Clamp(pos.y, mapMinY, mapMaxY);
            transform.position = pos;
            if (rb != null)
                rb.position = pos;
        }

        // 联机时己方无输入则再次清零速度，减少同步出去的位移漂移
        if ((NetworkClient.active || NetworkServer.active) && _netIdentity != null && moveInput.sqrMagnitude < 0.001f)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
}
