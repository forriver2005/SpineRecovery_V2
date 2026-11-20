using System.Collections.Generic;
using UnityEngine;

/// <summary>Colors separated lumbar and thorax meshes by rotation from the captured standing pose.</summary>
[DefaultExecutionOrder(2000)]
public sealed class SpineRotationColorVisualizer : MonoBehaviour
{
    [Header("Rotation color scale")]
    [Min(0.1f)] public float fullScaleDegrees = 12.0f;
    [Range(0.1f, 2.0f)] public float responseExponent = 0.65f;
    [Range(1.0f, 120.0f)] public float colorUpdateRateHz = 30.0f;
    public Color middleColor = new Color(1.0f, 0.72f, 0.08f, 1.0f);
    public Color maximumColor = new Color(1.0f, 0.04f, 0.02f, 1.0f);

    [Header("Diagnostics")]
    [SerializeField] private int trackedPartCount;
    [SerializeField] private float maximumCurrentRotation;
    [SerializeField] private string status = "Waiting for separated torso meshes";

    private readonly List<SkinnedMeshLoosePartSeparator.TorsoRotationTarget> targets =
        new List<SkinnedMeshLoosePartSeparator.TorsoRotationTarget>();
    private readonly List<TrackedPart> trackedParts = new List<TrackedPart>();
    private SkinnedMeshLoosePartSeparator separator;
    private LumbarSeamAligner seamAligner;
    private MaterialPropertyBlock propertyBlock;
    private float nextColorUpdateTime;

    public int TrackedPartCount { get { return trackedPartCount; } }
    public float MaximumCurrentRotation { get { return maximumCurrentRotation; } }
    public string Status { get { return status; } }

    private void Start()
    {
        CaptureStandingPose();
    }

    private void Update()
    {
        if (trackedParts.Count == 0)
        {
            CaptureStandingPose();
            if (trackedParts.Count == 0)
            {
                return;
            }
        }
        if (Time.unscaledTime < nextColorUpdateTime)
        {
            return;
        }
        nextColorUpdateTime = Time.unscaledTime + 1.0f / Mathf.Max(1.0f, colorUpdateRateHz);

        maximumCurrentRotation = 0.0f;
        float scale = Mathf.Max(0.1f, fullScaleDegrees);
        for (int index = 0; index < trackedParts.Count; index++)
        {
            TrackedPart part = trackedParts[index];
            if (part.Renderer == null)
            {
                continue;
            }

            Quaternion currentWorldRotation;
            if (!TryCalculateChildWorldRotation(part, out currentWorldRotation))
            {
                continue;
            }
            float angle = Quaternion.Angle(part.InitialWorldRotation, currentWorldRotation);
            maximumCurrentRotation = Mathf.Max(maximumCurrentRotation, angle);
            float normalized = Mathf.Pow(Mathf.Clamp01(angle / scale), responseExponent);
            Color color = EvaluateColor(part.NeutralColor, normalized);
            ApplyColor(part, color);
        }
    }

    private void OnDisable()
    {
        ClearColors();
    }

    private void OnDestroy()
    {
        ClearColors();
    }

    private void OnValidate()
    {
        fullScaleDegrees = Mathf.Max(0.1f, fullScaleDegrees);
        colorUpdateRateHz = Mathf.Max(1.0f, colorUpdateRateHz);
    }

    [ContextMenu("Capture Current Standing Pose")]
    public void CaptureStandingPose()
    {
        ClearColors();
        trackedParts.Clear();
        separator = separator != null ? separator : GetComponent<SkinnedMeshLoosePartSeparator>();
        seamAligner = seamAligner != null ? seamAligner : GetComponent<LumbarSeamAligner>();
        if (separator == null)
        {
            status = "Missing SkinnedMeshLoosePartSeparator on skel_female.";
            trackedPartCount = 0;
            return;
        }

        separator.GetTorsoRotationTargets(targets);
        for (int index = 0; index < targets.Count; index++)
        {
            SkinnedMeshLoosePartSeparator.TorsoRotationTarget target = targets[index];
            if (target.Renderer == null)
            {
                continue;
            }

            Renderer visibleRenderer = target.Renderer;
            int firstMaterialIndex = -1;
            int materialCount = 0;
            bool usesPostSkinCorrection = false;
            if (seamAligner != null)
            {
                usesPostSkinCorrection = seamAligner.TryGetVisiblePostSkinTarget(
                    target.Renderer,
                    out visibleRenderer,
                    out firstMaterialIndex,
                    out materialCount);
            }

            Mesh mesh = target.Renderer.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] boneWeights = mesh.boneWeights;
            TrackedPart part = new TrackedPart(
                visibleRenderer,
                firstMaterialIndex,
                materialCount,
                target.Renderer.bones,
                mesh.bindposes,
                usesPostSkinCorrection ? seamAligner : null,
                new VertexSample(vertices[target.SampleVertexA], boneWeights[target.SampleVertexA]),
                new VertexSample(vertices[target.SampleVertexB], boneWeights[target.SampleVertexB]),
                new VertexSample(vertices[target.SampleVertexC], boneWeights[target.SampleVertexC]),
                GetMaterialColor(visibleRenderer, firstMaterialIndex));
            Quaternion initialWorldRotation;
            if (!TryCalculateChildWorldRotation(part, out initialWorldRotation))
            {
                Debug.LogWarning(
                    "[Spine rotation] Could not build a geometric frame for " +
                    target.Region + " part " + target.PartNumber.ToString("000") + ".",
                    this);
                continue;
            }
            part.InitialWorldRotation = initialWorldRotation;
            trackedParts.Add(part);
            Debug.Log(
                "[Spine rotation baseline] " + target.Region + " part " +
                target.PartNumber.ToString("000") + " geometric frame quaternion " +
                FormatQuaternion(initialWorldRotation) + ", sample vertices (" +
                target.SampleVertexA + ", " + target.SampleVertexB + ", " +
                target.SampleVertexC + ")",
                this);
        }

        trackedPartCount = trackedParts.Count;
        if (trackedPartCount > 0)
        {
            status = "Captured standing-pose rotations for " + trackedPartCount +
                " separated lumbar/thorax meshes.";
            Debug.Log("[Spine rotation] " + status, this);
        }
        else
        {
            status = "No separated lumbar/thorax meshes are available yet.";
        }
    }

    private Color EvaluateColor(Color neutral, float normalized)
    {
        return normalized < 0.5f
            ? Color.Lerp(neutral, middleColor, normalized * 2.0f)
            : Color.Lerp(middleColor, maximumColor, (normalized - 0.5f) * 2.0f);
    }

    private void ApplyColor(TrackedPart part, Color color)
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
        if (part.FirstMaterialIndex >= 0 && part.MaterialCount > 0)
        {
            for (int index = 0; index < part.MaterialCount; index++)
            {
                int materialIndex = part.FirstMaterialIndex + index;
                part.Renderer.GetPropertyBlock(propertyBlock, materialIndex);
                SetBlockColor(propertyBlock, color);
                part.Renderer.SetPropertyBlock(propertyBlock, materialIndex);
                propertyBlock.Clear();
            }
            return;
        }
        part.Renderer.GetPropertyBlock(propertyBlock);
        SetBlockColor(propertyBlock, color);
        part.Renderer.SetPropertyBlock(propertyBlock);
        propertyBlock.Clear();
    }

    private static void SetBlockColor(MaterialPropertyBlock block, Color color)
    {
        block.SetColor("_Color", color);
        block.SetColor("_BaseColor", color);
    }

    private void ClearColors()
    {
        for (int index = 0; index < trackedParts.Count; index++)
        {
            if (trackedParts[index].Renderer != null)
            {
                TrackedPart part = trackedParts[index];
                if (part.FirstMaterialIndex >= 0 && part.MaterialCount > 0)
                {
                    for (int slot = 0; slot < part.MaterialCount; slot++)
                    {
                        part.Renderer.SetPropertyBlock(null, part.FirstMaterialIndex + slot);
                    }
                }
                else
                {
                    part.Renderer.SetPropertyBlock(null);
                }
            }
        }
    }

    private static Color GetMaterialColor(Renderer targetRenderer, int materialIndex)
    {
        Material[] materials = targetRenderer.sharedMaterials;
        Material material = materials.Length == 0
            ? null
            : materials[Mathf.Clamp(materialIndex, 0, materials.Length - 1)];
        if (material == null)
        {
            return Color.white;
        }
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }
        if (material.HasProperty("_Color"))
        {
            return material.GetColor("_Color");
        }
        return Color.white;
    }

    private static string FormatQuaternion(Quaternion value)
    {
        return "(" + value.x.ToString("F5") + ", " + value.y.ToString("F5") + ", " +
            value.z.ToString("F5") + ", " + value.w.ToString("F5") + ")";
    }

    private static bool TryCalculateChildWorldRotation(
        TrackedPart part,
        out Quaternion rotation)
    {
        Vector3 a;
        Vector3 b;
        Vector3 c;
        if (!TrySkinWorldVertex(part.SampleA, part.Bones, part.BindPoses, out a)
            || !TrySkinWorldVertex(part.SampleB, part.Bones, part.BindPoses, out b)
            || !TrySkinWorldVertex(part.SampleC, part.Bones, part.BindPoses, out c))
        {
            rotation = Quaternion.identity;
            return false;
        }

        Vector3 primary = b - a;
        if (primary.sqrMagnitude < 0.0000000001f)
        {
            rotation = Quaternion.identity;
            return false;
        }
        primary.Normalize();
        Vector3 secondary = c - a;
        secondary -= primary * Vector3.Dot(secondary, primary);
        if (secondary.sqrMagnitude < 0.0000000001f)
        {
            rotation = Quaternion.identity;
            return false;
        }
        rotation = Quaternion.LookRotation(primary, secondary.normalized);
        if (part.SeamAligner != null)
        {
            rotation = part.SeamAligner.CurrentPostSkinWorldRotation * rotation;
        }
        return true;
    }

    private static bool TrySkinWorldVertex(
        VertexSample sample,
        Transform[] bones,
        Matrix4x4[] bindPoses,
        out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        float totalWeight = 0.0f;
        AddSkinnedInfluence(sample.Position, sample.Weight.boneIndex0, sample.Weight.weight0,
            bones, bindPoses, ref worldPosition, ref totalWeight);
        AddSkinnedInfluence(sample.Position, sample.Weight.boneIndex1, sample.Weight.weight1,
            bones, bindPoses, ref worldPosition, ref totalWeight);
        AddSkinnedInfluence(sample.Position, sample.Weight.boneIndex2, sample.Weight.weight2,
            bones, bindPoses, ref worldPosition, ref totalWeight);
        AddSkinnedInfluence(sample.Position, sample.Weight.boneIndex3, sample.Weight.weight3,
            bones, bindPoses, ref worldPosition, ref totalWeight);
        if (totalWeight <= 0.00001f)
        {
            return false;
        }
        worldPosition /= totalWeight;
        return true;
    }

    private static void AddSkinnedInfluence(
        Vector3 position,
        int boneIndex,
        float weight,
        Transform[] bones,
        Matrix4x4[] bindPoses,
        ref Vector3 weightedPosition,
        ref float totalWeight)
    {
        if (weight <= 0.0f || boneIndex < 0 || boneIndex >= bones.Length
            || boneIndex >= bindPoses.Length || bones[boneIndex] == null)
        {
            return;
        }
        Vector3 boneLocal = bindPoses[boneIndex].MultiplyPoint3x4(position);
        weightedPosition += bones[boneIndex].localToWorldMatrix.MultiplyPoint3x4(boneLocal) * weight;
        totalWeight += weight;
    }

    private struct TrackedPart
    {
        public readonly Renderer Renderer;
        public readonly int FirstMaterialIndex;
        public readonly int MaterialCount;
        public readonly Transform[] Bones;
        public readonly Matrix4x4[] BindPoses;
        public readonly LumbarSeamAligner SeamAligner;
        public readonly VertexSample SampleA;
        public readonly VertexSample SampleB;
        public readonly VertexSample SampleC;
        public Quaternion InitialWorldRotation;
        public readonly Color NeutralColor;

        public TrackedPart(
            Renderer renderer,
            int firstMaterialIndex,
            int materialCount,
            Transform[] bones,
            Matrix4x4[] bindPoses,
            LumbarSeamAligner seamAligner,
            VertexSample sampleA,
            VertexSample sampleB,
            VertexSample sampleC,
            Color neutralColor)
        {
            Renderer = renderer;
            FirstMaterialIndex = firstMaterialIndex;
            MaterialCount = materialCount;
            Bones = bones;
            BindPoses = bindPoses;
            SeamAligner = seamAligner;
            SampleA = sampleA;
            SampleB = sampleB;
            SampleC = sampleC;
            InitialWorldRotation = Quaternion.identity;
            NeutralColor = neutralColor;
        }
    }

    private readonly struct VertexSample
    {
        public readonly Vector3 Position;
        public readonly BoneWeight Weight;

        public VertexSample(Vector3 position, BoneWeight weight)
        {
            Position = position;
            Weight = weight;
        }
    }
}
