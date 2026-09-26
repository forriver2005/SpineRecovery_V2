using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Rotates the complete thorax group about a fixed upper landmark to continue the lumbar chain.</summary>
[DefaultExecutionOrder(32000)]
public sealed class LumbarSeamAligner : MonoBehaviour
{
    [Header("Targets (auto-detected when empty)")]
    public Transform lowerLumbar;
    public Transform upperThorax;
    public Transform head;

    [Header("Chain landmarks")]
    public string lumbarFirstPartName = "Lumbar Loose Part 001";
    public string lumbarSecondPartName = "Lumbar Loose Part 002";
    public string thoraxMovingPartName = "Thorax Loose Part 012";
    public string thoraxTopPartName = "Thorax Loose Part 001";
    public string headLowestPartName = "Head Loose Part 009";
    public string headSecondLowestPartName = "Head Loose Part 008";

    [Header("Automatic alignment")]
    public bool autoAlignOnPlay = true;
    public bool maintainAlignment = true;
    [Range(0.0f, 90.0f)] public float maximumRotationDegrees = 25.0f;
    [Range(1.0f, 120.0f)] public float alignmentSolveRateHz = 30.0f;

    [Header("Computed thorax-only transform")]
    [SerializeField] private Vector3 upperLocalRotationOffset;
    [SerializeField] private Vector3 upperRotationPivotLocal;
    [SerializeField] private Vector3 upperLocalTranslationOffset;

    [Header("Diagnostics")]
    [SerializeField] private bool hasAlignmentSolution;
    [SerializeField] private float chainErrorBefore;
    [SerializeField] private float chainErrorAfter;
    [SerializeField] private float upperChainErrorBefore;
    [SerializeField] private float upperChainErrorAfter;
    [SerializeField] private float fixedPivotDrift;
    [SerializeField] private float appliedRotationDegrees;
    [SerializeField] private float appliedTranslationDistance;
    [SerializeField] private string selectedContinuation;
    [SerializeField] private string status = "Not aligned";

    private PostSkinGroup upperGroup;
    private readonly Dictionary<SkinnedMeshRenderer, CentroidSampler> centroidSamplers
        = new Dictionary<SkinnedMeshRenderer, CentroidSampler>();
    private DualEndSolution cachedLiveSolution;
    private bool hasCachedLiveSolution;
    private float nextAlignmentSolveTime;
    private Quaternion currentPostSkinWorldRotation = Quaternion.identity;

    public bool HasAlignmentSolution { get { return hasAlignmentSolution; } }
    public float ChainErrorBefore { get { return chainErrorBefore; } }
    public float ChainErrorAfter { get { return chainErrorAfter; } }
    public float FixedPivotDrift { get { return fixedPivotDrift; } }
    public string Status { get { return status; } }
    public Quaternion CurrentPostSkinWorldRotation { get { return currentPostSkinWorldRotation; } }

    public bool TryGetVisiblePostSkinTarget(
        SkinnedMeshRenderer sourceRenderer,
        out Renderer outputRenderer,
        out int firstMaterialIndex,
        out int materialCount)
    {
        if (sourceRenderer != null && upperGroup != null)
        {
            for (int index = 0; index < upperGroup.PartMappings.Length; index++)
            {
                PostSkinPartMapping mapping = upperGroup.PartMappings[index];
                if (mapping.Source == sourceRenderer && upperGroup.OutputRenderer != null)
                {
                    outputRenderer = upperGroup.OutputRenderer;
                    firstMaterialIndex = mapping.FirstMaterialIndex;
                    materialCount = mapping.MaterialCount;
                    return true;
                }
            }
        }
        outputRenderer = sourceRenderer;
        firstMaterialIndex = -1;
        materialCount = 0;
        return false;
    }

    private void Awake()
    {
        ResolveTargets();
    }

    private void Start()
    {
        if (autoAlignOnPlay && !hasAlignmentSolution)
        {
            AlignNow();
        }
        else if (hasAlignmentSolution)
        {
            ApplyAlignment();
        }
    }

    private void LateUpdate()
    {
        if (hasAlignmentSolution && maintainAlignment)
        {
            UpdatePostSkinSurfaces();
        }
    }

    private void OnDisable()
    {
        ReleasePostSkinSurfaces();
    }

    private void OnDestroy()
    {
        ReleasePostSkinSurfaces();
    }

    [ContextMenu("Align Thorax From Lumbar Chain")]
    public void AlignNow()
    {
        ReleasePostSkinSurfaces();
        if (!ResolveTargets())
        {
            return;
        }

        SkinnedMeshLoosePartSeparator separator = GetComponent<SkinnedMeshLoosePartSeparator>();
        if (separator != null && !separator.HasGeneratedParts)
        {
            separator.SeparateLooseParts();
            return;
        }

        DualEndSolution solution;
        string failure;
        if (!TryCalculateDualEndSolution(out solution, out failure))
        {
            hasAlignmentSolution = false;
            status = failure;
            Debug.LogWarning("[Lumbar chain align] " + status, this);
            return;
        }

        Transform upperSpace = solution.ReferenceRenderer.transform;
        Quaternion localRotation = Quaternion.Inverse(upperSpace.rotation)
            * solution.Rotation * upperSpace.rotation;
        upperLocalRotationOffset = ToSignedEuler(localRotation);
        upperRotationPivotLocal = upperSpace.InverseTransformPoint(solution.Pivot);
        upperLocalTranslationOffset = upperSpace.InverseTransformVector(solution.Translation);
        chainErrorBefore = solution.LowerErrorBefore;
        chainErrorAfter = solution.LowerErrorAfter;
        upperChainErrorBefore = solution.UpperErrorBefore;
        upperChainErrorAfter = solution.UpperErrorAfter;
        appliedRotationDegrees = solution.AppliedDegrees;
        appliedTranslationDistance = solution.Translation.magnitude;
        selectedContinuation = solution.Continuation;
        fixedPivotDrift = 0.0f;
        cachedLiveSolution = solution;
        hasCachedLiveSolution = true;
        nextAlignmentSolveTime = Time.unscaledTime + GetSolveInterval();
        hasAlignmentSolution = true;

        ApplyAlignment();

        status = "Dual-end post-skinned Biological_thorax.001 using " + selectedContinuation
            + "; rotation " + appliedRotationDegrees.ToString("0.00") + " deg, translation "
            + appliedTranslationDistance.ToString("0.0000") + " m; lower error "
            + chainErrorBefore.ToString("0.0000") + " -> "
            + chainErrorAfter.ToString("0.0000") + " m; upper error "
            + upperChainErrorBefore.ToString("0.0000") + " -> "
            + upperChainErrorAfter.ToString("0.0000") + " m.";
        Debug.Log("[Lumbar chain align] " + status, this);
    }

    [ContextMenu("Apply Stored Thorax Rotation")]
    public void ApplyAlignment()
    {
        if (!ResolveTargets() || !EnsureUpperPostSkinSurfaces())
        {
            return;
        }
        UpdatePostSkinSurfaces();
    }

    public void RebuildForCurrentRenderers()
    {
        ReleasePostSkinSurfaces();
        if ((Application.isPlaying && autoAlignOnPlay) || hasAlignmentSolution)
        {
            AlignNow();
        }
    }

    [ContextMenu("Clear Thorax Chain Alignment")]
    public void ClearAlignment()
    {
        ReleasePostSkinSurfaces();
        upperLocalRotationOffset = Vector3.zero;
        upperRotationPivotLocal = Vector3.zero;
        upperLocalTranslationOffset = Vector3.zero;
        hasAlignmentSolution = false;
        chainErrorBefore = 0.0f;
        chainErrorAfter = 0.0f;
        upperChainErrorBefore = 0.0f;
        upperChainErrorAfter = 0.0f;
        fixedPivotDrift = 0.0f;
        appliedRotationDegrees = 0.0f;
        appliedTranslationDistance = 0.0f;
        selectedContinuation = string.Empty;
        hasCachedLiveSolution = false;
        status = "Alignment cleared";
    }

    public bool ResolveTargets()
    {
        if (lowerLumbar == null)
        {
            lowerLumbar = FindDescendant(transform, "Biological_lumbar_body");
        }
        if (upperThorax == null)
        {
            upperThorax = FindDescendant(transform, "Biological_thorax.001");
            SkinnedMeshLoosePartSeparator separator = GetComponent<SkinnedMeshLoosePartSeparator>();
            if (upperThorax == null && separator != null && separator.useHeightBandFallback)
            {
                upperThorax = FindDescendant(transform, "Biological_thorax");
            }
        }
        if (head == null)
        {
            head = FindDescendant(transform, "Biological_head");
        }
        if (lowerLumbar != null && upperThorax != null && head != null)
        {
            return true;
        }
        status = "Missing Biological_lumbar_body, Biological_thorax.001, or Biological_head below skel_female.";
        return false;
    }

    private bool TryCalculateDualEndSolution(
        out DualEndSolution solution,
        out string failure)
    {
        SkinnedMeshRenderer lumbarFirst = FindPartRenderer(lowerLumbar, lumbarFirstPartName);
        SkinnedMeshRenderer lumbarSecond = FindPartRenderer(lowerLumbar, lumbarSecondPartName);
        SkinnedMeshRenderer thoraxBottom = FindPartRenderer(upperThorax, thoraxMovingPartName);
        SkinnedMeshRenderer thoraxTop = FindPartRenderer(upperThorax, thoraxTopPartName);
        SkinnedMeshRenderer headLowest = FindPartRenderer(head, headLowestPartName);
        SkinnedMeshRenderer headSecondLowest = FindPartRenderer(head, headSecondLowestPartName);
        if (lumbarFirst == null || lumbarSecond == null || thoraxBottom == null
            || thoraxTop == null || headLowest == null || headSecondLowest == null)
        {
            solution = default(DualEndSolution);
            failure = "Missing one or more dual-end landmarks: " + lumbarFirstPartName + ", "
                + lumbarSecondPartName + ", " + thoraxMovingPartName + ", "
                + thoraxTopPartName + ", " + headLowestPartName + ", "
                + headSecondLowestPartName + ".";
            return false;
        }

        Vector3 lumbarFirstCenter;
        Vector3 lumbarSecondCenter;
        Vector3 thoraxBottomCenter;
        Vector3 thoraxTopCenter;
        Vector3 headLowestCenter;
        Vector3 headSecondLowestCenter;
        if (!TryGetSurfaceCentroid(lumbarFirst, out lumbarFirstCenter)
            || !TryGetSurfaceCentroid(lumbarSecond, out lumbarSecondCenter)
            || !TryGetSurfaceCentroid(thoraxBottom, out thoraxBottomCenter)
            || !TryGetSurfaceCentroid(thoraxTop, out thoraxTopCenter)
            || !TryGetSurfaceCentroid(headLowest, out headLowestCenter)
            || !TryGetSurfaceCentroid(headSecondLowest, out headSecondLowestCenter))
        {
            solution = default(DualEndSolution);
            failure = "Could not calculate one or more dual-end area-weighted centroids.";
            return false;
        }

        Vector3 lumbarStep = lumbarSecondCenter - lumbarFirstCenter;
        Vector3 firstToSecondTarget = lumbarSecondCenter + lumbarStep;
        Vector3 secondToFirstTarget = lumbarFirstCenter - lumbarStep;
        Vector3 upperTarget = 2.0f * headLowestCenter - headSecondLowestCenter;
        DualEndSolution firstToSecond;
        DualEndSolution secondToFirst;
        bool hasFirstToSecond = TrySolveDualEndRigidTransform(
            thoraxBottom,
            thoraxBottomCenter,
            thoraxTopCenter,
            firstToSecondTarget,
            upperTarget,
            maximumRotationDegrees,
            "L001 -> L002 -> T012",
            out firstToSecond);
        bool hasSecondToFirst = TrySolveDualEndRigidTransform(
            thoraxBottom,
            thoraxBottomCenter,
            thoraxTopCenter,
            secondToFirstTarget,
            upperTarget,
            maximumRotationDegrees,
            "L002 -> L001 -> T012",
            out secondToFirst);
        if (!hasFirstToSecond && !hasSecondToFirst)
        {
            solution = default(DualEndSolution);
            failure = "Degenerate dual-end landmark geometry.";
            return false;
        }

        solution = hasFirstToSecond
            && (!hasSecondToFirst || firstToSecond.Score <= secondToFirst.Score)
                ? firstToSecond
                : secondToFirst;
        failure = string.Empty;
        return true;
    }

    private static bool TrySolveDualEndRigidTransform(
        SkinnedMeshRenderer referenceRenderer,
        Vector3 currentLower,
        Vector3 currentUpper,
        Vector3 targetLower,
        Vector3 targetUpper,
        float maximumDegrees,
        string continuation,
        out DualEndSolution solution)
    {
        Vector3 currentAxis = currentUpper - currentLower;
        Vector3 targetAxis = targetUpper - targetLower;
        if (currentAxis.sqrMagnitude < 0.00000001f || targetAxis.sqrMagnitude < 0.00000001f)
        {
            solution = default(DualEndSolution);
            return false;
        }

        Quaternion requested = Quaternion.FromToRotation(currentAxis, targetAxis);
        float requestedDegrees = Quaternion.Angle(Quaternion.identity, requested);
        float appliedDegrees = Mathf.Min(requestedDegrees, maximumDegrees);
        Quaternion rotation = requestedDegrees > 0.0001f
            ? Quaternion.Slerp(Quaternion.identity, requested, appliedDegrees / requestedDegrees)
            : Quaternion.identity;
        Vector3 currentMidpoint = (currentLower + currentUpper) * 0.5f;
        Vector3 targetMidpoint = (targetLower + targetUpper) * 0.5f;
        Vector3 translation = targetMidpoint - currentMidpoint;
        Vector3 transformedLower = currentMidpoint
            + rotation * (currentLower - currentMidpoint) + translation;
        Vector3 transformedUpper = currentMidpoint
            + rotation * (currentUpper - currentMidpoint) + translation;
        float lowerAfter = Vector3.Distance(transformedLower, targetLower);
        float upperAfter = Vector3.Distance(transformedUpper, targetUpper);
        solution = new DualEndSolution(
            referenceRenderer,
            currentMidpoint,
            rotation,
            translation,
            appliedDegrees,
            Vector3.Distance(currentLower, targetLower),
            lowerAfter,
            Vector3.Distance(currentUpper, targetUpper),
            upperAfter,
            lowerAfter * lowerAfter + upperAfter * upperAfter,
            continuation);
        return true;
    }

    private bool EnsureUpperPostSkinSurfaces()
    {
        if (upperGroup != null)
        {
            return true;
        }
        SkinnedMeshRenderer source = upperThorax.GetComponent<SkinnedMeshRenderer>();
        SkinnedMeshRenderer[] loosePartRenderers = GetVisibleRenderers(upperThorax);
        upperGroup = CreatePostSkinGroup(source, loosePartRenderers);
        if (upperGroup == null)
        {
            status = "Could not create post-skin thorax surfaces.";
            return false;
        }
        return true;
    }

    private void UpdatePostSkinSurfaces()
    {
        if (!EnsureUpperPostSkinSurfaces())
        {
            return;
        }
        if (maintainAlignment
            && (!hasCachedLiveSolution || Time.unscaledTime >= nextAlignmentSolveTime))
        {
            DualEndSolution liveSolution;
            string ignoredFailure;
            if (TryCalculateDualEndSolution(out liveSolution, out ignoredFailure))
            {
                cachedLiveSolution = liveSolution;
                hasCachedLiveSolution = true;
                chainErrorBefore = liveSolution.LowerErrorBefore;
                chainErrorAfter = liveSolution.LowerErrorAfter;
                upperChainErrorBefore = liveSolution.UpperErrorBefore;
                upperChainErrorAfter = liveSolution.UpperErrorAfter;
            }
            nextAlignmentSolveTime = Time.unscaledTime + GetSolveInterval();
        }
        if (maintainAlignment && hasCachedLiveSolution)
        {
            currentPostSkinWorldRotation = cachedLiveSolution.Rotation;
            ApplyPostSkinGroup(
                upperGroup,
                cachedLiveSolution.Pivot,
                cachedLiveSolution.Rotation,
                cachedLiveSolution.Translation);
            return;
        }
        UpdateStoredPostSkinGroup(upperGroup);
    }

    private float GetSolveInterval()
    {
        return 1.0f / Mathf.Max(1.0f, alignmentSolveRateHz);
    }

    private void UpdateStoredPostSkinGroup(PostSkinGroup group)
    {
        Transform space = group.Surfaces[0].Source.transform;
        Quaternion spaceRotation = space.rotation;
        Quaternion worldRotation = spaceRotation * Quaternion.Euler(upperLocalRotationOffset)
            * Quaternion.Inverse(spaceRotation);
        currentPostSkinWorldRotation = worldRotation;
        Vector3 worldPivot = space.TransformPoint(upperRotationPivotLocal);
        Vector3 worldTranslation = space.TransformVector(upperLocalTranslationOffset);
        ApplyPostSkinGroup(group, worldPivot, worldRotation, worldTranslation);
    }

    private static void ApplyPostSkinGroup(
        PostSkinGroup group,
        Vector3 worldPivot,
        Quaternion worldRotation,
        Vector3 worldTranslation)
    {
        for (int index = 0; index < group.Surfaces.Length; index++)
        {
            PostSkinSurface surface = group.Surfaces[index];
            if (surface.Source == null || surface.OutputTransform == null || surface.Mesh == null)
            {
                continue;
            }
            BakeAndTransformSurface(surface, worldPivot, worldRotation, worldTranslation);
        }
    }

    private static PostSkinGroup CreatePostSkinGroup(
        SkinnedMeshRenderer source,
        SkinnedMeshRenderer[] loosePartRenderers)
    {
        if (source == null || source.sharedMesh == null)
        {
            return null;
        }

        List<RendererVisibilityState> visibilityStates = new List<RendererVisibilityState>
        {
            new RendererVisibilityState(source, source.enabled)
        };
        List<SkinnedMeshRenderer> surfaceSources = new List<SkinnedMeshRenderer>();
        for (int index = 0; index < loosePartRenderers.Length; index++)
        {
            SkinnedMeshRenderer renderer = loosePartRenderers[index];
            if (renderer == null || renderer == source)
            {
                continue;
            }
            visibilityStates.Add(new RendererVisibilityState(renderer, renderer.enabled));
            surfaceSources.Add(renderer);
            renderer.enabled = false;
        }
        source.enabled = false;

        if (surfaceSources.Count == 0)
        {
            surfaceSources.Add(source);
        }

        GameObject outputObject = new GameObject(source.gameObject.name + " Post-Skin Alignment");
        outputObject.hideFlags = HideFlags.DontSave;
        outputObject.layer = source.gameObject.layer;
        Transform outputTransform = outputObject.transform;
        outputTransform.SetParent(source.transform, false);
        outputTransform.localPosition = Vector3.zero;
        outputTransform.localRotation = Quaternion.identity;
        outputTransform.localScale = Vector3.one;

        Mesh outputMesh = Instantiate(source.sharedMesh);
        outputMesh.name = source.sharedMesh.name + " Child Submeshes Post-Skin Alignment";
        outputMesh.hideFlags = HideFlags.DontSave;
        outputMesh.MarkDynamic();
        int totalMaterialCount = 0;
        for (int index = 0; index < surfaceSources.Count; index++)
        {
            totalMaterialCount += surfaceSources[index].sharedMesh.subMeshCount;
        }
        outputMesh.subMeshCount = totalMaterialCount;

        Material[] outputMaterials = new Material[totalMaterialCount];
        PostSkinPartMapping[] mappings = new PostSkinPartMapping[surfaceSources.Count];
        int materialIndex = 0;
        for (int partIndex = 0; partIndex < surfaceSources.Count; partIndex++)
        {
            SkinnedMeshRenderer part = surfaceSources[partIndex];
            int firstMaterialIndex = materialIndex;
            Material[] partMaterials = part.sharedMaterials;
            for (int subMeshIndex = 0; subMeshIndex < part.sharedMesh.subMeshCount; subMeshIndex++)
            {
                outputMesh.SetIndices(
                    part.sharedMesh.GetIndices(subMeshIndex),
                    part.sharedMesh.GetTopology(subMeshIndex),
                    materialIndex,
                    false);
                outputMaterials[materialIndex] = partMaterials.Length == 0
                    ? null
                    : partMaterials[Mathf.Min(subMeshIndex, partMaterials.Length - 1)];
                materialIndex++;
            }
            mappings[partIndex] = new PostSkinPartMapping(
                part,
                firstMaterialIndex,
                materialIndex - firstMaterialIndex);
        }
        outputMesh.bounds = source.sharedMesh.bounds;

        outputObject.AddComponent<MeshFilter>().sharedMesh = outputMesh;
        MeshRenderer output = outputObject.AddComponent<MeshRenderer>();
        CopyRendererSettings(source, output);
        output.sharedMaterials = outputMaterials;
        Mesh bakedMesh = new Mesh
        {
            name = source.sharedMesh.name + " Shared Post-Skin Bake",
            hideFlags = HideFlags.DontSave
        };
        bakedMesh.MarkDynamic();
        PostSkinSurface surface = new PostSkinSurface(
            source,
            outputTransform,
            outputMesh,
            bakedMesh);
        return new PostSkinGroup(
            new[] { surface },
            visibilityStates.ToArray(),
            output,
            mappings);
    }

    private void ReleasePostSkinSurfaces()
    {
        ReleasePostSkinGroup(ref upperGroup);
        ClearCentroidSamplers();
        hasCachedLiveSolution = false;
        currentPostSkinWorldRotation = Quaternion.identity;
    }

    private static void ReleasePostSkinGroup(ref PostSkinGroup group)
    {
        if (group == null)
        {
            return;
        }
        for (int index = 0; index < group.VisibilityStates.Length; index++)
        {
            RendererVisibilityState state = group.VisibilityStates[index];
            if (state.Renderer != null)
            {
                state.Renderer.enabled = state.WasEnabled;
            }
        }
        for (int index = 0; index < group.Surfaces.Length; index++)
        {
            PostSkinSurface surface = group.Surfaces[index];
            if (surface.OutputTransform != null)
            {
                DestroyGeneratedObject(surface.OutputTransform.gameObject);
            }
            if (surface.Mesh != null)
            {
                DestroyGeneratedObject(surface.Mesh);
            }
            if (surface.BakedMesh != null)
            {
                DestroyGeneratedObject(surface.BakedMesh);
            }
        }
        group = null;
    }

    private void ClearCentroidSamplers()
    {
        foreach (KeyValuePair<SkinnedMeshRenderer, CentroidSampler> pair in centroidSamplers)
        {
            if (pair.Value != null && pair.Value.BakedMesh != null)
            {
                DestroyGeneratedObject(pair.Value.BakedMesh);
            }
        }
        centroidSamplers.Clear();
    }

    private static void BakeAndTransformSurface(
        PostSkinSurface surface,
        Vector3 worldPivot,
        Quaternion worldRotation,
        Vector3 worldTranslation)
    {
        surface.Source.BakeMesh(surface.BakedMesh, false);
        surface.BakedMesh.GetVertices(surface.Vertices);
        for (int index = 0; index < surface.Vertices.Count; index++)
        {
            Vector3 world = surface.Source.transform.TransformPoint(surface.Vertices[index]);
            world = worldPivot + worldRotation * (world - worldPivot) + worldTranslation;
            surface.Vertices[index] = surface.OutputTransform.InverseTransformPoint(world);
        }
        surface.Mesh.SetVertices(surface.Vertices);

        surface.BakedMesh.GetNormals(surface.Normals);
        for (int index = 0; index < surface.Normals.Count; index++)
        {
            Vector3 world = surface.Source.transform.TransformDirection(surface.Normals[index]);
            surface.Normals[index] = surface.OutputTransform
                .InverseTransformDirection(worldRotation * world).normalized;
        }
        surface.Mesh.SetNormals(surface.Normals);

        surface.BakedMesh.GetTangents(surface.Tangents);
        for (int index = 0; index < surface.Tangents.Count; index++)
        {
            Vector4 sourceTangent = surface.Tangents[index];
            Vector3 tangent = new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z);
            Vector3 world = surface.Source.transform.TransformDirection(tangent);
            Vector3 local = surface.OutputTransform.InverseTransformDirection(worldRotation * world).normalized;
            surface.Tangents[index] = new Vector4(local.x, local.y, local.z, sourceTangent.w);
        }
        surface.Mesh.SetTangents(surface.Tangents);
        surface.Mesh.RecalculateBounds();
    }

    private static void CopyRendererSettings(Renderer source, MeshRenderer destination)
    {
        destination.sharedMaterials = source.sharedMaterials;
        destination.shadowCastingMode = source.shadowCastingMode;
        destination.receiveShadows = source.receiveShadows;
        destination.lightProbeUsage = source.lightProbeUsage;
        destination.reflectionProbeUsage = source.reflectionProbeUsage;
        destination.probeAnchor = source.probeAnchor;
        destination.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        destination.renderingLayerMask = source.renderingLayerMask;
    }

    private bool TryGetSurfaceCentroid(
        SkinnedMeshRenderer renderer,
        out Vector3 centroid)
    {
        Mesh sourceMesh = renderer.sharedMesh;
        if (sourceMesh == null || !sourceMesh.isReadable)
        {
            centroid = Vector3.zero;
            return false;
        }

        CentroidSampler sampler;
        if (!centroidSamplers.TryGetValue(renderer, out sampler)
            || sampler == null
            || sampler.SourceMesh != sourceMesh)
        {
            if (sampler != null && sampler.BakedMesh != null)
            {
                DestroyGeneratedObject(sampler.BakedMesh);
            }
            sampler = new CentroidSampler(sourceMesh);
            centroidSamplers[renderer] = sampler;
        }

        renderer.BakeMesh(sampler.BakedMesh, false);
        sampler.BakedMesh.GetVertices(sampler.Vertices);
        double totalArea = 0.0;
        Vector3 weightedCenter = Vector3.zero;
        for (int subMeshIndex = 0; subMeshIndex < sampler.TriangleIndices.Length; subMeshIndex++)
        {
            int[] indices = sampler.TriangleIndices[subMeshIndex];
            if (indices == null)
            {
                continue;
            }
            for (int index = 0; index + 2 < indices.Length; index += 3)
            {
                Vector3 a = renderer.transform.TransformPoint(sampler.Vertices[indices[index]]);
                Vector3 b = renderer.transform.TransformPoint(sampler.Vertices[indices[index + 1]]);
                Vector3 c = renderer.transform.TransformPoint(sampler.Vertices[indices[index + 2]]);
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area <= 0.0000000001f)
                {
                    continue;
                }
                weightedCenter += ((a + b + c) / 3.0f) * area;
                totalArea += area;
            }
        }

        if (totalArea <= 0.0000000001)
        {
            centroid = Vector3.zero;
            return false;
        }
        centroid = weightedCenter / (float)totalArea;
        return true;
    }

    private static SkinnedMeshRenderer FindPartRenderer(Transform root, string partName)
    {
        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        SkinnedMeshRenderer fallback = null;
        for (int index = 0; index < descendants.Length; index++)
        {
            Transform candidate = descendants[index];
            if (!string.Equals(candidate.name, partName, StringComparison.Ordinal))
            {
                continue;
            }
            SkinnedMeshRenderer renderer = candidate.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null)
            {
                continue;
            }
            if (candidate.gameObject.activeInHierarchy)
            {
                return renderer;
            }
            fallback = renderer;
        }
        return fallback;
    }

    private static SkinnedMeshRenderer[] GetVisibleRenderers(Transform target)
    {
        SkinnedMeshRenderer[] all = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        List<SkinnedMeshRenderer> result = new List<SkinnedMeshRenderer>();
        for (int index = 0; index < all.Length; index++)
        {
            if (all[index].enabled && all[index].gameObject.activeInHierarchy && all[index].sharedMesh != null)
            {
                result.Add(all[index]);
            }
        }
        return result.ToArray();
    }

    private static Transform FindDescendant(Transform root, string targetName)
    {
        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
        {
            if (string.Equals(descendants[index].name, targetName, StringComparison.Ordinal))
            {
                return descendants[index];
            }
        }
        return null;
    }

    private static Vector3 ToSignedEuler(Quaternion rotation)
    {
        Vector3 euler = rotation.eulerAngles;
        return new Vector3(ToSignedAngle(euler.x), ToSignedAngle(euler.y), ToSignedAngle(euler.z));
    }

    private static float ToSignedAngle(float angle)
    {
        return angle > 180.0f ? angle - 360.0f : angle;
    }

    private static void DestroyGeneratedObject(UnityEngine.Object value)
    {
        if (value == null)
        {
            return;
        }
        if (Application.isPlaying)
        {
            Destroy(value);
        }
        else
        {
            DestroyImmediate(value);
        }
    }

    private sealed class PostSkinGroup
    {
        public readonly PostSkinSurface[] Surfaces;
        public readonly RendererVisibilityState[] VisibilityStates;
        public readonly MeshRenderer OutputRenderer;
        public readonly PostSkinPartMapping[] PartMappings;

        public PostSkinGroup(
            PostSkinSurface[] surfaces,
            RendererVisibilityState[] visibilityStates,
            MeshRenderer outputRenderer,
            PostSkinPartMapping[] partMappings)
        {
            Surfaces = surfaces;
            VisibilityStates = visibilityStates;
            OutputRenderer = outputRenderer;
            PartMappings = partMappings;
        }
    }

    private readonly struct PostSkinPartMapping
    {
        public readonly SkinnedMeshRenderer Source;
        public readonly int FirstMaterialIndex;
        public readonly int MaterialCount;

        public PostSkinPartMapping(
            SkinnedMeshRenderer source,
            int firstMaterialIndex,
            int materialCount)
        {
            Source = source;
            FirstMaterialIndex = firstMaterialIndex;
            MaterialCount = materialCount;
        }
    }

    private sealed class CentroidSampler
    {
        public readonly Mesh SourceMesh;
        public readonly Mesh BakedMesh;
        public readonly List<Vector3> Vertices;
        public readonly int[][] TriangleIndices;

        public CentroidSampler(Mesh sourceMesh)
        {
            SourceMesh = sourceMesh;
            BakedMesh = new Mesh
            {
                name = sourceMesh.name + " Centroid Cache",
                hideFlags = HideFlags.DontSave
            };
            BakedMesh.MarkDynamic();
            Vertices = new List<Vector3>(sourceMesh.vertexCount);
            TriangleIndices = new int[sourceMesh.subMeshCount][];
            for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                if (sourceMesh.GetTopology(subMeshIndex) == MeshTopology.Triangles)
                {
                    TriangleIndices[subMeshIndex] = sourceMesh.GetIndices(subMeshIndex);
                }
            }
        }
    }

    private readonly struct RendererVisibilityState
    {
        public readonly SkinnedMeshRenderer Renderer;
        public readonly bool WasEnabled;

        public RendererVisibilityState(SkinnedMeshRenderer renderer, bool wasEnabled)
        {
            Renderer = renderer;
            WasEnabled = wasEnabled;
        }
    }

    private readonly struct DualEndSolution
    {
        public readonly SkinnedMeshRenderer ReferenceRenderer;
        public readonly Vector3 Pivot;
        public readonly Quaternion Rotation;
        public readonly Vector3 Translation;
        public readonly float AppliedDegrees;
        public readonly float LowerErrorBefore;
        public readonly float LowerErrorAfter;
        public readonly float UpperErrorBefore;
        public readonly float UpperErrorAfter;
        public readonly float Score;
        public readonly string Continuation;

        public DualEndSolution(
            SkinnedMeshRenderer referenceRenderer,
            Vector3 pivot,
            Quaternion rotation,
            Vector3 translation,
            float appliedDegrees,
            float lowerErrorBefore,
            float lowerErrorAfter,
            float upperErrorBefore,
            float upperErrorAfter,
            float score,
            string continuation)
        {
            ReferenceRenderer = referenceRenderer;
            Pivot = pivot;
            Rotation = rotation;
            Translation = translation;
            AppliedDegrees = appliedDegrees;
            LowerErrorBefore = lowerErrorBefore;
            LowerErrorAfter = lowerErrorAfter;
            UpperErrorBefore = upperErrorBefore;
            UpperErrorAfter = upperErrorAfter;
            Score = score;
            Continuation = continuation;
        }
    }

    private readonly struct PostSkinSurface
    {
        public readonly SkinnedMeshRenderer Source;
        public readonly Transform OutputTransform;
        public readonly Mesh Mesh;
        public readonly Mesh BakedMesh;
        public readonly List<Vector3> Vertices;
        public readonly List<Vector3> Normals;
        public readonly List<Vector4> Tangents;

        public PostSkinSurface(
            SkinnedMeshRenderer source,
            Transform outputTransform,
            Mesh mesh,
            Mesh bakedMesh)
        {
            Source = source;
            OutputTransform = outputTransform;
            Mesh = mesh;
            BakedMesh = bakedMesh;
            Vertices = new List<Vector3>(source.sharedMesh.vertexCount);
            Normals = new List<Vector3>(source.sharedMesh.vertexCount);
            Tangents = new List<Vector4>(source.sharedMesh.vertexCount);
        }
    }

}
