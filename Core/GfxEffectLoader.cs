using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public sealed record GfxTextureFrame(
    DecodedTexture Texture,
    string SourcePath,
    int FrameLength,
    int SequenceId,
    float UvMinX,
    float UvMinY,
    float UvMaxX,
    float UvMaxY);

public sealed record GfxColorKey(
    double Time,
    float Red,
    float Green,
    float Blue);

public sealed record GfxScalarKey(
    double Time,
    float Value);

public sealed record GfxVectorKey(
    double Time,
    float X,
    float Y,
    float Z);

public sealed record GfxEmitterSettings(
    double FirstTickTime,
    double GenerateInterval,
    int GenerateParticles,
    int LimitParticles,
    double ParticleLife,
    float VelocityFrom,
    float VelocityTo,
    float RadiusXFrom,
    float RadiusXTo,
    float RadiusYFrom,
    float RadiusYTo,
    float RadiusZFrom,
    float RadiusZTo);

public sealed record GfxHaloSettings(
    IReadOnlyList<GfxVectorKey> ShapeKeys,
    int CircleSegments,
    float RadiusScale);

public sealed record GfxEffectLayer(
    string Name,
    string Type,
    int StartFrame,
    int EndFrame,
    IReadOnlyList<GfxTextureFrame> Frames,
    IReadOnlyList<string> MeshReferences,
    IReadOnlyList<GfxColorKey> ColorKeys,
    IReadOnlyList<GfxScalarKey> AlphaKeys,
    IReadOnlyList<GfxVectorKey> ScaleKeys,
    float PositionX,
    float PositionY,
    float PositionZ,
    int TileFrom,
    int TileTo,
    GfxEmitterSettings? Emitter,
    GfxHaloSettings? Halo,
    int ColorOperator,
    int AlphaOperator,
    int ScreenMix);

public sealed record GfxEffectPreviewData(
    string Name,
    int FrameCount,
    IReadOnlyList<GfxEffectLayer> Layers,
    IReadOnlyList<string> MeshReferences,
    int TextureCount,
    int SkippedTextureCount);

public static class GfxEffectLoader
{
    private const int MaximumLayers = 32;
    private const int MaximumTextureFrames = 40;
    private const int MaximumDecodedPixels = 16_000_000;

    public static GfxEffectPreviewData Load(DpkWorkspace workspace, AssetEntry effectAsset, byte[] effectData)
    {
        XDocument document = XDocument.Parse(ReadText(effectData));
        var assets = new Dictionary<string, AssetEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetEntry asset in workspace.Assets)
        {
            AddAssetLookupKeys(assets, asset);
        }

        int frameCount = FindFrameCount(document);
        int skippedTextures = 0;
        int decodedPixels = 0;
        var textureCache = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
        var meshReferences = new List<string>();
        var layers = new List<GfxEffectLayer>();
        XElement[] effectNodes = document.Descendants()
            .Where(element => element.Name.LocalName.Equals("FX_Effect", StringComparison.OrdinalIgnoreCase))
            .Take(MaximumLayers)
            .ToArray();

        if (effectNodes.Length == 0)
        {
            GfxEffectLayer? layer = BuildLayer(document.Root, "Effect", "FX_Effect", 0, frameCount - 1,
                workspace, assets, textureCache, meshReferences, ref skippedTextures, ref decodedPixels);
            if (layer is not null) layers.Add(layer);
        }
        else
        {
            for (int index = 0; index < effectNodes.Length; index++)
            {
                XElement node = effectNodes[index];
                string type = (string?)node.Attribute("FX_Flag") ?? "FX_Effect";
                string name = (string?)node.Attribute("name") ?? $"特效层 {index + 1}";
                GfxEffectLayer? layer = BuildLayer(node, name, type, 0, frameCount - 1,
                    workspace, assets, textureCache, meshReferences, ref skippedTextures, ref decodedPixels);
                if (layer is not null) layers.Add(layer);
            }
        }

        if (layers.Count == 0)
        {
            layers.Add(new GfxEffectLayer("特效配置", "FX_Effect", 0, Math.Max(0, frameCount - 1),
                Array.Empty<GfxTextureFrame>(), meshReferences,
                Array.Empty<GfxColorKey>(), Array.Empty<GfxScalarKey>(), Array.Empty<GfxVectorKey>(),
                0, 0, 0, 0, -1, null, null, 4, 4, 0));
        }

        int textureCount = layers.Sum(layer => layer.Frames.Count);
        return new GfxEffectPreviewData(
            effectAsset.Name,
            frameCount,
            layers,
            meshReferences.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            textureCount,
            skippedTextures);
    }

    private static GfxEffectLayer? BuildLayer(
        XElement? node,
        string name,
        string type,
        int startFrame,
        int endFrame,
        DpkWorkspace workspace,
        IReadOnlyDictionary<string, AssetEntry> assets,
        IDictionary<string, DecodedTexture> textureCache,
        List<string> allMeshReferences,
        ref int skippedTextures,
        ref int decodedPixels)
    {
        if (node is null) return null;

        XElement[] localNodes = GetLocalNodes(node).ToArray();
        var textureReferences = new List<string>();
        var meshReferences = new List<string>();
        foreach (XElement valueNode in localNodes.Where(element => !element.HasElements))
        {
            string value = valueNode.Value.Trim();
            if (string.IsNullOrEmpty(value)) continue;
            string extension = Path.GetExtension(value);
            if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tlt", StringComparison.OrdinalIgnoreCase))
            {
                textureReferences.Add(value);
            }
            else if (extension.Equals(".vmm", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".mgx", StringComparison.OrdinalIgnoreCase))
            {
                meshReferences.Add(value);
            }
        }

        var frames = new List<GfxTextureFrame>();
        foreach (string reference in textureReferences.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (frames.Count >= MaximumTextureFrames) break;
            AddTextureReference(reference, workspace, assets, textureCache, frames,
                ref skippedTextures, ref decodedPixels);
        }

        string[] normalizedMeshReferences = meshReferences
            .Select(NormalizeReference)
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        allMeshReferences.AddRange(normalizedMeshReferences);

        IReadOnlyList<GfxColorKey> colorKeys = ParseColorKeys(node, localNodes);
        IReadOnlyList<GfxScalarKey> alphaKeys = ParseScalarKeys(node, localNodes, "pAlpha");
        IReadOnlyList<GfxVectorKey> scaleKeys = ParseVectorKeys(node, localNodes, "pScale");
        (float positionX, float positionY, float positionZ) = ParseLocalVector(localNodes, "ref_pos");
        (int tileFrom, int tileTo) = ParseTileRange(node, localNodes, frames.Count);
        GfxEmitterSettings? emitter = ParseEmitter(node);
        GfxHaloSettings? halo = ParseHalo(node, type, localNodes);
        int colorOperator = FindIntValue(localNodes, "color_operator", 4);
        int alphaOperator = FindIntValue(localNodes, "alpha_operator", 4);
        int screenMix = FindIntValue(localNodes, "screen_mix", 0);

        return frames.Count > 0 || normalizedMeshReferences.Length > 0 || halo is not null
            ? new GfxEffectLayer(name, type, startFrame, endFrame, frames, normalizedMeshReferences,
                colorKeys, alphaKeys, scaleKeys, positionX, positionY, positionZ,
                tileFrom, tileTo, emitter, halo, colorOperator, alphaOperator, screenMix)
            : null;
    }

    private static IEnumerable<XElement> GetLocalNodes(XElement node)
    {
        bool isEffectNode = IsEffectNode(node);
        foreach (XElement candidate in node.DescendantsAndSelf())
        {
            if (!isEffectNode || ReferenceEquals(candidate.AncestorsAndSelf().FirstOrDefault(IsEffectNode), node))
                yield return candidate;
        }
    }

    private static bool IsEffectNode(XElement element) =>
        element.Name.LocalName.Equals("FX_Effect", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<GfxColorKey> ParseColorKeys(
        XElement effectNode,
        IReadOnlyList<XElement> localNodes)
    {
        XElement? ticker = FindTicker(effectNode, localNodes, "pColor");
        if (ticker is null) return Array.Empty<GfxColorKey>();

        var keys = new List<GfxColorKey>();
        foreach (XElement valueNode in ticker.Descendants().Where(element =>
                     element.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadKey(valueNode, out double time, out string value)) continue;
            string[] components = value.Trim().Trim('{', '}').Split(',', StringSplitOptions.TrimEntries);
            if (components.Length < 3 ||
                !TryParseFloat(components[0], out float red) ||
                !TryParseFloat(components[1], out float green) ||
                !TryParseFloat(components[2], out float blue))
                continue;
            keys.Add(new GfxColorKey(time, Math.Clamp(red, 0, 1), Math.Clamp(green, 0, 1),
                Math.Clamp(blue, 0, 1)));
        }
        return keys.OrderBy(key => key.Time).ToArray();
    }

    private static IReadOnlyList<GfxScalarKey> ParseScalarKeys(
        XElement effectNode,
        IReadOnlyList<XElement> localNodes,
        string flag)
    {
        XElement? ticker = FindTicker(effectNode, localNodes, flag);
        if (ticker is null) return Array.Empty<GfxScalarKey>();

        var keys = new List<GfxScalarKey>();
        foreach (XElement valueNode in ticker.Descendants().Where(element =>
                     element.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadKey(valueNode, out double time, out string value) ||
                !TryParseFloat(value, out float parsed))
                continue;
            keys.Add(new GfxScalarKey(time, Math.Clamp(parsed, 0, 1)));
        }
        return keys.OrderBy(key => key.Time).ToArray();
    }

    private static IReadOnlyList<GfxVectorKey> ParseVectorKeys(
        XElement effectNode,
        IReadOnlyList<XElement> localNodes,
        string flag)
    {
        XElement? ticker = FindTicker(effectNode, localNodes, flag);
        if (ticker is null) return Array.Empty<GfxVectorKey>();

        var keys = new List<GfxVectorKey>();
        foreach (XElement valueNode in ticker.Descendants().Where(element =>
                     element.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadKey(valueNode, out double time, out string value) ||
                !TryParseVector(value, out float x, out float y, out float z))
                continue;
            keys.Add(new GfxVectorKey(time, x, y, z));
        }
        return keys.OrderBy(key => key.Time).ToArray();
    }

    private static (float X, float Y, float Z) ParseLocalVector(
        IEnumerable<XElement> localNodes,
        string name)
    {
        XElement? node = localNodes.FirstOrDefault(element => !element.HasElements &&
            element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return node is not null && TryParseVector(node.Value, out float x, out float y, out float z)
            ? (x, y, z)
            : (0, 0, 0);
    }

    private static (int From, int To) ParseTileRange(
        XElement effectNode,
        IReadOnlyList<XElement> localNodes,
        int frameCount)
    {
        XElement? ticker = FindTicker(effectNode, localNodes, "pAniTile");
        if (ticker is null) return (0, Math.Max(-1, frameCount - 1));

        XElement? range = ticker.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals("range_play", StringComparison.OrdinalIgnoreCase));
        if (range is null) return (0, Math.Max(-1, frameCount - 1));
        int from = FindIntValue(range.Descendants(), "range_from", 0);
        int to = FindIntValue(range.Descendants(), "range_to", Math.Max(0, frameCount - 1));
        return (Math.Min(from, to), Math.Max(from, to));
    }

    private static GfxEmitterSettings? ParseEmitter(XElement effectNode)
    {
        XElement? emitter = effectNode.AncestorsAndSelf().FirstOrDefault(element =>
            IsEffectNode(element) &&
            ((string?)element.Attribute("FX_Flag"))?.StartsWith("eEmitter",
                StringComparison.OrdinalIgnoreCase) == true);
        if (emitter is null) return null;

        XElement[] emitterNodes = GetLocalNodes(emitter).ToArray();
        XElement? generator = emitterNodes.FirstOrDefault(element =>
            element.Name.LocalName.Equals("FX_Generator", StringComparison.OrdinalIgnoreCase));
        XElement? particle = emitterNodes.FirstOrDefault(element =>
            element.Name.LocalName.Equals("FX_Particle", StringComparison.OrdinalIgnoreCase));
        if (generator is null || particle is null) return null;

        double firstTickTime = FindDoubleValue(emitterNodes, "first_tick_time", 0);
        double interval = FindDoubleValue(generator.Descendants(), "generate_interval", 0);
        int particles = FindIntValue(generator.Descendants(), "generate_particles", 1);
        int limit = FindIntValue(emitterNodes, "limit_particles", 30);
        XElement? particleLife = particle.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals("life_time", StringComparison.OrdinalIgnoreCase));
        double life = particleLife is null ? 1 : ParseDouble(particleLife.Value, 1);
        XElement? action = particle.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals("FX_PAction", StringComparison.OrdinalIgnoreCase));
        if (action is null) return null;

        (float lifeFrom, float lifeTo) = ParseRange(action, "range_life");
        if (lifeTo > 0) life = Math.Max(lifeFrom, lifeTo);
        (float velocityFrom, float velocityTo) = ParseRange(action, "range_velocity");
        (float radiusXFrom, float radiusXTo) = ParseRange(action, "range_radii_X");
        (float radiusYFrom, float radiusYTo) = ParseRange(action, "range_radii_Y");
        (float radiusZFrom, float radiusZTo) = ParseRange(action, "range_radii_Z");
        if (interval <= 0 || life <= 0) return null;

        return new GfxEmitterSettings(Math.Max(0, firstTickTime), interval, Math.Clamp(particles, 1, 64),
            Math.Clamp(limit, 1, 512), life, velocityFrom, velocityTo,
            radiusXFrom, radiusXTo, radiusYFrom, radiusYTo, radiusZFrom, radiusZTo);
    }

    private static GfxHaloSettings? ParseHalo(
        XElement effectNode,
        string type,
        IReadOnlyList<XElement> localNodes)
    {
        IReadOnlyList<GfxVectorKey> shapeKeys = ParseVectorKeys(effectNode, localNodes, "pHalo");
        if (!type.Equals("eHalo", StringComparison.OrdinalIgnoreCase) && shapeKeys.Count == 0)
            return null;

        if (shapeKeys.Count == 0)
            shapeKeys = new[] { new GfxVectorKey(0, 0, 1, 1) };
        int segments = Math.Clamp(FindIntValue(localNodes, "circle", 40), 12, 180);
        float radiusScale = (float)Math.Clamp(FindDoubleValue(localNodes, "radii_scale", 1), 0.05, 8);
        return new GfxHaloSettings(shapeKeys, segments, radiusScale);
    }

    private static (float From, float To) ParseRange(XElement root, string name)
    {
        XElement? range = root.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (range is null) return (0, 0);
        float from = (float)FindDoubleValue(range.Descendants(), "range_from", 0);
        float to = (float)FindDoubleValue(range.Descendants(), "range_to", from);
        return (from, to);
    }

    private static XElement? FindTicker(
        XElement effectNode,
        IEnumerable<XElement> localNodes,
        string flag)
    {
        XElement? ticker = FindLocalTicker(localNodes, flag);
        if (ticker is not null) return ticker;

        foreach (XElement ancestor in effectNode.Ancestors().Where(IsEffectNode))
        {
            ticker = FindLocalTicker(GetLocalNodes(ancestor), flag);
            if (ticker is not null) return ticker;
        }
        return null;
    }

    private static XElement? FindLocalTicker(IEnumerable<XElement> localNodes, string flag) =>
        localNodes.FirstOrDefault(element =>
            element.Name.LocalName.Equals("FX_Ticker", StringComparison.OrdinalIgnoreCase) &&
            string.Equals((string?)element.Attribute("FX_Flag"), flag, StringComparison.OrdinalIgnoreCase));

    private static bool TryReadKey(XElement valueNode, out double time, out string value)
    {
        time = 0;
        XElement? timeNode = valueNode.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("time", StringComparison.OrdinalIgnoreCase));
        XElement? lineNode = valueNode.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("line_c", StringComparison.OrdinalIgnoreCase));
        value = lineNode?.Value.Trim() ?? string.Empty;
        return timeNode is not null && lineNode is not null &&
               double.TryParse(timeNode.Value.Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out time);
    }

    private static bool TryParseFloat(string value, out float parsed) =>
        float.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out parsed);

    private static bool TryParseVector(
        string value,
        out float x,
        out float y,
        out float z)
    {
        x = 0;
        y = 0;
        z = 0;
        string[] components = value.Trim().Trim('{', '}')
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (components.Length < 2 ||
            !TryParseFloat(components[0], out x) ||
            !TryParseFloat(components[1], out y))
            return false;
        return components.Length < 3 || TryParseFloat(components[2], out z);
    }

    private static double FindDoubleValue(
        IEnumerable<XElement> localNodes,
        string name,
        double fallback)
    {
        XElement? node = localNodes.FirstOrDefault(element => !element.HasElements &&
            element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return node is null ? fallback : ParseDouble(node.Value, fallback);
    }

    private static double ParseDouble(string value, double fallback) =>
        double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;

    private static int FindIntValue(IEnumerable<XElement> localNodes, string name, int fallback)
    {
        XElement? node = localNodes.FirstOrDefault(element => !element.HasElements &&
            element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return node is not null && int.TryParse(node.Value.Trim(), out int parsed) ? parsed : fallback;
    }

    private static void AddTextureReference(
        string reference,
        DpkWorkspace workspace,
        IReadOnlyDictionary<string, AssetEntry> assets,
        IDictionary<string, DecodedTexture> textureCache,
        List<GfxTextureFrame> frames,
        ref int skippedTextures,
        ref int decodedPixels)
    {
        string normalized = NormalizeReference(reference);
        if (!assets.TryGetValue(normalized, out AssetEntry? asset))
        {
            skippedTextures++;
            return;
        }

        if (asset.Extension.Equals(".tlt", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                XDocument textureList = XDocument.Parse(ReadText(workspace.Extract(asset)));
                int fallbackId = 0;
                foreach (XElement keyFrame in textureList.Descendants()
                             .Where(element => element.Name.LocalName.Equals("KeyFrame", StringComparison.OrdinalIgnoreCase)))
                {
                    if (frames.Count >= MaximumTextureFrames) return;
                    string? textureName = keyFrame.Descendants()
                        .FirstOrDefault(element => element.Name.LocalName.Equals("TextureName", StringComparison.OrdinalIgnoreCase))?
                        .Value
                        .Trim();
                    if (string.IsNullOrWhiteSpace(textureName)) continue;
                    int frameLength = ParsePositiveInt((string?)keyFrame.Attribute("FrameLength"), 1);
                    int sequenceId = ParseNonNegativeInt(
                        (string?)keyFrame.Attribute("ID") ?? (string?)keyFrame.Attribute("Id") ??
                        (string?)keyFrame.Attribute("id"), fallbackId);
                    (float uvMinX, float uvMinY) = ParseUv(keyFrame, "UVMin", 0, 0);
                    (float uvMaxX, float uvMaxY) = ParseUv(keyFrame, "UVMax", 1, 1);
                    AddDdsFrame(textureName, frameLength, sequenceId,
                        uvMinX, uvMinY, uvMaxX, uvMaxY,
                        workspace, assets, textureCache, frames,
                        ref skippedTextures, ref decodedPixels);
                    fallbackId++;
                }
            }
            catch
            {
                skippedTextures++;
            }
            return;
        }

        AddDdsFrame(reference, 1, 0, 0, 0, 1, 1,
            workspace, assets, textureCache, frames, ref skippedTextures, ref decodedPixels);
    }

    private static void AddDdsFrame(
        string reference,
        int frameLength,
        int sequenceId,
        float uvMinX,
        float uvMinY,
        float uvMaxX,
        float uvMaxY,
        DpkWorkspace workspace,
        IReadOnlyDictionary<string, AssetEntry> assets,
        IDictionary<string, DecodedTexture> textureCache,
        List<GfxTextureFrame> frames,
        ref int skippedTextures,
        ref int decodedPixels)
    {
        string normalized = NormalizeReference(reference);
        if (!assets.TryGetValue(normalized, out AssetEntry? asset) ||
            !asset.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            skippedTextures++;
            return;
        }

        try
        {
            if (!textureCache.TryGetValue(normalized, out DecodedTexture? texture))
            {
                texture = DdsDecoder.Decode(workspace.Extract(asset));
                int pixelCount = checked(texture.Width * texture.Height);
                if (decodedPixels + pixelCount > MaximumDecodedPixels)
                {
                    skippedTextures++;
                    return;
                }

                decodedPixels += pixelCount;
                textureCache[normalized] = texture;
            }

            uvMinX = Math.Clamp(uvMinX, 0, 1);
            uvMinY = Math.Clamp(uvMinY, 0, 1);
            uvMaxX = Math.Clamp(uvMaxX, 0, 1);
            uvMaxY = Math.Clamp(uvMaxY, 0, 1);
            if (uvMaxX <= uvMinX || uvMaxY <= uvMinY)
            {
                uvMinX = 0;
                uvMinY = 0;
                uvMaxX = 1;
                uvMaxY = 1;
            }
            frames.Add(new GfxTextureFrame(texture, asset.Entry.Path,
                Math.Clamp(frameLength, 1, 600), sequenceId,
                uvMinX, uvMinY, uvMaxX, uvMaxY));
        }
        catch
        {
            skippedTextures++;
        }
    }

    private static int FindFrameCount(XDocument document)
    {
        foreach (XElement element in document.Descendants())
        {
            if (!element.Name.LocalName.Equals("Time", StringComparison.OrdinalIgnoreCase)) continue;
            string value = element.Value.Trim();
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double duration) || duration <= 0)
                continue;
            return Math.Clamp(duration <= 30 ? (int)Math.Ceiling(duration * 30) : (int)Math.Ceiling(duration), 1, 3600);
        }

        return 90;
    }

    private static int ParsePositiveInt(string? value, int fallback) =>
        int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;

    private static int ParseNonNegativeInt(string? value, int fallback) =>
        int.TryParse(value, out int parsed) && parsed >= 0 ? parsed : fallback;

    private static (float X, float Y) ParseUv(
        XElement keyFrame,
        string name,
        float fallbackX,
        float fallbackY)
    {
        XElement? node = keyFrame.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (node is null || !TryParseVector(node.Value, out float x, out float y, out _))
            return (fallbackX, fallbackY);
        return (x, y);
    }

    private static string ReadText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string NormalizeReference(string referencePath)
    {
        string path = referencePath.Trim().Trim('"').Replace('\\', '/');
        path = Regex.Replace(path, @"\$\(\s*res\s*\)", string.Empty, RegexOptions.IgnoreCase);
        path = path.TrimStart('/');
        int gfxIndex = path.IndexOf("gfx/", StringComparison.OrdinalIgnoreCase);
        if (gfxIndex >= 0) path = path[gfxIndex..];
        while (path.StartsWith("res/", StringComparison.OrdinalIgnoreCase)) path = path[4..];
        return path.ToLowerInvariant();
    }

    private static void AddAssetLookupKeys(IDictionary<string, AssetEntry> assets, AssetEntry asset)
    {
        string key = NormalizeReference(asset.Entry.Path);
        if (string.IsNullOrEmpty(key)) return;

        assets.TryAdd(key, asset);
        if (key.StartsWith("gfx/", StringComparison.OrdinalIgnoreCase))
            assets.TryAdd(key[4..], asset);
        else
            assets.TryAdd("gfx/" + key, asset);
    }
}
