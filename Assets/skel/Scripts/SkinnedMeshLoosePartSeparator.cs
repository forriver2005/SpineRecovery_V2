using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Separates the two torso meshes into triangle-connected loose parts.</summary>
[ExecuteAlways]
[DefaultExecutionOrder(-2000)]
public sealed class SkinnedMeshLoosePartSeparator : MonoBehaviour
{
    [Header("Targets (auto-detected when empty)")]
    public Transform lowerLumbar;
    public Transform upperThorax;
    public Transform head;

    [Header("Automatic separation")]
    public bool autoSeparateOnPlay = true;

    [Header("Male mesh fallback")]
    [Tooltip("Use only for models exported with disconnected triangles. Female anatomy keeps this disabled.")]
    public bool useHeightBandFallback;
    [Min(1)] public int maleThoraxPartCount = 12;
    [Min(1)] public int maleHeadPartCount = 9;
    [Min(2)] public int fallbackComponentThreshold = 100;

    [Header("Diagnostics")]
    [SerializeField] private int lowerPartCount;
    [SerializeField] private int upperPartCount;
    [SerializeField] private int headPartCount;
    [SerializeField] private string status = "Not separated";

    private readonly List<GeneratedPart> generatedParts = new List<GeneratedPart>();
    private readonly List<SourceRendererState> sourceRenderers = new List<SourceRendererState>();

    public int LowerPartCount { get { return lowerPartCount; } }
    public int UpperPartCount { get { return upperPartCount; } }
    public int HeadPartCount { get { return headPartCount; } }
    public bool HasGeneratedParts { get { return generatedParts.Count > 0; } }
    public string Status { get { return status; } }

    public void GetTorsoRotationTargets(List<TorsoRotationTarget> targets)
    {
        if (targets == null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        targets.Clear();
        for (int index = 0; index < generatedParts.Count; index++)
        {
            GeneratedPart part = generatedParts[index];
            if (part.Renderer != null &&
                (part.Region == "Lumbar" || part.Region == "Thorax"))
            {
                targets.Add(new TorsoRotationTarget(
                    part.Renderer,
                    part.Region,
                    part.PartNumber,
                    part.SampleVertexA,
                    part.SampleVertexB,
                    part.SampleVertexC));
            }
        }
    }

    private void OnEnable()
    {
        if (!Application.isPlaying && generatedParts.Count == 0)
        {
            RestoreSourceRendererVisibility();
        }
    }

    private void Start()
    {
        if (Application.isPlaying && autoSeparateOnPlay && generatedParts.Count == 0)
        {
            SeparateLooseParts();
        }
    }

    private void OnDisable()
    {
        ClearSeparatedMeshes();
    }

    private void OnDestroy()
    {
        ClearSeparatedMeshes();
    }

    [ContextMenu("Separate Torso Meshes By Loose Parts")]
    public void SeparateLooseParts()
    {
        ClearSeparatedMeshes();
        if (!ResolveTargets())
        {
            return;
        }

        lowerPartCount = SeparateTarget(lowerLumbar, "Lumbar", 0);
        upperPartCount = SeparateTarget(
            upperThorax,
            "Thorax",
            useHeightBandFallback ? maleThoraxPartCount : 0);
        headPartCount = SeparateTarget(
            head,
            "Head",
            useHeightBandFallback ? maleHeadPartCount : 0);
        status = "Loose-part separation created " + lowerPartCount + " lumbar and "
            + upperPartCount + " thorax and " + headPartCount + " head child meshes.";
        Debug.Log("[SKEL loose parts] " + status, this);

        LumbarSeamAligner aligner = GetComponent<LumbarSeamAligner>();
        if (aligner != null)
        {
            aligner.RebuildForCurrentRenderers();
        }
    }

    [ContextMenu("Clear Separated Torso Meshes")]
    public void ClearSeparatedMeshes()
    {
        for (int index = 0; index < sourceRenderers.Count; index++)
        {
            SourceRendererState source = sourceRenderers[index];
            if (source.Renderer != null)
            {
                source.Renderer.enabled = source.WasEnabled;
            }
        }
        sourceRenderers.Clear();

        for (int index = 0; index < generatedParts.Count; index++)
        {
            GeneratedPart part = generatedParts[index];
            if (part.GameObject != null)
            {
                part.GameObject.SetActive(false);
                DestroyGeneratedObject(part.GameObject);
            }
            if (part.Mesh != null)
            {
                DestroyGeneratedObject(part.Mesh);
            }
        }
        generatedParts.Clear();
        lowerPartCount = 0;
        upperPartCount = 0;
        headPartCount = 0;
        status = "Separated meshes cleared";
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
            if (upperThorax == null && useHeightBandFallback)
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

    private int SeparateTarget(Transform target, string label, int fallbackPartCount)
    {
        SkinnedMeshRenderer sourceRenderer = target.GetComponent<SkinnedMeshRenderer>();
        if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
        {
            Debug.LogWarning("[SKEL loose parts] No source SkinnedMeshRenderer on " + target.name + ".", this);
            return 0;
        }

        Mesh sourceMesh = sourceRenderer.sharedMesh;
        if (!sourceMesh.isReadable)
        {
            Debug.LogError("[SKEL loose parts] Mesh '" + sourceMesh.name
                + "' is not readable. Enable Read/Write in the FBX importer.", this);
            return 0;
        }

        List<LooseComponent> components = BuildLooseComponents(sourceMesh);
        if (fallbackPartCount > 0 && components.Count >= fallbackComponentThreshold)
        {
            components = BuildHeightBandComponents(sourceMesh, fallbackPartCount);
            Debug.Log(
                "[SKEL loose parts] " + target.name + " has " +
                "disconnected triangle topology; using " + fallbackPartCount +
                " height bands for the male mesh fallback.",
                this);
        }
        if (!SortComponentsByWorldHeight(sourceRenderer, components))
        {
            Debug.LogWarning("[SKEL loose parts] Could not bake '" + sourceMesh.name
                + "' for world-height sorting; falling back to mesh-local Y.", this);
            SortComponentsByLocalHeight(sourceMesh, components);
        }
        sourceRenderers.Add(new SourceRendererState(sourceRenderer, sourceRenderer.enabled));

        for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            LooseComponent component = components[componentIndex];
            int partNumber = componentIndex + 1;
            Mesh partMesh = Instantiate(sourceMesh);
            partMesh.name = sourceMesh.name + "_" + label + "_LoosePart_" + partNumber.ToString("000");
            partMesh.hideFlags = HideFlags.DontSave;
            partMesh.subMeshCount = sourceMesh.subMeshCount;
            for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                List<int> indices = component.IndicesBySubMesh[subMeshIndex];
                partMesh.SetIndices(
                    indices.Count == 0 ? Array.Empty<int>() : indices.ToArray(),
                    sourceMesh.GetTopology(subMeshIndex),
                    subMeshIndex,
                    false);
            }
            partMesh.bounds = sourceMesh.bounds;

            GameObject partObject = new GameObject(label + " Loose Part " + partNumber.ToString("000"));
            partObject.hideFlags = HideFlags.DontSave;
            partObject.layer = target.gameObject.layer;
            Transform partTransform = partObject.transform;
            partTransform.SetParent(target, false);
            partTransform.localPosition = Vector3.zero;
            partTransform.localRotation = Quaternion.identity;
            partTransform.localScale = Vector3.one;

            SkinnedMeshRenderer partRenderer = partObject.AddComponent<SkinnedMeshRenderer>();
            CopyRendererSettings(sourceRenderer, partRenderer, partMesh);
            int sampleVertexA;
            int sampleVertexB;
            int sampleVertexC;
            component.GetOrientationSampleVertices(
                sourceMesh.vertices,
                out sampleVertexA,
                out sampleVertexB,
                out sampleVertexC);
            generatedParts.Add(new GeneratedPart(
                partObject,
                partMesh,
                partRenderer,
                label,
                partNumber,
                sampleVertexA,
                sampleVertexB,
                sampleVertexC));
        }

        // In Edit Mode the generated meshes are temporary. Keep the imported source
        // visible so a domain reload cannot leave this anatomy hidden.
        if (Application.isPlaying)
        {
            sourceRenderer.enabled = false;
        }
        return components.Count;
    }

    private void RestoreSourceRendererVisibility()
    {
        if (!ResolveTargets())
        {
            return;
        }

        SkinnedMeshRenderer lowerRenderer = lowerLumbar.GetComponent<SkinnedMeshRenderer>();
        SkinnedMeshRenderer upperRenderer = upperThorax.GetComponent<SkinnedMeshRenderer>();
        SkinnedMeshRenderer headRenderer = head.GetComponent<SkinnedMeshRenderer>();
        if (lowerRenderer != null)
        {
            lowerRenderer.enabled = true;
        }
        if (upperRenderer != null)
        {
            upperRenderer.enabled = true;
        }
        if (headRenderer != null)
        {
            headRenderer.enabled = true;
        }
        status = "Source torso meshes visible; loose parts not generated.";
    }

    private static List<LooseComponent> BuildLooseComponents(Mesh mesh)
    {
        DisjointSet vertices = new DisjointSet(mesh.vertexCount);
        List<TriangleRecord> triangles = new List<TriangleRecord>();

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            MeshTopology topology = mesh.GetTopology(subMeshIndex);
            int[] indices = mesh.GetIndices(subMeshIndex);
            if (topology != MeshTopology.Triangles)
            {
                continue;
            }

            for (int index = 0; index + 2 < indices.Length; index += 3)
            {
                int a = indices[index];
                int b = indices[index + 1];
                int c = indices[index + 2];
                vertices.Union(a, b);
                vertices.Union(b, c);
                triangles.Add(new TriangleRecord(subMeshIndex, a, b, c));
            }
        }

        Dictionary<int, LooseComponent> componentByRoot = new Dictionary<int, LooseComponent>();
        for (int index = 0; index < triangles.Count; index++)
        {
            TriangleRecord triangle = triangles[index];
            int root = vertices.Find(triangle.A);
            LooseComponent component;
            if (!componentByRoot.TryGetValue(root, out component))
            {
                component = new LooseComponent(mesh.subMeshCount);
                componentByRoot.Add(root, component);
            }
            List<int> destination = component.IndicesBySubMesh[triangle.SubMesh];
            destination.Add(triangle.A);
            destination.Add(triangle.B);
            destination.Add(triangle.C);
        }

        return new List<LooseComponent>(componentByRoot.Values);
    }

    private static List<LooseComponent> BuildHeightBandComponents(Mesh mesh, int partCount)
    {
        List<TriangleRecord> triangles = new List<TriangleRecord>();
        Vector3[] vertices = mesh.vertices;
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                continue;
            }

            int[] indices = mesh.GetIndices(subMeshIndex);
            for (int index = 0; index + 2 < indices.Length; index += 3)
            {
                triangles.Add(new TriangleRecord(
                    subMeshIndex,
                    indices[index],
                    indices[index + 1],
                    indices[index + 2]));
            }
        }

        triangles.Sort((left, right) =>
        {
            float leftHeight = (vertices[left.A].y + vertices[left.B].y + vertices[left.C].y) / 3.0f;
            float rightHeight = (vertices[right.A].y + vertices[right.B].y + vertices[right.C].y) / 3.0f;
            return rightHeight.CompareTo(leftHeight);
        });

        int count = Mathf.Min(Mathf.Max(1, partCount), triangles.Count);
        List<LooseComponent> components = new List<LooseComponent>(count);
        for (int index = 0; index < count; index++)
        {
            components.Add(new LooseComponent(mesh.subMeshCount));
        }

        for (int index = 0; index < triangles.Count; index++)
        {
            TriangleRecord triangle = triangles[index];
            LooseComponent component = components[Mathf.Min(count - 1, index * count / triangles.Count)];
            component.IndicesBySubMesh[triangle.SubMesh].Add(triangle.A);
            component.IndicesBySubMesh[triangle.SubMesh].Add(triangle.B);
            component.IndicesBySubMesh[triangle.SubMesh].Add(triangle.C);
        }
        return components;
    }

    private static bool SortComponentsByWorldHeight(
        SkinnedMeshRenderer renderer,
        List<LooseComponent> components)
    {
        Mesh bakedMesh = new Mesh { hideFlags = HideFlags.DontSave };
        try
        {
            // TransformPoint below applies the renderer scale exactly once.
            renderer.BakeMesh(bakedMesh, false);
            Vector3[] bakedVertices = bakedMesh.vertices;
            if (bakedVertices.Length != renderer.sharedMesh.vertexCount)
            {
                return false;
            }

            Transform rendererTransform = renderer.transform;
            for (int index = 0; index < components.Count; index++)
            {
                components[index].CalculateHeight(
                    bakedVertices,
                    vertex => rendererTransform.TransformPoint(vertex).y);
            }
            components.Sort(CompareTopToBottom);
            return true;
        }
        finally
        {
            DestroyGeneratedObject(bakedMesh);
        }
    }

    private static void SortComponentsByLocalHeight(
        Mesh mesh,
        List<LooseComponent> components)
    {
        Vector3[] vertices = mesh.vertices;
        for (int index = 0; index < components.Count; index++)
        {
            components[index].CalculateHeight(vertices, vertex => vertex.y);
        }
        components.Sort(CompareTopToBottom);
    }

    private static int CompareTopToBottom(LooseComponent left, LooseComponent right)
    {
        int centerComparison = right.CenterHeight.CompareTo(left.CenterHeight);
        if (centerComparison != 0)
        {
            return centerComparison;
        }
        int topComparison = right.MaximumHeight.CompareTo(left.MaximumHeight);
        if (topComparison != 0)
        {
            return topComparison;
        }
        int triangleComparison = right.TriangleCount.CompareTo(left.TriangleCount);
        return triangleComparison != 0
            ? triangleComparison
            : left.MinimumVertexIndex.CompareTo(right.MinimumVertexIndex);
    }

    private static void CopyRendererSettings(
        SkinnedMeshRenderer source,
        SkinnedMeshRenderer destination,
        Mesh mesh)
    {
        destination.sharedMesh = mesh;
        destination.sharedMaterials = source.sharedMaterials;
        destination.bones = source.bones;
        destination.rootBone = source.rootBone;
        destination.localBounds = source.localBounds;
        destination.quality = source.quality;
        destination.updateWhenOffscreen = source.updateWhenOffscreen;
        destination.skinnedMotionVectors = source.skinnedMotionVectors;
        destination.shadowCastingMode = source.shadowCastingMode;
        destination.receiveShadows = source.receiveShadows;
        destination.lightProbeUsage = source.lightProbeUsage;
        destination.reflectionProbeUsage = source.reflectionProbeUsage;
        destination.probeAnchor = source.probeAnchor;
        destination.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        destination.renderingLayerMask = source.renderingLayerMask;
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

    private sealed class LooseComponent
    {
        public readonly List<int>[] IndicesBySubMesh;
        public float CenterHeight { get; private set; }
        public float MaximumHeight { get; private set; }
        public int MinimumVertexIndex { get; private set; }

        public int TriangleCount
        {
            get
            {
                int indexCount = 0;
                for (int index = 0; index < IndicesBySubMesh.Length; index++)
                {
                    indexCount += IndicesBySubMesh[index].Count;
                }
                return indexCount / 3;
            }
        }

        public LooseComponent(int subMeshCount)
        {
            IndicesBySubMesh = new List<int>[subMeshCount];
            for (int index = 0; index < subMeshCount; index++)
            {
                IndicesBySubMesh[index] = new List<int>();
            }
        }

        public void CalculateHeight(Vector3[] vertices, Func<Vector3, float> getHeight)
        {
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            int minimumVertexIndex = int.MaxValue;
            for (int subMeshIndex = 0; subMeshIndex < IndicesBySubMesh.Length; subMeshIndex++)
            {
                List<int> indices = IndicesBySubMesh[subMeshIndex];
                for (int index = 0; index < indices.Count; index++)
                {
                    int vertexIndex = indices[index];
                    float height = getHeight(vertices[vertexIndex]);
                    minimum = Mathf.Min(minimum, height);
                    maximum = Mathf.Max(maximum, height);
                    minimumVertexIndex = Mathf.Min(minimumVertexIndex, vertexIndex);
                }
            }
            MaximumHeight = maximum;
            CenterHeight = (minimum + maximum) * 0.5f;
            MinimumVertexIndex = minimumVertexIndex;
        }

        public void GetOrientationSampleVertices(
            Vector3[] vertices,
            out int sampleA,
            out int sampleB,
            out int sampleC)
        {
            HashSet<int> uniqueSet = new HashSet<int>();
            List<int> uniqueVertices = new List<int>();
            for (int subMeshIndex = 0; subMeshIndex < IndicesBySubMesh.Length; subMeshIndex++)
            {
                List<int> indices = IndicesBySubMesh[subMeshIndex];
                for (int index = 0; index < indices.Count; index++)
                {
                    int vertexIndex = indices[index];
                    if (uniqueSet.Add(vertexIndex))
                    {
                        uniqueVertices.Add(vertexIndex);
                    }
                }
            }

            sampleA = uniqueVertices.Count > 0 ? uniqueVertices[0] : 0;
            sampleA = FindFarthestVertex(vertices, uniqueVertices, sampleA);
            sampleB = FindFarthestVertex(vertices, uniqueVertices, sampleA);
            sampleC = FindFarthestFromLine(vertices, uniqueVertices, sampleA, sampleB);
        }

        private static int FindFarthestVertex(
            Vector3[] vertices,
            List<int> candidates,
            int originIndex)
        {
            int result = originIndex;
            float maximumDistance = -1.0f;
            Vector3 origin = vertices[originIndex];
            for (int index = 0; index < candidates.Count; index++)
            {
                int candidate = candidates[index];
                float distance = (vertices[candidate] - origin).sqrMagnitude;
                if (distance > maximumDistance)
                {
                    maximumDistance = distance;
                    result = candidate;
                }
            }
            return result;
        }

        private static int FindFarthestFromLine(
            Vector3[] vertices,
            List<int> candidates,
            int lineStartIndex,
            int lineEndIndex)
        {
            int result = lineStartIndex;
            float maximumArea = -1.0f;
            Vector3 start = vertices[lineStartIndex];
            Vector3 axis = vertices[lineEndIndex] - start;
            for (int index = 0; index < candidates.Count; index++)
            {
                int candidate = candidates[index];
                float area = Vector3.Cross(axis, vertices[candidate] - start).sqrMagnitude;
                if (area > maximumArea)
                {
                    maximumArea = area;
                    result = candidate;
                }
            }
            return result;
        }
    }

    private sealed class DisjointSet
    {
        private readonly int[] parent;
        private readonly byte[] rank;

        public DisjointSet(int count)
        {
            parent = new int[count];
            rank = new byte[count];
            for (int index = 0; index < count; index++)
            {
                parent[index] = index;
            }
        }

        public int Find(int value)
        {
            int root = value;
            while (parent[root] != root)
            {
                root = parent[root];
            }
            while (parent[value] != value)
            {
                int next = parent[value];
                parent[value] = root;
                value = next;
            }
            return root;
        }

        public void Union(int left, int right)
        {
            int leftRoot = Find(left);
            int rightRoot = Find(right);
            if (leftRoot == rightRoot)
            {
                return;
            }
            if (rank[leftRoot] < rank[rightRoot])
            {
                parent[leftRoot] = rightRoot;
            }
            else if (rank[leftRoot] > rank[rightRoot])
            {
                parent[rightRoot] = leftRoot;
            }
            else
            {
                parent[rightRoot] = leftRoot;
                rank[leftRoot]++;
            }
        }
    }

    private readonly struct TriangleRecord
    {
        public readonly int SubMesh;
        public readonly int A;
        public readonly int B;
        public readonly int C;

        public TriangleRecord(int subMesh, int a, int b, int c)
        {
            SubMesh = subMesh;
            A = a;
            B = b;
            C = c;
        }
    }

    private readonly struct SourceRendererState
    {
        public readonly SkinnedMeshRenderer Renderer;
        public readonly bool WasEnabled;

        public SourceRendererState(SkinnedMeshRenderer renderer, bool wasEnabled)
        {
            Renderer = renderer;
            WasEnabled = wasEnabled;
        }
    }

    private readonly struct GeneratedPart
    {
        public readonly GameObject GameObject;
        public readonly Mesh Mesh;
        public readonly SkinnedMeshRenderer Renderer;
        public readonly string Region;
        public readonly int PartNumber;
        public readonly int SampleVertexA;
        public readonly int SampleVertexB;
        public readonly int SampleVertexC;

        public GeneratedPart(
            GameObject gameObject,
            Mesh mesh,
            SkinnedMeshRenderer renderer,
            string region,
            int partNumber,
            int sampleVertexA,
            int sampleVertexB,
            int sampleVertexC)
        {
            GameObject = gameObject;
            Mesh = mesh;
            Renderer = renderer;
            Region = region;
            PartNumber = partNumber;
            SampleVertexA = sampleVertexA;
            SampleVertexB = sampleVertexB;
            SampleVertexC = sampleVertexC;
        }
    }

    public readonly struct TorsoRotationTarget
    {
        public readonly SkinnedMeshRenderer Renderer;
        public readonly string Region;
        public readonly int PartNumber;
        public readonly int SampleVertexA;
        public readonly int SampleVertexB;
        public readonly int SampleVertexC;

        public TorsoRotationTarget(
            SkinnedMeshRenderer renderer,
            string region,
            int partNumber,
            int sampleVertexA,
            int sampleVertexB,
            int sampleVertexC)
        {
            Renderer = renderer;
            Region = region;
            PartNumber = partNumber;
            SampleVertexA = sampleVertexA;
            SampleVertexB = sampleVertexB;
            SampleVertexC = sampleVertexC;
        }
    }
}
