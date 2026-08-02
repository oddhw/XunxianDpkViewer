using System.Globalization;
using System.Numerics;
using System.Text;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class BvhExporter
{
    public static void Export(
        string path,
        SkeletonData skeleton,
        SkeletalAnimation animation)
    {
        if (skeleton.Bones.Count == 0)
            throw new InvalidDataException("骨骼为空，无法导出 BVH。");

        List<int>[] children = BuildChildren(skeleton);
        int[] roots = skeleton.Bones
            .Where(bone => bone.ParentIndex < 0 || bone.ParentIndex >= skeleton.Bones.Count)
            .Select(bone => bone.Index)
            .ToArray();
        if (roots.Length == 0) roots = new[] { 0 };

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("HIERARCHY");
        var channelOrder = new List<int>();
        bool syntheticRoot = roots.Length > 1;
        if (syntheticRoot)
        {
            writer.WriteLine("ROOT SceneRoot");
            writer.WriteLine("{");
            writer.WriteLine("  OFFSET 0 0 0");
            writer.WriteLine("  CHANNELS 6 Xposition Yposition Zposition Xrotation Yrotation Zrotation");
            channelOrder.Add(-1);
            foreach (int root in roots)
                WriteBoneHierarchy(writer, skeleton, children, root, 1, channelOrder);
            writer.WriteLine("}");
        }
        else
        {
            WriteBoneHierarchy(writer, skeleton, children, roots[0], 0, channelOrder, isRoot: true);
        }

        int frameCount = Math.Max(1, animation.FrameCount);
        writer.WriteLine("MOTION");
        writer.WriteLine($"Frames: {frameCount.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine($"Frame Time: {(1d / animation.SampleRate).ToString("0.########", CultureInfo.InvariantCulture)}");
        for (int frame = 0; frame < frameCount; frame++)
        {
            float time = Math.Min(animation.Duration, frame / (float)animation.SampleRate);
            var values = new List<string>(channelOrder.Count * 6);
            foreach (int boneIndex in channelOrder)
            {
                if (boneIndex < 0)
                {
                    AppendTransform(values, Vector3.Zero, Quaternion.Identity);
                    continue;
                }

                SkeletonBone bone = skeleton.Bones[boneIndex];
                AnimationPoseSampler.SampleLocalTransform(
                    bone,
                    animation,
                    time,
                    out Vector3 translation,
                    out Quaternion rotation);
                AppendTransform(values, translation - bone.BindTranslation, rotation);
            }
            writer.WriteLine(string.Join(' ', values));
        }
    }

    private static List<int>[] BuildChildren(SkeletonData skeleton)
    {
        var children = Enumerable.Range(0, skeleton.Bones.Count)
            .Select(_ => new List<int>())
            .ToArray();
        foreach (SkeletonBone bone in skeleton.Bones)
        {
            if (bone.ParentIndex >= 0 && bone.ParentIndex < children.Length && bone.ParentIndex != bone.Index)
                children[bone.ParentIndex].Add(bone.Index);
        }
        return children;
    }

    private static void WriteBoneHierarchy(
        TextWriter writer,
        SkeletonData skeleton,
        IReadOnlyList<List<int>> children,
        int boneIndex,
        int depth,
        ICollection<int> channelOrder,
        bool isRoot = false)
    {
        SkeletonBone bone = skeleton.Bones[boneIndex];
        string indent = new(' ', depth * 2);
        writer.WriteLine($"{indent}{(isRoot ? "ROOT" : "JOINT")} {SanitizeBoneName(bone.Name, bone.Index)}");
        writer.WriteLine($"{indent}{{");
        writer.WriteLine(
            $"{indent}  OFFSET {Format(bone.BindTranslation.X)} {Format(bone.BindTranslation.Y)} {Format(bone.BindTranslation.Z)}");
        writer.WriteLine(
            $"{indent}  CHANNELS 6 Xposition Yposition Zposition Xrotation Yrotation Zrotation");
        channelOrder.Add(boneIndex);

        if (children[boneIndex].Count == 0)
        {
            writer.WriteLine($"{indent}  End Site");
            writer.WriteLine($"{indent}  {{");
            writer.WriteLine($"{indent}    OFFSET 0 0 0");
            writer.WriteLine($"{indent}  }}");
        }
        else
        {
            foreach (int child in children[boneIndex])
                WriteBoneHierarchy(writer, skeleton, children, child, depth + 1, channelOrder);
        }

        writer.WriteLine($"{indent}}}");
    }

    private static void AppendTransform(ICollection<string> values, Vector3 translation, Quaternion rotation)
    {
        Vector3 degrees = QuaternionToEulerDegrees(rotation);
        values.Add(Format(translation.X));
        values.Add(Format(translation.Y));
        values.Add(Format(translation.Z));
        values.Add(Format(degrees.X));
        values.Add(Format(degrees.Y));
        values.Add(Format(degrees.Z));
    }

    private static Vector3 QuaternionToEulerDegrees(Quaternion value)
    {
        Quaternion q = value.LengthSquared() > 0.000001f
            ? Quaternion.Normalize(value)
            : Quaternion.Identity;

        float sinXCosY = 2f * (q.W * q.X + q.Y * q.Z);
        float cosXCosY = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float x = MathF.Atan2(sinXCosY, cosXCosY);

        float sinY = 2f * (q.W * q.Y - q.Z * q.X);
        float y = MathF.Abs(sinY) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinY)
            : MathF.Asin(sinY);

        float sinZCosY = 2f * (q.W * q.Z + q.X * q.Y);
        float cosZCosY = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float z = MathF.Atan2(sinZCosY, cosZCosY);
        const float radiansToDegrees = 180f / MathF.PI;
        return new Vector3(x, y, z) * radiansToDegrees;
    }

    private static string SanitizeBoneName(string name, int index)
    {
        string cleaned = new(name
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? $"Bone_{index}" : $"{cleaned}_{index}";
    }

    private static string Format(float value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
