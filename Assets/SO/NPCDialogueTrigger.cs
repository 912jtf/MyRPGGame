using UnityEngine;

/// <summary>
/// 挂在 NPC 身上：玩家进入触发范围时显示感叹号，按空格开始对话。
/// 需要：NPC 有一个 2D Collider 勾选 IsTrigger。
/// </summary>
public class NPCDialogueTrigger : MonoBehaviour
{
    [Header("对话数据")]
    public DialogueSO startDialogue;       // 这名 NPC 的起始对话节点

    [Header("提示图标")]
    public GameObject hintIcon;           // 头顶感叹号（子物体），进入范围显示

    [Header("输入设置")]
    public KeyCode interactKey = KeyCode.Space;
    [Header("提示呼吸特效")]
    [Tooltip("提示图标显示时是否播放呼吸效果。")]
    public bool useHintBreathing = true;
    [Tooltip("呼吸速度。")]
    public float hintBreathSpeed = 5.5f;
    [Tooltip("缩放呼吸强度（0.15 表示在原始尺寸上下约 15% 变化）。")]
    public float hintScaleBreathAmount = 0.15f;
    [Tooltip("透明呼吸最小倍率。1 表示不做透明变化。")]
    [Range(0.1f, 1f)] public float hintAlphaMin = 0.6f;

    bool playerInRange;
    Vector3 _hintBaseScale = Vector3.one;
    SpriteRenderer _hintSpriteRenderer;
    Color _hintBaseColor = Color.white;

    void Start()
    {
        if (hintIcon != null)
        {
            _hintBaseScale = hintIcon.transform.localScale;
            _hintSpriteRenderer = hintIcon.GetComponent<SpriteRenderer>();
            if (_hintSpriteRenderer != null)
                _hintBaseColor = _hintSpriteRenderer.color;
            hintIcon.SetActive(false);
        }
    }

    void Update()
    {
        UpdateHintBreathing();

        if (!playerInRange)
            return;

        // 已经在对话中时，不重复触发
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
            return;

        if (Input.GetKeyDown(interactKey))
        {
            TryStartDialogue();
        }
    }

    void TryStartDialogue()
    {
        if (startDialogue == null || DialogueManager.Instance == null)
            return;

        DialogueManager.Instance.StartDialogue(startDialogue);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;

        playerInRange = true;

        if (hintIcon != null)
        {
            hintIcon.SetActive(true);
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;

        playerInRange = false;

        if (hintIcon != null)
        {
            hintIcon.SetActive(false);
            ResetHintVisual();
        }
    }

    void UpdateHintBreathing()
    {
        if (!useHintBreathing || hintIcon == null || !hintIcon.activeSelf)
            return;

        float speed = Mathf.Max(0.1f, hintBreathSpeed);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * speed);

        float s = 1f + hintScaleBreathAmount * (pulse * 2f - 1f);
        hintIcon.transform.localScale = _hintBaseScale * s;

        if (_hintSpriteRenderer != null)
        {
            Color c = _hintBaseColor;
            c.a *= Mathf.Lerp(hintAlphaMin, 1f, pulse);
            _hintSpriteRenderer.color = c;
        }
    }

    void ResetHintVisual()
    {
        if (hintIcon != null)
            hintIcon.transform.localScale = _hintBaseScale;
        if (_hintSpriteRenderer != null)
            _hintSpriteRenderer.color = _hintBaseColor;
    }
}

