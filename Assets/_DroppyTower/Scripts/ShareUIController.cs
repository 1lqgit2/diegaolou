using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

namespace _DroppyTower
{
    public class ShareUIController : MonoBehaviour
    {
        public enum ImageFormat
        {
            GIF,
            PNG
        }

        public enum ScaleMode
        {
            AutoHeight,
            AutoWidth
        }

        public ImageFormat ImageType
        { 
            get { return _imageType; } 
            private set { _imageType = value; }
        }

        public Texture2D ImgTex { get; set; }

        [Header("Object References")]
        public GameObject container;
        public GameObject modal;
        public GameObject mask;
        public Image staticImage;
        public GameObject noImageMsg;
        public GameObject clipPlayer;
        public GameObject noClipMsg;
        public GameObject toolbar;
        public GameObject statusbar;
        public Text statusText;
        public Image progressBar;
        public Button gifButton;
        public Button pngButton;
        public GameObject giphyLogo;
        public GameObject emLogo;

        [Header("Apperance Config")]
        public ScaleMode scaleMode = ScaleMode.AutoHeight;
        [Tooltip("侧边面板最大宽度占屏幕宽度的比例")]
        [Range(0.15f, 0.9f)]
        public float maxWidthRatio = 0.32f;
        [Tooltip("侧边面板最大高度占屏幕高度的比例")]
        [Range(0.15f, 0.9f)]
        public float maxHeightRatio = 0.44f;
        [Tooltip("面板左侧留白（UI 单位）")]
        public float edgeMargin = 32f;
        public Color buttonEnableColor = Color.white;
        public Color buttonDisableColor = Color.gray;
        [HideInInspector]
        public float clipStartDelay = 0.5f;

        RectTransform containerRT;

        #pragma warning disable 0414
        Image gifButtonImage;
        Image pngButtonImage;
        #pragma warning restore 0414

        ImageFormat _imageType = ImageFormat.PNG;

        void Awake()
        {
            gifButtonImage = gifButton.GetComponent<Image>();
            pngButtonImage = pngButton.GetComponent<Image>();

            containerRT = container.GetComponent<RectTransform>();
            FitContainerToCanvas();
            staticImage.GetComponent<RectTransform>().sizeDelta = containerRT.sizeDelta;
            clipPlayer.GetComponent<RectTransform>().sizeDelta = containerRT.sizeDelta;

            modal.SetActive(false);
            noImageMsg.SetActive(false);
            noClipMsg.SetActive(false);
            toolbar.SetActive(true);
            statusbar.SetActive(false);
            giphyLogo.SetActive(false);
            emLogo.SetActive(false);

        }

        void OnEnable()
        {
            if (ImageType == ImageFormat.GIF)
            {
                LoadAnimatedClip(clipStartDelay);
            }
            else
            {
                LoadStaticImage();
            }
        }

        void Update()
        {
            gifButtonImage.color = ImageType == ImageFormat.GIF ? buttonEnableColor : buttonDisableColor;
            pngButtonImage.color = ImageType == ImageFormat.PNG ? buttonEnableColor : buttonDisableColor;
        }

        public void SwitchFormat()
        {
            ImageType = (ImageType == ImageFormat.GIF) ? ImageFormat.PNG : ImageFormat.GIF;

            if (ImageType == ImageFormat.GIF)
            {
                LoadAnimatedClip(clipStartDelay);
            }
            else
                LoadStaticImage();
        }

        public void Share()
        {
            ShareToDouyin();
        }

        void SharePNG()
        {
            ShareToDouyin();
        }

        void ShareGIF()
        {
            // 抖音小游戏通道只分享静态截图，GIF 选项同样按静态图分享
            ShareToDouyin();
        }

        /// <summary>
        /// 调用抖音分享接口。截图为内存纹理，先落盘成 PNG 再把本地路径交给 SDK。
        /// </summary>
        void ShareToDouyin()
        {
            if (ImgTex == null)
            {
                Debug.Log("SharePNG failed: no captured screenshot.");
                return;
            }

            try
            {
                JsonData shareData = new JsonData
                {
                    ["title"] = AppInfo.Instance.APP_NAME,
                    ["desc"] = ConstructShareMessage(),
                    ["channel"] = "video"
                };

                string imagePath = SaveScreenshotForShare();
                if (!string.IsNullOrEmpty(imagePath))
                    shareData["imageUrl"] = imagePath;

                TT.ShareAppMessage(shareData);
                Debug.Log("[分享] 已调用抖音分享");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[分享] 调用抖音分享失败：" + e.Message);
            }
        }

        string SaveScreenshotForShare()
        {
            try
            {
                byte[] png = ImgTex.EncodeToPNG();
                string path = System.IO.Path.Combine(Application.persistentDataPath, "share_screenshot.png");
                System.IO.File.WriteAllBytes(path, png);
                return path;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[分享] 截图落盘失败，仅分享文字：" + e.Message);
                return null;
            }
        }

        void ShowToolbar()
        {
            toolbar.SetActive(true);
            statusbar.SetActive(false);
        }

        string ConstructShareMessage()
        {
            string msg = ScreenshotSharer.Instance.shareMessage;
            msg = msg.Replace("[score]", ScoreManager.Instance.Score.ToString());
            msg = msg.Replace("[AppName]", AppInfo.Instance.APP_NAME);
            msg = msg.Replace("[#AppName]", "#" + AppInfo.Instance.APP_NAME.Replace(" ", ""));

            return msg;
        }

        void LoadStaticImage()
        {

            if (ImgTex != null)
            {
                noImageMsg.SetActive(false);
                Sprite sprite = Sprite.Create(ImgTex, new Rect(0.0f, 0.0f, ImgTex.width, ImgTex.height), new Vector2(0.5f, 0.5f));
                Transform imgTf = staticImage.gameObject.transform;
                RectTransform imgRtf = staticImage.GetComponent<RectTransform>();
                float scaleFactor = 1;

                if (scaleMode == ScaleMode.AutoHeight)
                    scaleFactor = imgRtf.rect.width / sprite.rect.width;
                else
                    scaleFactor = imgRtf.rect.height / sprite.rect.height;
            
                staticImage.sprite = sprite;
                staticImage.SetNativeSize();
                imgTf.localScale = imgTf.localScale * scaleFactor;

                ScaleContainer(sprite.rect.width / sprite.rect.height);
            }
            else
            {
                noImageMsg.SetActive(true);
            }

            clipPlayer.SetActive(false);
            staticImage.gameObject.SetActive(true);
        }

        void LoadAnimatedClip(float playStartDelay = 0f)
        {
            StartCoroutine(CRLoadAnimatedClip(playStartDelay));
        }

        IEnumerator CRLoadAnimatedClip(float playStartDelay)
        {
            staticImage.gameObject.SetActive(false);
            clipPlayer.SetActive(true);
            yield return null;

            noClipMsg.SetActive(true);
        }

        Vector2 ClampContainerSize(float width, float aspect, float canvasW, float canvasH)
        {
            aspect = Mathf.Max(aspect, 0.01f);

            float maxW = Mathf.Min(canvasW * maxWidthRatio, canvasW - edgeMargin * 2f);
            float maxH = canvasH * maxHeightRatio;
            if (maxW <= 0f || maxH <= 0f)
                return containerRT.sizeDelta;

            float w, h;
            if (scaleMode == ScaleMode.AutoHeight)
            {
                w = Mathf.Min(width, maxW);
                h = w / aspect;
                if (h > maxH)
                {
                    h = maxH;
                    w = h * aspect;
                }
            }
            else
            {
                h = Mathf.Min(containerRT.sizeDelta.y, maxH);
                w = h * aspect;
                if (w > maxW)
                {
                    w = maxW;
                    h = w / aspect;
                }
            }
            return new Vector2(w, h);
        }

        /// <summary>
        /// 面板尺寸按屏幕比例收敛，并贴左留边距，避免在长屏手机上过大挡住玩法
        /// </summary>
        void FitContainerToCanvas()
        {
            RectTransform canvasRT = containerRT.parent as RectTransform;
            if (canvasRT == null)
                return;

            float aspect = containerRT.sizeDelta.x / Mathf.Max(containerRT.sizeDelta.y, 1f);
            containerRT.sizeDelta = ClampContainerSize(containerRT.sizeDelta.x, aspect,
                canvasRT.rect.width, canvasRT.rect.height);
            containerRT.anchoredPosition = new Vector2(edgeMargin, containerRT.anchoredPosition.y);
        }

        void ScaleContainer(float aspect)
        {
            RectTransform canvasRT = containerRT.parent as RectTransform;
            float canvasW = canvasRT != null ? canvasRT.rect.width : containerRT.sizeDelta.x;
            float canvasH = canvasRT != null ? canvasRT.rect.height : containerRT.sizeDelta.y;

            containerRT.sizeDelta = ClampContainerSize(containerRT.sizeDelta.x, aspect, canvasW, canvasH);
        }

    }
}