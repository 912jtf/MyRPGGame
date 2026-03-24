using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Elevation_Exit : MonoBehaviour
{
    public Collider2D[] mountainColliders;
    public Collider2D[] boundaryColliders;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision == null) return;
        if (!collision.CompareTag("Player")) return;

        // 只恢复「玩家碰撞体 vs 山体」的忽略状态，山体本身不要再 enabled=false/true
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
                    Physics2D.IgnoreCollision(playerCol, mountain, false);
                }
            }
        }

        // 边界碰撞按原逻辑关闭
        if (boundaryColliders != null)
        {
            foreach (Collider2D boundary in boundaryColliders)
            {
                if (boundary == null) continue;
                boundary.enabled = false;
            }
        }

        SpriteRenderer sr = collision.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = 10;
    }
}
