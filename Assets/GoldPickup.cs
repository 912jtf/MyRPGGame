using UnityEngine;

/// <summary>
/// 场景中的可拾取金块。
/// 需要 2D Collider（建议 IsTrigger=true）。
/// </summary>
public class GoldPickup : MonoBehaviour
{
    [Min(1)] public int amount = 1;
    [Tooltip("生成后多久可拾取，防止刚生成就被同帧误触发。")]
    public float pickupDelay = 0.05f;
    [Tooltip("是否在一段时间后自动销毁（<=0 表示不自动销毁）。")]
    public float autoDestroyAfter = 30f;

    float _spawnTime;

    public bool CanBePicked => Time.time >= _spawnTime + Mathf.Max(0f, pickupDelay);

    void Awake()
    {
        _spawnTime = Time.time;
    }

    void Start()
    {
        if (autoDestroyAfter > 0f)
            Destroy(gameObject, autoDestroyAfter);
    }

    public int Consume()
    {
        int v = Mathf.Max(1, amount);
        Destroy(gameObject);
        return v;
    }
}
