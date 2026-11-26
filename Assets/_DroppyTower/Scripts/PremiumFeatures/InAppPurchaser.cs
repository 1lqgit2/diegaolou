using UnityEngine;
using System.Collections;


namespace _DroppyTower
{
    public class InAppPurchaser : MonoBehaviour
    {
        public static InAppPurchaser Instance { get; private set; }

        [System.Serializable]
        public struct CoinPack
        {
            public string productName;
            public string priceString;
            public int coinValue;
        }

        [Header("Name of Remove Ads products")]
        public string removeAds = "Remove_Ads";

        [Header("Name of coin pack products")]
        public CoinPack[] coinPacks;

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

        // 抖音小游戏暂不接入应用内购买（原第三方 IAP 通道已移除）。
        public void Purchase(string productName)
        {
            Debug.LogWarning("[内购] 当前版本未接入内购渠道：" + productName);
        }

        public void RestorePurchase()
        {
            Debug.LogWarning("[内购] 当前版本不支持恢复购买");
        }
    }
}

