using UnityEngine;
using System.Collections;


namespace _DroppyTower
{
    public class ScoreReporter : MonoBehaviour
    {
        [Header("Check to disable automatic score reporting")]
        public bool disable = false;

        [Header("排行榜名称（抖音后台配置后使用）")]
        public string leaderboardName = "Score";

    }
}