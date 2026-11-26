using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System;
using System.Collections.Generic;


namespace _DroppyTower
{
    public class UIManager : MonoBehaviour
    {
        [Header("Object References")]
        public GameObject mainCanvas;
        public GameObject characterSelectionUI;
        public GameObject header;
        public GameObject title;
        public Text score;
        public Text bestScore;
        public Text coinText;
        public GameObject life;
        public GameObject heart;
        public GameObject newBestScore;
        public GameObject playBtn;
        public GameObject restartBtn;
        public GameObject menuButtons;
        public GameObject dailyRewardBtn;
        public Text dailyRewardBtnText;
        public GameObject rewardUI;
        public GameObject settingsUI;
        public GameObject soundOnBtn;
        public GameObject soundOffBtn;
        public GameObject musicOnBtn;
        public GameObject musicOffBtn;

        [Header("Premium Features Buttons")]
        public GameObject watchRewardedAdBtn;

        [Tooltip("可选：另一个「激励」按钮（名字为 jiliBtn / JiLiBtn 的物体）。留空时运行时会自动按名字查找；找到后自动接上激励视频")]
        public GameObject jiliBtn;

        [Tooltip("侧边栏悬浮按钮。留空会自动在场景里按名字 cebianBtn 查找")]
        public GameObject cebianBtn;

        [Tooltip("看完激励视频发放的金币数（与 PremiumFeaturesManager 里的 rewardedCoins 保持一致）")]
        public int rewardedAdCoins = 50;

        [Header("插屏广告")]
        [Tooltip("打开设置面板时是否播放插屏广告")]
        public bool showInterstitialOnSettings = true;

        [Tooltip("已废弃：插屏频控改由 TikTokPlatform 按「真实展示时间」控制（SDK 要求 ≥30 秒），这个值不再生效")]
        public float interstitialInterval = 60f;

        [Header("游戏结束插屏（替代原 AdDisplayer 的每 N 局一插屏）")]
        [Tooltip("每 N 局结束弹一次插屏，0 表示关闭")]
        public int gamesPerInterstitial = 3;
        [Tooltip("游戏结束后延迟多少秒再弹插屏")]
        public float gameOverInterstitialDelay = 2f;
        int gamesSinceInterstitial;

        public GameObject leaderboardBtn;
        public GameObject achievementBtn;
        public GameObject shareBtn;
        public GameObject iapPurchaseBtn;
        public GameObject removeAdsBtn;
        public GameObject restorePurchaseBtn;

        [Header("In-App Purchase Store")]
        public GameObject storeUI;

        [Header("Sharing-Specific")]
        public GameObject shareUI;
        public ShareUIController shareUIController;

        Animator scoreAnimator;
        Animator dailyRewardAnimator;
        bool isWatchAdsForCoinBtnActive;
        private List<GameObject> _heart;
        private Dictionary<string, GameObject> _sceneLookup;
        private bool jiliBtnBound;

        // 悬浮按钮让位：面板打开时隐藏 jiliBtn / cebianBtn，关掉后恢复
        private GameObject cebianPanelRoot;
        private bool sideButtonsHidden;
        // 激励按钮「业务上该不该显示」：由 SetWatchAdBtnActive 维护，
        // 面板关闭时按这个值恢复，而不是按「隐藏前的快照」——快照在游戏结束那种
        // 「先隐藏再立刻切界面」的时序里会记成 false，导致按钮再也不出来。
        // 默认按「显示」算：主界面本来就该有激励按钮，
        // 初始值给 false 的话，第一次因面板让位被隐藏后就会恢复成隐藏，按钮再也回不来。
        private bool jiliBtnWanted = true;
        private float nextSideButtonCheck;
        private int lifeCount = 0;
        private GameState GameState = GameState.Prepare;

        void OnEnable()
        {
            PlayerController.PlayerLostLife += PlayerLostLife;
            GameManager.GameStateChanged += GameManager_GameStateChanged;
            ScoreManager.ScoreUpdated += OnScoreUpdated;
        }

        void OnDisable()
        {
            PlayerController.PlayerLostLife -= PlayerLostLife;
            GameManager.GameStateChanged -= GameManager_GameStateChanged;
            ScoreManager.ScoreUpdated -= OnScoreUpdated;
        }

        // Use this for initialization
        void Start()
        {
            _heart = new List<GameObject>();

            // 先把可能在 Inspector 里丢掉的引用兜底找回来，再取组件，避免空引用直接抛异常
            EnsureReferences();

            if (score != null)
                scoreAnimator = score.GetComponent<Animator>();
            if (dailyRewardBtn != null)
                dailyRewardAnimator = dailyRewardBtn.GetComponent<Animator>();

            ResetUI();
            ShowStartUI();

            // 审核驳回项：隐藏所有「点了没反应」和带第三方品牌图标的入口。
            // 放在 ShowStartUI 之后，避免它把 MenuButtons 子物体重新激活。
            HideNonFunctionalEntries();
        }

        // Update is called once per frame
        void Update()
        {
            if (score != null) score.text = ScoreManager.Instance.Score.ToString();
            if (bestScore != null) bestScore.text = ScoreManager.Instance.HighScore.ToString();
            if (coinText != null) coinText.text = CoinManager.Instance.Coins.ToString();

            if (!DailyRewardController.Instance.disable && dailyRewardBtn != null && dailyRewardBtn.activeInHierarchy)
            {
                if (dailyRewardBtnText != null)
                {
                    if (DailyRewardController.Instance.CanRewardNow())
                    {
                        dailyRewardBtnText.text = "领取奖励！";
                    }
                    else
                    {
                        TimeSpan timeToReward = DailyRewardController.Instance.TimeUntilReward;
                        dailyRewardBtnText.text = string.Format("{0:00}:{1:00}:{2:00} 后领取", timeToReward.Hours, timeToReward.Minutes, timeToReward.Seconds);
                    }
                }

                if (dailyRewardAnimator != null)
                {
                    if (DailyRewardController.Instance.CanRewardNow())
                        dailyRewardAnimator.SetTrigger("activate");
                    else
                        dailyRewardAnimator.SetTrigger("deactivate");
                }
            }

            if (settingsUI != null && settingsUI.activeSelf)
            {
                UpdateSoundButtons();
                UpdateMusicButtons();
            }

            // 面板打开时让两个悬浮按钮让位，避免压住面板内容（0.2 秒轮询一次，开销可忽略）
            if (Time.unscaledTime >= nextSideButtonCheck)
            {
                nextSideButtonCheck = Time.unscaledTime + 0.2f;
                RefreshSideButtons();
            }
        }

        /// <summary>
        /// jiliBtn / cebianBtn 是 MainCanvas 的最后几个子节点，层级比面板高，
        /// 面板一打开它们就压在最上面。这里只在面板打开期间把它们藏起来，
        /// 面板关掉后按「业务意愿」恢复：cebianBtn 直接回来，jiliBtn 看 jiliBtnWanted。
        /// 不用「隐藏前的快照」，否则游戏结束那种先隐藏再立刻切界面的时序会把状态记错。
        /// </summary>
        void RefreshSideButtons()
        {
            // 注意：这里刻意不把 shareUI（ShareUI_Portrait）算进来。
            // 它的 Container 固定在屏幕左侧（anchorMin/Max.x = 0、anchoredPosition.x = 32、宽 204），
            // 而 jiliBtn / cebianBtn 都贴在右上（anchor.x = 1），两者完全不重叠，不需要让位。
            // 更关键的是这个分享面板没有关闭按钮、HideShareUI 也没有被任何按钮绑定，
            // 一旦把它算成「面板打开」，两个按钮就会被永久藏起来，看起来就是「按钮消失了」。
            bool panelOpen =
                IsOpen(settingsUI) ||
                IsOpen(storeUI) ||
                IsOpen(rewardUI) ||
                IsOpen(cebianPanelRoot);

            if (panelOpen)
            {
                if (!sideButtonsHidden)
                {
                    sideButtonsHidden = true;
                    Act(jiliBtn, false);
                    Act(cebianBtn, false);
                }
                return;
            }

            if (!sideButtonsHidden) return;

            sideButtonsHidden = false;
            // 侧边按钮没有任何别的隐藏来源，面板一关就一定回来
            // （cebianBtn 常显：点它打开 cebianPanel，面板里的 go 按钮再跳抖音侧边栏）
            Act(cebianBtn, true);
            // 激励按钮按业务意愿恢复：游戏进行中该藏就藏，回主界面/游戏结束该显示就显示
            Act(jiliBtn, jiliBtnWanted);
            // 重新激活后补播一次入场动画，避免 Animator 停在 deactivate 状态看着像消失了
            if (jiliBtnWanted) PlayWatchAdBtnAnim();
        }

        static bool IsOpen(GameObject go)
        {
            return go != null && go.activeInHierarchy;
        }

        void GameManager_GameStateChanged(GameState newState, GameState oldState)
        {
            if (newState == GameState.Playing)
            {
                GameState = GameState.Playing;
                ShowGameUI();
            }
            else if (newState == GameState.PreGameOver)
            {
                // Before game over, i.e. game potentially will be recovered
            }
            else if (newState == GameState.GameOver)
            {
                GameState = GameState.GameOver;
                lifeCount = 0;
                Invoke("ShowGameOverUI", 0.1f);
            }
        }

        void OnScoreUpdated(int newScore)
        {
            if (scoreAnimator != null)
                scoreAnimator.Play("NewScore");
        }

        void PlayerLostLife()
        {
            if (_heart != null && lifeCount >= 0 && lifeCount < _heart.Count)
                Destroy(_heart[lifeCount]);
            lifeCount++;
        }

        /// <summary>
        /// 注意：这里刻意不再叫 Reset()。
        /// Reset() 是 Unity 的编辑器消息方法，往 GameObject 上挂组件或在 Inspector 里点 Reset 时会被调用，
        /// 那时所有引用都还没赋值，必然抛 UnassignedReferenceException，并且会把 Inspector 里的引用清空。
        /// </summary>
        void ResetUI()
        {
            if (mainCanvas != null) mainCanvas.SetActive(true);
            if (characterSelectionUI != null) characterSelectionUI.SetActive(false);
            if (header != null) header.SetActive(false);
            if (title != null) title.SetActive(false);
            if (score != null) score.gameObject.SetActive(false);
            if (newBestScore != null) newBestScore.SetActive(false);
            if (playBtn != null) playBtn.SetActive(false);
            if (menuButtons != null) menuButtons.SetActive(false);
            if (dailyRewardBtn != null) dailyRewardBtn.SetActive(false);

            // Enable or disable premium stuff
            bool enablePremium = IsPremiumFeaturesEnabled();
            if (leaderboardBtn != null) leaderboardBtn.SetActive(enablePremium);
            if (shareBtn != null) shareBtn.SetActive(enablePremium);
            if (iapPurchaseBtn != null) iapPurchaseBtn.SetActive(enablePremium);
            if (removeAdsBtn != null) removeAdsBtn.SetActive(enablePremium);
            if (restorePurchaseBtn != null) restorePurchaseBtn.SetActive(enablePremium);

            // Hidden by default
            if (storeUI != null) storeUI.SetActive(false);
            if (settingsUI != null) settingsUI.SetActive(false);
            if (shareUI != null) shareUI.SetActive(false);

            // These premium feature buttons are hidden by default
            // and shown when certain criteria are met (e.g. rewarded ad is loaded)
            SetWatchAdBtnActive(false);
        }

        /// <summary>
        /// 编辑器里引用丢失时（例如点了组件 Reset、场景被重新序列化）在运行时自动找回来。
        /// </summary>
        void EnsureReferences()
        {
            // 名字都取自 Main.unity 里实际的物体名，引用丢了也能在运行时找回来
            if (mainCanvas == null) mainCanvas = FindInScene("MainCanvas");
            if (header == null) header = FindInScene("Header");
            if (title == null) title = FindInScene("Title");
            if (newBestScore == null) newBestScore = FindInScene("NewBest");
            if (playBtn == null) playBtn = FindInScene("PlayBtn");
            if (restartBtn == null) restartBtn = FindInScene("RestartBtn");
            if (menuButtons == null) menuButtons = FindInScene("MenuButtons");
            if (dailyRewardBtn == null) dailyRewardBtn = FindInScene("DailyRewardBtn");
            if (rewardUI == null) rewardUI = FindInScene("RewardUI");
            if (settingsUI == null) settingsUI = FindInScene("SettingsUI");
            if (storeUI == null) storeUI = FindInScene("StoreUI");
            if (watchRewardedAdBtn == null) watchRewardedAdBtn = FindInScene("WatchRewardedAdBtn");

            // 可选别名：场景里如果有叫 jiliBtn / JiLiBtn 的「激励」按钮，也当作激励按钮用
            if (jiliBtn == null) jiliBtn = FindInScene("jiliBtn");
            if (jiliBtn == null) jiliBtn = FindInScene("JiLiBtn");
            if (jiliBtn == null) jiliBtn = FindInScene("JiliBtn");
            if (watchRewardedAdBtn == null) watchRewardedAdBtn = jiliBtn;

            // 侧边栏按钮 / 侧边栏面板：都不在 Inspector 里手连，按名字兜底查找
            if (cebianBtn == null) cebianBtn = FindInScene("cebianBtn");
            if (cebianPanelRoot == null) cebianPanelRoot = FindInScene("cebianPanel");

            BindJiliBtn();

            if (score == null) score = FindTextInScene("Score");
            if (bestScore == null) bestScore = FindTextInScene("BestScore");
            if (coinText == null) coinText = FindTextInScene("CoinText");
            if (dailyRewardBtnText == null && dailyRewardBtn != null)
                dailyRewardBtnText = dailyRewardBtn.GetComponentInChildren<Text>(true);

            if (watchRewardedAdBtn == null)
            {
                Debug.LogWarning("[UIManager] 场景里没找到 WatchRewardedAdBtn，看广告按钮将不可用。请检查 MainCanvas 下的按钮是否被删除。");
            }
        }

        Text FindTextInScene(string objectName)
        {
            GameObject go = FindInScene(objectName);
            return go != null ? go.GetComponent<Text>() : null;
        }

        GameObject FindInScene(string objectName)
        {
            // 只建一次索引，避免逐个字段都全场景遍历
            if (_sceneLookup == null)
            {
                _sceneLookup = new Dictionary<string, GameObject>();
                var roots = gameObject.scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    var all = roots[i].GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < all.Length; j++)
                    {
                        var t = all[j];
                        if (t != null && !_sceneLookup.ContainsKey(t.name))
                            _sceneLookup.Add(t.name, t.gameObject);
                    }
                }
            }

            GameObject found;
            return _sceneLookup.TryGetValue(objectName, out found) ? found : null;
        }

        // 统一入口：引用为空时安静跳过，不让整段 UI 逻辑崩掉
        static void Act(GameObject go, bool active)
        {
            if (go != null) go.SetActive(active);
        }

        // ==================== 审核合规（2026-11-23） ====================
        // 抖音审核驳回两条：
        //   ①「部分按钮功能实现效果与相应描述不符，无法实现对应功能」
        //   ②「涉及第三方品牌或其他动画形象，需完善商标/IP 授权」
        //
        // 原因：内购 / 评分 / 社交 / 排行榜 / 成就 这些能力在上一轮清掉第三方 SDK 后都成了空实现
        // （点下去只有一条 Debug.Log），「关注我们」那行还带着别家的商标图标。
        // 所以启动时统一隐藏，只留真正能用的按钮（角色选择 / 设置 / 音效 / 音乐 / 分享 / 领奖 / 看广告）。
        //
        // 以后接入抖音支付、TT.GetImRankList 排行榜、关注抖音号之后，
        // 把对应名字从这个数组里删掉就能恢复，不用改场景。
        static readonly string[] HiddenMenuEntries = { "Leaderboard", "Achievement", "Shop" };
        static readonly string[] HiddenSettingRows = { "Rate", "Twitter", "RemoveAds", "RestorePurchase" };

        // 侧边栏引导面板（cebianPanel）：入口和面板都保留（cebianBtn → 面板 → go 跳抖音侧边栏）。
        // HideCebianGuideStep2 控制是否挡掉「2，点击同款小游戏进入」这一步——那句话是导流，
        // 配图是别家小游戏截图（浮岛高台挑战），是审核驳回②的主要来源。
        // 2026-11-24 按用户要求改为 false，第 2 步恢复显示；提审前建议把图换成自有截图、文案改掉，
        // 改完再决定是否需要重新置 true。
        // 用 static 而不是 const：const false 会让下面的 if 分支变成「无法访问的代码」告警（CS0162）。
        static bool HideCebianGuideStep2 = false;

        void HideNonFunctionalEntries()
        {
            int hidden = 0;

            // 底部菜单栏（HorizontalLayoutGroup，隐藏后剩余按钮会自动居中）
            if (menuButtons != null)
            {
                for (int i = 0; i < HiddenMenuEntries.Length; i++)
                    if (HideChildByName(menuButtons.transform, HiddenMenuEntries[i])) hidden++;
            }

            // 设置面板：条目挂在 SettingsUI/Items 下（VerticalLayoutGroup，会自动重排）
            if (settingsUI != null)
            {
                Transform items = settingsUI.transform.Find("Items");
                Transform parent = items != null ? items : settingsUI.transform;
                for (int i = 0; i < HiddenSettingRows.Length; i++)
                    if (HideChildByName(parent, HiddenSettingRows[i])) hidden++;
            }

            // 商店面板整体不再可达（内购未接入，价格按钮原本只播点击音效）
            Act(storeUI, false);

            // 侧边栏链路：cebianBtn（onClick = SetActive cebianPanel）→ cebianPanel → box/go 跳抖音侧边栏。
            // 按钮和面板都保留，只挡掉「2，点击同款小游戏进入」那一步（导流文案 + 别家游戏截图）。
            SetupCebianPanel();
            if (HideCebianGuideStep2) HideCebianGuideStep2Content(cebianPanelRoot);

            Debug.Log($"[合规] 已隐藏 {hidden} 个无实现入口：排行榜/成就/商店/评分/关注我们/去除广告/恢复购买"
                      + (HideCebianGuideStep2 ? "；侧边栏引导第 2 步（同款小游戏导流 + 第三方截图）" : ""));
        }

        /// <summary>
        /// 保证 cebianPanel 上有 cebian 控制器、并且初始是关闭状态。
        ///
        /// 打开链路用的是场景里现有的绑定（cebianBtn.onClick → cebianPanel.SetActive(true)），不需要改。
        /// 补组件的原因：box/go 和 box/close 的 onClick 在预制体里是空的，cebian.cs 也从没挂到任何物体上，
        /// 所以「前往侧边栏」和「关闭」原本都没有回调。cebian 组件会在 Awake/OnEnable 里自动找按钮绑定。
        /// </summary>
        void SetupCebianPanel()
        {
            if (cebianPanelRoot == null) cebianPanelRoot = FindInScene("cebianPanel");
            if (cebianPanelRoot == null)
            {
                Debug.LogWarning("[侧边栏] 场景里没找到 cebianPanel，跳过初始化");
                return;
            }

            if (cebianPanelRoot.GetComponent<cebian>() == null)
            {
                cebianPanelRoot.AddComponent<cebian>();
                Debug.Log("[侧边栏] 已补挂 cebian 组件（原先未挂载，go/close 按钮没有回调）");
            }

            // 面板默认关闭，由 cebianBtn 打开；避免一进游戏就压着引导层
            Act(cebianPanelRoot, false);
        }

        /// <summary>
        /// 隐藏引导面板第 2 步：「2，点击同款小游戏进入」是导流文案，配图是别家小游戏截图（浮岛高台挑战）。
        /// 想恢复：把 HideCebianGuideStep2 改成 false，但必须先把图换成自己的截图、文案改成自己的玩法说明。
        /// </summary>
        static void HideCebianGuideStep2Content(GameObject panel)
        {
            if (panel == null) return;

            Transform box = panel.transform.Find("box");
            Transform step2 = box != null ? box.Find("Text (Legacy) (1)") : null;
            if (step2 != null)
            {
                step2.gameObject.SetActive(false);
                Debug.Log("[合规] 已隐藏侧边栏引导第 2 步（同款小游戏导流 + 第三方截图）");
            }

            // 兜底：贴图名命中「浮岛/挑战」的 Image 一并关掉
            HideThirdPartyTutorialImage(panel);
        }

        static void HideThirdPartyTutorialImage(GameObject panel)
        {
            if (panel == null) return;
            var images = panel.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                var sp = images[i].sprite;
                if (sp == null) continue;
                if (sp.name.Contains("浮岛") || sp.name.Contains("挑战"))
                {
                    images[i].gameObject.SetActive(false);
                    Debug.Log($"[合规] 已隐藏侧边栏引导图（第三方游戏截图）：{sp.name}");
                }
            }
        }

        static bool HideChildByName(Transform parent, string childName)
        {
            if (parent == null) return false;
            var all = parent.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != parent && all[i].name == childName)
                {
                    all[i].gameObject.SetActive(false);
                    return true;
                }
            }
            Debug.LogWarning($"[合规] 没找到要隐藏的入口：{parent.name}/{childName}（可能已被改名或删除，忽略）");
            return false;
        }

        void SetWatchAdBtnActive(bool active)
        {
            // 先记住业务意愿：面板打开期间按钮是被临时藏起来的，
            // 这里只更新意愿、不动 UI，等面板关掉再由 RefreshSideButtons 统一应用，
            // 这样不会出现「面板还开着，按钮却被业务逻辑重新显示出来」。
            jiliBtnWanted = active;

            if (watchRewardedAdBtn == null) return;
            if (sideButtonsHidden) return;
            watchRewardedAdBtn.SetActive(active);
        }

        void PlayWatchAdBtnAnim()
        {
            if (watchRewardedAdBtn == null) return;
            var anim = watchRewardedAdBtn.GetComponent<Animator>();
            if (anim != null) anim.SetTrigger("activate");
        }

        /// <summary>
        /// 给可选的 jiliBtn 挂上激励视频点击事件（场景里没配 OnClick 也能用）。
        /// </summary>
        void BindJiliBtn()
        {
            if (jiliBtnBound || jiliBtn == null) return;
            jiliBtnBound = true;

            var button = jiliBtn.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning("[广告] jiliBtn 上没有 Button 组件，无法绑定点击事件");
                return;
            }

            // 场景里已经用 Inspector 配了点击事件时不要再加，
            // 否则「持久化监听 + 代码监听」会让一次点击触发两遍
            if (button.onClick.GetPersistentEventCount() > 0)
            {
                Debug.Log("[广告] jiliBtn 已在 Inspector 绑定点击事件，跳过自动绑定");
                return;
            }

            button.onClick.AddListener(WatchRewardedAd);
            Debug.Log("[广告] 已把 jiliBtn 绑定到激励视频");
        }

        /// <summary>
        /// 打开设置面板时播放插屏广告。
        /// 这里不再做自己的频控：以前按「请求时间」计时，第一次点完即使没播出来也会把时间戳刷新，
        /// 导致接下来 60 秒内怎么点都不响应。现在统一交给 TikTokPlatform 按「真实展示时间」控制
        /// （SDK 硬限制：模块启动 15 秒内不展示，两次展示间隔 ≥30 秒）。
        /// </summary>
        void TryShowSettingsInterstitial()
        {
            if (!showInterstitialOnSettings)
            {
                Debug.Log("[插屏] UIManager 里 showInterstitialOnSettings 关掉了，本次不播");
                return;
            }

            if (AdManager.Instance == null)
            {
                Debug.Log("[插屏] 场景里没有 AdManager，跳过插屏广告");
                return;
            }

            Debug.Log("[插屏] 打开设置面板，请求插屏广告");
            AdManager.Instance.ShowInterstitialAd();
        }

        public void StartGame()
        {
            GameManager.Instance.StartGame();
        }

        public void EndGame()
        {
            GameManager.Instance.GameOver();
        }

        public void RestartGame()
        {
            lifeCount = 0;
            GameManager.Instance.RestartGame(0.2f);
        }

        public void ShowStartUI()
        {
            Act(settingsUI, false);

            Act(header, true);
            Act(title, true);
            Act(playBtn, true);
            Act(restartBtn, false);
            Act(menuButtons, true);
            Act(shareBtn, false);

            // 激励按钮：只要广告可用就显示，不再限制「首局」，回主界面随时能看广告领奖励
            ShowWatchForCoinsBtn();

            // 每日奖励仍然按原来的首局逻辑
            if (GameManager.GameCount == 0)
            {
                ShowDailyRewardBtn();
            }

            // 立即同步一次悬浮按钮，别等 0.2 秒轮询
            RefreshSideButtons();
        }

        public void ShowGameUI()
        {
            if (heart != null && life != null)
            {
                for (int i = 0; i < GameManager.Instance.Life; i++)
                {
                    var createImage = Instantiate(heart) as GameObject;
                    createImage.transform.SetParent(life.transform, false);
                    _heart.Add(createImage);
                }
            }
            Act(header, true);
            Act(title, false);
            Act(score != null ? score.gameObject : null, true);
            Act(playBtn, false);
            Act(menuButtons, false);
            Act(dailyRewardBtn, false);
            SetWatchAdBtnActive(false);
            RefreshSideButtons();
        }

        public void ShowGameOverUI()
        {
            Act(header, true);
            Act(title, false);
            Act(score != null ? score.gameObject : null, true);
            Act(newBestScore, ScoreManager.Instance.HasNewHighScore);

            Act(playBtn, false);
            Act(restartBtn, true);
            Act(menuButtons, true);
            Act(settingsUI, false);

            // Show 'daily reward' button
            ShowDailyRewardBtn();

            // Show these if premium features are enabled (and relevant conditions are met)
            if (IsPremiumFeaturesEnabled())
            {
                ShowShareUI();
            }

            // 激励按钮不依赖 PremiumFeaturesManager（它没被放进场景），每局结束都能看广告领金币
            ShowWatchForCoinsBtn();

            // 原 AdDisplayer 的「每 N 局一插屏」改成抖音插屏
            TryShowGameOverInterstitial();

            GameManager.Instance.playerController.die = true;

            // 游戏结束要把两个悬浮按钮放出来（之前只靠 0.2 秒轮询，时序上会被漏掉）
            RefreshSideButtons();
        }

        /// <summary>
        /// 游戏结束后按局数弹一次抖音插屏。AdManager 不存在时静默跳过。
        /// </summary>
        void TryShowGameOverInterstitial()
        {
            if (AdManager.Instance == null)
                return;
            if (gamesPerInterstitial <= 0)
                return;

            gamesSinceInterstitial++;
            if (gamesSinceInterstitial < gamesPerInterstitial)
                return;

            gamesSinceInterstitial = 0;
            StartCoroutine(CRShowGameOverInterstitial());
        }

        IEnumerator CRShowGameOverInterstitial()
        {
            yield return new WaitForSeconds(gameOverInterstitialDelay);

            if (AdManager.Instance != null)
                AdManager.Instance.ShowInterstitialAd();
        }

        void ShowWatchForCoinsBtn()
        {
            // Only show "watch for coins button" if a rewarded ad is loaded and premium features are enabled
            // 抖音 AdSDK 通道：没有 AdManager 就不显示按钮，避免出现“点了没反应”的死按钮。
            // 这里不再判断 PremiumFeaturesManager —— 该管理器当前没被放进场景，判断了按钮会一直隐身。
            if (AdManager.Instance != null)
            {
                SetWatchAdBtnActive(true);
                PlayWatchAdBtnAnim();
            }
            else
            {
                SetWatchAdBtnActive(false);
            }
        }

        void ShowDailyRewardBtn()
        {
            // Not showing the daily reward button if the feature is disabled
            if (!DailyRewardController.Instance.disable)
            {
                Act(dailyRewardBtn, true);
            }
        }

        public void ShowSettingsUI()
        {
            Act(settingsUI, true);
            RefreshSideButtons();
            TryShowSettingsInterstitial();
        }

        public void HideSettingsUI()
        {
            Act(settingsUI, false);
            // 面板关掉了就别再等广告加载补播，否则十几秒后突然弹出来很突兀
            if (AdManager.Instance != null) AdManager.Instance.CancelPendingInterstitial();
            RefreshSideButtons();
        }

        public void ShowStoreUI()
        {
            // 审核要求：商店里的价格按钮点了没有任何效果（内购未接入抖音渠道），
            // 面板已整体隐藏。这里再拦一道，避免场景里残留的旧绑定把空面板点开。
            Debug.Log("[商店] 当前版本未接入内购，商店面板已禁用");
        }

        public void HideStoreUI()
        {
            Act(storeUI, false);
            RefreshSideButtons();
        }

        public void ShowCharacterSelectionScene()
        {
            Act(mainCanvas, false);
            Act(characterSelectionUI, true);
        }

        public void CloseCharacterSelectionScene()
        {
            Act(mainCanvas, true);
            Act(characterSelectionUI, false);
            if (GameState == GameState.Prepare)
                StartGame();
            if (GameState == GameState.GameOver)
            {
                lifeCount = 0;
                GameManager.Instance.RestartGame(0);
            }
        }

        public void WatchRewardedAd()
        {
            // 抖音 AdSDK 通道（唯一的广告渠道）
            if (AdManager.Instance == null)
            {
                Debug.LogWarning("[广告] 未接入 AdManager，无法播放激励视频");
                return;
            }

            // 领奖动画还没结束（奖励面板还开着）时再点会撞上 RewardUIController 的 isRewarding 锁，
            // 结果是广告放完了却不发奖，所以这里直接忽略，等玩家把奖励面板关掉再点。
            if (rewardUI != null && rewardUI.activeSelf)
            {
                Debug.Log("[广告] 奖励面板还开着，忽略这次激励视频点击");
                return;
            }

            AdConfirm.Show(GrantRewardedAdCoins);
        }

        // 激励视频看完后发金币（抖音 AdSDK 通道）
        void GrantRewardedAdCoins()
        {
            // 不要隐藏按钮：原来这里 SetWatchAdBtnActive(false)，领完一次奖 jiliBtn 就消失了。
            // 现在保留按钮，玩家可以反复看广告领金币。
            ShowRewardUI(rewardedAdCoins);
        }

        public void GrabDailyReward()
        {
            if (!DailyRewardController.Instance.CanRewardNow())
                return;

            // 看广告领奖励：接入 AdManager 后先弹广告确认框，看完激励视频才发奖；
            // 未配置广告（AdManager 不存在）时退化为直接发放，避免真机上按钮点了没反应。
            if (AdManager.Instance != null)
                AdConfirm.Show(GrantDailyReward);
            else
                GrantDailyReward();
        }

        // 真正发放每日奖励（激励视频看完后的回调）
        void GrantDailyReward()
        {
            int reward = DailyRewardController.Instance.GetRandomReward();

            // Round the number and make it mutiplies of 5 only.
            int roundedReward = (reward / 5) * 5;

            // Show the reward UI
            ShowRewardUI(roundedReward);

            // Update next time for the reward
            DailyRewardController.Instance.ResetNextRewardTime();
        }

        public void ShowRewardUI(int reward)
        {
            if (rewardUI == null) return;

            rewardUI.SetActive(true);
            RefreshSideButtons();
            var rewardUIController = rewardUI.GetComponent<RewardUIController>();
            if (rewardUIController != null)
                rewardUIController.Reward(reward);
        }

        public void HideRewardUI()
        {
            if (rewardUI == null) return;

            var rewardUIController = rewardUI.GetComponent<RewardUIController>();
            if (rewardUIController != null)
                rewardUIController.Close();

            RefreshSideButtons();
        }

        // 排行榜/成就：原 Easy Mobile 的 Google Play Games / Game Center 通道已移除，
        // 抖音小游戏的排行榜需在开发者后台配置后用 TT.GetImRankList 自行实现，
        // 这里保留方法签名（场景里有按钮绑定），不再调用任何第三方 SDK。
        public void ShowLeaderboardUI()
        {
            Debug.Log("[排行榜] 抖音渠道暂未接入排行榜面板");
        }

        public void ShowAchievementsUI()
        {
            Debug.Log("[成就] 抖音渠道暂未接入成就面板");
        }

        // 内购：抖音小游戏走的是平台支付，原 Easy Mobile IAP 已整体移除。
        public void PurchaseRemoveAds()
        {
            Debug.Log("[内购] 当前版本未接入内购渠道");
        }

        public void RestorePurchase()
        {
            Debug.Log("[内购] 当前版本不支持恢复购买");
        }

        public void ShowShareUI()
        {
            StartCoroutine(CR_ScreenShot());
        }

        IEnumerator CR_ScreenShot()
        {
            yield return new WaitForSeconds(2);

            // ScreenshotSharer 可能没进场景，取不到实例时也要照常打开分享面板
            if (ScreenshotSharer.Instance != null && !ScreenshotSharer.Instance.disableSharing)
            {
                Texture2D texture = ScreenshotSharer.Instance.CapturedScreenshot;
                if (shareUIController != null)
                    shareUIController.ImgTex = texture;
            }

            Act(shareUI, true);
            RefreshSideButtons();
        }
        public void HideShareUI()
        {
            Act(shareUI, false);
            RefreshSideButtons();
        }

        public void ToggleSound()
        {
            SoundManager.Instance.ToggleSound();
        }

        public void ToggleMusic()
        {
            SoundManager.Instance.ToggleMusic();
        }

        public void RateApp()
        {
            Utilities.RateApp();
        }

        public void OpenTwitterPage()
        {
            Utilities.OpenTwitterPage();
        }

        public void OpenFacebookPage()
        {
            Utilities.OpenFacebookPage();
        }

        public void ButtonClickSound()
        {
            Utilities.ButtonClickSound();
        }

        void UpdateSoundButtons()
        {
            if (soundOnBtn == null || soundOffBtn == null) return;

            if (SoundManager.Instance.IsSoundOff())
            {
                soundOnBtn.SetActive(false);
                soundOffBtn.SetActive(true);
            }
            else
            {
                soundOnBtn.SetActive(true);
                soundOffBtn.SetActive(false);
            }
        }

        void UpdateMusicButtons()
        {
            if (musicOnBtn == null || musicOffBtn == null) return;

            if (SoundManager.Instance.IsMusicOff())
            {
                musicOffBtn.SetActive(true);
                musicOnBtn.SetActive(false);
            }
            else
            {
                musicOffBtn.SetActive(false);
                musicOnBtn.SetActive(true);
            }
        }

        bool IsPremiumFeaturesEnabled()
        {
            return PremiumFeaturesManager.Instance != null && PremiumFeaturesManager.Instance.enablePremiumFeatures;
        }
    }
}