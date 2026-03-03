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

    bool playerInRange;

    void Start()
    {
        if (hintIcon != null)
        {
            hintIcon.SetActive(false);
        }
    }

    void Update()
    {
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
        }
    }
}

