using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 教程/演示用：受击位移与受击动画（原导入资源中的 Enemy，为避免与项目 <see cref="Enemy"/> 重命名）。
/// </summary>
public class TutorialHitEnemy : MonoBehaviour
{
    public float speed;
    private Vector2 direction;
    private bool isHit;
    private AnimatorStateInfo info;

    private Animator animator;
    private Animator hitAnimator;
    new private Rigidbody2D rigidbody;

    void Start()
    {
        animator = transform.GetComponent<Animator>();
        hitAnimator = transform.GetChild(0).GetComponent<Animator>();
        rigidbody = transform.GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        info = animator.GetCurrentAnimatorStateInfo(0);
        if (isHit)
        {
            rigidbody.velocity = direction * speed;
            if (info.normalizedTime >= .6f)
                isHit = false;
        }
    }

    public void GetHit(Vector2 direction)
    {
        transform.localScale = new Vector3(-direction.x, 1, 1);
        isHit = true;
        this.direction = direction; ;
        animator.SetTrigger("Hit");
        hitAnimator.SetTrigger("Hit");
    }
}
