using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    public Collider2D[] mountainColliders;
    public Collider2D[] boundaryColliders;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 只处理 PlayerNet（你的 PlayerNet 预制体 tag 是 "Player"）
        if (collision == null) return;
        if (!collision.CompareTag("Player")) return;

        // 山体永远不要 enabled=false：否则野怪也会在山碰撞消失时穿上来
        // 改为：只忽略「玩家碰撞体 vs 山体碰撞体」的碰撞，让玩家能走上去
        Collider2D[] playerColliders = collision.GetComponentsInParent<Collider2D>();
        if (playerColliders == null || playerColliders.Length == 0)
            playerColliders = collision.GetComponentsInChildren<Collider2D>();

        if (mountainColliders != null && playerColliders != null)
        {
            foreach (Collider2D playerCol in playerColliders)
            {
                if (playerCol == null) continue;
                foreach (Collider2D mountain in mountainColliders)
                {
                    if (mountain == null) continue;
                    Physics2D.IgnoreCollision(playerCol, mountain, true);
                }
            }
        }

        // 边界碰撞仍按原逻辑开关（这里不影响野怪，因为山体碰撞不会被关闭）
        if (boundaryColliders != null)
        {
            foreach (Collider2D boundary in boundaryColliders)
            {
                if (boundary == null) continue;
                boundary.enabled = true;
            }
        }

        // 渲染层级（按你原需求）
        SpriteRenderer sr = collision.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = 15;
    }
}
