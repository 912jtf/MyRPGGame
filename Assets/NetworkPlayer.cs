using UnityEngine;
using Mirror;

// 专门用于联机的“网络玩家壳”，不要挂在你现在的单机 Player 上
[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]
public class NetworkPlayer : NetworkBehaviour
{
    PlayerMovement movement;
    Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Animator animator;

    [SyncVar(hook = nameof(OnFaceLeftChanged))]
    bool faceLeft;

    [SyncVar(hook = nameof(OnAnimHChanged))]
    float syncAnimH;

    [SyncVar(hook = nameof(OnAnimSChanged))]
    float syncAnimS;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        if (movement != null)
            movement.enabled = false;
    }

    public override void OnStartClient()
    {
        if (!netIdentity.isOwned)
        {
            // 远端角色：立刻关物理并清零速度，避免“没按键却自己动”（顺序早于 Start）
            SetRemoteRigidbody();
            ApplyRemoteState();
        }
    }

    void SetRemoteRigidbody()
    {
        if (rb == null) return;
        rb.isKinematic = true;
        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.interpolation = RigidbodyInterpolation2D.None;
    }

    void OnFaceLeftChanged(bool _, bool newVal)
    {
        if (!netIdentity.isOwned && spriteRenderer != null)
            spriteRenderer.flipX = newVal;
    }

    void OnAnimHChanged(float _, float newVal)
    {
        if (!netIdentity.isOwned && animator != null)
            animator.SetFloat("h", newVal);
    }

    void OnAnimSChanged(float _, float newVal)
    {
        if (!netIdentity.isOwned && animator != null)
            animator.SetFloat("s", newVal);
    }

    void ApplyRemoteState()
    {
        if (!netIdentity.isOwned && spriteRenderer != null)
            spriteRenderer.flipX = faceLeft;
        if (!netIdentity.isOwned && animator != null)
        {
            animator.SetFloat("h", syncAnimH);
            animator.SetFloat("s", syncAnimS);
        }
    }

    public override void OnStartAuthority()
    {
        if (movement != null)
            movement.enabled = true;
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation2D.None;
        }
    }

    void Start()
    {
        if (!netIdentity.isOwned)
            SetRemoteRigidbody();
    }

    void LateUpdate()
    {
        // 远端每帧强制 kinematic + 速度零，避免被物理/插值带出漂移
        if (!netIdentity.isOwned)
            SetRemoteRigidbody();
    }

    void Update()
    {
        if (!netIdentity.isOwned) return;

        if (spriteRenderer != null)
        {
            bool fl = spriteRenderer.flipX;
            if (fl != faceLeft)
                CmdSetFaceLeft(fl);
        }

        if (animator != null)
        {
            float h = animator.GetFloat("h");
            float s = animator.GetFloat("s");
            if (Mathf.Abs(h - syncAnimH) > 0.01f || Mathf.Abs(s - syncAnimS) > 0.01f)
                CmdSetAnim(h, s);
        }
    }

    [Command]
    void CmdSetFaceLeft(bool value)
    {
        faceLeft = value;
    }

    [Command]
    void CmdSetAnim(float h, float s)
    {
        syncAnimH = h;
        syncAnimS = s;
    }
}

