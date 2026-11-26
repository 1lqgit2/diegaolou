using UnityEngine;
using System.Collections;

namespace _DroppyTower
{
    /// <summary>
    /// 截图分享器。原版依赖 Easy Mobile 的 GIF 录制/Giphy 上传，已全部移除，
    /// 只保留「游戏结束时抓一张静态截图」，供分享面板显示、交给抖音分享使用。
    /// </summary>
    public class ScreenshotSharer : MonoBehaviour
    {
        public enum SharedImageType
        {
            PNG,
            GIF,
            Both
        }

        [Header("Check to disable sharing")]
        public bool disableSharing = false;

        [Header("Sharing Config")]
        public SharedImageType sharedImageFormat = SharedImageType.PNG;
        [Tooltip("Any instances of [score] will be replaced by the actual score achieved in the last game, [AppName] will be replaced by the app name declared in AppInfo")]
        [TextArea(3, 3)]
        public string shareMessage = "太棒了！我在[AppName]里拿下了 [score] 分！[#AppName]";
        public string pngFilename = "screenshot";

        public static ScreenshotSharer Instance { get; private set; }

        public Texture2D CapturedScreenshot { get; private set; }

        void Awake()
        {
            if (Instance)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 抖音通道只支持静态截图分享，不再录制 GIF
            sharedImageFormat = SharedImageType.PNG;
        }

        void OnEnable()
        {
            PlayerController.PlayerDied += PlayerController_PlayerDied;
        }

        void OnDisable()
        {
            PlayerController.PlayerDied -= PlayerController_PlayerDied;
        }

        void PlayerController_PlayerDied()
        {
            // PremiumFeaturesManager 可能没进场景 / 是 inactive 的，这里必须判空
            bool premiumOn = PremiumFeaturesManager.Instance == null || PremiumFeaturesManager.Instance.enablePremiumFeatures;

            if (premiumOn && !disableSharing
                && (sharedImageFormat == SharedImageType.PNG || sharedImageFormat == SharedImageType.Both))
            {
                StartCoroutine(CRCaptureScreenshot());
            }
        }

        IEnumerator CRCaptureScreenshot()
        {
            // Wait for right timing to take screenshot
            yield return new WaitForSeconds(2);

            // Temporarily render the camera content to our screenshotRenderTexture.
            RenderTexture tempRT = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            Camera.main.targetTexture = tempRT;
            Camera.main.Render();
            yield return null;
            Camera.main.targetTexture = null;
            yield return null;

            RenderTexture.active = tempRT;

            if (CapturedScreenshot == null)
                CapturedScreenshot = new Texture2D(tempRT.width, tempRT.height, TextureFormat.RGB24, false);

            CapturedScreenshot.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
            CapturedScreenshot.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(tempRT);
        }
    }
}
