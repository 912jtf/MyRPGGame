using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI 引用")]
    public Image npcPortraitImage;          // NPC 画像
    public TMP_Text dialogueText;           // 对话文本（TextMeshProUGUI）

    [Header("根面板")]
    public GameObject dialogueRoot;         // 整个对话 UI 面板（例如 NPC对话 这个物体）

    [Header("分支选项 UI")]
    public GameObject choicesGroup;         // 挂有 GridLayoutGroup 的选项组
    public List<Button> choiceButtons;      // 3 个带 Text 的 Image（加 Button 组件）

    [Header("输入设置")]
    public KeyCode nextKey = KeyCode.Space; // 显示下一句的按键

    DialogueSO currentNode;
    int currentLineIndex;
    bool isDialogueActive;
    bool waitingForChoice;

    /// <summary>
    /// 外部可读，用于判断当前是否在对话中
    /// </summary>
    public bool IsDialogueActive => isDialogueActive;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 预先绑定按钮点击事件
        for (int i = 0; i < choiceButtons.Count; i++)
        {
            int index = i;
            if (choiceButtons[index] != null)
            {
                choiceButtons[index].onClick.AddListener(() => OnChoiceClicked(index));
            }
        }

        SetChoicesVisible(false);

        // 初始时隐藏整个对话面板
        if (dialogueRoot != null)
        {
            dialogueRoot.SetActive(false);
        }
    }

    void Update()
    {
        if (!isDialogueActive || waitingForChoice)
            return;

        if (Input.GetKeyDown(nextKey))
        {
            ShowNextLine();
        }
    }

    /// <summary>
    /// 从外部调用，开始一段对话
    /// </summary>
    public void StartDialogue(DialogueSO rootNode)
    {
        if (rootNode == null)
            return;

        if (dialogueRoot != null)
        {
            dialogueRoot.SetActive(true);
        }

        isDialogueActive = true;
        waitingForChoice = false;
        currentNode = rootNode;
        currentLineIndex = 0;

        RefreshNpcUI();
        ShowCurrentLine();
    }

    void ShowNextLine()
    {
        if (currentNode == null)
        {
            EndDialogue();
            return;
        }

        currentLineIndex++;

        // 还有当前节点剩余的行
        if (currentNode.lines != null && currentLineIndex < currentNode.lines.Length)
        {
            ShowCurrentLine();
            return;
        }

        // 当前节点的行已经结束
        // 若有分支选项，显示分支
        if (currentNode.choices != null && currentNode.choices.Count > 0)
        {
            ShowChoices();
            return;
        }

        // 无分支，则尝试进入线性下一节点
        if (currentNode.next != null)
        {
            EnterNode(currentNode.next);
            return;
        }

        // 没有下一句，对话结束
        EndDialogue();
    }

    void ShowCurrentLine()
    {
        if (currentNode == null || dialogueText == null)
            return;

        string speakerName = currentNode.npc != null ? currentNode.npc.npcName : string.Empty;
        string lineText = string.Empty;

        if (currentNode.lines != null &&
            currentLineIndex >= 0 &&
            currentLineIndex < currentNode.lines.Length)
        {
            lineText = currentNode.lines[currentLineIndex];
        }

        if (string.IsNullOrEmpty(speakerName))
        {
            dialogueText.text = lineText;
        }
        else
        {
            // 使用英文冒号，避免部分字体缺少全角冒号导致 TMP 警告
            dialogueText.text = $"{speakerName}: {lineText}";
        }
    }

    void RefreshNpcUI()
    {
        if (npcPortraitImage == null)
            return;

        if (currentNode != null && currentNode.npc != null && currentNode.npc.portrait != null)
        {
            npcPortraitImage.sprite = currentNode.npc.portrait;
            npcPortraitImage.enabled = true;
        }
        else
        {
            npcPortraitImage.sprite = null;
            npcPortraitImage.enabled = false;
        }
    }

    void ShowChoices()
    {
        waitingForChoice = true;
        SetChoicesVisible(true);

        for (int i = 0; i < choiceButtons.Count; i++)
        {
            if (choiceButtons[i] == null)
                continue;

            if (currentNode != null &&
                currentNode.choices != null &&
                i < currentNode.choices.Count &&
                currentNode.choices[i] != null)
            {
                choiceButtons[i].gameObject.SetActive(true);

                // 查找按钮上的文本组件（TextMeshProUGUI 或 Text）
                TMP_Text tmp = choiceButtons[i].GetComponentInChildren<TMP_Text>();
                if (tmp != null)
                {
                    tmp.text = currentNode.choices[i].optionText;
                }
            }
            else
            {
                choiceButtons[i].gameObject.SetActive(false);
            }
        }
    }

    void OnChoiceClicked(int index)
    {
        if (currentNode == null ||
            currentNode.choices == null ||
            index < 0 ||
            index >= currentNode.choices.Count)
        {
            return;
        }

        DialogueSO nextNode = currentNode.choices[index].nextNode;

        if (nextNode == null)
        {
            // 选择后没有下一个节点，结束对话
            EndDialogue();
            return;
        }

        EnterNode(nextNode);
    }

    void EnterNode(DialogueSO node)
    {
        currentNode = node;
        currentLineIndex = 0;
        waitingForChoice = false;

        SetChoicesVisible(false);
        RefreshNpcUI();
        ShowCurrentLine();
    }

    void EndDialogue()
    {
        isDialogueActive = false;
        waitingForChoice = false;
        currentNode = null;
        currentLineIndex = 0;

        SetChoicesVisible(false);

        if (dialogueText != null)
        {
            dialogueText.text = string.Empty;
        }

        if (dialogueRoot != null)
        {
            dialogueRoot.SetActive(false);
        }
    }

    void SetChoicesVisible(bool visible)
    {
        if (choicesGroup != null)
        {
            choicesGroup.SetActive(visible);
        }
    }
}

