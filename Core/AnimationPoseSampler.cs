using System.Numerics;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class AnimationPoseSampler
{
    public static Matrix4x4[] BuildSkinMatrices(
        SkeletonData skeleton,
        SkeletalAnimation animation,
        float time)
    {
        int boneCount = skeleton.Bones.Count;
        var worldMatrices = new Matrix4x4[boneCount];
        var skinMatrices = new Matrix4x4[boneCount];

        for (int index = 0; index < boneCount; index++)
        {
            SkeletonBone bone = skeleton.Bones[index];
            SampleLocalTransform(bone, animation, time, out Vector3 translation, out Quaternion rotation);
            Matrix4x4 local =
                Matrix4x4.CreateFromQuaternion(rotation) *
                Matrix4x4.CreateTranslation(translation);

            worldMatrices[index] = bone.ParentIndex >= 0 && bone.ParentIndex < index
                ? local * worldMatrices[bone.ParentIndex]
                : local;

            Matrix4x4 inverseBind =
                Matrix4x4.CreateFromQuaternion(bone.InverseBindRotation) *
                Matrix4x4.CreateTranslation(bone.InverseBindTranslation);
            skinMatrices[index] = inverseBind * worldMatrices[index];
        }

        return skinMatrices;
    }

    public static void SampleLocalTransform(
        SkeletonBone bone,
        SkeletalAnimation animation,
        float time,
        out Vector3 translation,
        out Quaternion rotation)
    {
        if (!animation.Tracks.TryGetValue(bone.Index, out AnimationTrack? track))
        {
            translation = bone.BindTranslation;
            rotation = bone.BindRotation;
            return;
        }

        translation = SampleVector(track.TranslationKeys, time, animation.SampleRate, bone.BindTranslation);
        rotation = SampleQuaternion(track.RotationKeys, time, animation.SampleRate, bone.BindRotation);
    }

    private static Vector3 SampleVector(
        IReadOnlyList<Vector3> keys,
        float time,
        int sampleRate,
        Vector3 fallback)
    {
        if (keys.Count == 0) return fallback;
        if (keys.Count == 1) return keys[0];
        float frame = Math.Clamp(time * sampleRate, 0f, keys.Count - 1);
        int first = (int)MathF.Floor(frame);
        int second = Math.Min(keys.Count - 1, first + 1);
        return Vector3.Lerp(keys[first], keys[second], frame - first);
    }

    private static Quaternion SampleQuaternion(
        IReadOnlyList<Quaternion> keys,
        float time,
        int sampleRate,
        Quaternion fallback)
    {
        if (keys.Count == 0) return fallback;
        if (keys.Count == 1) return keys[0];
        float frame = Math.Clamp(time * sampleRate, 0f, keys.Count - 1);
        int first = (int)MathF.Floor(frame);
        int second = Math.Min(keys.Count - 1, first + 1);
        return Quaternion.Normalize(Quaternion.Slerp(keys[first], keys[second], frame - first));
    }
}
