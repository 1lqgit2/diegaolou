using UnityEngine;
using System.Collections;


namespace _DroppyTower
{
    /// <summary>
    /// 原 Easy Mobile 广告封装，已整体下线。
    /// Banner / 插屏 / 激励视频现在全部由抖音 AdSDK 的 AdManager 负责：
    ///   - 激励视频：UIManager.WatchRewardedAd → AdConfirm → AdManager.ShowRewardedVideoAd
    ///   - 插屏：UIManager.TryShowSettingsInterstitial / TryShowGameOverInterstitial → AdManager.ShowInterstitialAd
    /// 这个类只保留字段与单例，用于兼容场景里已挂的组件，不再调用任何广告 SDK。
    /// </summary>
    public class AdDisplayer : MonoBehaviour
    {
        

        public enum BannerAdPos
        {
            Top,
            Bottom
        }
        public static AdDisplayer Instance { get; private set; }

        [Header("Banner Ad Display Config")]
        [Tooltip("Whether or not to show banner ad")]
        public bool showBannerAd = true;
        public BannerAdPos bannerAdPosition = BannerAdPos.Bottom;

        [Header("Interstitial Ad Display Config")]
        [Tooltip("Whether or not to show interstitial ad")]
        public bool showInterstitialAd = true;
        [Tooltip("Show interstitial ad every [how many] games")]
        public int gamesPerInterstitial = 3;
        [Tooltip("How many seconds after game over that interstitial ad is shown")]
        public float showInterstitialDelay = 2f;

        [Header("Rewarded Ad Display Config")]
        [Tooltip("Check to allow watching ad to earn coins")]
        public bool watchAdToEarnCoins = true;
        [Tooltip("How many coins the user earns after watching a rewarded ad")]
        public int rewardedCoins = 50;

        void Awake()
        {
            if (Instance)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
        }

    }
}
