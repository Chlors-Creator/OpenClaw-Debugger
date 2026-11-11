namespace OpenClawDebugger;

/// <summary>
/// 缩略图编码。原实现依赖 WPF 的 BitmapDecoder，属于 Windows 专属能力，
/// 因此抽成接口由各平台提供实现。
/// </summary>
public interface IThumbnailEncoder
{
    /// <summary>
    /// 把原图按长边缩放到 <paramref name="maxEdge"/> 以内并编码为 PNG。
    /// 无法解码时抛出 <see cref="InvalidDataException"/>，由调用方降级为占位图。
    /// </summary>
    byte[] CreatePngThumbnail(byte[] original, int maxEdge);
}
