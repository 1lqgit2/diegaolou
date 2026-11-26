using UnityEngine;

/// <summary>
/// 广告初始化入口。
///
/// 把抖音小游戏后台拿到的 AppId 和广告位 ID 填到下面的常量里，
/// 运行时会自动创建一个常驻的 AdManager，不需要手动往场景里挂物体。
///
/// 注意：AppId 只用于 ECPM / 转化回传埋点，**不影响广告播放**。
/// 所以即使留空也会创建 AdManager，走 AdManager 里的默认广告位，
/// 保证「激励按钮」「设置面板插屏」在没填 ID 的情况下也能跑通流程。
/// </summary>
public static class AdBootstrap
{
    // ↓↓↓↓↓↓ 在这里填抖音小游戏后台的配置 ↓↓↓↓↓↓
    public const string APP_ID = "";                                  // 小游戏 AppId（只影响埋点，可留空）
    public static readonly string[] VIDEO_AD_IDS = new string[0];     // 激励视频广告位 ID（可填多个，随机取一个）
    public static readonly string[] INTERSTITIAL_AD_IDS = new string[0]; // 插屏广告位 ID
    // ↑↑↑↑↑↑ ---------------------------------- ↑↑↑↑↑↑

    /// <summary>
    /// 场景加载**之后**再执行：
    /// 场景里如果已经手动放了 AdManager（填好广告位的那种），这里必须让位给它，
    /// 否则会在场景加载前抢先建一个用默认广告位的实例，把场景里配置好的那个挤掉。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (AdManager.Instance != null)
        {
            Debug.Log("[广告] 场景里已有 AdManager，跳过自动创建");
            return;
        }

        if (string.IsNullOrEmpty(APP_ID))
        {
            Debug.Log("[广告] 未配置 APP_ID，将使用 AdManager 内置的默认广告位（埋点上报不可用）");
        }

        var go = new GameObject("[AdManager]");
        Object.DontDestroyOnLoad(go);

        var manager = go.AddComponent<AdManager>();
        // AddComponent 会立即执行 Awake，Start 在下一帧前执行，这里赋值仍然有效
        manager.AppId = APP_ID;
        manager.videoAdIdList = new System.Collections.Generic.List<string>(VIDEO_AD_IDS);
        manager.interAdIdList = new System.Collections.Generic.List<string>(INTERSTITIAL_AD_IDS);

        Debug.Log("[广告] AdManager 已自动创建，AppId=" + (string.IsNullOrEmpty(APP_ID) ? "(默认)" : APP_ID));
    }
}
