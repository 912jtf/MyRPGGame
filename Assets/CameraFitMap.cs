using UnityEngine;

/// <summary>
/// 挂在主相机上，运行时按当前屏幕比例和地图尺寸自动设置 Orthographic Size，
/// 保证打包后上下左右都能完整显示地图，不裁边。
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFitMap : MonoBehaviour
{
    [Header("地图世界尺寸")]
    [Tooltip("地图 Y 方向总高度（世界单位）")]
    public float mapWorldHeight = 10f;

    [Tooltip("地图 X 方向总宽度。设为 0 则只按高度适配")]
    public float mapWorldWidth = 0f;

    [Tooltip("四边多留一点余量（世界单位），避免贴边被裁")]
    public float padding = 0.5f;

    private void Awake()
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null || !cam.orthographic) return;

        float aspect = (float)Screen.width / Screen.height;
        float halfH = (mapWorldHeight + padding * 2f) * 0.5f;
        float size = halfH;

        if (mapWorldWidth > 0f)
        {
            float halfW = (mapWorldWidth + padding * 2f) * 0.5f;
            float sizeW = halfW / aspect;
            size = Mathf.Max(halfH, sizeW);
        }

        cam.orthographicSize = size;
    }
}
