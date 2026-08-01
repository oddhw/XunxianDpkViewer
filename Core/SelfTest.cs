using System.Text;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class SelfTest
{
    public static string Run()
    {
        string[] candidates =
        {
            @"D:\Program Files\腾讯游戏\新寻仙\res",
            @"C:\Program Files\腾讯游戏\新寻仙\res"
        };
        string root = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "gui.dpk")))
            ?? throw new DirectoryNotFoundException("没有发现《新寻仙》res 目录。");

        var report = new StringBuilder()
            .AppendLine($"资源目录: {root}")
            .AppendLine($"时间: {DateTimeOffset.Now:O}");

        CheckFile(report, root, "gui.dpk", ".png", data =>
        {
            if (!data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException("PNG 文件头不正确。");
            return "PNG 文件头正确";
        });
        CheckFile(report, root, "sound.dpk", ".ogg", data =>
        {
            if (!data.AsSpan(0, 4).SequenceEqual("OggS"u8))
                throw new InvalidDataException("OGG 文件头不正确。");
            return "OGG 文件头正确";
        });
        CheckFile(report, root, "obj.dpk", ".pmf", data =>
        {
            PmfMesh mesh = PmfParser.Parse(data);
            string target = Path.Combine(Path.GetTempPath(), "xunxian-dpk-self-test.obj");
            try
            {
                ObjExporter.Export(mesh, target, "self_test");
                if (!File.Exists(target) || new FileInfo(target).Length == 0)
                    throw new InvalidDataException("OBJ 导出结果为空。");
            }
            finally
            {
                File.Delete(target);
            }
            return DescribeMesh(mesh) + "，OBJ 导出正确";
        });
        CheckFile(report, root, "cha.dpk", ".pmf", data => DescribeMesh(PmfParser.Parse(data)));
        CheckModelTexture(report, root, "obj.dpk", "share/mesh/gx_jzjcxjgwkc_004_h.pmf");
        CheckModelTexture(report, root, "cha.dpk", "share/mesh/cw/mz635_mz_001.pmf");
        CheckModelTexture(report, root, "cha.dpk", "special/zj_tuzinv_042/mesh/hd_001.pmf",
            "special/zj_tuzinv_042/texture/zj_zjhd_042_h.dds");
        CheckModelTexture(report, root, "cha.dpk", "special/zj_tuzinv_042/mesh/mz_001.pmf",
            "special/zj_tuzinv_042/texture/zj_zjmz_042_h.dds");
        CheckSmallScaleModel(report, root, "cha.dpk", "special/zj_tuzinv_042/mesh/hd_001.pmf");
        CheckSmallScaleModel(report, root, "cha.dpk", "special/zj_tuzinv_042/mesh/mz_001.pmf");
        CheckCompositeModel(report, root, "cha.dpk", "special/gw_cwbiyiniaoludi_1313");
        CheckCompositeModel(report, root, "cha.dpk", "special/gw_hlnubing_187");
        CheckCompositeModel(report, root, "cha.dpk", "special/zj_tuzinv_042", maximumParts: 64);
        CheckCompositeModel(report, root, "cha.dpk", "special/zj_waiguonan_025", maximumParts: 64);
        CheckCompositeObjExport(report, root, "cha.dpk", "special/gw_errenzhuanmao_1692", minimumParts: 3);
        CheckCompleteClientIndex(report, root);
        report.AppendLine("SELF-TEST PASSED");
        return report.ToString();
    }

    private static void CheckFile(
        StringBuilder report,
        string root,
        string archiveName,
        string extension,
        Func<byte[], string> validate)
    {
        using var reader = new DpkReader(Path.Combine(root, archiveName));
        IReadOnlyList<Models.DpkEntry> entries = reader.ReadEntries();
        Models.DpkEntry sample = entries.First(entry => entry.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        byte[] data = reader.Extract(sample);
        report.AppendLine($"{archiveName}: {entries.Count:N0} 项；{sample.Path}；{data.Length:N0} 字节；{validate(data)}");
    }

    private static void CheckCompositeModel(
        StringBuilder report,
        string root,
        string archiveName,
        string folderPath,
        int maximumParts = int.MaxValue)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        CompositeModelEntry composite = workspace.FindCompositeModels(
            workspace.ArchivePaths.Single(), folderPath).First();
        if (composite.Parts.Count > maximumParts)
            throw new InvalidDataException(
                $"Composite {folderPath} has {composite.Parts.Count:N0} parts; expected at most {maximumParts:N0}.");
        int texturedParts = composite.Parts.Count(part => part.TextureBinding is not null);
        foreach (CompositeModelPart part in composite.Parts)
            _ = PmfParser.Parse(workspace.Extract(part.MeshAsset));
        report.AppendLine($"{archiveName} 组合模型: {composite.Name}；{composite.Parts.Count:N0} 个部件；{texturedParts:N0} 个贴图材质");
    }

    private static void CheckCompositeObjExport(
        StringBuilder report,
        string root,
        string archiveName,
        string folderPath,
        int minimumParts)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        CompositeModelEntry? composite = workspace.FindCompositeModels(workspace.ArchivePaths.Single(), folderPath)
            .FirstOrDefault();
        if (composite is null)
        {
            report.AppendLine($"{archiveName} 组合 OBJ 导出: {folderPath} 未找到，跳过兼容性检查");
            return;
        }

        if (composite.Parts.Count < minimumParts)
            throw new InvalidDataException(
                $"{folderPath} 组合部件过少：{composite.Parts.Count:N0} / {minimumParts:N0}。");

        ObjExporter.ObjPart[] parts = composite.Parts
            .Select((part, index) => new ObjExporter.ObjPart(
                $"{index + 1:000}_{Path.GetFileNameWithoutExtension(part.MeshAsset.Name)}",
                PmfParser.Parse(workspace.Extract(part.MeshAsset)),
                string.IsNullOrWhiteSpace(part.MaterialName)
                    ? Path.GetFileNameWithoutExtension(part.MeshAsset.Name)
                    : part.MaterialName,
                part.TextureBinding?.TextureAsset.Name))
            .ToArray();
        long compositeTriangles = parts.Sum(part => (long)part.Mesh.DeclaredTriangleCount);
        long largestSinglePart = parts.Max(part => (long)part.Mesh.DeclaredTriangleCount);
        if (compositeTriangles <= largestSinglePart)
            throw new InvalidDataException(
                $"{folderPath} 组合 OBJ 面数没有超过单个 PMF：组合 {compositeTriangles:N0}，单件最大 {largestSinglePart:N0}。");

        string target = Path.Combine(Path.GetTempPath(), "xunxian-dpk-composite-self-test.obj");
        string material = Path.ChangeExtension(target, ".mtl");
        try
        {
            ObjExporter.Export(parts, target, composite.Name);
            if (!File.Exists(target) || new FileInfo(target).Length == 0)
                throw new InvalidDataException("组合 OBJ 导出结果为空。");
            int exportedFaces = File.ReadLines(target).Count(line => line.StartsWith("f ", StringComparison.Ordinal));
            if (exportedFaces <= largestSinglePart)
                throw new InvalidDataException(
                    $"{folderPath} 组合 OBJ 导出的面数仍像单部件：{exportedFaces:N0} / {compositeTriangles:N0}。");
        }
        finally
        {
            File.Delete(target);
            File.Delete(material);
        }

        report.AppendLine($"{archiveName} 组合 OBJ 导出: {composite.Name}；{parts.Length:N0} 个部件；{compositeTriangles:N0} 三角面");
    }

    private static void CheckModelTexture(
        StringBuilder report,
        string root,
        string archiveName,
        string modelPath,
        string? expectedTexturePath = null)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        AssetEntry model = workspace.Assets.First(asset =>
            asset.Kind == AssetKind.Model && asset.Entry.Path.Equals(modelPath, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<ModelTextureBinding> bindings = workspace.ResolveModelTextures(model);
        ModelTextureBinding baseMap = bindings.First(binding =>
            binding.MapType.StartsWith("BaseMap", StringComparison.OrdinalIgnoreCase));
        if (expectedTexturePath is not null &&
            !baseMap.TextureAsset.Entry.Path.Equals(expectedTexturePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{modelPath} 默认贴图应为 {expectedTexturePath}，实际为 {baseMap.TextureAsset.Entry.Path}。");
        }

        DecodedTexture texture = DdsDecoder.Decode(workspace.Extract(baseMap.TextureAsset));
        report.AppendLine($"{archiveName} 贴图链: {model.Name} → {baseMap.ConfigPath} → {baseMap.TextureAsset.Entry.Path}；{texture.Width}×{texture.Height} {texture.Format}");
    }

    private static void CheckSmallScaleModel(StringBuilder report, string root, string archiveName, string modelPath)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        AssetEntry model = workspace.Assets.First(asset =>
            asset.Kind == AssetKind.Model && asset.Entry.Path.Equals(modelPath, StringComparison.OrdinalIgnoreCase));
        PmfMesh mesh = PmfParser.Parse(workspace.Extract(model));
        int renderableTriangles = 0;
        for (int offset = 0; offset + 2 < mesh.Indices.Count; offset += 3)
        {
            System.Numerics.Vector3 a = mesh.Vertices[mesh.Indices[offset]];
            System.Numerics.Vector3 b = mesh.Vertices[mesh.Indices[offset + 1]];
            System.Numerics.Vector3 c = mesh.Vertices[mesh.Indices[offset + 2]];
            System.Numerics.Vector3 normal = System.Numerics.Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() > 1e-20f) renderableTriangles++;
        }

        if (renderableTriangles < mesh.DeclaredTriangleCount * 0.9)
            throw new InvalidDataException(
                $"{modelPath} 可渲染三角面过少：{renderableTriangles:N0} / {mesh.DeclaredTriangleCount:N0}。");
        report.AppendLine($"{archiveName} 小尺度模型: {model.Name}；{renderableTriangles:N0} / {mesh.DeclaredTriangleCount:N0} 个三角面可填充");
    }

    private static void CheckCompleteClientIndex(StringBuilder report, string root)
    {
        string[] expectedArchives =
        {
            "cha.dpk", "font.dpk", "gfx.dpk", "gui.dpk", "movie.dpk", "music.dpk", "obj.dpk",
            "scn.dpk", "sky.dpk", "sound.dpk", "system.dpk", "terr.dpk", "water.dpk"
        };
        using var workspace = new DpkWorkspace();
        workspace.OpenClientResourceFolder(root);
        string[] loaded = workspace.ArchivePaths.Select(Path.GetFileName).OfType<string>().ToArray();
        string[] missing = expectedArchives.Except(loaded, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"未加载 DPK：{string.Join(", ", missing)}");
        int fonts = workspace.Assets.Count(asset => asset.Kind == AssetKind.Font);
        if (fonts != 3)
            throw new InvalidDataException($"font.dpk 应包含 3 个字体，实际识别到 {fonts} 个。");
        string? clientRoot = Directory.GetParent(root)?.FullName;
        if (clientRoot is not null && File.Exists(Path.Combine(clientRoot, "mb.dpk")))
        {
            AssetEntry[] mbTables = workspace.Assets
                .Where(asset => asset.Kind == AssetKind.MbTable)
                .ToArray();
            if (mbTables.Length == 0)
                throw new InvalidDataException("已发现 mb.dpk，但没有加载到 MB 表菜单。");
            if (mbTables.Any(asset => !asset.ArchiveName.Equals("mb.dpk", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("MB 表菜单混入了非 mb.dpk 资源。");
            report.AppendLine($"MB 表索引: {mbTables.Length:N0} 个表资源；示例 {mbTables[0].Entry.Path}");
        }
        AssetEntry grass = workspace.Assets.First(asset =>
            asset.ArchiveName.Equals("scn.dpk", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Equals("grass.byte", StringComparison.OrdinalIgnoreCase));
        ResourceExplanation grassExplanation = ResourceExplanationService.Explain(grass);
        if (grassExplanation.FriendlyName != "草地分布数据" ||
            ResourceExplanationService.GetFolderDisplayName("(3,2)") != "地图分块 X=3，Y=2")
            throw new InvalidDataException("新手说明映射自检失败。");
        report.AppendLine($"完整客户端索引: {loaded.Length:N0} 个 DPK；{workspace.Assets.Count:N0} 个资源；{fonts:N0} 个 TTF 字体");
        report.AppendLine($"新手说明: grass.byte → {grassExplanation.FriendlyName}；(3,2) → 地图分块 X=3，Y=2");
    }

    private static string DescribeMesh(Models.PmfMesh mesh) =>
        $"PMF v{mesh.Version}，{mesh.Vertices.Count:N0} 顶点，{mesh.DeclaredTriangleCount:N0} 三角面";
}
