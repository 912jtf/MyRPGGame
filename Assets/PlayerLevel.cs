using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家等级与经验系统。
/// 规则：
/// - 初始 1 级，满级 5 级。
/// - 每击杀 1 个敌人 +1 经验。
/// - 升级需求：
///   1->2: 7 经验
///   2->3: 10 经验
///   3->4: 13 经验
///   4->5: 16 经验
/// - 每升一级：回复 2 点生命值，攻击伤害 +1。
/// 挂在 PlayerNet 上。
/// </summary>
public class PlayerLevel : MonoBehaviour
{
    [Header("等级与经验")]
    [Tooltip("当前等级（1~5）")]
    public int level = 1;

    [Tooltip("当前经验值")]
    public float currentExp = 0f;

    [Tooltip("最大等级")]
    public int maxLevel = 5;

    // 升级所需经验表，下标 0 表示 1->2 所需经验
    // 对应题目要求：7, 10, 13, 16
    private readonly float[] _expToNext =
    {
        7f,  // 1->2
        10f, // 2->3
        13f, // 3->4
        16f  // 4->5
    };

    [Header("UI（可选）")]
    [Tooltip("经验条 Image（Filled，Horizontal），可留空自动按名字查找 ExpBar")]
    public Image expFillImage;

    [Tooltip("显示等级文字的 Text（例如 \"Lv.1\"），可留空自动按名字查找 LevelText")]
    public TMPro.TMP_Text levelText;

    [Tooltip("显示经验数值的 Text（例如 \"0/7\"），可留空自动按名字查找 ExpText")]
    public TMPro.TMP_Text expText;

    private PlayerHealth _playerHealth;
    private PlayerAttack _playerAttack;

    private void Awake()
    {
        _playerHealth = GetComponent<PlayerHealth>();
        _playerAttack = GetComponent<PlayerAttack>();

        // 确保初始等级合法
        level = Mathf.Clamp(level, 1, maxLevel);
    }

    private void Start()
    {
        AutoFindUI();
        RefreshUI();
    }

    /// <summary>
    /// 在场景里自动按名字查找经验条、等级文字和经验数值文字。
    /// </summary>
    private void AutoFindUI()
    {
        if (expFillImage == null)
        {
            GameObject bar = GameObject.Find("ExpBar");
            if (bar != null)
            {
                expFillImage = bar.GetComponent<Image>();
            }
        }

        if (levelText == null)
        {
            GameObject go = GameObject.Find("LevelText");
            if (go != null)
            {
                levelText = go.GetComponent<TMPro.TMP_Text>();
            }
        }

        if (expText == null)
        {
            GameObject go = GameObject.Find("ExpText");
            if (go != null)
            {
                expText = go.GetComponent<TMPro.TMP_Text>();
            }
        }
    }

    /// <summary>
    /// 外部（例如 EnemyHealth.Die）调用，为玩家增加经验。
    /// </summary>
    public void AddExp(float amount)
    {
        if (amount <= 0) return;
        if (level >= maxLevel) return; // 满级后不再积累经验

        currentExp += amount;

        // 可能一次升多级，因此使用 while 循环
        while (level < maxLevel && currentExp >= GetExpToNextLevel(level))
        {
            currentExp -= GetExpToNextLevel(level);
            LevelUp();
        }

        RefreshUI();
    }

    /// <summary>
    /// 获取当前等级升到下一级所需经验。
    /// </summary>
    private float GetExpToNextLevel(int currentLevel)
    {
        int index = currentLevel - 1;
        if (index < 0 || index >= _expToNext.Length)
        {
            return float.MaxValue;
        }
        return _expToNext[index];
    }

    /// <summary>
    /// 处理升级：等级 +1，回复 2 点生命，攻击伤害 +0.5。
    /// </summary>
    private void LevelUp()
    {
        level++;

        // 提高 2 点生命值上限，同时增加 2 点当前生命值
        if (_playerHealth != null)
        {
            _playerHealth.maxHealth += 2f;
            _playerHealth.Heal(2f);
        }

        // 攻击伤害 +0.5（永久累加）
        if (_playerAttack != null)
        {
            _playerAttack.attackDamage += 0.5f;
        }
    }

    private void RefreshUI()
    {
        // 更新经验条
        if (expFillImage != null)
        {
            if (level >= maxLevel)
            {
                // 满级经验条拉满
                expFillImage.fillAmount = 1f;
            }
            else
            {
                float need = GetExpToNextLevel(level);
                float ratio = need > 0 ? (float)currentExp / need : 0f;
                expFillImage.fillAmount = Mathf.Clamp01(ratio);
            }
        }

        // 更新等级文字
        if (levelText != null)
        {
            levelText.text = $"Lv.{level}";
        }

        // 更新经验数值文字（如 0/7 或 Lv.Max）
        if (expText != null)
        {
            if (level >= maxLevel)
            {
                expText.text = "Lv.Max";
            }
            else
            {
                float needExp = GetExpToNextLevel(level);
                // 显示浮点数，保留1位小数
                expText.text = $"{currentExp:F1}/{needExp:F1}";
            }
        }
    }
}

