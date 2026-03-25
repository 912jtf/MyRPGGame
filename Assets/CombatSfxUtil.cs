using UnityEngine;

/// <summary>
/// 战斗音效统一入口：clip 为空则静默跳过。
/// 使用 PlayClipAtPoint，适合 2D 顶视角（依赖场景中的 AudioListener，一般为 Main Camera）。
/// </summary>
public static class CombatSfxUtil
{
    public static void Play2D(AudioClip clip, Vector3 worldPosition, float volumeScale = 1f)
    {
        if (clip == null || volumeScale <= 0f)
            return;
        AudioSource.PlayClipAtPoint(clip, worldPosition, Mathf.Clamp01(volumeScale));
    }
}
