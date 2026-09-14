using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using XunxianDpkViewer.Core;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Controls;

public sealed partial class EffectPreviewControl : UserControl
{
    private const int TargetWidth = 960;
    private const int TargetHeight = 620;
    private const float EffectUnitPixels = 180f;
    private readonly DispatcherTimer _timer;
    private GfxEffectPreviewData? _effect;
    private int _frame;
    private bool _isPlaying;
    private bool _isUpdatingTimeline;
    private bool _isTimelineDragging;
    private EffectSceneMode _sceneMode = EffectSceneMode.Auto;

    private enum EffectSceneMode
    {
        Auto,
        Character,
        Target,
        Ground,
        Raw
    }

    private enum EffectSceneKind
    {
        Character,
        Projectile,
        Target,
        Ground,
        Raw
    }

    public EffectPreviewControl()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += Timer_Tick;
        Unloaded += (_, _) => _timer.Stop();
    }

    public void SetEffect(GfxEffectPreviewData? effect)
    {
        _timer.Stop();
        _effect = effect;
        _frame = 0;
        _isPlaying = effect is not null && effect.FrameCount > 1;
        _isTimelineDragging = false;

        if (effect is null)
        {
            RasterImage.Source = null;
            TimelinePanel.Visibility = Visibility.Collapsed;
            EffectNameText.Text = string.Empty;
            EffectSummaryText.Text = string.Empty;
            SceneSummaryText.Text = string.Empty;
            EmptyText.Text = string.Empty;
            EmptyPanel.Visibility = Visibility.Visible;
            UsagePanel.Visibility = Visibility.Collapsed;
            ClearUsageText();
            return;
        }

        TimelinePanel.Visibility = effect.FrameCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        _isUpdatingTimeline = true;
        Timeline.Minimum = 0;
        Timeline.Maximum = Math.Max(0, effect.FrameCount - 1);
        Timeline.Value = 0;
        _isUpdatingTimeline = false;
        EffectNameText.Text = effect.Name;
        UpdateSceneSummary();
        EffectSummaryText.Text = string.Empty;
        bool hasDrawableLayer = effect.Layers.Any(layer => layer.Frames.Count > 0 || layer.Halo is not null);
        EmptyText.Text = string.Empty;
        EmptyPanel.Visibility = hasDrawableLayer ? Visibility.Collapsed : Visibility.Visible;
        RenderFrame();
        UpdatePlaybackChrome();
        if (_isPlaying) _timer.Start();
    }

    public void SetUsageLoading()
    {
        UsagePanel.Visibility = Visibility.Collapsed;
        ClearUsageText();
    }

    public void SetUsage(IReadOnlyList<EffectUsageInfo> usages)
    {
        UsagePanel.Visibility = Visibility.Collapsed;
        string[] skills = GetUsageTitles(usages, "所属技能");
        string[] users = GetUsageTitles(usages, "使用者");
        string[] scenes = GetUsageTitles(usages, "出现位置");
        SkillUsageText.Text = FormatUsageTitles(skills);
        UserUsageText.Text = FormatUsageTitles(users);
        SceneUsageText.Text = FormatUsageTitles(scenes);

        int confirmedCount = skills.Length + users.Length + scenes.Length;
        UsageStatusText.Text = confirmedCount == 0
            ? "未找到直接引用。"
            : string.Join(" · ", new[]
            {
                skills.Length > 0 ? $"技能 {skills.Length:N0}" : string.Empty,
                users.Length > 0 ? $"使用者 {users.Length:N0}" : string.Empty,
                scenes.Length > 0 ? $"场景 {scenes.Length:N0}" : string.Empty
            }.Where(text => text.Length > 0));
    }

    private static string[] GetUsageTitles(IReadOnlyList<EffectUsageInfo> usages, string category) => usages
        .Where(usage => usage.Category == category && !string.IsNullOrWhiteSpace(usage.Title))
        .Select(usage => usage.Title.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string FormatUsageTitles(IReadOnlyList<string> titles)
    {
        if (titles.Count == 0)
            return "未找到直接引用";
        string text = string.Join("、", titles.Take(2));
        return titles.Count > 2 ? $"{text} 等 {titles.Count:N0} 项" : text;
    }

    private void ClearUsageText()
    {
        UsageStatusText.Text = string.Empty;
        SkillUsageText.Text = string.Empty;
        UserUsageText.Text = string.Empty;
        SceneUsageText.Text = string.Empty;
    }

    public void SetUsageError(string message)
    {
        UsagePanel.Visibility = Visibility.Collapsed;
        ClearUsageText();
    }

    private void Timer_Tick(object? sender, object e)
    {
        if (_effect is null || !_isPlaying || _isTimelineDragging) return;
        int next = _frame + 1;
        if (next >= _effect.FrameCount) next = 0;
        SetFrame(next, true);
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_effect is null || _effect.FrameCount <= 1) return;
        _isPlaying = !_isPlaying;
        if (_isPlaying) _timer.Start(); else _timer.Stop();
        UpdatePlaybackChrome();
    }

    private void SceneModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected ||
            !Enum.TryParse(selected.Tag?.ToString(), out EffectSceneMode mode))
            return;

        _sceneMode = mode;
        AutoSceneButton.IsChecked = mode == EffectSceneMode.Auto;
        CharacterSceneButton.IsChecked = mode == EffectSceneMode.Character;
        TargetSceneButton.IsChecked = mode == EffectSceneMode.Target;
        GroundSceneButton.IsChecked = mode == EffectSceneMode.Ground;
        RawSceneButton.IsChecked = mode == EffectSceneMode.Raw;
        UpdateSceneSummary();
        RenderFrame();
    }

    private void Timeline_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingTimeline || _effect is null) return;
        SetFrame((int)Math.Round(e.NewValue), false);
    }

    private void Timeline_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isTimelineDragging = true;
        _isPlaying = false;
        _timer.Stop();
        UpdatePlaybackChrome();
    }

    private void Timeline_PointerReleased(object sender, PointerRoutedEventArgs e) => EndTimelineDrag();
    private void Timeline_PointerCanceled(object sender, PointerRoutedEventArgs e) => EndTimelineDrag();
    private void Timeline_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndTimelineDrag();

    private void EndTimelineDrag()
    {
        _isTimelineDragging = false;
        if (_effect is not null) SetFrame((int)Math.Round(Timeline.Value), false);
    }

    private void SetFrame(int frame, bool updateTimeline)
    {
        if (_effect is null) return;
        _frame = Math.Clamp(frame, 0, Math.Max(0, _effect.FrameCount - 1));
        if (updateTimeline)
        {
            _isUpdatingTimeline = true;
            Timeline.Value = _frame;
            _isUpdatingTimeline = false;
        }
        RenderFrame();
    }

    private void UpdatePlaybackChrome()
    {
        PlayPauseIcon.Glyph = _isPlaying ? "\uE769" : "\uE768";
    }

    private void RenderFrame()
    {
        if (_effect is null) return;
        byte[] pixels = new byte[TargetWidth * TargetHeight * 4];
        FillBackground(pixels);
        DrawSceneReference(pixels, ResolveSceneKind());
        double time = _frame / 30d;
        foreach (GfxEffectLayer layer in _effect.Layers)
        {
            if (_frame < layer.StartFrame || _frame > layer.EndFrame) continue;
            if (layer.Halo is not null)
                DrawHaloLayer(pixels, layer, time);
            if (layer.Frames.Count == 0) continue;
            if (layer.Emitter is not null)
                DrawEmitterLayer(pixels, layer, time);
            else
                DrawLayerInstance(pixels, layer, time, time, 0, 0);
        }

        var bitmap = new WriteableBitmap(TargetWidth, TargetHeight);
        using Stream stream = bitmap.PixelBuffer.AsStream();
        stream.Write(pixels, 0, pixels.Length);
        RasterImage.Source = bitmap;
        FrameText.Text = $"{_frame + 1:N0} / {_effect.FrameCount:N0} 帧";
    }

    private EffectSceneKind ResolveSceneKind()
    {
        if (_sceneMode == EffectSceneMode.Raw) return EffectSceneKind.Raw;
        if (_sceneMode == EffectSceneMode.Character) return EffectSceneKind.Character;
        if (_sceneMode == EffectSceneMode.Target) return EffectSceneKind.Target;
        if (_sceneMode == EffectSceneMode.Ground) return EffectSceneKind.Ground;
        if (_effect is null) return EffectSceneKind.Raw;

        string name = _effect.Name.ToLowerInvariant();
        if (_effect.Layers.Any(layer => layer.Halo is not null) ||
            ContainsAny(name, "diquan", "dizhen", "fazhen", "lingyu", "fanwei", "guanghuan"))
            return EffectSceneKind.Ground;
        if (ContainsAny(name, "baozha", "baolie", "mingzhong", "shouji", "jizhong", "hit", "huofeng"))
            return EffectSceneKind.Target;
        if (_effect.Layers.Any(layer => layer.Emitter is { VelocityTo: > 0.35f }) ||
            ContainsAny(name, "feidan", "huoqiu", "jianqi", "dan_", "qiu_", "chongji", "feixing"))
            return EffectSceneKind.Projectile;
        return EffectSceneKind.Character;
    }

    private void UpdateSceneSummary()
    {
        EffectSceneKind kind = ResolveSceneKind();
        SceneSummaryText.Text = string.Empty;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(value.Contains);

    private static void DrawSceneReference(byte[] pixels, EffectSceneKind kind)
    {
        if (kind == EffectSceneKind.Raw) return;

        const int groundY = 500;
        DrawGroundGrid(pixels, groundY);
        switch (kind)
        {
            case EffectSceneKind.Character:
                DrawHumanoid(pixels, TargetWidth / 2, groundY, 1f, false);
                DrawReferenceRing(pixels, TargetWidth / 2, TargetHeight / 2, 34, 14, 34, 84, 112, 96);
                break;
            case EffectSceneKind.Projectile:
                DrawHumanoid(pixels, 190, groundY, 0.82f, false);
                DrawTargetDummy(pixels, 770, groundY, 0.9f);
                DrawDirectionGuide(pixels, 285, TargetHeight / 2, 675, TargetHeight / 2);
                break;
            case EffectSceneKind.Target:
                DrawHumanoid(pixels, 225, groundY, 0.72f, false);
                DrawTargetDummy(pixels, TargetWidth / 2, groundY, 1f);
                DrawDirectionGuide(pixels, 305, 345, 420, 325);
                break;
            case EffectSceneKind.Ground:
                DrawHumanoid(pixels, 300, groundY, 0.78f, false);
                DrawGroundRange(pixels, TargetWidth / 2, TargetHeight / 2);
                break;
        }
    }

    private static void DrawGroundGrid(byte[] pixels, int groundY)
    {
        for (int offset = -300; offset <= 300; offset += 75)
            DrawReferenceLine(pixels, TargetWidth / 2 + offset, groundY, TargetWidth / 2 + offset / 3,
                groundY - 155, 34, 54, 72, 34);
        for (int row = 0; row < 5; row++)
        {
            int y = groundY - row * 34;
            int inset = row * 50;
            DrawReferenceLine(pixels, 90 + inset, y, TargetWidth - 90 - inset, y, 34, 54, 72, 38);
        }
    }

    private static void DrawGroundRange(byte[] pixels, int centerX, int centerY)
    {
        DrawReferenceRing(pixels, centerX, centerY, 190, 72, 45, 82, 104, 82);
        DrawReferenceRing(pixels, centerX, centerY, 104, 40, 45, 82, 104, 58);
        DrawReferenceLine(pixels, centerX - 190, centerY, centerX + 190, centerY, 45, 82, 104, 40);
        DrawReferenceLine(pixels, centerX, centerY - 72, centerX, centerY + 72, 45, 82, 104, 40);
    }

    private static void DrawHumanoid(byte[] pixels, int centerX, int feetY, float scale, bool target)
    {
        byte red = target ? (byte)98 : (byte)38;
        byte green = target ? (byte)52 : (byte)72;
        byte blue = target ? (byte)58 : (byte)94;
        int headY = feetY - (int)(222 * scale);
        int shoulderY = feetY - (int)(176 * scale);
        int hipY = feetY - (int)(92 * scale);
        int headRadius = Math.Max(9, (int)(22 * scale));
        FillReferenceEllipse(pixels, centerX, headY, headRadius, headRadius, red, green, blue, 74);
        FillReferenceEllipse(pixels, centerX, (shoulderY + hipY) / 2,
            Math.Max(13, (int)(27 * scale)), Math.Max(24, (int)(52 * scale)), red, green, blue, 54);
        DrawReferenceLine(pixels, centerX - (int)(20 * scale), shoulderY,
            centerX - (int)(48 * scale), hipY + (int)(12 * scale), red, green, blue, 110, 3);
        DrawReferenceLine(pixels, centerX + (int)(20 * scale), shoulderY,
            centerX + (int)(48 * scale), hipY + (int)(12 * scale), red, green, blue, 110, 3);
        DrawReferenceLine(pixels, centerX - (int)(12 * scale), hipY,
            centerX - (int)(19 * scale), feetY, red, green, blue, 110, 4);
        DrawReferenceLine(pixels, centerX + (int)(12 * scale), hipY,
            centerX + (int)(19 * scale), feetY, red, green, blue, 110, 4);
        DrawReferenceRing(pixels, centerX, feetY + 3, 46 * scale, 13 * scale, red, green, blue, 62);
    }

    private static void DrawTargetDummy(byte[] pixels, int centerX, int feetY, float scale)
    {
        DrawHumanoid(pixels, centerX, feetY, scale, true);
        int centerY = feetY - (int)(137 * scale);
        DrawReferenceRing(pixels, centerX, centerY, 44 * scale, 44 * scale, 118, 66, 62, 84);
        DrawReferenceRing(pixels, centerX, centerY, 20 * scale, 20 * scale, 118, 66, 62, 84);
    }

    private static void DrawDirectionGuide(byte[] pixels, int x0, int y0, int x1, int y1)
    {
        DrawReferenceLine(pixels, x0, y0, x1, y1, 56, 94, 120, 95, 2);
        DrawReferenceLine(pixels, x1, y1, x1 - 18, y1 - 10, 56, 94, 120, 95, 2);
        DrawReferenceLine(pixels, x1, y1, x1 - 18, y1 + 10, 56, 94, 120, 95, 2);
    }

    private static void DrawReferenceRing(byte[] pixels, int centerX, int centerY,
        float radiusX, float radiusY, byte red, byte green, byte blue, byte alpha)
    {
        const int segments = 80;
        float previousX = centerX + radiusX;
        float previousY = centerY;
        for (int segment = 1; segment <= segments; segment++)
        {
            double angle = segment * Math.PI * 2 / segments;
            float currentX = centerX + (float)Math.Cos(angle) * radiusX;
            float currentY = centerY + (float)Math.Sin(angle) * radiusY;
            DrawReferenceLine(pixels, previousX, previousY, currentX, currentY,
                red, green, blue, alpha);
            previousX = currentX;
            previousY = currentY;
        }
    }

    private static void FillReferenceEllipse(byte[] pixels, int centerX, int centerY,
        int radiusX, int radiusY, byte red, byte green, byte blue, byte alpha)
    {
        for (int y = -radiusY; y <= radiusY; y++)
        {
            float normalizedY = y / (float)Math.Max(1, radiusY);
            int width = (int)(radiusX * Math.Sqrt(Math.Max(0, 1 - normalizedY * normalizedY)));
            for (int x = -width; x <= width; x++)
                BlendReferencePixel(pixels, centerX + x, centerY + y, red, green, blue, alpha);
        }
    }

    private static void DrawReferenceLine(byte[] pixels, float x0, float y0, float x1, float y1,
        byte red, byte green, byte blue, byte alpha, int thickness = 1)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))));
        for (int step = 0; step <= steps; step++)
        {
            float amount = step / (float)steps;
            int x = (int)Math.Round(Lerp(x0, x1, amount));
            int y = (int)Math.Round(Lerp(y0, y1, amount));
            for (int offsetY = -thickness / 2; offsetY <= thickness / 2; offsetY++)
                for (int offsetX = -thickness / 2; offsetX <= thickness / 2; offsetX++)
                    BlendReferencePixel(pixels, x + offsetX, y + offsetY, red, green, blue, alpha);
        }
    }

    private static void BlendReferencePixel(byte[] pixels, int x, int y,
        byte red, byte green, byte blue, byte alpha)
    {
        if (x < 0 || x >= TargetWidth || y < 0 || y >= TargetHeight) return;
        int index = (y * TargetWidth + x) * 4;
        int inverse = 255 - alpha;
        pixels[index] = (byte)((blue * alpha + pixels[index] * inverse) / 255);
        pixels[index + 1] = (byte)((green * alpha + pixels[index + 1] * inverse) / 255);
        pixels[index + 2] = (byte)((red * alpha + pixels[index + 2] * inverse) / 255);
        pixels[index + 3] = 255;
    }

    private static void DrawEmitterLayer(byte[] pixels, GfxEffectLayer layer, double effectTime)
    {
        GfxEmitterSettings emitter = layer.Emitter!;
        double activeTime = effectTime - emitter.FirstTickTime;
        if (activeTime < 0) return;
        int lastEmission = (int)Math.Floor(activeTime / emitter.GenerateInterval);
        int firstEmission = Math.Max(0,
            (int)Math.Ceiling((activeTime - emitter.ParticleLife) / emitter.GenerateInterval));
        int emitted = 0;
        for (int emission = firstEmission; emission <= lastEmission; emission++)
        {
            double age = activeTime - emission * emitter.GenerateInterval;
            if (age < 0 || age > emitter.ParticleLife) continue;
            for (int particle = 0; particle < emitter.GenerateParticles; particle++)
            {
                if (emitted >= emitter.LimitParticles) return;
                DrawLayerInstance(pixels, layer, age, effectTime,
                    emission * emitter.GenerateParticles + particle, emitter.ParticleLife);
                emitted++;
            }
        }
    }

    private static void DrawHaloLayer(byte[] pixels, GfxEffectLayer layer, double effectTime)
    {
        GfxHaloSettings halo = layer.Halo!;
        GfxVectorKey shape = SampleVector(halo.ShapeKeys, effectTime,
            new GfxVectorKey(0, 0, 1, 1));
        float radiusX = Math.Clamp(Math.Abs(shape.Y) * EffectUnitPixels * halo.RadiusScale,
            8, TargetWidth * 0.38f);
        float radiusY = Math.Clamp(Math.Abs(shape.Z) * EffectUnitPixels * halo.RadiusScale,
            8, TargetHeight * 0.38f);
        int centerX = TargetWidth / 2 + (int)Math.Round(layer.PositionX * EffectUnitPixels);
        int centerY = TargetHeight / 2 -
                      (int)Math.Round((layer.PositionY + layer.PositionZ * 0.25f) * EffectUnitPixels);
        GfxColorKey color = SampleColor(layer.ColorKeys, effectTime);
        float alpha = SampleScalar(layer.AlphaKeys, effectTime, 1f);

        for (int ring = -1; ring <= 1; ring++)
        {
            float ringScale = 1f + ring * 0.035f;
            DrawEllipse(pixels, centerX, centerY, radiusX * ringScale, radiusY * ringScale,
                shape.X, halo.CircleSegments, color, alpha * (ring == 0 ? 0.9f : 0.35f));
        }
    }

    private static void DrawEllipse(
        byte[] pixels,
        int centerX,
        int centerY,
        float radiusX,
        float radiusY,
        float rotation,
        int segments,
        GfxColorKey color,
        float alpha)
    {
        double cosine = Math.Cos(rotation);
        double sine = Math.Sin(rotation);
        float previousX = centerX + radiusX;
        float previousY = centerY;
        for (int segment = 1; segment <= segments; segment++)
        {
            double angle = segment * Math.PI * 2 / segments;
            double localX = Math.Cos(angle) * radiusX;
            double localY = Math.Sin(angle) * radiusY;
            float currentX = centerX + (float)(localX * cosine - localY * sine);
            float currentY = centerY + (float)(localX * sine + localY * cosine);
            DrawAdditiveLine(pixels, previousX, previousY, currentX, currentY, color, alpha);
            previousX = currentX;
            previousY = currentY;
        }
    }

    private static void DrawAdditiveLine(
        byte[] pixels,
        float x0,
        float y0,
        float x1,
        float y1,
        GfxColorKey color,
        float alpha)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))));
        int red = Math.Clamp((int)Math.Round(color.Red * alpha * 255), 0, 255);
        int green = Math.Clamp((int)Math.Round(color.Green * alpha * 255), 0, 255);
        int blue = Math.Clamp((int)Math.Round(color.Blue * alpha * 255), 0, 255);
        for (int step = 0; step <= steps; step++)
        {
            float amount = step / (float)steps;
            int x = (int)Math.Round(Lerp(x0, x1, amount));
            int y = (int)Math.Round(Lerp(y0, y1, amount));
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                int targetY = y + offsetY;
                if (targetY < 0 || targetY >= TargetHeight) continue;
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int targetX = x + offsetX;
                    if (targetX < 0 || targetX >= TargetWidth) continue;
                    float falloff = offsetX == 0 && offsetY == 0 ? 1f : 0.28f;
                    int index = (targetY * TargetWidth + targetX) * 4;
                    pixels[index] = (byte)Math.Min(255, pixels[index] + blue * falloff);
                    pixels[index + 1] = (byte)Math.Min(255, pixels[index + 1] + green * falloff);
                    pixels[index + 2] = (byte)Math.Min(255, pixels[index + 2] + red * falloff);
                    pixels[index + 3] = 255;
                }
            }
        }
    }

    private static void DrawLayerInstance(
        byte[] pixels,
        GfxEffectLayer layer,
        double animationTime,
        double effectTime,
        int particleId,
        double particleLife)
    {
        GfxTextureFrame? frame = GetTextureFrame(layer, animationTime, effectTime, particleLife);
        if (frame is null) return;

        GfxVectorKey scale = SampleVector(layer.ScaleKeys, animationTime,
            new GfxVectorKey(0, 0, 0, 0));
        int sourceWidth = Math.Max(1,
            (int)Math.Ceiling((frame.UvMaxX - frame.UvMinX) * frame.Texture.Width));
        int sourceHeight = Math.Max(1,
            (int)Math.Ceiling((frame.UvMaxY - frame.UvMinY) * frame.Texture.Height));
        int targetWidth = layer.ScaleKeys.Count > 0
            ? Math.Clamp((int)Math.Round(Math.Abs(scale.X) * EffectUnitPixels), 2, TargetWidth * 3 / 4)
            : Math.Clamp((int)Math.Round(sourceWidth * 0.68), 24, TargetWidth * 3 / 4);
        int targetHeight = layer.ScaleKeys.Count > 0
            ? Math.Clamp((int)Math.Round(Math.Abs(scale.Y) * EffectUnitPixels), 2, TargetHeight * 3 / 4)
            : Math.Clamp((int)Math.Round(sourceHeight * 0.68), 24, TargetHeight * 3 / 4);

        float x = layer.PositionX;
        float y = layer.PositionY;
        float z = layer.PositionZ;
        if (layer.Emitter is GfxEmitterSettings emitter)
        {
            float randomX = HashToUnit(particleId, 11);
            float randomY = HashToUnit(particleId, 29);
            float randomZ = HashToUnit(particleId, 47);
            x += Lerp(emitter.RadiusXFrom, emitter.RadiusXTo, randomX);
            y += Lerp(emitter.RadiusYFrom, emitter.RadiusYTo, randomY);
            z += Lerp(emitter.RadiusZFrom, emitter.RadiusZTo, randomZ);
            float speed = Lerp(emitter.VelocityFrom, emitter.VelocityTo,
                HashToUnit(particleId, 71));
            double angle = HashToUnit(particleId, 89) * Math.PI * 2;
            x += (float)(Math.Cos(angle) * speed * animationTime);
            y += (float)(Math.Sin(angle) * speed * animationTime);
        }

        int centerX = TargetWidth / 2 + (int)Math.Round(x * EffectUnitPixels);
        int centerY = TargetHeight / 2 - (int)Math.Round((y + z * 0.25f) * EffectUnitPixels);
        GfxColorKey color = SampleColor(layer.ColorKeys, animationTime);
        float alpha = SampleScalar(layer.AlphaKeys, animationTime, 1f);
        DrawTexture(pixels, frame,
            centerX - targetWidth / 2, centerY - targetHeight / 2,
            targetWidth, targetHeight, color, alpha,
            layer.ColorOperator, layer.AlphaOperator, layer.ScreenMix);
    }

    private static GfxTextureFrame? GetTextureFrame(
        GfxEffectLayer layer,
        double animationTime,
        double effectTime,
        double particleLife)
    {
        if (layer.Frames.Count == 0) return null;
        GfxTextureFrame[] eligible = layer.Frames
            .Where(frame => frame.SequenceId >= layer.TileFrom && frame.SequenceId <= layer.TileTo)
            .OrderBy(frame => frame.SequenceId)
            .ToArray();
        if (eligible.Length == 0) eligible = layer.Frames.ToArray();

        if (particleLife > 0)
        {
            double progress = Math.Clamp(animationTime / particleLife, 0, 0.999999);
            return eligible[Math.Min(eligible.Length - 1, (int)(progress * eligible.Length))];
        }

        int framePosition = Math.Max(0, (int)Math.Floor(effectTime * 30));
        int totalFrames = eligible.Sum(item => item.FrameLength);
        if (totalFrames <= 0) return eligible[0];
        int position = framePosition % totalFrames;
        foreach (GfxTextureFrame candidate in eligible)
        {
            if (position < candidate.FrameLength) return candidate;
            position -= candidate.FrameLength;
        }
        return eligible[^1];
    }

    private static void FillBackground(byte[] pixels)
    {
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 18;
            pixels[index + 1] = 14;
            pixels[index + 2] = 9;
            pixels[index + 3] = 255;
        }
    }

    private static GfxColorKey SampleColor(IReadOnlyList<GfxColorKey> keys, double time)
    {
        if (keys.Count == 0) return new GfxColorKey(0, 1, 1, 1);
        if (time <= keys[0].Time) return keys[0];
        for (int index = 1; index < keys.Count; index++)
        {
            GfxColorKey next = keys[index];
            if (time > next.Time) continue;
            GfxColorKey previous = keys[index - 1];
            float amount = InterpolationAmount(previous.Time, next.Time, time);
            return new GfxColorKey(time,
                Lerp(previous.Red, next.Red, amount),
                Lerp(previous.Green, next.Green, amount),
                Lerp(previous.Blue, next.Blue, amount));
        }
        return keys[^1];
    }

    private static float SampleScalar(IReadOnlyList<GfxScalarKey> keys, double time, float fallback)
    {
        if (keys.Count == 0) return fallback;
        if (time <= keys[0].Time) return keys[0].Value;
        for (int index = 1; index < keys.Count; index++)
        {
            GfxScalarKey next = keys[index];
            if (time > next.Time) continue;
            GfxScalarKey previous = keys[index - 1];
            return Lerp(previous.Value, next.Value, InterpolationAmount(previous.Time, next.Time, time));
        }
        return keys[^1].Value;
    }

    private static GfxVectorKey SampleVector(
        IReadOnlyList<GfxVectorKey> keys,
        double time,
        GfxVectorKey fallback)
    {
        if (keys.Count == 0) return fallback;
        if (time <= keys[0].Time) return keys[0];
        for (int index = 1; index < keys.Count; index++)
        {
            GfxVectorKey next = keys[index];
            if (time > next.Time) continue;
            GfxVectorKey previous = keys[index - 1];
            float amount = InterpolationAmount(previous.Time, next.Time, time);
            return new GfxVectorKey(time,
                Lerp(previous.X, next.X, amount),
                Lerp(previous.Y, next.Y, amount),
                Lerp(previous.Z, next.Z, amount));
        }
        return keys[^1];
    }

    private static float HashToUnit(int value, int salt)
    {
        uint hash = unchecked((uint)(value * 374761393 + salt * 668265263));
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        hash ^= hash >> 16;
        return (hash & 0x00FFFFFF) / 16777215f;
    }

    private static float InterpolationAmount(double start, double end, double value) =>
        end <= start ? 1f : (float)Math.Clamp((value - start) / (end - start), 0, 1);

    private static float Lerp(float start, float end, float amount) => start + (end - start) * amount;

    private static void DrawTexture(
        byte[] destination,
        GfxTextureFrame frame,
        int left,
        int top,
        int width,
        int height,
        GfxColorKey color,
        float layerAlpha,
        int colorOperator,
        int alphaOperator,
        int screenMix)
    {
        DecodedTexture texture = frame.Texture;
        int sourceLeft = Math.Clamp((int)Math.Floor(frame.UvMinX * texture.Width), 0, texture.Width - 1);
        int sourceTop = Math.Clamp((int)Math.Floor(frame.UvMinY * texture.Height), 0, texture.Height - 1);
        int sourceRight = Math.Clamp((int)Math.Ceiling(frame.UvMaxX * texture.Width), sourceLeft + 1, texture.Width);
        int sourceBottom = Math.Clamp((int)Math.Ceiling(frame.UvMaxY * texture.Height), sourceTop + 1, texture.Height);
        int sourceWidth = sourceRight - sourceLeft;
        int sourceHeight = sourceBottom - sourceTop;
        float colorGain = colorOperator == 5 ? 2f : 1f;
        float alphaGain = alphaOperator == 5 ? 2f : 1f;
        float redTint = Math.Clamp(color.Red * colorGain, 0, 2);
        float greenTint = Math.Clamp(color.Green * colorGain, 0, 2);
        float blueTint = Math.Clamp(color.Blue * colorGain, 0, 2);
        for (int y = 0; y < height; y++)
        {
            int targetY = top + y;
            if (targetY < 0 || targetY >= TargetHeight) continue;
            int sourceY = sourceTop + y * sourceHeight / height;
            for (int x = 0; x < width; x++)
            {
                int targetX = left + x;
                if (targetX < 0 || targetX >= TargetWidth) continue;
                int sourceX = sourceLeft + x * sourceWidth / width;
                int sourceIndex = (sourceY * texture.Width + sourceX) * 4;
                byte alpha = (byte)Math.Clamp(
                    texture.BgraPixels[sourceIndex + 3] * layerAlpha * alphaGain, 0, 255);
                if (alpha == 0) continue;
                int targetIndex = (targetY * TargetWidth + targetX) * 4;
                int sourceBlue = Math.Clamp((int)(texture.BgraPixels[sourceIndex] * blueTint), 0, 255);
                int sourceGreen = Math.Clamp((int)(texture.BgraPixels[sourceIndex + 1] * greenTint), 0, 255);
                int sourceRed = Math.Clamp((int)(texture.BgraPixels[sourceIndex + 2] * redTint), 0, 255);
                if (screenMix == 2)
                {
                    destination[targetIndex] = (byte)Math.Min(255,
                        destination[targetIndex] + sourceBlue * alpha / 255);
                    destination[targetIndex + 1] = (byte)Math.Min(255,
                        destination[targetIndex + 1] + sourceGreen * alpha / 255);
                    destination[targetIndex + 2] = (byte)Math.Min(255,
                        destination[targetIndex + 2] + sourceRed * alpha / 255);
                    destination[targetIndex + 3] = 255;
                    continue;
                }
                int inverse = 255 - alpha;
                destination[targetIndex] = (byte)((sourceBlue * alpha + destination[targetIndex] * inverse) / 255);
                destination[targetIndex + 1] = (byte)((sourceGreen * alpha + destination[targetIndex + 1] * inverse) / 255);
                destination[targetIndex + 2] = (byte)((sourceRed * alpha + destination[targetIndex + 2] * inverse) / 255);
                destination[targetIndex + 3] = 255;
            }
        }
    }
}
