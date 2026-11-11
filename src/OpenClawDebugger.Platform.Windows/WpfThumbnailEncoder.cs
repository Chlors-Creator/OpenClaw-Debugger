using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenClawDebugger;

/// <summary>
/// 用 WPF 图像栈解码并缩放缩略图。逻辑与原先内嵌在 StickerThumbnailCache 中的实现一致，
/// 只是把"解码失败"统一归一化成 InvalidDataException，交给调用方降级为占位图。
/// </summary>
public sealed class WpfThumbnailEncoder : IThumbnailEncoder
{
    public byte[] CreatePngThumbnail(byte[] original, int maxEdge)
    {
        try
        {
            using var input = new MemoryStream(original, writable: false);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault()
                ?? throw new InvalidDataException("图片没有可用的图像帧。");
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
                throw new InvalidDataException("图片尺寸无效。");

            var scale = Math.Min(1d, Math.Min(
                maxEdge / (double)frame.PixelWidth,
                maxEdge / (double)frame.PixelHeight));
            BitmapSource source = frame;
            if (scale < 0.999d)
            {
                var transformed = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                transformed.Freeze();
                source = transformed;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("无法解码该图片以生成缩略图。", ex);
        }
    }
}
