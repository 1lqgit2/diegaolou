using UnityEngine;

/// <summary>
/// 侧边面板（cebianPanel）屏幕自适应。
///
/// 面板的原有布局是按 640×1136 的设计分辨率手摆的：box 固定 540×566，
/// 里面所有内容（标题、两段说明、前往按钮、关闭按钮）都挂在 box 下面。
/// 只要画布的逻辑宽度不是 640（比如 19.5:9 的手机，逻辑宽度只有 ~525），
/// 540 宽的 box 就会顶出屏幕两侧，关闭按钮也会被裁掉一部分。
///
/// 这里不做重排，只按画布实际尺寸给 box 乘一个统一缩放：
///   scale = min(1, 可用宽/540, 可用高/566)
/// 因为内容是 box 的子节点，缩放会一起生效，视觉比例完全不变，
/// 既不会在窄屏出屏，也不会在宽屏变得很小。
/// </summary>
[DisallowMultipleComponent]
public class CebianPanelFit : MonoBehaviour
{
    [Header("box 的设计尺寸（缩放基准）")]
    public Vector2 designSize = new Vector2(540f, 566.32f);

    [Header("面板最多占画布可用宽度 / 高度的比例")]
    [Range(0.4f, 1f)] public float maxWidthRatio = 0.86f;
    [Range(0.3f, 1f)] public float maxHeightRatio = 0.70f;

    RectTransform self;
    RectTransform box;
    Vector2 lastSize;

    void Awake()
    {
        Cache();
        Fit();
    }

    void OnEnable()
    {
        Cache();
        Fit();
    }

    void Update()
    {
        if (self == null || box == null)
        {
            Cache();
            if (self == null || box == null) return;
        }

        Vector2 size = self.rect.size;
        // 旋转屏幕 / 开发者工具改窗口尺寸时重新适配
        if ((size - lastSize).sqrMagnitude > 0.01f)
        {
            Fit();
        }
    }

    void Cache()
    {
        self = transform as RectTransform;
        if (box == null)
        {
            box = transform.Find("box") as RectTransform;
        }
    }

    /// <summary>
    /// 按当前画布尺寸重新计算 box 的缩放。可在 Inspector 右键菜单里手动触发。
    /// </summary>
    [ContextMenu("重新适配面板")]
    public void Fit()
    {
        if (self == null) self = transform as RectTransform;
        if (box == null) box = transform.Find("box") as RectTransform;
        if (self == null || box == null) return;
        if (designSize.x <= 0.01f || designSize.y <= 0.01f) return;

        Vector2 canvas = GetCanvasSize();
        if (canvas.x <= 1f || canvas.y <= 1f) return; // 布局还没算出来，等下一帧

        lastSize = self.rect.size;

        float scale = 1f;
        scale = Mathf.Min(scale, canvas.x * maxWidthRatio / designSize.x);
        scale = Mathf.Min(scale, canvas.y * maxHeightRatio / designSize.y);
        // 下限只是兜底，避免极端尺寸下面板缩成一个点
        scale = Mathf.Clamp(scale, 0.25f, 1f);

        box.localScale = new Vector3(scale, scale, 1f);
    }

    Vector2 GetCanvasSize()
    {
        if (self != null)
        {
            Vector2 size = self.rect.size;
            if (size.x > 1f && size.y > 1f) return size;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            var rt = root.transform as RectTransform;
            if (rt != null && rt.rect.width > 1f && rt.rect.height > 1f)
            {
                return rt.rect.size;
            }
        }

        return new Vector2(Screen.width, Screen.height);
    }
}
