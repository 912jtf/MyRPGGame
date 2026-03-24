using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class PlayerHealth : MonoBehaviour
{
    [Header("生命设置")]
    public float maxHealth = 3f;

    [Header("UI 引用")]
    public Image healthFillImage;   // 预留 Image，通过 fillAmount 显示血量
    public TMPro.TMP_Text healthText;  // 血量文字（显示如 100/100）

    [Header("受击表现")]
    public Color hurtColor = Color.red;
    public float hurtFlashTime = 0.1f;
    [Header("死亡表现")]
    [Tooltip("玩家死亡时切换到该图片（建议拖 Dead_9）。")]
    public Sprite deadSprite;
    [Tooltip("如果未手动拖 deadSprite，则尝试按名称自动查找。")]
    public string deadSpriteName = "Dead_9";
    [Header("失败滤镜（可选）")]
    [Tooltip("玩家死亡后显示全屏灰色滤镜。")]
    public bool showGameOverGrayOverlay = true;
    [Tooltip("灰色滤镜颜色与透明度。")]
    public Color gameOverOverlayColor = new Color(0.2f, 0.2f, 0.2f, 0.58f);
    [Tooltip("失败标题文字。")]
    public string gameOverTitle = "Game Over";
    [Tooltip("按钮宽高。")]
    public Vector2 gameOverButtonSize = new Vector2(300f, 78f);
    [Header("胜负判定（矿点）")]
    [Tooltip("对局总时长（秒）。在该时间内把金矿回满即胜利。")]
    public float matchDurationSeconds = 90f;
    [Tooltip("留空时自动查找场景中的 GoldMineController。")]
    public GoldMineController goldMineController;
    [Tooltip("失败标题。")]
    public string failTitle = "Game Over";
    [Tooltip("成功标题。")]
    public string successTitle = "Victory!";
    [Tooltip("成功标题颜色。")]
    public Color successTitleColor = new Color(1f, 0.92f, 0.3f, 1f);
    [Tooltip("标题呼吸动画速度。")]
    public float titlePulseSpeed = 4.2f;
    [Tooltip("标题呼吸动画缩放幅度。")]
    public float titlePulseScaleAmount = 0.08f;
    [Header("倒计时 UI（可选）")]
    [Tooltip("显示顶部倒计时文本。")]
    public bool showCountdownText = true;
    [Tooltip("可手动指定倒计时文本；留空则运行时自动创建。")]
    public TextMeshProUGUI countdownText;
    [Tooltip("当 countdownText 为空时，按该名字在场景中自动查找 UI 文本。")]
    public string countdownTextObjectName = "daojishi";
    [Tooltip("倒计时文本颜色。")]
    public Color countdownTextColor = Color.white;
    [Tooltip("倒计时字体大小。")]
    public float countdownFontSize = 44f;
    [Tooltip("倒计时在 Canvas 上的锚点坐标（当前以上中为锚点）。")]
    public Vector2 countdownAnchoredPosition = new Vector2(0f, -22f);
    [Tooltip("若能找到该 UI 名称，则倒计时会跟随它显示在其下方。")]
    public string countdownFollowTargetName = "ExperienceBar";
    [Tooltip("相对跟随目标的位置偏移（通常 Y 为负表示在下方）。")]
    public Vector2 countdownFollowOffset = new Vector2(0f, -48f);
    [Tooltip("倒计时文字整体透明度。")]
    [Range(0f, 1f)] public float countdownBaseAlpha = 0.78f;
    [Tooltip("启用倒计时呼吸感（缩放+透明度脉冲）。")]
    public bool countdownUrgencyPulse = true;
    [Tooltip("呼吸速度。")]
    public float countdownPulseSpeed = 3.8f;
    [Tooltip("呼吸缩放幅度。")]
    public float countdownPulseScaleAmount = 0.06f;
    [Tooltip("呼吸透明波动幅度。")]
    [Range(0f, 1f)] public float countdownPulseAlphaAmount = 0.18f;

    [Header("事件（可选）")]
    public UnityEvent onPlayerDied;

    public float CurrentHealth => currentHealth;
    public bool IsDead => _isDead;
    public event Action Died;

    private float currentHealth;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private bool _isDead;
    GameObject _gameOverOverlay;
    bool _matchEnded;
    float _matchRemainingTime;
    TextMeshProUGUI _resultTitleText;
    Vector3 _resultTitleBaseScale;
    Coroutine _titlePulseRoutine;

    private void Awake()
    {
        currentHealth = maxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        if (deadSprite == null && !string.IsNullOrWhiteSpace(deadSpriteName))
            deadSprite = TryFindSpriteByName(deadSpriteName);

        // 如果在 Prefab 上没法手动拖引用，尝试在场景中自动寻找血条 Image
        if (healthFillImage == null)
        {
            // 直接通过名字查找 "HealthBar"（敌人的叫 "EnemyHealthBar"，所以不会冲突）
            GameObject bar = GameObject.Find("HealthBar");

            if (bar != null)
            {
                healthFillImage = bar.GetComponent<Image>();
            }
        }

        // 自动查找血量文字（如果没有手动赋值）
        // 必须改名为 "PlayerHealthText" 避免和敌人血量条冲突
        if (healthText == null)
        {
            GameObject textObj = GameObject.Find("PlayerHealthText");
            if (textObj != null)
            {
                healthText = textObj.GetComponent<TMPro.TMP_Text>();
            }
        }

        UpdateHealthUI();
        _matchRemainingTime = Mathf.Max(1f, matchDurationSeconds);
        if (goldMineController == null)
            goldMineController = FindObjectOfType<GoldMineController>();
        if (goldMineController != null)
            goldMineController.GoldChanged += OnMineGoldChanged;

        if (showCountdownText)
        {
            EnsureCountdownText();
            RefreshCountdownText();
        }
    }

    private void OnDestroy()
    {
        if (goldMineController != null)
            goldMineController.GoldChanged -= OnMineGoldChanged;
    }

    private void Update()
    {
        if (_matchEnded || _isDead)
            return;

        _matchRemainingTime -= Time.deltaTime;
        if (showCountdownText)
            RefreshCountdownText();
        if (_matchRemainingTime <= 0f)
        {
            _matchRemainingTime = 0f;
            if (showCountdownText)
                RefreshCountdownText();
            EndMatch(false, failTitle);
        }
    }

    public void TakeDamage(float damage)
    {
        if (_isDead || damage <= 0f)
            return;

        currentHealth -= damage;
        if (currentHealth < 0)
            currentHealth = 0;

        // 受击闪红
        if (spriteRenderer != null)
        {
            spriteRenderer.color = hurtColor;
            CancelInvoke(nameof(ResetColor));
            Invoke(nameof(ResetColor), hurtFlashTime);
        }

        UpdateHealthUI();

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void ResetColor()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }
    }

    private void UpdateHealthUI()
    {
        // 更新血量条
        if (healthFillImage != null && maxHealth > 0)
        {
            healthFillImage.fillAmount = currentHealth / maxHealth;
        }

        // 更新血量文字（显示浮点数）
        if (healthText != null)
        {
            healthText.text = $"{currentHealth:F1}/{maxHealth:F1}";
        }
    }

    private void Die()
    {
        if (_isDead)
            return;
        _isDead = true;

        if (spriteRenderer != null && deadSprite != null)
        {
            // 死亡后固定到 Dead 图，避免继续被动画覆盖。
            Animator anim = GetComponent<Animator>();
            if (anim != null)
                anim.enabled = false;
            spriteRenderer.sprite = deadSprite;
            spriteRenderer.color = Color.white;
        }

        EndMatch(false, failTitle);

        // TODO: 播放死亡动画 / 复活逻辑等
        Debug.Log("Player died.");
        Died?.Invoke();
        onPlayerDied?.Invoke();
    }

    /// <summary>
    /// 为玩家回复生命值，最多不超过 maxHealth。
    /// </summary>
    /// <param name="amount">回复量（正数有效）</param>
    public void Heal(float amount)
    {
        if (_isDead || amount <= 0) return;

        currentHealth += amount;
        if (currentHealth > maxHealth)
        {
            currentHealth = maxHealth;
        }

        UpdateHealthUI();
    }

    Sprite TryFindSpriteByName(string spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
            return null;

        Sprite[] allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int i = 0; i < allSprites.Length; i++)
        {
            Sprite s = allSprites[i];
            if (s == null) continue;
            if (s.name == spriteName)
                return s;
        }
        return null;
    }

    void DisablePlayerControl()
    {
        // 按当前项目实际脚本禁用操作输入。
        PlayerMovement move = GetComponent<PlayerMovement>();
        if (move != null) move.enabled = false;

        PlayerAttack attack = GetComponent<PlayerAttack>();
        if (attack != null) attack.enabled = false;

        PlayerSkills skills = GetComponent<PlayerSkills>();
        if (skills != null) skills.enabled = false;

        PlayerGoldCarrier goldCarrier = GetComponent<PlayerGoldCarrier>();
        if (goldCarrier != null) goldCarrier.enabled = false;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void ShowGameOverOverlay()
    {
        if (_gameOverOverlay != null)
            return;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
            return;

        _gameOverOverlay = new GameObject("GameOverGrayOverlay");
        _gameOverOverlay.transform.SetParent(canvas.transform, false);

        RectTransform rt = _gameOverOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = _gameOverOverlay.AddComponent<Image>();
        img.color = gameOverOverlayColor;
        img.raycastTarget = true;

        CreateGameOverUI(rt);
    }

    void CreateGameOverUI(RectTransform root)
    {
        GameObject panel = new GameObject("GameOverPanel");
        panel.transform.SetParent(root, false);
        RectTransform panelRt = panel.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(420f, 320f);
        panelRt.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 16f;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;

        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateTitleText(panel.transform, gameOverTitle);
        CreateActionButton(panel.transform, "Restart", RestartGame);
        CreateActionButton(panel.transform, "Quit", QuitGame);
    }

    void CreateTitleText(Transform parent, string text)
    {
        GameObject titleGo = new GameObject("GameOverText");
        titleGo.transform.SetParent(parent, false);
        RectTransform rt = titleGo.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(520f, 120f);

        TextMeshProUGUI title = titleGo.AddComponent<TextMeshProUGUI>();
        title.text = text;
        title.alignment = TextAlignmentOptions.Center;
        title.fontSize = 82f;
        title.color = Color.white;
        _resultTitleText = title;
        _resultTitleBaseScale = title.rectTransform.localScale;
    }

    void CreateActionButton(Transform parent, string label, UnityAction onClick)
    {
        GameObject btnGo = new GameObject(label + "Button");
        btnGo.transform.SetParent(parent, false);
        RectTransform btnRt = btnGo.AddComponent<RectTransform>();
        btnRt.sizeDelta = gameOverButtonSize;

        Image bg = btnGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);

        Button btn = btnGo.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 0.95f);
        cb.highlightedColor = new Color(1f, 1f, 1f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.selectedColor = cb.highlightedColor;
        cb.disabledColor = new Color(1f, 1f, 1f, 0.4f);
        btn.colors = cb;
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick);

        GameObject txtGo = new GameObject("Label");
        txtGo.transform.SetParent(btnGo.transform, false);
        RectTransform txtRt = txtGo.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;

        TextMeshProUGUI txt = txtGo.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontSize = 42f;
        txt.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        txt.raycastTarget = false;
    }

    void RestartGame()
    {
        Time.timeScale = 1f;
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.buildIndex);
    }

    void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnMineGoldChanged(int current, int max)
    {
        if (_matchEnded)
            return;

        if (current <= 0)
        {
            EndMatch(false, failTitle);
            return;
        }

        if (max > 0 && current >= max && _matchRemainingTime > 0f)
        {
            EndMatch(true, successTitle);
        }
    }

    void EndMatch(bool isSuccess, string title)
    {
        if (_matchEnded)
            return;
        _matchEnded = true;

        DisablePlayerControl();
        if (showGameOverGrayOverlay)
            ShowGameOverOverlay();

        ApplyResultTitleVisual(isSuccess, title);
        if (countdownText != null)
            countdownText.gameObject.SetActive(false);
    }

    void ApplyResultTitleVisual(bool isSuccess, string title)
    {
        if (_resultTitleText == null)
            return;
        _resultTitleText.text = string.IsNullOrWhiteSpace(title) ? gameOverTitle : title;
        _resultTitleText.color = isSuccess ? successTitleColor : Color.white;
        if (_titlePulseRoutine != null)
            StopCoroutine(_titlePulseRoutine);
        _titlePulseRoutine = StartCoroutine(CoPulseResultTitle());
    }

    IEnumerator CoPulseResultTitle()
    {
        if (_resultTitleText == null)
            yield break;

        while (_resultTitleText != null && _gameOverOverlay != null)
        {
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * titlePulseSpeed) * titlePulseScaleAmount;
            _resultTitleText.rectTransform.localScale = _resultTitleBaseScale * pulse;
            yield return null;
        }
    }

    void EnsureCountdownText()
    {
        if (countdownText != null)
            return;

        if (!string.IsNullOrWhiteSpace(countdownTextObjectName))
        {
            GameObject existing = GameObject.Find(countdownTextObjectName);
            if (existing != null)
            {
                TextMeshProUGUI existingTmp = existing.GetComponent<TextMeshProUGUI>();
                if (existingTmp != null)
                {
                    countdownText = existingTmp;
                    return;
                }
            }

            // 兼容：如果对象是 inactive，GameObject.Find 找不到，这里用全量 TMP 扫描补救。
            TextMeshProUGUI[] allTmp = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            for (int i = 0; i < allTmp.Length; i++)
            {
                TextMeshProUGUI t = allTmp[i];
                if (t == null) continue;
                if (t.gameObject == null) continue;
                if (!t.gameObject.scene.IsValid()) continue; // 排除项目资源/Prefab 资产
                if (t.gameObject.name != countdownTextObjectName) continue;
                countdownText = t;
                break;
            }

            if (countdownText != null)
                return;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
            return;

        GameObject go = new GameObject(string.IsNullOrWhiteSpace(countdownTextObjectName) ? "MatchCountdownText" : countdownTextObjectName);
        go.transform.SetParent(canvas.transform, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = countdownAnchoredPosition;
        rt.sizeDelta = new Vector2(520f, 80f);

        countdownText = go.AddComponent<TextMeshProUGUI>();
        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.raycastTarget = false;
        Debug.Log($"[PlayerHealth] Auto-created countdown text: {go.name}");
    }

    void RefreshCountdownText()
    {
        if (countdownText == null)
        {
            Debug.LogWarning("[PlayerHealth] countdownText is null, cannot update countdown.");
            return;
        }

        int sec = Mathf.CeilToInt(Mathf.Max(0f, _matchRemainingTime));
        RectTransform rt = countdownText.rectTransform;
        bool isBoundToNamedCountdown = !string.IsNullOrWhiteSpace(countdownTextObjectName) &&
                                       countdownText.gameObject.name == countdownTextObjectName;
        bool shouldDrivePosition = !isBoundToNamedCountdown;
        Vector2 pos = countdownAnchoredPosition;
        if (shouldDrivePosition && !string.IsNullOrWhiteSpace(countdownFollowTargetName))
        {
            GameObject target = GameObject.Find(countdownFollowTargetName);
            if (target != null)
            {
                RectTransform targetRt = target.GetComponent<RectTransform>();
                RectTransform parentRt = rt.parent as RectTransform;
                if (targetRt != null && parentRt != null)
                {
                    Vector2 anchored;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parentRt,
                        RectTransformUtility.WorldToScreenPoint(null, targetRt.position),
                        null,
                        out anchored))
                    {
                        pos = anchored + countdownFollowOffset;
                    }
                }
            }
        }
        if (shouldDrivePosition)
            rt.anchoredPosition = pos;
        if (!countdownText.gameObject.activeSelf)
            countdownText.gameObject.SetActive(true);
        countdownText.fontSize = countdownFontSize;

        float pulseScale = 1f;
        float pulseAlphaMul = 1f;
        if (countdownUrgencyPulse)
        {
            float s = Mathf.Sin(Time.unscaledTime * countdownPulseSpeed);
            pulseScale = 1f + s * countdownPulseScaleAmount;
            pulseAlphaMul = 1f - Mathf.Abs(s) * countdownPulseAlphaAmount;
        }
        rt.localScale = new Vector3(pulseScale, pulseScale, 1f);

        Color c = countdownTextColor;
        c.a = Mathf.Clamp01(countdownBaseAlpha * pulseAlphaMul);
        countdownText.color = c;
        countdownText.text = $"Time Left: {sec}s";
    }
}

