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
        // 非自己控制的角色一出现就应用同步的朝向和动画（用 isOwned 判断，Host 上看 Client 的角色也是“别人”）
        if (!netIdentity.isOwned)
            ApplyRemoteState();
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
            rb.isKinematic = false;
    }

    void Start()
    {
        // 别人看到的“幽灵”：只由 NetworkTransform 驱动位置，关物理避免漂移；自己控制的保持非 kinematic 才能和地形碰撞
        if (!netIdentity.isOwned && rb != null)
            rb.isKinematic = true;
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

