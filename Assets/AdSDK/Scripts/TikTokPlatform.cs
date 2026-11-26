using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

//#if TickTok
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

/*
 * 抖音平台广告
 */
public class TikTokPlatform : IAdPlatform
{
    /// <summary>
    /// 是否向第三方（ecpm.xxhygames.com）上报 openid/机型等数据做 ECPM 与转化回传。
    /// 默认关闭：上面报会把用户信息发到外部服务器，抖音审核和隐私政策里必须申报，
    /// 确认是自己的后台、且已在上架材料里写明后再改成 true。
    /// </summary>
    public static bool EnableRemoteTracking = false;

    // 小游戏ID
    private readonly string miniAppid;

    // 激励视频
    private TTRewardedVideoAd rewardedVideoAd = null;
    private string videoAdId = "";
    private Action onRewardSuccess;
    private Action<string> onRewardFail;
    private float prevTimeScale = 1f;
    // 激励视频广告位被平台判定无效（1002）的熔断
    private int videoConfigErrorCount;
    private bool videoConfigBroken;

    // 插屏广告
    private TTInterstitialAd interAdIns;
    private string interAdId = "";
    private float moduleStartTime;
    private float lastShowTime;

    // SDK 限制（以官网 tt.createInterstitialAd 文档为准，TTSDK 里的 XML 注释是旧值）：
    //   · 小游戏启动后前 30s 不能展示插屏
    //   · 两次插屏之间不少于 60s
    //   · 展示过激励视频后，插屏要跟激励视频间隔 60s
    // 命中任何一条都不能直接 return，否则玩家看到的就是「点设置没反应」。
    private const float MinModuleAliveTime = 30f;
    private const float MinShowInterval = 60f;
    private const float MinGapAfterRewarded = 60f;
    // 待播有效期：玩家点设置后广告还在加载时，等这么久都算「本次想播」。
    // 之前只有 10s，而抖音插屏从 create/load 到 OnLoad 经常要 5~10s 以上，
    // 慢一点就错过窗口，日志只停在「加载完成」，玩家看到的就是点了没反应。
    private const float PendingShowTimeout = 30f;
    private const int MaxAutoRetry = 3;

    // 激励视频最后一次展示的时间，用于「激励后 60s 内不弹插屏」
    private float lastRewardedShowTime = -999f;

    // SDK 就绪门控：TT.InitSDK 是异步的，未就绪时 create 广告实例会稳定失败
    private bool sdkReady;
    private float platformCreateTime;
    private bool sdkNotReadyLogged;
    // 开发者工具（沙盒）不支持插屏，见 IsDevToolEnvironment
    private bool? isDevTool;
    private bool devToolProbeFailLogged;
    // 兜底：宿主不回调 / 调用方忘了通知时，超过这个时间也放行，避免广告永久不可用
    private const float SdkReadyTimeout = 5f;

    private bool pendingShow;
    private float pendingDeadline;
    private float pendingNotBefore;
    private float nextPendingCheck;
    private int autoRetryCount;

    // 埋点
    private readonly string ecpmURL = "https://ecpm.xxhygames.com/openid/openlog/insertlog";  // ECPM后台地址
    private readonly string conversionURL = "https://analytics.oceanengine.com/api/v2/conversion";   // 巨量引擎API接口

    private TTSystemInfo systemInfo;

    public TikTokPlatform(string appId, string videoAdId, string interAdId)
    {
        this.miniAppid = appId ?? "";
        this.videoAdId = videoAdId;
        this.interAdId = interAdId;
        if (string.IsNullOrEmpty(this.miniAppid))
        {
            // AppID 只用于 ECPM/转化回传，广告本身按广告位 ID 播放，不影响收益流程。
            Debug.Log("[抖音广告] 未填写小游戏 AppID，广告可正常播放，仅埋点上报不可用");
        }
        else if (EnableRemoteTracking && !this.miniAppid.EndsWith("07"))
        {
            // 只有开启回传时才需要 AppID 格式校验，否则这条警告纯属噪音
            Debug.LogWarning($"[抖音广告] AppID「{this.miniAppid}」格式可能不匹配回传接口，广告播放不受影响");
        }

        if (EnableRemoteTracking)
        {
            try
            {
                this.Login();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[抖音广告] 埋点初始化失败（不影响广告）：{e.Message}");
            }
        }

        // 注意：这里绝对不能创建广告实例。
        // TT.InitSDK 是异步的，AdManager.Start 里刚发起初始化就 new TikTokPlatform，
        // 构造函数同步跑到这里时 SDK 还没就绪，会稳定拿到 createInterstitialAd:fail（code=0、errMsg 为空），
        // 紧接着 Load() 又打在一个 error 实例上，控制台再炸一条 InterstitialAd.load:fail + Uncaught (in promise)。
        // 现在改为等 AdManager 转来 OnSdkReady() 之后再预创建。
        this.platformCreateTime = Time.realtimeSinceStartup;

        moduleStartTime = Time.realtimeSinceStartup;
        lastShowTime = -30f;
    }

    /// <summary>
    /// TT.InitSDK 成功（或超时兜底）后由 AdManager 调用，此时才允许创建广告实例。
    /// </summary>
    public void OnSdkReady()
    {
        if (this.sdkReady) return;
        this.sdkReady = true;

        // 广告模块是从「第一次调用广告 API」才开始算启动的，
        // 保护期从这一刻计时，才和官方「启动 30s 内不能展示插屏」对得上。
        this.moduleStartTime = Time.realtimeSinceStartup;

        Debug.Log("[抖音广告] SDK 已就绪，开始预创建广告实例");
        try
        {
            if (this.rewardedVideoAd == null) this.CreateRewardedVideoAd();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[抖音广告] 预创建激励视频失败：{e.Message}");
        }
        this.EnsureInterstitialAd();
    }

    /// <summary>
    /// SDK 是否可用于创建广告实例。未收到就绪通知时，超过兜底时间也放行。
    /// </summary>
    private bool IsSdkUsable()
    {
        if (this.sdkReady) return true;

        if (Time.realtimeSinceStartup - this.platformCreateTime >= SdkReadyTimeout)
        {
            Debug.LogWarning($"[抖音广告] 超过 {SdkReadyTimeout:F0}s 没收到 InitSDK 回调，先放行广告创建");
            this.sdkReady = true;
            this.moduleStartTime = Time.realtimeSinceStartup;
            return true;
        }

        return false;
    }


    #region 插屏广告

    /// 创建并播放插屏广告，在广告模块启动15s内不允许展示插屏广告，在任何地方调用API.GetStarkAdManager，就会启动广告模块
    /// 两个插屏广告展示间隔不能少于30s
    /// 插屏广告支持多实例。简单回调设置，插屏广告完成和错误的回调函数
    /// 插屏广告实例只支持展示一次，出现加载错误，或 展示成功，点击关闭按钮或视频广告外的其他地方，广告实例都会自动销毁。下一次需再次创建。
    /// 提供接口可以手动销毁
    /// 错误码由sdk传出，原因参考
    /// https://microapp.bytedance.com/docs/zh-CN/mini-game/develop/open-capacity/ads/tt-create-interstitial-ad
    public override void ShowInterstitialAd(string AdId)
    {
        // 不要再按「沙盒不支持插屏」去跳过：官方《IDE 模拟器实现差异》里写得很清楚，
        // tt.createInterstitialAd 在开发者工具中是「模拟实现，调用 API 展示模拟广告页面」。
        // 之前 IDE 里创建失败的真实原因是打包 appid 与广告位不匹配，不是模拟器不支持。
        Debug.Log($"[插屏] 收到播放请求，广告位={AdId}"
                  + (this.IsDevToolEnvironment() ? "（开发者工具：展示模拟插屏）" : string.Empty));

        // 广告位变化时重建实例，避免继续使用旧广告位
        if (!string.IsNullOrEmpty(AdId) && this.interAdId != AdId)
        {
            this.interAdId = AdId;
            this.RemoveInterstitialAd();
        }

        this.autoRetryCount = 0;
        this.pendingShow = false;
        this.TryPresent();
    }

    /// <summary>
    /// 是否运行在抖音开发者工具（模拟器）里。
    /// 只用于「跳过宿主侧频控」和日志标注，**不再用来跳过插屏**——
    /// 官方《IDE 模拟器实现差异》明确写了 tt.createInterstitialAd 在开发者工具中是模拟实现。
    /// 真机 platform 只会是 ios / android。
    /// </summary>
    private bool IsDevToolEnvironment()
    {
        if (this.isDevTool.HasValue) return this.isDevTool.Value;

        bool devtool = false;
        try
        {
            var info = TT.GetSystemInfo();
            string platform = info?.platform ?? string.Empty;
            string hostName = info?.hostName ?? string.Empty;
            devtool = string.Equals(platform, "devtools", StringComparison.OrdinalIgnoreCase)
                      || hostName.IndexOf("devtools", StringComparison.OrdinalIgnoreCase) >= 0;

            // 只有拿成功才缓存结果；抛异常时不缓存，下次再探
            this.isDevTool = devtool;
            return devtool;
        }
        catch (Exception e)
        {
            // 拿不到系统信息时按真机处理，宁可多试一次也不要误跳过广告
            if (!this.devToolProbeFailLogged)
            {
                this.devToolProbeFailLogged = true;
                Debug.LogWarning($"[插屏] 获取系统信息失败，按真机处理：{e.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// 真正判断能不能播。能播就播；暂时播不了就记下「待播」，
    /// 等启动 30s 保护期结束 / 激励视频间隔满 60s / 广告加载完成 / 重试成功后自动补播。
    /// </summary>
    private void TryPresent()
    {
        if (this.interAdIns == null)
        {
            this.EnsureInterstitialAd();
        }

        float now = Time.realtimeSinceStartup;

        // 频控（启动 30s / 两次间隔 60s / 激励后 60s）是宿主侧规则，开发者工具的模拟插屏不校验。
        // 在 IDE 里照样等待，只会让「点设置没反应」更难排查，所以只在真机上生效。
        if (!this.IsDevToolEnvironment())
        {
            float alive = now - this.moduleStartTime;
            if (alive < MinModuleAliveTime)
            {
                float wait = MinModuleAliveTime - alive + 0.2f;
                Debug.Log($"[插屏] 小游戏启动只有 {alive:F1}s，SDK 要求前 {MinModuleAliveTime:F0}s 不能展示，{wait:F1}s 后自动补播");
                // 到点自动补播，由 AdManager.Update → TickPendingShow 驱动
                this.SchedulePendingShow(wait, wait + PendingShowTimeout);
                return;
            }

            float sinceRewarded = now - this.lastRewardedShowTime;
            if (sinceRewarded < MinGapAfterRewarded)
            {
                float wait = MinGapAfterRewarded - sinceRewarded;
                if (wait <= 15f)
                {
                    // 差得不多了，等满 60s 再补播
                    Debug.Log($"[插屏] 距上次激励视频 {sinceRewarded:F1}s，官方要求间隔 60s，{wait:F1}s 后补播");
                    this.SchedulePendingShow(wait + 0.5f, wait + PendingShowTimeout);
                }
                else
                {
                    Debug.LogWarning($"[插屏] 距上次激励视频只有 {sinceRewarded:F1}s，官方要求间隔 60s，本次跳过（{wait:F0}s 后再点设置即可）");
                }
                return;
            }

            if (this.lastShowTime > 0f && now - this.lastShowTime < MinShowInterval)
            {
                float remain = MinShowInterval - (now - this.lastShowTime);
                Debug.LogWarning($"[插屏] 距上次展示只有 {now - this.lastShowTime:F1}s，还需 {remain:F1}s，本次跳过");
                return;
            }
        }

        if (!this.IsInterstitialLoaded())
        {
            Debug.Log("[插屏] 广告还没加载完，已登记待播：OnLoad 会立即补播，AdManager 每 0.2s 还会兜底轮询");
            this.MarkPending(PendingShowTimeout);
            return;
        }

        this.Present();
    }

    private void Present()
    {
        if (this.interAdIns == null)
        {
            Debug.LogWarning("[插屏] 实例为空，无法展示");
            this.pendingShow = false;
            return;
        }

        try
        {
            Debug.Log("[插屏] 开始展示");
            this.interAdIns.Show();
            this.lastShowTime = Time.realtimeSinceStartup;
            this.pendingShow = false;
            Debug.Log("[插屏] Show() 调用成功");
        }
        catch (Exception e)
        {
            this.pendingShow = false;
            Debug.LogWarning($"[插屏] 展示异常：{e.Message}");
            // 展示异常基本是实例状态坏了，重建一次，下次点击/下次进设置还能用
            this.RemoveInterstitialAd();
            this.EnsureInterstitialAd();
        }
    }

    /// <summary>
    /// 玩家已经把设置面板关掉了，就别再等加载补播了，否则十几秒后突然弹广告很突兀。
    /// </summary>
    public void CancelPendingInterstitial()
    {
        if (!this.pendingShow) return;
        this.pendingShow = false;
        Debug.Log("[插屏] 设置面板已关闭，取消本次待播");
    }

    private void MarkPending(float timeout, float notBefore = 0f)
    {
        this.pendingShow = true;
        this.pendingDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, timeout);
        this.pendingNotBefore = Time.realtimeSinceStartup + Mathf.Max(0f, notBefore);
    }

    /// <summary>
    /// 延迟补播现在只登记一个时间点，不再起协程。
    /// 之前用 AdManager.StartCoroutine，一旦 AdManager.Instance 取不到（初始化顺序/场景切换），
    /// 协程根本不会运行，表现就是「排上队了但永远不播」。
    /// </summary>
    private void SchedulePendingShow(float delay, float timeout)
    {
        this.MarkPending(timeout, delay);
    }

    /// <summary>
    /// 由 AdManager.Update 驱动（每 0.2 秒一次），是「到点补播」的唯一入口，
    /// 同时也是 OnLoad 没回调时的兜底：只要 IsLoaded 变 true 就播。
    /// </summary>
    public void TickPendingShow()
    {
        if (!this.pendingShow) return;

        float now = Time.realtimeSinceStartup;

        // 还没到允许播放的时间点（启动 15s 保护期 / 激励视频后 60s）
        if (now < this.pendingNotBefore) return;

        if (now < this.nextPendingCheck) return;
        this.nextPendingCheck = now + 0.2f;

        if (now > this.pendingDeadline)
        {
            this.pendingShow = false;
            Debug.LogWarning("[插屏] 待播超时（广告一直没加载好），本次不再补播");
            return;
        }

        // 实例还没建（多半是 SDK 就绪前玩家就点了设置）时在这里补建，
        // 否则 TickPendingShow 只会一直查「加载好没有」，等到超时都等不到东西。
        if (this.interAdIns == null)
        {
            this.EnsureInterstitialAd();
            if (this.interAdIns == null) return; // SDK 还没就绪，下个 0.2s 再试
        }

        if (!this.IsInterstitialLoaded())
        {
            return; // 还没加载好，0.2 秒后再看
        }

        Debug.Log("[插屏] 待播到点，开始补播");
        this.TryPresent();
    }

    private bool IsInterstitialLoaded()
    {
        if (this.interAdIns == null) return false;

        try
        {
            return this.interAdIns.IsLoaded();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[插屏] 查询加载状态异常：{e.Message}");
            return false;
        }
    }

    private void EnsureInterstitialAd()
    {
        if (this.interAdIns != null) return;

        // SDK 未就绪时创建必定失败，直接跳过，等 OnSdkReady / 兜底时间到了再建
        if (!this.IsSdkUsable())
        {
            // 这个方法会被 TickPendingShow 每 0.2s 调一次，日志只打第一条，否则会刷屏
            if (!this.sdkNotReadyLogged)
            {
                this.sdkNotReadyLogged = true;
                Debug.Log("[插屏] SDK 尚未初始化完成，暂不创建实例，等 OnSdkReady 后再建");
            }
            return;
        }

        try
        {
            this.CreateInterstitialAd();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[插屏] 创建实例失败：{e.Message}（若在抖音开发者工具里，插屏可能不被模拟器支持，请用真机预览验证）");
        }
    }

    private void CreateInterstitialAd()
    {
        interAdIns = TT.CreateInterstitialAd(new CreateInterstitialAdParam { InterstitialAdId = this.interAdId });
        try
        {
            this.BindInterstitialEvents();
            interAdIns.Load();
            Debug.Log($"[插屏] 已创建实例并开始加载，广告位={this.interAdId}");
        }
        catch (Exception e)
        {
            // 绑定失败/加载抛异常时把实例清掉，否则会留一个「永远 IsLoaded=false 又没有回调」的死实例
            this.RemoveInterstitialAd();
            Debug.LogWarning($"[插屏] 创建后初始化异常：{e.Message}");
            throw;
        }
    }

    private void BindInterstitialEvents()
    {
        interAdIns.OnClose += () =>
        {
            Debug.Log("[插屏] 广告关闭");
            // 1.销毁实例（插屏实例只能展示一次）
            this.RemoveInterstitialAd();
            // 2.重新创建新的广告实例，提前预加载
            try { this.CreateInterstitialAd(); }
            catch (Exception e) { Debug.LogWarning($"[抖音广告] 预创建插屏失败：{e.Message}"); }
            // 3.发送埋点
            this.SendGameAddiction();
        };
        interAdIns.OnLoad += () =>
        {
            Debug.Log("[插屏] 加载完成");
            // 之前点过设置但广告还没加载好（或还在启动保护期）时，这里补播。
            // 不再用「是否超过待播时限」卡死：OnLoad 回来就说明广告确实可播，
            // 只要这次请求还在有效期内就直接放，否则玩家永远等不到。
            if (!this.pendingShow)
            {
                Debug.Log("[插屏] 本次只是预加载，等玩家点设置时直接展示");
                return;
            }

            float remain = this.pendingDeadline - Time.realtimeSinceStartup;
            Debug.Log($"[插屏] 有待播请求，距截止还有 {remain:F1}s，立即补播");
            this.TryPresent();
        };
        interAdIns.OnError += (code, message) =>
        {
            Debug.LogWarning($"[插屏] 加载/展示出错 code={code} message={message}" + ErrorHint(code, message));

            // 这里不要 Destroy：插屏在 load 失败后由平台自动销毁，
            // 手动 destroy 会和还没结束的 load Promise 撞车，控制台会多一条
            // Uncaught (in promise) API_ERROR: InterstitialAd.load:fail。只清引用。
            this.interAdIns = null;

            // 创建阶段就失败（广告位无效 / 不属于当前 appid / 类型不对）时重试没有意义，
            // 只会让控制台一副「不停报错」的样子，直接放弃本次。
            if (code == 0 && !string.IsNullOrEmpty(message) && message.Contains("createInterstitialAd"))
            {
                this.pendingShow = false;
                Debug.LogError($"[插屏] 创建失败，本次不再重试。请确认广告位 {this.interAdId} 满足三条："
                               + "① 属于当前小游戏 appid；② 类型是「插屏」不是「激励视频」；③ 后台已启用。"
                               + "开发者工具里 appid 取自 project.config.json，改完要关闭项目再重新导入才生效。");
                return;
            }

            // 待播状态下自动重试，避免「点一次没反应就彻底没戏」
            if (this.pendingShow && this.autoRetryCount < MaxAutoRetry)
            {
                this.autoRetryCount++;
                float[] backoff = { 3f, 6f, 10f };
                float delay = backoff[Mathf.Clamp(this.autoRetryCount - 1, 0, backoff.Length - 1)];
                Debug.Log($"[插屏] 第 {this.autoRetryCount} 次自动重试加载，{delay:F0}s 后");
                this.SchedulePendingShow(delay, delay + PendingShowTimeout);
            }
            else if (this.pendingShow)
            {
                this.pendingShow = false;
                Debug.LogWarning("[插屏] 已重试到上限，本次放弃");
            }
        };
    }

    private void RemoveInterstitialAd()
    {
        interAdIns?.Destroy();
        interAdIns = null;
    }

    /// <summary>
    /// 给错误码补一句人话解释，避免每次都要翻文档。
    /// </summary>
    private static string ErrorHint(int code, string message)
    {
        switch (code)
        {
            case 2001: return "（2001 = 小游戏启动 30s 内不允许展示插屏）";
            case 2002: return "（2002 = 频控：两次插屏间隔需满 60s，或距激励视频不足 60s）";
            case 2003: return "（2003 = 正在播放其它广告）";
            case 2005: return "（2005 = 插屏实例不允许跨页面调用）";
            case 158902: return "（158902 = 广告位不存在/已关闭，检查 adUnitId 是否属于当前 appid）";
            case 158987: return "（158987 = 当前宿主不支持，开发者工具/PC 版抖音常见，请用真机验证）";
            case 0:
                // 创建阶段失败时 errMsg 通常只有「createInterstitialAd:fail」，没有错误码
                if (!string.IsNullOrEmpty(message) && message.Contains("createInterstitialAd"))
                {
                    return "（创建失败两大原因：SDK 未初始化完成，或宿主不支持插屏——开发者工具沙盒不模拟插屏，请用真机验证）";
                }
                return string.Empty;
            default: return string.Empty;
        }
    }

    #endregion


    #region 侧边栏

    public override void NavigateToScene(Action onSuccess, Action<string> onFail)
    {
        var data = new JsonData { ["scene"] = "sidebar" };
        TT.NavigateToScene(data,
            () =>
            {
                Debug.Log("侧边栏唤起成功");
                onSuccess?.Invoke();
            },
            () => { },
            (errCode, errMsg) =>
            {
                onFail?.Invoke(errMsg);
                Debug.LogError($"侧边栏显示异常:{errCode}, {errMsg}");
            });
    }

    #endregion

    #region 激励视频
    public override void ShowRewardedVideoAd(string videoAdId, Action onSuccess, Action<string> onFail)
    {
        Debug.Log("播放激励视频广告");
        this.prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0; // 看广告需要暂停游戏

        // 广告位被平台判定为无效（1002）时，再点多少次都是同一个错，直接短路，避免刷屏
        if (this.videoConfigBroken)
        {
            Debug.LogWarning("[抖音广告] 激励视频广告位已被判定无效（1002），本次运行不再请求，请核对广告位配置");
            var brokenFail = onRewardFail;
            this.RestoreTimeScale();
            brokenFail?.Invoke("广告位配置有误，请稍后再试");
            return;
        }

        // 广告位发生变化时必须重建实例，否则会继续播旧的广告位
        bool needRecreate = rewardedVideoAd == null || this.videoAdId != videoAdId;
        this.videoAdId = videoAdId;
        onRewardSuccess = onSuccess;
        onRewardFail = onFail;

        if (needRecreate)
        {
            this.DelRewardedVideoAd();

            // 与插屏同理：SDK 没就绪时创建必失败，别让玩家卡在「广告播放中」
            if (!this.IsSdkUsable())
            {
                Debug.LogWarning("[抖音广告] SDK 尚未初始化完成，本次激励视频无法播放");
                var notReady = this.onRewardFail;
                this.RestoreTimeScale();
                notReady?.Invoke("广告还在准备中，请稍后再试");
                return;
            }

            try
            {
                this.CreateRewardedVideoAd();
            }
            catch (Exception e)
            {
                Debug.LogError($"[抖音广告] 创建激励视频异常：{e.Message}");
            }
        }

        if (rewardedVideoAd == null)
        {
            // 创建失败必须恢复暂停，否则游戏会一直卡在 timeScale=0
            var fail = onRewardFail;
            this.RestoreTimeScale();
            fail?.Invoke("激励视频创建失败");
            return;
        }

        try
        {
            rewardedVideoAd.Show();
            // 记录激励视频展示时间：官方要求激励视频之后 60s 内不能弹插屏
            this.lastRewardedShowTime = Time.realtimeSinceStartup;
        }
        catch (Exception e)
        {
            Debug.LogError($"[抖音广告] 展示激励视频异常：{e.Message}");
            var fail = onRewardFail;
            this.RestoreTimeScale();
            fail?.Invoke("激励视频展示失败");
        }
    }

    private void RestoreTimeScale()
    {
        onRewardSuccess = null;
        onRewardFail = null;
        Time.timeScale = prevTimeScale <= 0f ? 1f : prevTimeScale;
    }

    private void CreateRewardedVideoAd()
    {
        rewardedVideoAd = TT.CreateRewardedVideoAd(new CreateRewardedVideoAdParam() { AdUnitId = videoAdId });
        rewardedVideoAd.OnClose += (ended, count) =>
        {
            
            Debug.Log($"是否看完广告:{ended} 次数:{count}");
            var success = onRewardSuccess;
            var fail = onRewardFail;
            this.RestoreTimeScale();
            if (ended)
            {
                success?.Invoke();
                this.SendGameAddiction();
            }
            else
            {
                Debug.Log("广告未看完，不发放奖励");
                fail?.Invoke("广告未看完");
            }

            // 关闭广告后,创建新的实例（失败也不影响下次点击，下次会自动重建）
            this.DelRewardedVideoAd();
            try { this.CreateRewardedVideoAd(); }
            catch (Exception e) { Debug.LogWarning($"[抖音广告] 预创建激励视频失败：{e.Message}"); }
        };
        rewardedVideoAd.OnError += (errorCode, errorMessage) =>
        {
            this.DelRewardedVideoAd();
            var fail = onRewardFail;
            this.RestoreTimeScale();
            Debug.LogError($"激励视频错误 errorCode: {errorCode}, message: {errorMessage}");

            // 1002 = adUnitId 不在当前小游戏名下，或广告位类型不是激励视频。
            // 这是确定的配置错误，重试多少次都一样，连续两次就熔断，避免玩家每次点击都吃一屏报错。
            if (errorCode == 1002)
            {
                this.videoConfigErrorCount++;
                if (this.videoConfigErrorCount >= 2)
                {
                    this.videoConfigBroken = true;
                }
                Debug.LogError($"[抖音广告] 激励视频广告位无效（1002）：当前广告位={this.videoAdId}。"
                               + "请到开放平台「流量主 → 广告管理」核对：广告位是否属于本小游戏、类型是否为激励视频、是否已启用。");
                fail?.Invoke("广告暂不可用，请稍后再试");
                return;
            }

            fail?.Invoke(string.IsNullOrEmpty(errorMessage) ? $"广告加载失败（{errorCode}）" : errorMessage);
            // 不在这里重建实例，避免广告位无效时反复创建；下次点击时 needRecreate 会自动重建
        };
    }

    private void DelRewardedVideoAd()
    {
        rewardedVideoAd?.Destroy();
        rewardedVideoAd = null;
    }

    #endregion


    #region 埋点

    private void Login()
    {
        // 1.获取系统信息
        systemInfo = TT.GetSystemInfo();
        if (systemInfo?.system == null)
            return;

        // 2.登录-获取临时登录凭证 
        TT.Login(this.onLoginSuccess, (errMsg) =>
        {
            Debug.LogError("登录失败:" + errMsg);
        }, true);
        // 3.初始化埋点
        this.InitMaidian();
    }

    private void onLoginSuccess(string code, string anonymousCode, bool isLogin)
    {
        // 2.1获取用户的基本信息
        TT.GetUserInfo((ref TTUserInfo scUserInfo) => this.onGetUserInfo(ref scUserInfo, code, anonymousCode),
            (msg) =>
            {
                Debug.LogError($"用户信息获取失败: {msg}");
            });
    }

    private void onGetUserInfo(ref TTUserInfo scUserInfo, string code, string anonymousCode)
    {
        // 2.2向ECPM后台上传设备信息
        Debug.Log(scUserInfo);
        AdManager.Instance.StartCoroutine(this.SendOpenIdLog(
            miniAppid, scUserInfo.nickName, code, anonymousCode, systemInfo, (success, res) =>
            {
                if (success && res.d != null && res.d.user != null && !string.IsNullOrEmpty(res.d.user.openid))
                {
                    string useropenid = res.d.user.openid;
                    Debug.Log("openidlog==succ== " + res);
                    string currentDate = DateTime.UtcNow.ToString("o");
                    PlayerPrefs.SetString(miniAppid + TT.GetSystemInfo().model, currentDate);
                }
                else
                {
                    Debug.Log("openidlog==fail== " + res);
                }
            }));
    }

    private IEnumerator SendOpenIdLog(string appid, string nickName, string code, string anonymousCode, TTSystemInfo sysinfores, Action<bool, OpenIdResponse> callback)
    {
        var query = $"?appid={Uri.EscapeDataString(appid)}" +
                    $"&nickName={Uri.EscapeDataString(nickName)}" +
                    $"&code={Uri.EscapeDataString(code)}" +
                    $"&anonymousCode={Uri.EscapeDataString(anonymousCode)}" +
                    $"&system={Uri.EscapeDataString(sysinfores.system)}" +
                    $"&platform={Uri.EscapeDataString(sysinfores.platform)}" +
                    $"&brand={Uri.EscapeDataString(sysinfores.brand)}" +
                    $"&modelx={Uri.EscapeDataString(sysinfores.model)}" +
                    $"&version={Uri.EscapeDataString(sysinfores.ttVersion)}" +
                    $"&SDKVersion={Uri.EscapeDataString(sysinfores.sdkVersion)}" +
                    $"&appName={Uri.EscapeDataString(sysinfores.hostName)}";

        var www = UnityWebRequest.Get(this.ecpmURL + query);
        yield return www.SendWebRequest();
        if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.Log("openidlog==fail== " + www.error);
            callback(false, null);
        }
        else
        {
            try
            {
                var response = JsonUtility.FromJson<OpenIdResponse>(www.downloadHandler.text);
                callback(true, response);
            }
            catch (Exception ex)
            {
                Debug.LogError("SendOpenIdLog ERROR：" + ex.Message);
                callback(false, null);
            }
        }
    }

    private void InitMaidian()
    {
        // 3.1获取小游戏启动时的参数
        var launchOptions = TT.GetLaunchOptionsSync();
        Debug.Log($"小游戏启动参数: {launchOptions}");
        if (launchOptions == null || launchOptions.Query == null || !launchOptions.Query.ContainsKey("ad_params"))
        {
            Debug.LogWarning($"小游戏:{miniAppid}没添加埋点参数");
            return;
        }

        string decodedString = Uri.UnescapeDataString(launchOptions.Query["ad_params"]);
        AdParamsData jsonval = JsonUtility.FromJson<AdParamsData>(decodedString);
        string clickid = jsonval?.log_extra?.clickid;
        if (string.IsNullOrEmpty(clickid))
        {
            Debug.LogWarning($"小游戏:{miniAppid}没点击事件埋点");
            return;
        }

        Debug.Log($"clickid: {clickid}");
        PlayerPrefs.SetString(miniAppid + "ClickId", clickid);
        if (!CanClaimReward(miniAppid + "Active"))
        {
            Debug.LogWarning($"小游戏:{miniAppid}奖励已领取，无法重复领取");
            return;
        }

        // 3.2 API上报激活数据
        long timenow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        AdManager.Instance.StartCoroutine(SendConversionRequest(
            "active", clickid, timenow,
            (success, response) =>
            {
                Debug.Log($"激活埋点事件==={(success ? "success" : "fail")}");
                Debug.Log($"激活结果={response}");
            }
        ));
        string currentDate = DateTime.UtcNow.ToString("o");
        PlayerPrefs.SetString(miniAppid + "Active", currentDate);
    }

    /*是否允许领取奖励*/
    private bool CanClaimReward(string key)
    {
        string lastClaimDateStr = PlayerPrefs.GetString(miniAppid + key);
        if (string.IsNullOrEmpty(lastClaimDateStr))
        {
            return true;
        }

        if (!DateTime.TryParse(lastClaimDateStr, out DateTime lastClaimDate))
        {
            return true;
        }
        DateTime oneMonthAgo = DateTime.Now.AddMonths(-1);
        return lastClaimDate < oneMonthAgo;
    }

    /// <summary>
    /// API上报关键行为
    /// </summary>
    private void SendGameAddiction()
    {
        string clickid = PlayerPrefs.GetString(miniAppid + "ClickId");
        if (string.IsNullOrEmpty(clickid))
        {
            Debug.LogWarning($"小游戏:{miniAppid}没点击事件埋点");
            return;
        }

        long timenow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        AdManager.Instance.StartCoroutine(SendConversionRequest(
                "game_addiction", clickid, timenow,
                (success, response) =>
                {
                    Debug.Log($"埋点事件===={(success ? "success" : "fail")}");
                    Debug.Log($"关键行为结果={response}");
                }
            ));
        Debug.Log("已触发激活埋点");
    }

    /// <summary>
    /// 转化回传 API
    /// </summary>
    /// <param name="eventType">回传的事件：</param>
    /// <param name="clickid">clickid 参数</param>
    /// <param name="timestamp">事件发生的毫秒级时间戳</param>
    /// <param name="callback"></param>
    /// https://event-manager.oceanengine.com/docs/8650/app_api_docs
    /// <returns></returns>
    public IEnumerator SendConversionRequest(string eventType, string clickid, long timestamp, Action<bool, string> callback)
    {
        var data = new ConversionData
        {
            event_type = eventType,
            context = new Context { ad = new Ad { callback = clickid } },
            timestamp = timestamp
        };

        string json = JsonUtility.ToJson(data);
        byte[] postData = System.Text.Encoding.UTF8.GetBytes(json);

        Dictionary<string, string> headers = new Dictionary<string, string>();
        headers.Add("Content-Type", "application/json");

        using var www = new UnityWebRequest(conversionURL, "POST");
        www.uploadHandler = new UploadHandlerRaw(postData);
        www.downloadHandler = new DownloadHandlerBuffer();
        foreach (var header in headers)
        {
            www.SetRequestHeader(header.Key, header.Value);
        }
        yield return www.SendWebRequest();
        if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
        {
            callback(false, www.error);
        }
        else
        {
            callback(true, www.downloadHandler.text);
        }
    }
    public override void ShareGame(Action<bool> callBack)
    {
        // 通用分享
        JsonData shareJson = new JsonData();
        shareJson["channel"] = "video";
        shareJson["title"] = "Some Title";
        shareJson["extra"] = new JsonData();
        shareJson["extra"]["videoPath"] = "/xxx/xxx.mp4";//录屏分享的话，路径是 OnRecordComplete 拿到的路径
        JsonData videoTopics = new JsonData();
        videoTopics.SetJsonType(JsonType.Array);
        videoTopics.Add("Some Topic1");
        videoTopics.Add("Some Topic2");
        shareJson["extra"]["videoTopics"] = videoTopics;
        shareJson["extra"]["hashtag_list"] = videoTopics;

        Debug.Log($"ShareAppMessageBtnClicked jsonData: {videoTopics.ToJson()}");
        TT.ShareAppMessage(videoTopics, (data) =>
        {
            callBack?.Invoke(true);
            Debug.Log($"ShareAppMessage success: {videoTopics.ToJson()}");//
        },
            (errMsg) =>
            {
                callBack?.Invoke(false);
                Debug.Log($"ShareAppMessage failed: {errMsg}");
            },
            () =>
            {
                callBack?.Invoke(false);
                Debug.Log($"ShareAppMessage cancel");
            });
    }

    #endregion
}

//#endif