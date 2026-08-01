using System.Text;
using System.Xml.Linq;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public sealed class ModelTextureResolver
{
    private const int MaximumCompositeParts = 64;
    private const int MaximumCharacterCompositeParts = 40;
    private const int MaximumCharacterFallbackParts = 24;

    private readonly DpkWorkspace _workspace;
    private readonly Dictionary<string, IReadOnlyList<ModelTextureBinding>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<CompositeModelEntry>> _compositeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Dictionary<string, AssetEntry>? _assetByReference;

    private sealed record CompositePartCandidate(
        AssetEntry MeshAsset,
        string MaterialName,
        XElement SourceElement,
        bool UsesMaterialReferences);

    private sealed record CompositeCandidateSet(
        string Label,
        CompositePartCandidate[] Candidates);

    private sealed record NonCharacterVariantKey(
        string Family,
        string Code);

    public ModelTextureResolver(DpkWorkspace workspace)
    {
        _workspace = workspace;
    }

    public IReadOnlyList<ModelTextureBinding> Resolve(AssetEntry model)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(model.DisplayPath, out IReadOnlyList<ModelTextureBinding>? cached))
                return cached;

            IReadOnlyList<ModelTextureBinding> result = ResolveCore(model);
            _cache[model.DisplayPath] = result;
            return result;
        }
    }

    public IReadOnlyList<CompositeModelEntry> FindComposites(string archivePath, string folderPath)
    {
        string normalizedFolder = folderPath.Replace('\\', '/').Trim('/');
        if (normalizedFolder.Split('/', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            return Array.Empty<CompositeModelEntry>();
        string cacheKey = $"{archivePath}|{normalizedFolder}";
        lock (_gate)
        {
            if (_compositeCache.TryGetValue(cacheKey, out IReadOnlyList<CompositeModelEntry>? cached))
                return cached;
            IReadOnlyList<CompositeModelEntry> result = FindCompositesCore(archivePath, normalizedFolder);
            _compositeCache[cacheKey] = result;
            return result;
        }
    }

    private IReadOnlyList<CompositeModelEntry> FindCompositesCore(string archivePath, string folderPath)
    {
        var result = new List<CompositeModelEntry>();
        string prefix = folderPath + "/";
        foreach (AssetEntry config in _workspace.Assets.Where(asset =>
                     string.Equals(asset.ArchivePath, archivePath, StringComparison.OrdinalIgnoreCase) &&
                     asset.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase) &&
                     asset.Entry.Path.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(DecodeXml(_workspace.Extract(config)), LoadOptions.None);
            }
            catch
            {
                continue;
            }

            var candidates = new List<CompositePartCandidate>();
            XElement[] materialReferences = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("Material", StringComparison.OrdinalIgnoreCase) &&
                                  element.Value.Contains(".cmf", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (XElement modelElement in document.Descendants()
                         .Where(element => element.Name.LocalName.Equals("Model", StringComparison.OrdinalIgnoreCase)))
            {
                string? meshReference = modelElement.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("MeshFile", StringComparison.OrdinalIgnoreCase))?.Value;
                AssetEntry? mesh = meshReference is null ? null : FindReferencedAsset(meshReference);
                if (mesh?.Kind != AssetKind.Model) continue;
                string materialName = modelElement.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("MatName", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                candidates.Add(new CompositePartCandidate(mesh, materialName, modelElement, UsesMaterialReferences: true));
            }

            foreach (XElement meshElement in document.Descendants()
                         .Where(element => element.Name.LocalName.Equals("MeshFile", StringComparison.OrdinalIgnoreCase)))
            {
                AssetEntry? mesh = FindReferencedAsset(meshElement.Value);
                if (mesh?.Kind != AssetKind.Model || meshElement.Parent is null) continue;
                candidates.Add(new CompositePartCandidate(mesh, string.Empty, meshElement.Parent, UsesMaterialReferences: false));
            }

            string name = System.IO.Path.GetFileNameWithoutExtension(config.Name) + "（完整组合）";
            CompositePartCandidate[] distinctCandidates = candidates
                .DistinctBy(part => $"{part.MeshAsset.DisplayPath}|{part.MaterialName}", StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (CompositeCandidateSet candidateSet in SelectCompositeCandidateSets(distinctCandidates, config))
            {
                bool isLabeledVariant = !string.IsNullOrWhiteSpace(candidateSet.Label);
                if (candidateSet.Candidates.Length < 2 && !isLabeledVariant) continue;

                CompositeModelPart[] distinctParts = candidateSet.Candidates
                    .Select(candidate => new CompositeModelPart(
                        candidate.MeshAsset,
                        candidate.MaterialName,
                        ResolveCompositeTexture(candidate, materialReferences, config.Entry.Path)))
                    .ToArray();
                if (distinctParts.Length < 2 && !isLabeledVariant) continue;

                string displayName = string.IsNullOrWhiteSpace(candidateSet.Label)
                    ? name
                    : candidateSet.Candidates.Length == 1
                        ? $"{System.IO.Path.GetFileNameWithoutExtension(config.Name)}（独立模型 {candidateSet.Label}）"
                        : $"{System.IO.Path.GetFileNameWithoutExtension(config.Name)}（完整组合 {candidateSet.Label}）";
                result.Add(new CompositeModelEntry(displayName, config, distinctParts));
            }
        }
        return result.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<ModelTextureBinding> ResolveCore(AssetEntry model)
    {
        string modelReference = NormalizeReference($"{System.IO.Path.GetFileNameWithoutExtension(model.ArchiveName)}/{model.Entry.Path}");
        var bindings = new List<ModelTextureBinding>();
        foreach (AssetEntry config in GetTextureCandidateConfigs(model))
        {
            string text;
            try
            {
                text = DecodeXml(_workspace.Extract(config));
            }
            catch
            {
                continue;
            }
            if (!TextMayReferenceModel(text, model)) continue;

            XDocument document;
            try
            {
                document = XDocument.Parse(text, LoadOptions.None);
            }
            catch
            {
                continue;
            }

            foreach (XElement meshElement in document.Descendants()
                         .Where(element => element.Name.LocalName.Equals("MeshFile", StringComparison.OrdinalIgnoreCase)))
            {
                if (!ReferenceMatches(meshElement.Value, modelReference)) continue;
                XElement? owner = meshElement.Parent;
                if (owner is null) continue;
                AddTextureElements(bindings, owner.Elements(), string.Empty, config.Entry.Path);
            }

            foreach (XElement modelElement in document.Descendants()
                         .Where(element => element.Name.LocalName.Equals("Model", StringComparison.OrdinalIgnoreCase)))
            {
                XAttribute? meshAttribute = modelElement.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("MeshFile", StringComparison.OrdinalIgnoreCase));
                if (meshAttribute is null || !ReferenceMatches(meshAttribute.Value, modelReference)) continue;

                string materialName = modelElement.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("MatName", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                foreach (XElement materialReference in document.Descendants()
                             .Where(element => element.Name.LocalName.Equals("Material", StringComparison.OrdinalIgnoreCase) &&
                                               element.Value.Contains(".cmf", StringComparison.OrdinalIgnoreCase)))
                {
                    AssetEntry? cmf = FindReferencedAsset(materialReference.Value);
                    if (cmf is not null) AddCmfBindings(bindings, cmf, materialName, config.Entry.Path);
                }
            }
        }

        if (bindings.Count == 0) AddHeuristicBinding(bindings, model);
        string? modelRoot = GetMeshRoot(model.Entry.Path);
        return bindings
            .DistinctBy(binding => $"{binding.TextureAsset.DisplayPath}|{binding.MapType}|{binding.MaterialName}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(binding => IsTextureUnderRoot(binding, modelRoot) ? 0 : 1)
            .ThenBy(binding => binding.MapType.StartsWith("BaseMap", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(binding => binding.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ModelTextureBinding? ResolveCompositeTexture(
        CompositePartCandidate candidate,
        IReadOnlyList<XElement> materialReferences,
        string configPath)
    {
        var bindings = new List<ModelTextureBinding>();
        if (candidate.UsesMaterialReferences)
        {
            foreach (XElement materialReference in materialReferences)
            {
                AssetEntry? cmf = FindReferencedAsset(materialReference.Value);
                if (cmf is null) continue;
                AddCmfBindings(bindings, cmf, candidate.MaterialName, configPath);
                ModelTextureBinding? baseMap = SelectBaseMapBinding(bindings, GetMeshRoot(candidate.MeshAsset.Entry.Path));
                if (baseMap is not null) return baseMap;
            }
        }
        else
        {
            AddTextureElements(bindings, candidate.SourceElement.Elements(), candidate.MaterialName, configPath);
        }

        return SelectBaseMapBinding(bindings, GetMeshRoot(candidate.MeshAsset.Entry.Path));
    }

    private CompositePartCandidate[] SelectCompositeCandidates(
        IReadOnlyList<CompositePartCandidate> candidates,
        AssetEntry config)
    {
        string? root = GetConfigRoot(config.Entry.Path);
        CompositePartCandidate[] selected = candidates.ToArray();

        if (root is null) return selected;
        string configRoot = root;
        bool isCharacterConfig = IsCharacterConfig(config.Entry.Path);

        CompositePartCandidate[] local = selected
            .Where(candidate => IsUnderRoot(candidate.MeshAsset.Entry.Path, configRoot))
            .ToArray();

        if (selected.Length > MaximumCompositeParts)
        {
            if (!isCharacterConfig && local.Length >= 2) selected = local;
            else if (isCharacterConfig) selected = candidates.ToArray();
            else return Array.Empty<CompositePartCandidate>();
        }

        selected = CollapseMaterialVariants(selected, config.ArchivePath, configRoot);
        if (!isCharacterConfig && !IsLikelyCharacterWardrobeComposite(selected)) return selected;

        HashSet<string> localMaterialNames = GetLocalMaterialNames(config.ArchivePath, configRoot);
        CompositePartCandidate[] characterSource = selected;
        if (isCharacterConfig)
        {
            CompositePartCandidate[] defaultBody = SelectDefaultCharacterBodyCandidates(characterSource, localMaterialNames);
            CompositePartCandidate[] defaultFilled = FillMissingCharacterBodySlots(
                defaultBody,
                characterSource,
                localMaterialNames,
                preferDefaultBase: true);
            if (IsCompleteCharacterBody(defaultFilled)) return defaultFilled;
        }

        CompositePartCandidate[] materialMatched = SelectLocalMaterialCandidates(
            local.Length > 0 ? local : candidates,
            config.ArchivePath,
            configRoot);
        if (materialMatched.Length >= 2)
        {
            CompositePartCandidate[] body = SelectCharacterBodyCandidates(
                CollapseMaterialVariants(materialMatched, config.ArchivePath, configRoot),
                config.ArchivePath,
                configRoot,
                includeAccessoryFallback: false);
            CompositePartCandidate[] filled = FillMissingCharacterBodySlots(body, selected, localMaterialNames);
            if (filled.Length >= 2) return filled;
            return body.Length >= 2 ? body : CollapseMaterialVariants(materialMatched, config.ArchivePath, configRoot);
        }

        if (selected.Length <= MaximumCharacterCompositeParts && !IsLikelyCharacterWardrobeComposite(selected))
            return selected;

        CompositePartCandidate[] reduced = SelectCharacterBodyCandidates(
            selected,
            config.ArchivePath,
            configRoot,
            includeAccessoryFallback: true);
        reduced = FillMissingCharacterBodySlots(reduced, selected, localMaterialNames);
        return reduced.Length >= 2 ? reduced : Array.Empty<CompositePartCandidate>();
    }

    private IReadOnlyList<CompositeCandidateSet> SelectCompositeCandidateSets(
        IReadOnlyList<CompositePartCandidate> candidates,
        AssetEntry config)
    {
        CompositePartCandidate[] selected = SelectCompositeCandidates(candidates, config);
        if (selected.Length < 2)
            return Array.Empty<CompositeCandidateSet>();

        if (IsCharacterConfig(config.Entry.Path))
            return new[] { new CompositeCandidateSet(string.Empty, selected) };

        CompositeCandidateSet[] splitVariants = SplitNonCharacterVariantComposite(selected);
        return splitVariants.Length > 1
            ? splitVariants
            : new[] { new CompositeCandidateSet(string.Empty, selected) };
    }

    private static CompositeCandidateSet[] SplitNonCharacterVariantComposite(
        IReadOnlyList<CompositePartCandidate> candidates)
    {
        var parsed = candidates
            .Select(candidate => (Candidate: candidate, Variant: GetNonCharacterVariantKey(candidate)))
            .ToArray();

        Dictionary<string, HashSet<string>> variantFamilies = parsed
            .Where(item => item.Variant is not null)
            .GroupBy(item => item.Variant!.Family, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Family = group.Key,
                Codes = group
                    .Select(item => item.Variant!.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            })
            .Where(group => group.Codes.Count > 1)
            .ToDictionary(group => group.Family, group => group.Codes, StringComparer.OrdinalIgnoreCase);

        if (variantFamilies.Count == 0) return Array.Empty<CompositeCandidateSet>();

        string[] variantCodes = variantFamilies.Values
            .SelectMany(codes => codes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => int.TryParse(code, out int number) ? number : int.MaxValue)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (variantCodes.Length < 2 || variantCodes.Length > 12)
            return Array.Empty<CompositeCandidateSet>();

        Dictionary<string, int> variantCodeCounts = parsed
            .Where(item => item.Variant is not null && variantFamilies.ContainsKey(item.Variant.Family))
            .GroupBy(item => item.Variant!.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        if (variantFamilies.Count == 1)
        {
            bool everyCodeIsSingleMesh = variantCodes.All(code =>
                variantCodeCounts.TryGetValue(code, out int count) && count == 1);
            bool everyCodeIsSmallAccessory = variantCodes.All(code =>
                variantCodeCounts.TryGetValue(code, out int count) && count <= 2);

            if (!everyCodeIsSingleMesh && (!HasSharedNonVariantParts(parsed, variantFamilies) || !everyCodeIsSmallAccessory))
                return Array.Empty<CompositeCandidateSet>();
        }

        bool independentSingleMeshVariants = parsed.All(item =>
            item.Variant is not null && variantFamilies.ContainsKey(item.Variant.Family));
        bool hasSharedParts = parsed.Any(item =>
            item.Variant is null || !variantFamilies.ContainsKey(item.Variant.Family));

        var result = new List<CompositeCandidateSet>();
        foreach (string code in variantCodes)
        {
            CompositePartCandidate[] set = parsed
                .Where(item => item.Variant is null ||
                               !variantFamilies.ContainsKey(item.Variant.Family) ||
                               item.Variant.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Candidate)
                .ToArray();

            int keyedCount = set.Count(candidate =>
            {
                NonCharacterVariantKey? variant = GetNonCharacterVariantKey(candidate);
                return variant is not null &&
                       variantFamilies.ContainsKey(variant.Family) &&
                       variant.Code.Equals(code, StringComparison.OrdinalIgnoreCase);
            });
            if (keyedCount >= 2 ||
                (hasSharedParts && set.Length >= 2 && keyedCount >= 1) ||
                (independentSingleMeshVariants && set.Length == 1 && keyedCount == 1))
                result.Add(new CompositeCandidateSet(code, set));
        }

        return result.Count > 1 ? result.ToArray() : Array.Empty<CompositeCandidateSet>();
    }

    private static bool HasSharedNonVariantParts(
        IEnumerable<(CompositePartCandidate Candidate, NonCharacterVariantKey? Variant)> parsed,
        Dictionary<string, HashSet<string>> variantFamilies) =>
        parsed.Any(item => item.Variant is null || !variantFamilies.ContainsKey(item.Variant.Family));

    private static NonCharacterVariantKey? GetNonCharacterVariantKey(CompositePartCandidate candidate)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(candidate.MeshAsset.Name).ToLowerInvariant();
        string[] segments = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 1; index < segments.Length; index++)
        {
            string segment = segments[index];
            int digitEnd = 0;
            while (digitEnd < segment.Length && char.IsDigit(segment[digitEnd]))
                digitEnd++;

            if (digitEnd < 2) continue;
            string suffix = segment[digitEnd..];
            if (suffix.Length > 1 || suffix.Any(ch => !char.IsLetter(ch))) continue;

            string family = string.Join('_', segments.Take(index));
            if (family.Length == 0) continue;
            return new NonCharacterVariantKey(family, segment[..digitEnd]);
        }

        return null;
    }

    private static bool IsCharacterConfig(string configPath)
    {
        string normalized = NormalizeAssetPath(configPath);
        return normalized.StartsWith("special/zj_", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/special/zj_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyCharacterWardrobeComposite(IReadOnlyList<CompositePartCandidate> candidates)
    {
        if (candidates.Count <= MaximumCharacterFallbackParts) return false;

        // Character CCT files can contain a wardrobe library of mutually exclusive meshes.
        // Rendering all of them as one composite stacks every outfit and accessory together.
        return candidates.Count(IsWardrobePartName) >= candidates.Count / 2;
    }

    private static bool IsWardrobePartName(CompositePartCandidate candidate)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(candidate.MeshAsset.Name);
        return name.StartsWith("hd_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("mz_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("sz_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("yf_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("xz_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("kz_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("gj_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("dp_", StringComparison.OrdinalIgnoreCase);
    }

    private CompositePartCandidate[] SelectCharacterBodyCandidates(
        IReadOnlyList<CompositePartCandidate> candidates,
        string archivePath,
        string root,
        bool includeAccessoryFallback)
    {
        HashSet<string> localMaterialNames = GetLocalMaterialNames(archivePath, root);
        CompositePartCandidate[] result = candidates
            .Select(candidate => (Candidate: candidate, Slot: GetCharacterSlot(candidate, includeAccessoryFallback)))
            .Where(item => item.Slot is not null)
            .GroupBy(item => item.Slot!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => GetCharacterSlotOrder(group.Key))
            .Select(group => group
                .Select(item => item.Candidate)
                .OrderBy(candidate => ScoreCharacterPart(candidate, localMaterialNames))
                .ThenBy(candidate => candidate.MeshAsset.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .Take(MaximumCharacterFallbackParts)
            .ToArray();
        return result;
    }

    private static CompositePartCandidate[] SelectDefaultCharacterBodyCandidates(
        IReadOnlyList<CompositePartCandidate> candidates,
        HashSet<string> localMaterialNames)
    {
        return candidates
            .Select(candidate => (Candidate: candidate, Slot: GetCharacterSlot(candidate, includeAccessoryFallback: false)))
            .Where(item => item.Slot is not null)
            .GroupBy(item => item.Slot!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => GetCharacterSlotOrder(group.Key))
            .Select(group => group
                .Select(item => item.Candidate)
                .OrderBy(candidate => ScoreDefaultCharacterPart(candidate, localMaterialNames))
                .ThenBy(candidate => candidate.MeshAsset.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .Take(MaximumCharacterFallbackParts)
            .ToArray();
    }

    private static bool IsCompleteCharacterBody(IReadOnlyList<CompositePartCandidate> candidates)
    {
        HashSet<string> slots = candidates
            .Select(candidate => GetCharacterSlot(candidate, includeAccessoryFallback: true))
            .Where(slot => slot is not null)
            .Select(slot => slot!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return slots.Contains("hd") &&
               slots.Contains("st") &&
               slots.Contains("sz") &&
               (slots.Contains("kz") || slots.Contains("tui")) &&
               slots.Contains("xz") &&
               (slots.Contains("gl") || slots.Contains("gla")) &&
               (slots.Contains("gr") || slots.Contains("gra"));
    }

    private static CompositePartCandidate[] FillMissingCharacterBodySlots(
        IReadOnlyList<CompositePartCandidate> primary,
        IReadOnlyList<CompositePartCandidate> fallback,
        HashSet<string> localMaterialNames,
        bool preferDefaultBase = false)
    {
        var selectedBySlot = primary
            .Select(candidate => (Candidate: candidate, Slot: GetCharacterSlot(candidate, includeAccessoryFallback: true)))
            .Where(item => item.Slot is not null)
            .GroupBy(item => item.Slot!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.Candidate)
                    .OrderBy(candidate => preferDefaultBase
                        ? ScoreDefaultCharacterPart(candidate, localMaterialNames)
                        : ScoreCharacterPart(candidate, localMaterialNames))
                    .ThenBy(candidate => candidate.MeshAsset.Name, StringComparer.OrdinalIgnoreCase)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        if (preferDefaultBase && selectedBySlot.ContainsKey("kz"))
            selectedBySlot.Remove("tui");

        string? preferredStyleCode = preferDefaultBase
            ? null
            : GetDominantCharacterStyleCode(selectedBySlot.Values);

        foreach (IGrouping<string, CompositePartCandidate> group in fallback
                     .Select(candidate => (Candidate: candidate, Slot: GetCharacterSlot(candidate, includeAccessoryFallback: false)))
                     .Where(item => item.Slot is not null)
                     .GroupBy(item => item.Slot!, item => item.Candidate, StringComparer.OrdinalIgnoreCase))
        {
            if (selectedBySlot.ContainsKey(group.Key)) continue;
            if (group.Key.Equals("tui", StringComparison.OrdinalIgnoreCase) &&
                selectedBySlot.ContainsKey("kz") &&
                preferredStyleCode is null)
            {
                continue;
            }

            if (group.Key.Equals("kz", StringComparison.OrdinalIgnoreCase) &&
                selectedBySlot.ContainsKey("tui"))
            {
                continue;
            }

            CompositePartCandidate[] candidates = group.ToArray();
            if (group.Key.Equals("tui", StringComparison.OrdinalIgnoreCase) &&
                preferredStyleCode is not null)
            {
                candidates = candidates
                    .Where(candidate => GetCharacterStyleCode(candidate).Equals(preferredStyleCode, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (candidates.Length == 0) continue;
            }

            selectedBySlot[group.Key] = candidates
                .OrderBy(candidate => preferDefaultBase
                    ? ScoreDefaultCharacterPart(candidate, localMaterialNames)
                    : ScoreCharacterPart(candidate, localMaterialNames, selectedBySlot.Values))
                .ThenBy(candidate => candidate.MeshAsset.Name, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        if (preferredStyleCode is not null)
        {
            foreach (IGrouping<string, CompositePartCandidate> group in fallback
                         .Select(candidate => (Candidate: candidate, Slot: GetCharacterAccessorySlot(candidate)))
                         .Where(item => item.Slot is not null &&
                                        GetCharacterStyleCode(item.Candidate).Equals(preferredStyleCode, StringComparison.OrdinalIgnoreCase))
                         .GroupBy(item => item.Slot!, item => item.Candidate, StringComparer.OrdinalIgnoreCase))
            {
                if (selectedBySlot.ContainsKey(group.Key)) continue;
                selectedBySlot[group.Key] = group
                    .OrderBy(candidate => ScoreCharacterPart(candidate, localMaterialNames, selectedBySlot.Values))
                    .ThenBy(candidate => candidate.MeshAsset.Name, StringComparer.OrdinalIgnoreCase)
                    .First();
            }
        }
        else
        {
            AddSameStyleAccessorySlot(selectedBySlot, fallback, localMaterialNames, "yd", "001");
        }

        return selectedBySlot
            .OrderBy(pair => GetCharacterSlotOrder(pair.Key))
            .ThenBy(pair => pair.Value.MeshAsset.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value)
            .Take(MaximumCharacterFallbackParts)
            .ToArray();
    }

    private static void AddSameStyleAccessorySlot(
        Dictionary<string, CompositePartCandidate> selectedBySlot,
        IReadOnlyList<CompositePartCandidate> fallback,
        HashSet<string> localMaterialNames,
        string slot,
        string styleCode)
    {
        if (selectedBySlot.ContainsKey(slot)) return;

        CompositePartCandidate? candidate = fallback
            .Where(candidate =>
                string.Equals(GetCharacterAccessorySlot(candidate), slot, StringComparison.OrdinalIgnoreCase) &&
                GetCharacterStyleCode(candidate).Equals(styleCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => ScoreCharacterPart(candidate, localMaterialNames, selectedBySlot.Values))
            .ThenBy(candidate => candidate.MeshAsset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (candidate is not null)
            selectedBySlot[slot] = candidate;
    }

    private static string? GetCharacterSlot(CompositePartCandidate candidate, bool includeAccessoryFallback)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(candidate.MeshAsset.Name).ToLowerInvariant();
        if (name.StartsWith("hd_")) return "hd";
        if (name.StartsWith("mz_")) return "mz";
        if (name.StartsWith("st_")) return "st";
        if (name.StartsWith("sz_")) return "sz";
        if (name.StartsWith("tui_")) return "tui";
        if (name.StartsWith("xz_")) return "xz";
        if (name.StartsWith("kz_")) return "kz";
        if (name.StartsWith("gl_")) return "gl";
        if (name.StartsWith("gla_")) return "gla";
        if (name.StartsWith("gr_")) return "gr";
        if (name.StartsWith("gra_")) return "gra";

        if (!includeAccessoryFallback) return null;
        int marker = name.IndexOf('_');
        if (marker <= 0) return null;
        string prefix = name[..marker];
        return prefix is "gj" or "mj" or "dp" or "tf" or "qz" or "xw" or "yd" or "sy" or "gb"
            ? prefix
            : null;
    }

    private static string? GetCharacterAccessorySlot(CompositePartCandidate candidate)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(candidate.MeshAsset.Name).ToLowerInvariant();
        int marker = name.IndexOf('_');
        if (marker <= 0) return null;
        string prefix = name[..marker];
        return prefix is "qz" or "yd" or "xw" or "sy" ? prefix : null;
    }

    private static int GetCharacterSlotOrder(string slot) => slot switch
    {
        "hd" => 0,
        "mz" => 1,
        "st" => 2,
        "sz" => 3,
        "qz" => 4,
        "yd" => 5,
        "xw" => 6,
        "sy" => 7,
        "tui" => 8,
        "kz" => 9,
        "xz" => 10,
        "gl" => 11,
        "gla" => 12,
        "gr" => 13,
        "gra" => 14,
        _ => 20
    };

    private static int ScoreCharacterPart(
        CompositePartCandidate candidate,
        HashSet<string> localMaterialNames,
        IEnumerable<CompositePartCandidate>? preferredStyleSource = null)
    {
        string meshName = System.IO.Path.GetFileNameWithoutExtension(candidate.MeshAsset.Name).ToLowerInvariant();
        string material = candidate.MaterialName;
        int score = MaterialMatchesLocal(localMaterialNames, material) ? 0 : 100;
        string styleCode = GetCharacterStyleCode(candidate);
        string? preferredStyleCode = preferredStyleSource is null
            ? null
            : GetDominantCharacterStyleCode(preferredStyleSource);
        if (preferredStyleCode is not null)
            score += styleCode.Equals(preferredStyleCode, StringComparison.OrdinalIgnoreCase) ? -40 : 40;
        if (material.StartsWith("zj_zj", StringComparison.OrdinalIgnoreCase)) score -= 10;
        if (meshName.EndsWith("_001", StringComparison.OrdinalIgnoreCase)) score -= 5;
        if (meshName.Contains("_993", StringComparison.OrdinalIgnoreCase) ||
            meshName.Contains("_983", StringComparison.OrdinalIgnoreCase) ||
            meshName.Contains("_996", StringComparison.OrdinalIgnoreCase))
            score += 10;
        return score;
    }

    private static int ScoreDefaultCharacterPart(
        CompositePartCandidate candidate,
        HashSet<string> localMaterialNames)
    {
        string meshName = System.IO.Path.GetFileNameWithoutExtension(candidate.MeshAsset.Name).ToLowerInvariant();
        string material = candidate.MaterialName;
        string styleCode = GetCharacterStyleCode(candidate);
        int score = 0;

        if (styleCode.Equals("001", StringComparison.OrdinalIgnoreCase)) score -= 220;
        else if (styleCode.Length > 0) score += 35;

        if (material.StartsWith("zj_zj", StringComparison.OrdinalIgnoreCase)) score -= 45;
        else if (material.StartsWith("c_yd101", StringComparison.OrdinalIgnoreCase)) score -= 20;
        else if (MaterialMatchesLocal(localMaterialNames, material)) score -= 5;

        if (LooksLikeNumberedWardrobeMaterial(material)) score += 70;

        if (meshName.Contains("_983", StringComparison.OrdinalIgnoreCase) ||
            meshName.Contains("_987", StringComparison.OrdinalIgnoreCase) ||
            meshName.Contains("_993", StringComparison.OrdinalIgnoreCase) ||
            meshName.Contains("_996", StringComparison.OrdinalIgnoreCase))
            score += 120;

        return score;
    }

    private static bool LooksLikeNumberedWardrobeMaterial(string materialName)
    {
        string material = materialName.ToLowerInvariant();
        if (material.StartsWith("c_yd101", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (string marker in new[] { "yf", "st", "sz", "xz", "kz", "gb", "qz", "yd", "xw", "sy" })
        {
            int searchFrom = 0;
            while (searchFrom < material.Length)
            {
                int index = material.IndexOf(marker, searchFrom, StringComparison.Ordinal);
                if (index < 0) break;
                int digitStart = index + marker.Length;
                int digitCount = 0;
                while (digitStart + digitCount < material.Length &&
                       char.IsDigit(material[digitStart + digitCount]))
                    digitCount++;
                if (digitCount >= 3) return true;
                searchFrom = digitStart + Math.Max(1, digitCount);
            }
        }

        return false;
    }

    private static string GetCharacterStyleCode(CompositePartCandidate candidate)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(candidate.MeshAsset.Name);
        int marker = name.LastIndexOf('_');
        if (marker < 0 || marker == name.Length - 1) return string.Empty;
        string suffix = name[(marker + 1)..];
        int end = 0;
        while (end < suffix.Length && char.IsDigit(suffix[end])) end++;
        return end == 0 ? string.Empty : suffix[..end];
    }

    private static string? GetDominantCharacterStyleCode(IEnumerable<CompositePartCandidate> candidates) =>
        candidates
            .Select(GetCharacterStyleCode)
            .Where(code => code.Length > 0 && !code.Equals("001", StringComparison.OrdinalIgnoreCase))
            .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();

    private CompositePartCandidate[] SelectLocalMaterialCandidates(
        IReadOnlyList<CompositePartCandidate> candidates,
        string archivePath,
        string root)
    {
        HashSet<string> localMaterialNames = GetLocalMaterialNames(archivePath, root);
        if (localMaterialNames.Count == 0) return Array.Empty<CompositePartCandidate>();

        return candidates
            .Where(candidate => MaterialMatchesLocal(localMaterialNames, candidate.MaterialName))
            .GroupBy(candidate => candidate.MeshAsset.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private CompositePartCandidate[] CollapseMaterialVariants(
        IReadOnlyList<CompositePartCandidate> candidates,
        string archivePath,
        string root)
    {
        HashSet<string> localMaterialNames = GetLocalMaterialNames(archivePath, root);

        return candidates
            .GroupBy(candidate => candidate.MeshAsset.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => localMaterialNames.Count == 0
                ? group.First()
                : group.FirstOrDefault(candidate => MaterialMatchesLocal(localMaterialNames, candidate.MaterialName)) ?? group.First())
            .ToArray();
    }

    private HashSet<string> GetLocalMaterialNames(string archivePath, string root) =>
        _workspace.Assets
            .Where(asset => string.Equals(asset.ArchivePath, archivePath, StringComparison.OrdinalIgnoreCase) &&
                            asset.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) &&
                            IsUnderRoot(asset.Entry.Path, root))
            .Select(asset => System.IO.Path.GetFileNameWithoutExtension(asset.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool MaterialMatchesLocal(HashSet<string> localMaterialNames, string materialName)
    {
        if (localMaterialNames.Contains(materialName)) return true;

        string alias = TrimMaterialVariantSuffix(materialName);
        return !alias.Equals(materialName, StringComparison.OrdinalIgnoreCase) &&
               localMaterialNames.Contains(alias);
    }

    private static bool MaterialNameMatches(string? actualName, string requestedName)
    {
        if (string.IsNullOrWhiteSpace(actualName)) return false;
        if (actualName.Equals(requestedName, StringComparison.OrdinalIgnoreCase)) return true;

        string requestedAlias = TrimMaterialVariantSuffix(requestedName);
        return !requestedAlias.Equals(requestedName, StringComparison.OrdinalIgnoreCase) &&
               actualName.Equals(requestedAlias, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimMaterialVariantSuffix(string materialName)
    {
        string trimmed = materialName.Trim();
        int index = trimmed.Length - 1;
        while (index >= 0 && char.IsDigit(trimmed[index])) index--;
        if (index < trimmed.Length - 1 && index >= 0 && trimmed[index] == 'h')
            return trimmed[..(index + 1)];
        return trimmed;
    }

    private IReadOnlyList<AssetEntry> GetTextureCandidateConfigs(AssetEntry model)
    {
        AssetEntry[] configs = _workspace.Assets
            .Where(asset => string.Equals(asset.ArchivePath, model.ArchivePath, StringComparison.OrdinalIgnoreCase) &&
                            asset.Extension.Equals(".cct", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string? root = GetMeshRoot(model.Entry.Path);
        if (root is not null)
        {
            string configPrefix = root + "/config/";
            AssetEntry[] localConfigs = configs
                .Where(config => NormalizeAssetPath(config.Entry.Path).StartsWith(configPrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (localConfigs.Length > 0) return localConfigs;
        }

        return configs;
    }

    private static bool TextMayReferenceModel(string text, AssetEntry model)
    {
        string path = NormalizeAssetPath(model.Entry.Path);
        return text.Contains(path, StringComparison.OrdinalIgnoreCase) ||
               text.Contains(path.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
    }

    private void AddCmfBindings(List<ModelTextureBinding> bindings, AssetEntry cmf, string materialName, string configPath)
    {
        try
        {
            XDocument document = XDocument.Parse(DecodeXml(_workspace.Extract(cmf)), LoadOptions.None);
            IEnumerable<XElement> materials = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("Material", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(materialName))
            {
                materials = materials.Where(element => MaterialNameMatches(
                    element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))?.Value,
                    materialName));
            }

            foreach (XElement material in materials)
                AddTextureElements(bindings, material.Elements(), materialName, $"{configPath} → {cmf.Entry.Path}");
        }
        catch
        {
            // 个别历史 CMF 不符合 XML 规范时，保留实体着色预览。
        }
    }

    private void AddTextureElements(
        List<ModelTextureBinding> bindings,
        IEnumerable<XElement> elements,
        string materialName,
        string configPath)
    {
        foreach (XElement element in elements.Where(element =>
                     element.Name.LocalName.Contains("Map", StringComparison.OrdinalIgnoreCase) &&
                     element.Value.Contains(".dds", StringComparison.OrdinalIgnoreCase)))
        {
            AssetEntry? texture = FindReferencedAsset(element.Value);
            if (texture is not null)
                bindings.Add(new ModelTextureBinding(texture, element.Name.LocalName, materialName, configPath));
        }
    }

    private void AddHeuristicBinding(List<ModelTextureBinding> bindings, AssetEntry model)
    {
        string basename = System.IO.Path.GetFileNameWithoutExtension(model.Name);
        AssetEntry? texture = _workspace.Assets.FirstOrDefault(asset =>
            string.Equals(asset.ArchivePath, model.ArchivePath, StringComparison.OrdinalIgnoreCase) &&
            asset.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) &&
            System.IO.Path.GetFileNameWithoutExtension(asset.Name).Equals(basename, StringComparison.OrdinalIgnoreCase));
        if (texture is not null)
            bindings.Add(new ModelTextureBinding(texture, "同名贴图", string.Empty, "文件名回退匹配"));
    }

    private AssetEntry? FindReferencedAsset(string reference)
    {
        string normalized = NormalizeReference(reference);
        return AssetByReference.TryGetValue(normalized, out AssetEntry? asset) ? asset : null;
    }

    private Dictionary<string, AssetEntry> AssetByReference =>
        _assetByReference ??= _workspace.Assets
            .GroupBy(asset => NormalizeAssetPath(
                $"{System.IO.Path.GetFileNameWithoutExtension(asset.ArchiveName)}/{asset.Entry.Path}"),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static bool ReferenceMatches(string reference, string normalizedTarget) =>
        NormalizeReference(reference).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string reference)
    {
        string normalized = reference.Trim().Replace('\\', '/');
        if (normalized.StartsWith("$(res)", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[6..].TrimStart('/');
        return normalized.TrimStart('/');
    }

    private static string NormalizeAssetPath(string path) => path.Replace('\\', '/').Trim('/');

    private static string? GetConfigRoot(string configPath)
    {
        string normalized = NormalizeAssetPath(configPath);
        int marker = normalized.IndexOf("/config/", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? normalized[..marker] : null;
    }

    private static string? GetMeshRoot(string meshPath)
    {
        string normalized = NormalizeAssetPath(meshPath);
        int marker = normalized.IndexOf("/mesh/", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? normalized[..marker] : null;
    }

    private static bool IsUnderRoot(string assetPath, string root)
    {
        string normalized = NormalizeAssetPath(assetPath);
        return normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextureUnderRoot(ModelTextureBinding binding, string? root) =>
        root is not null && IsUnderRoot(binding.TextureAsset.Entry.Path, root);

    private static ModelTextureBinding? SelectBaseMapBinding(
        IReadOnlyList<ModelTextureBinding> bindings,
        string? root) =>
        bindings
            .Where(binding => binding.MapType.StartsWith("BaseMap", StringComparison.OrdinalIgnoreCase))
            .OrderBy(binding => IsTextureUnderRoot(binding, root) ? 0 : 1)
            .ThenBy(binding => binding.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static string DecodeXml(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data).TrimStart('\uFEFF');
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data).TrimStart('\uFEFF');
        return Encoding.UTF8.GetString(data).TrimStart('\uFEFF');
    }
}
