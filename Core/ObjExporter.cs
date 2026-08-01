using System.Globalization;
using System.Numerics;
using System.Text;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class ObjExporter
{
    public sealed record ObjPart(
        string ObjectName,
        PmfMesh Mesh,
        string? MaterialName = null,
        string? TextureFileName = null);

    public static void Export(PmfMesh mesh, string path, string objectName)
    {
        Export(new[] { new ObjPart(objectName, mesh) }, path, objectName);
    }

    public static void Export(IReadOnlyList<ObjPart> parts, string path, string modelName)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        ObjPart[] exportParts = parts.Where(part => part.Mesh.Vertices.Count > 0).ToArray();
        if (exportParts.Length == 0) throw new InvalidDataException("没有可导出的 PMF 部件。");

        string materialLibraryName = Path.GetFileNameWithoutExtension(path) + ".mtl";
        bool hasMaterials = exportParts.Any(part =>
            !string.IsNullOrWhiteSpace(part.MaterialName) || !string.IsNullOrWhiteSpace(part.TextureFileName));
        Dictionary<ObjPart, string> materialNames = hasMaterials
            ? BuildMaterialNames(exportParts)
            : new Dictionary<ObjPart, string>();

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false), 1024 * 64);
        writer.WriteLine("# Exported by Xunxian DPK Resource Viewer");
        writer.WriteLine($"# {SanitizeName(modelName)}; {exportParts.Length} parts; {exportParts.Sum(part => part.Mesh.Vertices.Count)} vertices; {exportParts.Sum(part => (long)part.Mesh.DeclaredTriangleCount)} triangles");
        if (hasMaterials) writer.WriteLine($"mtllib {materialLibraryName}");

        int vertexOffset = 0;
        int textureOffset = 0;
        int normalOffset = 0;
        HashSet<string> usedObjectNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (ObjPart part in exportParts)
        {
            PmfMesh mesh = part.Mesh;
            Vector3[] normals = CalculateNormals(mesh);
            bool hasUv = mesh.TextureCoordinates.Count == mesh.Vertices.Count;
            writer.WriteLine();
            writer.WriteLine($"o {MakeUniqueName(SanitizeName(part.ObjectName), usedObjectNames)}");
            if (materialNames.TryGetValue(part, out string? materialName)) writer.WriteLine($"usemtl {materialName}");

            foreach (Vector3 vertex in mesh.Vertices)
                writer.WriteLine(FormattableString.Invariant($"v {vertex.X:R} {vertex.Y:R} {vertex.Z:R}"));
            if (hasUv)
            {
                foreach (Vector2 uv in mesh.TextureCoordinates)
                    writer.WriteLine(FormattableString.Invariant($"vt {uv.X:R} {1f - uv.Y:R}"));
            }
            foreach (Vector3 normal in normals)
                writer.WriteLine(FormattableString.Invariant($"vn {normal.X:R} {normal.Y:R} {normal.Z:R}"));

            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                int ia = mesh.Indices[i];
                int ib = mesh.Indices[i + 1];
                int ic = mesh.Indices[i + 2];
                if (ia >= mesh.Vertices.Count || ib >= mesh.Vertices.Count || ic >= mesh.Vertices.Count) continue;

                int a = ia + 1 + vertexOffset;
                int b = ib + 1 + vertexOffset;
                int c = ic + 1 + vertexOffset;
                int na = ia + 1 + normalOffset;
                int nb = ib + 1 + normalOffset;
                int nc = ic + 1 + normalOffset;
                if (hasUv)
                {
                    int ta = ia + 1 + textureOffset;
                    int tb = ib + 1 + textureOffset;
                    int tc = ic + 1 + textureOffset;
                    writer.WriteLine($"f {a}/{ta}/{na} {b}/{tb}/{nb} {c}/{tc}/{nc}");
                }
                else
                {
                    writer.WriteLine($"f {a}//{na} {b}//{nb} {c}//{nc}");
                }
            }

            vertexOffset += mesh.Vertices.Count;
            if (hasUv) textureOffset += mesh.TextureCoordinates.Count;
            normalOffset += normals.Length;
        }

        if (hasMaterials) WriteMaterialLibrary(path, materialLibraryName, exportParts, materialNames);
    }

    private static Vector3[] CalculateNormals(PmfMesh mesh)
    {
        var normals = new Vector3[mesh.Vertices.Count];
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            int a = mesh.Indices[i];
            int b = mesh.Indices[i + 1];
            int c = mesh.Indices[i + 2];
            if (a >= mesh.Vertices.Count || b >= mesh.Vertices.Count || c >= mesh.Vertices.Count) continue;
            Vector3 normal = Vector3.Cross(mesh.Vertices[b] - mesh.Vertices[a], mesh.Vertices[c] - mesh.Vertices[a]);
            if (normal.LengthSquared() < 1e-20f) continue;
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].LengthSquared() < 1e-20f ? Vector3.UnitY : Vector3.Normalize(normals[i]);
        return normals;
    }

    private static Dictionary<ObjPart, string> BuildMaterialNames(IReadOnlyList<ObjPart> parts)
    {
        var result = new Dictionary<ObjPart, string>();
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < parts.Count; i++)
        {
            ObjPart part = parts[i];
            string sourceName = !string.IsNullOrWhiteSpace(part.MaterialName)
                ? part.MaterialName!
                : Path.GetFileNameWithoutExtension(part.ObjectName);
            string materialName = MakeUniqueName(SanitizeName($"mat_{sourceName}"), used);
            result[part] = materialName;
        }

        return result;
    }

    private static void WriteMaterialLibrary(
        string objPath,
        string materialLibraryName,
        IReadOnlyList<ObjPart> parts,
        IReadOnlyDictionary<ObjPart, string> materialNames)
    {
        string? directory = Path.GetDirectoryName(objPath);
        string mtlPath = string.IsNullOrWhiteSpace(directory)
            ? materialLibraryName
            : Path.Combine(directory, materialLibraryName);
        using var writer = new StreamWriter(mtlPath, false, new UTF8Encoding(false), 1024 * 16);
        writer.WriteLine("# Exported by Xunxian DPK Resource Viewer");
        foreach (ObjPart part in parts)
        {
            if (!materialNames.TryGetValue(part, out string? materialName)) continue;
            writer.WriteLine();
            writer.WriteLine($"newmtl {materialName}");
            writer.WriteLine("Ka 1 1 1");
            writer.WriteLine("Kd 1 1 1");
            writer.WriteLine("Ks 0 0 0");
            writer.WriteLine("illum 2");
            if (!string.IsNullOrWhiteSpace(part.TextureFileName))
                writer.WriteLine($"map_Kd {part.TextureFileName}");
        }
    }

    private static string MakeUniqueName(string name, HashSet<string> used)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "xunxian_model" : name;
        string candidate = baseName;
        int suffix = 2;
        while (!used.Add(candidate))
            candidate = $"{baseName}_{suffix++}";
        return candidate;
    }

    private static string SanitizeName(string name)
    {
        var result = new StringBuilder(name.Length);
        foreach (char value in name)
            result.Append(char.IsLetterOrDigit(value) || value is '_' or '-' ? value : '_');
        return result.Length == 0 ? "xunxian_model" : result.ToString();
    }
}
