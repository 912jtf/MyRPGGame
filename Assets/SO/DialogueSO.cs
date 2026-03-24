using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Dialogue_", menuName = "SO/Dialogue Node")]
public class DialogueSO : ScriptableObject
{
    [Header("说话的 NPC")]
    public NPCSO npc;

    [Header("对话内容（多行）")]
    [TextArea(2, 5)]
    public string[] lines;

    [Header("线性下一句（无分支时使用，可选）")]
    public DialogueSO next;

    [Header("节点效果（可选）")]
    [Tooltip("进入该节点时将玩家生命值回满。")]
    public bool healPlayerToFullOnEnter = false;

    [Header("分支选项（可为空）")]
    public List<DialogueChoice> choices = new List<DialogueChoice>();

    [Serializable]
    public class DialogueChoice
    {
        [TextArea(1, 3)]
        public string optionText;   // 选项文字
        public DialogueSO nextNode; // 选择后跳转到的下一个对话节点
    }
}

