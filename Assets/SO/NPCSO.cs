using UnityEngine;

[CreateAssetMenu(fileName = "NPC_", menuName = "SO/NPC")]
public class NPCSO : ScriptableObject
{
    [Header("基础信息")]
    public string npcName;
    public Sprite portrait;
}

