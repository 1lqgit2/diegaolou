using UnityEngine;
using UnityEngine.UI;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

/// <summary>
/// 侧边栏引导面板（cebianPanel）控制器。
///
/// 打开链路：MainCanvas/cebianBtn 的 onClick → cebianPanel.SetActive(true)（场景里已绑好）
/// 面板内：
///   box/go    → 跳抖音侧边栏（TT.NavigateToScene, scene=sidebar）
///   box/close → 关闭面板
///
/// 历史问题：这个脚本原来没有挂在任何物体上，box/go 和 box/close 的 onClick 在预制体里也是空的，
/// 表现就是「cebianBtn 能打开面板，但点前往侧边栏、点关闭都毫无反应」。
/// 现在由 UIManager.SetupCebianPanel() 在启动时补挂组件，并在 Awake/OnEnable 里自动找按钮绑定，
/// 不依赖 Inspector 里的引用，避免场景/prefab 序列化再丢一次。
/// </summary>
[DisallowMultipleComponent]
public class cebian : MonoBehaviour
{
    [Tooltip("前往侧边栏按钮（留空会自动找 box/go）")]
    public Button btn_Go;

    [Tooltip("关闭按钮（留空会自动找 box/close）")]
    public Button btn_Close;

    void Awake()
    {
        Resolve();
        Bind();
    }

    // 面板是被 cebianBtn SetActive(true) 打开的，每次打开都会走 OnEnable，
    // 在这里重新绑一次，防止有人在 Inspector 里把引用清空或存成 Missing。
    void OnEnable()
    {
        Resolve();
        Bind();
    }

    void Resolve()
    {
        if (btn_Go == null)
        {
            Transform t = transform.Find("box/go");
            if (t != null) btn_Go = t.GetComponent<Button>();
        }

        if (btn_Close == null)
        {
            Transform t = transform.Find("box/close");
            if (t != null) btn_Close = t.GetComponent<Button>();
        }
    }

    void Bind()
    {
        if (btn_Go != null)
        {
            btn_Go.onClick.RemoveListener(NavigateToSideBar);
            btn_Go.onClick.AddListener(NavigateToSideBar);
        }
        else
        {
            Debug.LogWarning("[侧边栏] 没找到 box/go 按钮，无法跳转侧边栏");
        }

        if (btn_Close != null)
        {
            btn_Close.onClick.RemoveListener(ClosePanel);
            btn_Close.onClick.AddListener(ClosePanel);
        }
    }

    /// <summary>关闭引导面板。不关的话面板会一直压着游戏，悬浮按钮也回不来。</summary>
    public void ClosePanel()
    {
        gameObject.SetActive(false);
    }

    /// <summary>跳转到抖音侧边栏。</summary>
    public void NavigateToSideBar()
    {
        Debug.Log("[侧边栏] 点击前往侧边栏");

        try
        {
            var data = new JsonData
            {
                ["scene"] = "sidebar",
            };

            TT.NavigateToScene(data,
                () =>
                {
                    Debug.Log("[侧边栏] navigate to scene success");
                    // 跳转成功才收起引导层：失败时保留面板，用户还能重试或点关闭
                    ClosePanel();
                },
                () =>
                {
                    Debug.Log("[侧边栏] navigate to scene complete");
                    ClosePanel();
                },
                (errCode, errMsg) =>
                {
                    Debug.LogWarning($"[侧边栏] navigate to scene error, errCode:{errCode}, errMsg:{errMsg}");
                });
        }
        catch (System.Exception e)
        {
            Debug.LogError("[侧边栏] 跳转到侧边栏异常：" + e.Message);
        }
    }
}
