using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace _DroppyTower
{
    public class PremiumFeaturesManager : MonoBehaviour
    {
        public static PremiumFeaturesManager Instance { get; private set; }

        [Header("是否启用高级功能入口（分享 / 排行榜 / 内购按钮）")]
        public bool enablePremiumFeatures = true;

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