using System;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class PlayerHealth : NetworkBehaviour
{
    // 只允许一个服务端实例负责“倒计时/胜负判断”（避免每个玩家都在倒计时导致不同步）
    static PlayerHealth s_serverMatchController;
    static bool s_serverMatchEndedGuard;

    // 缓存玩家列表，避免服务器每帧 FindObjectsOfType 卡死
    static PlayerHealth[] s_cachedPlayers;
    static float s_cachedPlayersUntil;
    static float s_cachedPlayersRefreshInterval = 1f;

    [Header("生命设置")]
    public float maxHealth = 3f;

    [Header("UI 引用")]
    public Image healthFillImage;   // 预留 Image，通过 fillAmount 显示血量
    public TMPro.TMP_Text healthText;  // 血量文字（显示如 100/100）

    [Header("受击表现")]
    public Color hurtColor = Color.red;
    public float hurtFlashTime = 0.1f;
    [Header("音效（拖入 AudioClip；留空则静音）")]
    [Tooltip("本地玩家受伤时播放（联机时仅本机听到）")]
    public AudioClip hurtSfx;
    [Range(0f, 1f)] public float hurtSfxVolume = 1f;
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

    [SyncVar(hook = nameof(OnCurrentHealthNetChanged))]
    private float currentHealth;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private bool _isDead;
    GameObject _gameOverOverlay;
    [SyncVar(hook = nameof(OnMatchEndedNetChanged))]
    bool _matchEnded;
    [SyncVar]
    float _matchRemainingTime;
    [SyncVar]
    bool _matchIsSuccess;
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

        // 自动绑定：优先绑定到“自己身上”的 UI，避免 host/client 两个玩家互相改同一份 UI
        if (healthFillImage == null)
        {
            Transform barTr = transform.Find("HealthBar");
            if (barTr != null)
                healthFillImage = barTr.GetComponent<Image>();

            if (healthFillImage == null)
            {
                Image[] imgs = GetComponentsInChildren<Image>(true);
                for (int i = 0; i < imgs.Length; i++)
                {
                    if (imgs[i] != null && imgs[i].name == "HealthBar")
                    {
                        healthFillImage = imgs[i];
                        break;
                    }
                }
            }

            // 最后兜底：退回到全局查找（尽量避免）
            if (healthFillImage == null)
            {
                GameObject bar = GameObject.Find("HealthBar");
                if (bar != null)
                    healthFillImage = bar.GetComponent<Image>();
            }
        }

        if (healthText == null)
        {
            Transform textTr = transform.Find("PlayerHealthText");
            if (textTr != null)
                healthText = textTr.GetComponent<TMPro.TMP_Text>();

            if (healthText == null)
            {
                TMPro.TMP_Text[] texts = GetComponentsInChildren<TMPro.TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != null && texts[i].name == "PlayerHealthText")
                    {
                        healthText = texts[i];
                        break;
                    }
                }
            }

            if (healthText == null)
            {
                GameObject textObj = GameObject.Find("PlayerHealthText");
                if (textObj != null)
                    healthText = textObj.GetComponent<TMPro.TMP_Text>();
            }
        }

        UpdateHealthUI();
    }

    private void OnDestroy()
    {
        if (isServer && goldMineController != null)
            goldMineController.GoldChanged -= OnMineGoldChanged;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _matchRemainingTime = Mathf.Max(1f, matchDurationSeconds);
        _matchEnded = false;
        _matchIsSuccess = false;

        // 只让第一个进入 OnStartServer 的玩家对象作为“服务端倒计时控制器”
        if (s_serverMatchController == null)
            s_serverMatchController = this;

        if (this == s_serverMatchController)
        {
            s_serverMatchEndedGuard = false;

            if (goldMineController == null)
                goldMineController = FindObjectOfType<GoldMineController>();

            if (goldMineController != null)
            {
                goldMineController.GoldChanged += OnMineGoldChanged;
                // 初始同步一次金量，保证客户端 UI 与 host 一致
                RpcSyncMineGold(goldMineController.CurrentGold, goldMineController.maxGold);
            }
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (showCountdownText && isLocalPlayer)
        {
            EnsureCountdownText();
            RefreshCountdownText();
        }
    }

    private void Update()
    {
        if (_matchEnded)
            return;

        if (isServer)
        {
            // 仅控制器实例负责倒计时，并同步到所有玩家实例，保证客户端倒计时一致
            if (this == s_serverMatchController)
            {
                _matchRemainingTime -= Time.deltaTime;

                if (_matchRemainingTime <= 0f)
                    _matchRemainingTime = 0f;

                PlayerHealth[] all = GetServerPlayersCache();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    all[i]._matchRemainingTime = _matchRemainingTime;
                }

                if (_matchRemainingTime <= 0f)
                    EndMatchServer(false);
            }
        }

        if (showCountdownText && isLocalPlayer)
            RefreshCountdownText();
    }

    public void TakeDamage(float damage)
    {
        if (!isServer)
            return;

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

        NetworkIdentity ni = GetComponent<NetworkIdentity>();
        if (ni == null || ni.isLocalPlayer)
            CombatSfxUtil.Play2D(hurtSfx, transform.position, hurtSfxVolume);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void OnCurrentHealthNetChanged(float oldValue, float newValue)
    {
        // 只有 UI/表现需要在客户端同步；服务器逻辑仍由 TakeDamage/Die 处理
        UpdateHealthUI();

        // 客户端受击表现：当血量下降时，触发红色闪烁
        if (!isServer && newValue < oldValue - 0.0001f)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = hurtColor;
                CancelInvoke(nameof(ResetColor));
                Invoke(nameof(ResetColor), hurtFlashTime);
            }
        }

        // 服务器上死亡/胜负判定由 Die() 统一处理，避免 hook 抢先把 _isDead 置为 true，
        // 导致 Die() 早退从而不触发“所有玩家死亡”的胜负逻辑。
        if (isServer)
            return;

        if (newValue <= 0f && !_isDead)
        {
            // 客户端仅切换死亡表现（不触发胜负判定）
            _isDead = true;
            if (spriteRenderer != null && deadSprite != null)
            {
                Animator anim = GetComponent<Animator>();
                if (anim != null)
                    anim.enabled = false;
                spriteRenderer.sprite = deadSprite;
                spriteRenderer.color = Color.white;
            }
        }
    }

    void OnMatchEndedNetChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
            return;

        // 同步到每个客户端后，在“本地玩家实例”上禁用操作并弹出结算 UI
        OnMatchEndedClient();
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
        // 场景里通常只有“一份全局玩家血量 UI”（名字叫 HealthBar / PlayerHealthText）。
        // 为了让 host/client 两边的血条不会互相被远端玩家覆盖：只让本地玩家实例写 UI。
        if (NetworkClient.active && !isLocalPlayer)
            return;

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

        // TODO: 播放死亡动画 / 复活逻辑等
        Debug.Log("Player died.");
        Died?.Invoke();
        onPlayerDied?.Invoke();

        // 胜负判定：只有“所有玩家都死亡”才失败
        if (isServer)
        {
            TryEndMatchIfAllPlayersDead();
        }
    }

    void TryEndMatchIfAllPlayersDead()
    {
        if (_matchEnded)
            return;

        PlayerHealth[] all = GetServerPlayersCache();
        int alive = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if (!all[i]._isDead)
                alive++;
        }

        if (alive <= 0)
        {
            EndMatchServer(false);
        }
    }

    /// <summary>
    /// 为玩家回复生命值，最多不超过 maxHealth。
    /// </summary>
    /// <param name="amount">回复量（正数有效）</param>
    public void Heal(float amount)
    {
        if (!isServer)
            return;
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
        if (!isServer)
            return;
        if (this != s_serverMatchController)
            return;
        if (_matchEnded)
            return;

        // 同步金矿数值到所有客户端，保证 UI 也跟随变化
        RpcSyncMineGold(current, max);

        if (current <= 0)
        {
            EndMatchServer(false);
            return;
        }

        if (max > 0 && current >= max && _matchRemainingTime > 0f)
        {
            EndMatchServer(true);
        }
    }

    [ClientRpc]
    void RpcSyncMineGold(int current, int max)
    {
        // 避免 host（Server+Client）下出现递归：
        // OnMineGoldChanged(服务器) -> RpcSyncMineGold(Host本地执行) -> SetGoldNetwork -> GoldChanged -> OnMineGoldChanged ...
        if (isServer)
            return;

        if (goldMineController == null)
            goldMineController = FindObjectOfType<GoldMineController>();

        if (goldMineController != null)
            goldMineController.SetGoldNetwork(current, max);
    }

    void EndMatchServer(bool isSuccess)
    {
        if (!isServer)
            return;
        if (s_serverMatchEndedGuard || _matchEnded)
            return;

        s_serverMatchEndedGuard = true;

        // 将胜负结果同步给所有玩家对象，保证每个客户端的“本地玩家实例”都会触发胜负 UI。
        PlayerHealth[] all = GetServerPlayersCache();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            all[i]._matchEnded = true;
            all[i]._matchIsSuccess = isSuccess;
            all[i]._matchRemainingTime = 0f;
        }

        // 服务器当前对象也可以立即更新（host 模式）
        OnMatchEndedClient();
    }

    static PlayerHealth[] GetServerPlayersCache()
    {
        if (s_cachedPlayers != null && Time.time < s_cachedPlayersUntil)
            return s_cachedPlayers;

        s_cachedPlayers = FindObjectsOfType<PlayerHealth>();
        s_cachedPlayersUntil = Time.time + Mathf.Max(0.1f, s_cachedPlayersRefreshInterval);
        return s_cachedPlayers;
    }

    void OnMatchEndedClient()
    {
        // 只在本地玩家上禁用操作并显示 UI
        if (!isLocalPlayer)
            return;

        DisablePlayerControl();
        if (showGameOverGrayOverlay)
            ShowGameOverOverlay();

        bool isSuccess = _matchIsSuccess;
        string title = isSuccess ? successTitle : failTitle;

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

