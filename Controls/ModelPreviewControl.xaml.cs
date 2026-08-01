using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Controls;

public enum ModelRenderMode
{
    Textured,
    Solid,
    Wireframe
}

internal enum TextureAlphaMode
{
    Opaque,
    Straight,
    Inverted
}

internal enum TextureColorKeyMode
{
    None,
    DarkOnly,
    Chroma
}

public sealed partial class ModelPreviewControl : UserControl
{
    private const int MaximumRasterDimension = 1400;
    private const int MaximumRenderedTriangles = 120_000;
    private const byte BackgroundBlue = 32;
    private const byte BackgroundGreen = 21;
    private const byte BackgroundRed = 13;
    private const float DefaultYaw = 2.59f;
    private const float DefaultPitch = 0.18f;
    private const float DefaultZoom = 0.86f;
    private IReadOnlyList<ModelRenderPart> _parts = Array.Empty<ModelRenderPart>();
    private string _modelName = string.Empty;
    private ModelRenderMode _mode = ModelRenderMode.Solid;
    private float _yaw = DefaultYaw;
    private float _pitch = DefaultPitch;
    private float _zoom = DefaultZoom;
    private Windows.Foundation.Point _lastPointer;
    private uint? _capturedPointerId;
    private bool _renderQueued;

    public ModelPreviewControl()
    {
        InitializeComponent();
    }

    public void SetMesh(PmfMesh? mesh)
    {
        _parts = mesh is null
            ? Array.Empty<ModelRenderPart>()
            : new[] { new ModelRenderPart(string.Empty, mesh, null, string.Empty) };
        _modelName = string.Empty;
        ResetCamera();
        TextureModeButton.IsEnabled = false;
        SetMode(ModelRenderMode.Solid);
        EmptyText.Visibility = mesh is null ? Visibility.Visible : Visibility.Collapsed;
        UpdateHint();
        ScheduleRender();
    }

    public void SetComposite(IReadOnlyList<ModelRenderPart> parts, string modelName)
    {
        _parts = parts;
        _modelName = modelName;
        ResetCamera();
        TextureModeButton.IsEnabled = parts.Any(part => part.Texture is not null);
        SetMode(TextureModeButton.IsEnabled ? ModelRenderMode.Textured : ModelRenderMode.Solid);
        EmptyText.Visibility = parts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateHint();
        ScheduleRender();
    }

    public void SetTexture(DecodedTexture? texture, string? textureName, bool selectTexturedMode = true)
    {
        if (_parts.Count == 0) return;
        ModelRenderPart part = _parts[0];
        _parts = new[] { part with { Texture = texture, TextureName = textureName ?? string.Empty } };
        TextureModeButton.IsEnabled = texture is not null;
        if (texture is not null && selectTexturedMode) SetMode(ModelRenderMode.Textured);
        else if (texture is null && _mode == ModelRenderMode.Textured) SetMode(ModelRenderMode.Solid);
        UpdateHint();
        ScheduleRender();
    }

    private void SetMode(ModelRenderMode mode)
    {
        if (mode == ModelRenderMode.Textured && !_parts.Any(part => part.Texture is not null)) mode = ModelRenderMode.Solid;
        _mode = mode;
        TextureModeButton.IsChecked = mode == ModelRenderMode.Textured;
        SolidModeButton.IsChecked = mode == ModelRenderMode.Solid;
        WireframeModeButton.IsChecked = mode == ModelRenderMode.Wireframe;
        UpdateHint();
        ScheduleRender();
    }

    private void ResetCamera()
    {
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _zoom = DefaultZoom;
    }

    private void UpdateHint()
    {
        if (_parts.Count == 0)
        {
            HintText.Text = "模型预览 · 拖拽旋转 · 滚轮缩放";
            return;
        }

        string mode = _mode switch
        {
            ModelRenderMode.Textured when _parts.Count == 1 && !string.IsNullOrWhiteSpace(_parts[0].TextureName) => $"贴图 · {_parts[0].TextureName}",
            ModelRenderMode.Textured => "贴图",
            ModelRenderMode.Wireframe => "线框",
            _ => "实体着色"
        };
        int vertices = _parts.Sum(part => part.Mesh.Vertices.Count);
        long triangles = _parts.Sum(part => (long)part.Mesh.DeclaredTriangleCount);
        string parts = _parts.Count > 1 ? $" · {_parts.Count:N0} 部件" : string.Empty;
        string name = string.IsNullOrWhiteSpace(_modelName) ? string.Empty : $"{_modelName} · ";
        HintText.Text = $"{name}{mode}{parts} · {vertices:N0} 顶点 · {triangles:N0} 面";
    }

    private void ScheduleRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _renderQueued = false;
            RenderMesh();
        });
    }

    private void RenderMesh()
    {
        if (_parts.Count == 0 || Surface.ActualWidth < 20 || Surface.ActualHeight < 20)
        {
            RasterImage.Source = null;
            return;
        }

        double rasterScale = Math.Min(1d, MaximumRasterDimension / Math.Max(Surface.ActualWidth, Surface.ActualHeight));
        int width = Math.Max(1, (int)Math.Round(Surface.ActualWidth * rasterScale));
        int height = Math.Max(1, (int)Math.Round(Surface.ActualHeight * rasterScale));
        byte[] pixels;
        try
        {
            pixels = RenderPixels(_parts, _mode, width, height);
        }
        catch
        {
            RasterImage.Source = null;
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        var bitmap = new WriteableBitmap(width, height);
        using (Stream stream = bitmap.PixelBuffer.AsStream())
            stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        RasterImage.Source = bitmap;
        EmptyText.Visibility = Visibility.Collapsed;
    }

    private byte[] RenderPixels(IReadOnlyList<ModelRenderPart> parts, ModelRenderMode mode, int width, int height)
    {
        if (mode == ModelRenderMode.Textured)
            return RenderPixelsCore(parts, ModelRenderMode.Textured, width, height, null, false);

        return RenderPixelsCore(parts, mode, width, height, null, false);
    }

    private byte[] RenderPixelsCore(
        IReadOnlyList<ModelRenderPart> parts,
        ModelRenderMode mode,
        int width,
        int height,
        byte[]? basePixels,
        bool preserveLowContrastTexture)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        if (basePixels is not null)
        {
            basePixels.CopyTo(pixels, 0);
        }
        else
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = BackgroundBlue;
                pixels[i + 1] = BackgroundGreen;
                pixels[i + 2] = BackgroundRed;
                pixels[i + 3] = 255;
            }
        }

        ModelRenderPart? firstPart = parts.FirstOrDefault(part => part.Mesh.Vertices.Count > 0);
        if (firstPart is null) return pixels;

        Vector3 min = firstPart.Mesh.Vertices[0];
        Vector3 max = min;
        foreach (Vector3 vertex in parts.SelectMany(part => part.Mesh.Vertices))
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }

        Vector3 center = (min + max) * 0.5f;
        float extent = Math.Max(0.0001f, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)));
        Matrix4x4 rotation = Matrix4x4.CreateRotationY(_yaw) * Matrix4x4.CreateRotationX(_pitch);
        float scale = Math.Min(width, height) * 0.82f / extent * _zoom;
        float centerX = width * 0.5f;
        float centerY = height * 0.52f;
        float[]? depthBuffer = mode == ModelRenderMode.Wireframe
            ? null
            : Enumerable.Repeat(float.NegativeInfinity, width * height).ToArray();
        Vector3 light = Vector3.Normalize(new Vector3(-0.35f, 0.65f, 0.9f));
        int renderedTriangles = 0;
        foreach (ModelRenderPart part in parts)
        {
            PmfMesh mesh = part.Mesh;
            IReadOnlyList<Vector3> source = mesh.Vertices;
            var transformed = new Vector3[source.Count];
            var projected = new Vector2[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                transformed[i] = Vector3.Transform(source[i] - center, rotation);
                projected[i] = new Vector2(centerX + transformed[i].X * scale, centerY - transformed[i].Y * scale);
            }

            IReadOnlyList<ushort> indices = mesh.Indices;
            if (mode == ModelRenderMode.Wireframe)
            {
                for (int offset = 0; offset + 2 < indices.Count; offset += 3)
                {
                    if (renderedTriangles++ >= MaximumRenderedTriangles) return pixels;
                    Vector2 a = projected[indices[offset]];
                    Vector2 b = projected[indices[offset + 1]];
                    Vector2 c = projected[indices[offset + 2]];
                    DrawLine(pixels, width, height, a, b);
                    DrawLine(pixels, width, height, b, c);
                    DrawLine(pixels, width, height, c, a);
                }
                continue;
            }

            bool useTexture = mode == ModelRenderMode.Textured && part.Texture is not null && mesh.TextureCoordinates.Count == source.Count;
            TextureAlphaMode textureAlphaMode = useTexture && part.Texture is not null
                ? DetectTextureAlphaMode(mesh, part.Texture)
                : TextureAlphaMode.Opaque;
            TextureColorKeyMode colorKeyMode = useTexture && textureAlphaMode == TextureAlphaMode.Opaque
                ? GetTextureColorKeyMode(part)
                : TextureColorKeyMode.None;
            for (int offset = 0; offset + 2 < indices.Count; offset += 3)
            {
                if (renderedTriangles++ >= MaximumRenderedTriangles) return pixels;
                int ia = indices[offset];
                int ib = indices[offset + 1];
                int ic = indices[offset + 2];
                Vector3 normal = Vector3.Cross(transformed[ib] - transformed[ia], transformed[ic] - transformed[ia]);
                float normalLengthSquared = normal.LengthSquared();
                float brightness = mode == ModelRenderMode.Textured ? 1f : 0.78f;
                if (normalLengthSquared > 1e-20f)
                {
                    normal = Vector3.Normalize(normal);
                    float lightAmount = MathF.Abs(Vector3.Dot(normal, light));
                    brightness = mode == ModelRenderMode.Textured
                        ? 0.98f + 0.02f * lightAmount
                        : 0.28f + 0.72f * lightAmount;
                }
                RasterizeTriangle(
                    pixels, depthBuffer!, width, height,
                    projected[ia], projected[ib], projected[ic],
                    transformed[ia].Z, transformed[ib].Z, transformed[ic].Z,
                    useTexture ? mesh.TextureCoordinates[ia] : default,
                    useTexture ? mesh.TextureCoordinates[ib] : default,
                    useTexture ? mesh.TextureCoordinates[ic] : default,
                    useTexture ? part.Texture : null,
                    textureAlphaMode,
                    colorKeyMode,
                    brightness,
                    preserveLowContrastTexture);
            }
        }
        return pixels;
    }

    private static void RasterizeTriangle(
        byte[] pixels,
        float[] depthBuffer,
        int width,
        int height,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        float za,
        float zb,
        float zc,
        Vector2 uva,
        Vector2 uvb,
        Vector2 uvc,
        DecodedTexture? texture,
        TextureAlphaMode textureAlphaMode,
        TextureColorKeyMode colorKeyMode,
        float brightness,
        bool preserveLowContrastTexture)
    {
        float area = Edge(a, b, c);
        if (MathF.Abs(area) < 0.0001f) return;
        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, width - 1);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, height - 1);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                float wa = Edge(b, c, point) / area;
                float wb = Edge(c, a, point) / area;
                float wc = Edge(a, b, point) / area;
                if (wa < -0.0001f || wb < -0.0001f || wc < -0.0001f) continue;

                int pixelIndex = y * width + x;
                float depth = wa * za + wb * zb + wc * zc;
                // Character faces, eyes and decorations are often later material passes on
                // exactly the same mesh. Let equal-depth pixels through so their alpha layer
                // can be composited over the base skin instead of disappearing behind it.
                if (depth < depthBuffer[pixelIndex] - 0.000001f) continue;

                byte blue;
                byte green;
                byte red;
                byte alpha = 255;
                if (texture is not null)
                {
                    Vector2 uv = wa * uva + wb * uvb + wc * uvc;
                    float wrappedU = uv.X - MathF.Floor(uv.X);
                    float wrappedV = uv.Y - MathF.Floor(uv.Y);
                    SampleTextureBilinear(texture, wrappedU, wrappedV, out blue, out green, out red, out alpha);
                    if (IsTextureColorKey(blue, green, red, colorKeyMode))
                        continue;
                    blue = ApplyBrightness(blue, brightness);
                    green = ApplyBrightness(green, brightness);
                    red = ApplyBrightness(red, brightness);
                    alpha = ApplyTextureAlphaMode(alpha, textureAlphaMode);
                    if (alpha < 8) continue;
                    if (preserveLowContrastTexture && GetBackgroundContrast(blue, green, red) < 36)
                        continue;
                }
                else
                {
                    blue = (byte)(214 * brightness);
                    green = (byte)(151 * brightness);
                    red = (byte)(76 * brightness);
                }

                depthBuffer[pixelIndex] = depth;
                int target = pixelIndex * 4;
                if (alpha >= 250)
                {
                    pixels[target] = blue;
                    pixels[target + 1] = green;
                    pixels[target + 2] = red;
                }
                else
                {
                    int inverse = 255 - alpha;
                    pixels[target] = (byte)((blue * alpha + pixels[target] * inverse) / 255);
                    pixels[target + 1] = (byte)((green * alpha + pixels[target + 1] * inverse) / 255);
                    pixels[target + 2] = (byte)((red * alpha + pixels[target + 2] * inverse) / 255);
                }
            }
        }
    }


    private static void SampleTextureBilinear(
        DecodedTexture texture,
        float wrappedU,
        float wrappedV,
        out byte blue,
        out byte green,
        out byte red,
        out byte alpha)
    {
        float textureX = wrappedU * Math.Max(0, texture.Width - 1);
        float textureY = wrappedV * Math.Max(0, texture.Height - 1);
        int x0 = Math.Clamp((int)MathF.Floor(textureX), 0, texture.Width - 1);
        int y0 = Math.Clamp((int)MathF.Floor(textureY), 0, texture.Height - 1);
        int x1 = Math.Min(texture.Width - 1, x0 + 1);
        int y1 = Math.Min(texture.Height - 1, y0 + 1);
        float tx = textureX - x0;
        float ty = textureY - y0;

        int o00 = (y0 * texture.Width + x0) * 4;
        int o10 = (y0 * texture.Width + x1) * 4;
        int o01 = (y1 * texture.Width + x0) * 4;
        int o11 = (y1 * texture.Width + x1) * 4;

        blue = InterpolateTextureChannel(texture.BgraPixels[o00], texture.BgraPixels[o10], texture.BgraPixels[o01], texture.BgraPixels[o11], tx, ty);
        green = InterpolateTextureChannel(texture.BgraPixels[o00 + 1], texture.BgraPixels[o10 + 1], texture.BgraPixels[o01 + 1], texture.BgraPixels[o11 + 1], tx, ty);
        red = InterpolateTextureChannel(texture.BgraPixels[o00 + 2], texture.BgraPixels[o10 + 2], texture.BgraPixels[o01 + 2], texture.BgraPixels[o11 + 2], tx, ty);
        alpha = InterpolateTextureChannel(texture.BgraPixels[o00 + 3], texture.BgraPixels[o10 + 3], texture.BgraPixels[o01 + 3], texture.BgraPixels[o11 + 3], tx, ty);
    }

    private static byte InterpolateTextureChannel(byte topLeft, byte topRight, byte bottomLeft, byte bottomRight, float tx, float ty)
    {
        float top = topLeft + (topRight - topLeft) * tx;
        float bottom = bottomLeft + (bottomRight - bottomLeft) * tx;
        return ClampToByte(top + (bottom - top) * ty);
    }

    private static byte ApplyBrightness(byte value, float brightness) =>
        ClampToByte(value * brightness);

    private static byte ClampToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    private static TextureColorKeyMode GetTextureColorKeyMode(ModelRenderPart part)
    {
        string name = $"{part.Name} {part.TextureName}".ToLowerInvariant();
        if (name.Contains("qz_") ||
            name.Contains("xw_") ||
            name.Contains("sy_"))
            return TextureColorKeyMode.Chroma;

        if (name.Contains("mza_") ||
            name.Contains("mzb_") ||
            name.Contains("mzc_"))
            return TextureColorKeyMode.DarkOnly;

        return TextureColorKeyMode.None;
    }

    private static bool IsTextureColorKey(byte blue, byte green, byte red, TextureColorKeyMode mode)
    {
        if (mode == TextureColorKeyMode.None) return false;

        int maximum = Math.Max(red, Math.Max(green, blue));
        int minimum = Math.Min(red, Math.Min(green, blue));

        if (maximum <= 12 && maximum - minimum <= 5)
            return true;

        if (mode == TextureColorKeyMode.DarkOnly) return false;

        bool chromaBlue = blue >= 120 &&
                          red <= 90 &&
                          blue >= green + 28 &&
                          blue >= red + 55;
        if (chromaBlue)
            return true;

        bool chromaCyan = blue >= 105 &&
                          green >= 95 &&
                          red <= 70 &&
                          Math.Abs(blue - green) <= 80;
        return chromaCyan;
    }

    private static byte ApplyTextureAlphaMode(byte alpha, TextureAlphaMode mode) => mode switch
    {
        TextureAlphaMode.Inverted => (byte)(255 - alpha),
        TextureAlphaMode.Opaque => 255,
        _ => alpha
    };

    private static TextureAlphaMode DetectTextureAlphaMode(PmfMesh mesh, DecodedTexture texture)
    {
        if (mesh.TextureCoordinates.Count != mesh.Vertices.Count || mesh.Indices.Count < 3)
            return TextureAlphaMode.Opaque;

        int triangleCount = mesh.Indices.Count / 3;
        int stride = Math.Max(1, triangleCount / 4096);
        int samples = 0;
        int low = 0;
        int middle = 0;
        int high = 0;
        long lowSignal = 0;
        long highSignal = 0;

        for (int triangle = 0; triangle < triangleCount; triangle += stride)
        {
            int offset = triangle * 3;
            if (offset + 2 >= mesh.Indices.Count) break;
            Vector2 uva = mesh.TextureCoordinates[mesh.Indices[offset]];
            Vector2 uvb = mesh.TextureCoordinates[mesh.Indices[offset + 1]];
            Vector2 uvc = mesh.TextureCoordinates[mesh.Indices[offset + 2]];
            AccumulateTextureAlphaSample(texture, uva, ref samples, ref low, ref middle, ref high, ref lowSignal, ref highSignal);
            AccumulateTextureAlphaSample(texture, uvb, ref samples, ref low, ref middle, ref high, ref lowSignal, ref highSignal);
            AccumulateTextureAlphaSample(texture, uvc, ref samples, ref low, ref middle, ref high, ref lowSignal, ref highSignal);
            AccumulateTextureAlphaSample(texture, (uva + uvb + uvc) / 3f, ref samples, ref low, ref middle, ref high, ref lowSignal, ref highSignal);
        }

        if (samples == 0) return TextureAlphaMode.Opaque;
        if (low == 0 && middle == 0) return TextureAlphaMode.Opaque;
        if (high == 0 && middle == 0) return TextureAlphaMode.Inverted;
        if (middle > samples / 4) return TextureAlphaMode.Opaque;

        double lowRatio = low / (double)samples;
        double highRatio = high / (double)samples;
        double lowAverageSignal = low == 0 ? 0 : lowSignal / (double)low;
        double highAverageSignal = high == 0 ? 0 : highSignal / (double)high;

        if (lowRatio > 0.18 && low > high * 3 / 2 && lowAverageSignal > highAverageSignal + 10)
            return TextureAlphaMode.Inverted;
        if (highRatio > 0.18 && high > low * 3 / 2)
            return TextureAlphaMode.Straight;
        return TextureAlphaMode.Opaque;
    }

    private static void AccumulateTextureAlphaSample(
        DecodedTexture texture,
        Vector2 uv,
        ref int samples,
        ref int low,
        ref int middle,
        ref int high,
        ref long lowSignal,
        ref long highSignal)
    {
        float wrappedU = uv.X - MathF.Floor(uv.X);
        float wrappedV = uv.Y - MathF.Floor(uv.Y);
        int textureX = Math.Clamp((int)(wrappedU * texture.Width), 0, texture.Width - 1);
        int textureY = Math.Clamp((int)(wrappedV * texture.Height), 0, texture.Height - 1);
        int textureOffset = (textureY * texture.Width + textureX) * 4;
        byte alpha = texture.BgraPixels[textureOffset + 3];
        int signal = GetColorSignal(
            texture.BgraPixels[textureOffset],
            texture.BgraPixels[textureOffset + 1],
            texture.BgraPixels[textureOffset + 2]);

        samples++;
        if (alpha <= 24)
        {
            low++;
            lowSignal += signal;
        }
        else if (alpha >= 231)
        {
            high++;
            highSignal += signal;
        }
        else
        {
            middle++;
        }
    }

    private static int GetColorSignal(byte blue, byte green, byte red)
    {
        int maximum = Math.Max(red, Math.Max(green, blue));
        int minimum = Math.Min(red, Math.Min(green, blue));
        return maximum - minimum + maximum / 4;
    }
    private static float Edge(Vector2 a, Vector2 b, Vector2 point) =>
        (point.X - a.X) * (b.Y - a.Y) - (point.Y - a.Y) * (b.X - a.X);

    private static int GetBackgroundContrast(byte blue, byte green, byte red) =>
        Math.Abs(blue - BackgroundBlue) +
        Math.Abs(green - BackgroundGreen) +
        Math.Abs(red - BackgroundRed);

    private static void DrawLine(byte[] pixels, int width, int height, Vector2 start, Vector2 end)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(start, end)));
        for (int i = 0; i <= steps; i++)
        {
            float amount = i / (float)steps;
            int x = (int)MathF.Round(start.X + (end.X - start.X) * amount);
            int y = (int)MathF.Round(start.Y + (end.Y - start.Y) * amount);
            if ((uint)x >= width || (uint)y >= height) continue;
            int target = (y * width + x) * 4;
            pixels[target] = 255;
            pixels[target + 1] = 181;
            pixels[target + 2] = 91;
            pixels[target + 3] = 255;
        }
    }

    private void TextureModeButton_Click(object sender, RoutedEventArgs e) => SetMode(ModelRenderMode.Textured);
    private void SolidModeButton_Click(object sender, RoutedEventArgs e) => SetMode(ModelRenderMode.Solid);
    private void WireframeModeButton_Click(object sender, RoutedEventArgs e) => SetMode(ModelRenderMode.Wireframe);
    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleRender();

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _capturedPointerId = e.Pointer.PointerId;
        _lastPointer = e.GetCurrentPoint(Surface).Position;
        Surface.CapturePointer(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_capturedPointerId != e.Pointer.PointerId) return;
        Windows.Foundation.Point current = e.GetCurrentPoint(Surface).Position;
        _yaw += (float)(current.X - _lastPointer.X) * 0.012f;
        _pitch += (float)(current.Y - _lastPointer.Y) * 0.012f;
        _pitch = Math.Clamp(_pitch, -1.5f, 1.5f);
        _lastPointer = current;
        ScheduleRender();
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_capturedPointerId != e.Pointer.PointerId) return;
        Surface.ReleasePointerCapture(e.Pointer);
        _capturedPointerId = null;
        ProtectedCursor = null;
    }

    private void Surface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(Surface).Properties.MouseWheelDelta;
        _zoom = Math.Clamp(_zoom * (delta > 0 ? 1.12f : 0.89f), 0.15f, 8f);
        ScheduleRender();
        e.Handled = true;
    }
}
