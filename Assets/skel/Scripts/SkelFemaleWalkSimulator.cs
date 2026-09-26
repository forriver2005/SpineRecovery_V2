using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Procedural walking test data and kinematic-tree visualization for skel_female.</summary>
[DefaultExecutionOrder(1000)]
public sealed class SkelFemaleWalkSimulator : MonoBehaviour
{
    [Header("Simulation")]
    public bool autoStartOnPlay = true;
    public bool simulateWalking = true;
    [Min(0.1f)] public float cycleDuration = 1.1f;
    [Range(0.0f, 2.0f)] public float motionScale = 1.0f;
    public bool translateForward;
    [Min(0.0f)] public float forwardSpeed = 0.8f;

    [Header("Lumbar and chest motion")]
    public bool addTorsoBending = true;
    [Min(1.0f)] public float bendCycleDuration = 4.4f;
    [Range(0.0f, 120.0f)] public float forwardBendDegrees = 60.0f;
    [Range(0.0f, 120.0f)] public float backwardBendDegrees = 60.0f;
    [Range(0.0f, 120.0f)] public float chestMotionDegrees = 60.0f;

    [Header("Leg motion")]
    [Range(0.0f, 1.5f)] public float kneeFlexionScale = 0.85f;
    [Range(0.0f, 1.5f)] public float ankleMotionScale = 0.85f;

    [Header("Kinematic tree spheres")]
    public bool showJointSpheres = true;
    [Min(0.001f)] public float sphereDiameter = 0.025f;
    public Color jointColor = new Color(0.1f, 0.9f, 0.35f, 1.0f);

    [Header("Diagnostics")]
    [SerializeField] private int drivenJointCount;
    [SerializeField] private int visualizedJointCount;
    [SerializeField] private string status = "Waiting for skel_female";

    private static readonly float[] HipFlexion = { -18f, -8f, 6f, 20f, 10f, -4f, -18f, -24f, -18f };
    private static readonly float[] KneeFlexion = { 5f, 10f, 18f, 10f, 4f, 10f, 46f, 28f, 5f };
    private static readonly float[] AnkleFlexion = { 2f, -7f, -12f, 5f, 9f, 1f, -10f, -4f, 2f };
    private static readonly float[] RootBob = { 0f, -0.012f, 0f, 0.014f, 0f, -0.012f, 0f, 0.014f, 0f };
    private static readonly float[] RootSway = { 0f, -0.012f, -0.018f, -0.01f, 0f, 0.012f, 0.018f, 0.01f, 0f };

    private readonly Dictionary<Transform, Quaternion> restRotations = new Dictionary<Transform, Quaternion>();
    private readonly List<JointMarker> markers = new List<JointMarker>();
    private Transform skeletonRoot;
    private Transform pelvis;
    private Transform lumbar;
    private Transform thorax;
    private Transform leftHip;
    private Transform rightHip;
    private Transform leftKnee;
    private Transform rightKnee;
    private Transform leftAnkle;
    private Transform rightAnkle;
    private Transform leftShoulder;
    private Transform rightShoulder;
    private Vector3 restRootLocalPosition;
    private float simulationTime;
    private Transform markerRoot;
    private Material markerMaterial;
    private bool initialized;

    public int DrivenJointCount { get { return drivenJointCount; } }
    public int VisualizedJointCount { get { return visualizedJointCount; } }
    public string Status { get { return status; } }

    private void Start()
    {
        Initialize();
        simulateWalking = autoStartOnPlay || simulateWalking;
        Debug.Log("[SKEL walk] " + status, this);
    }

    private void Update()
    {
        if (!initialized && !Initialize())
        {
            return;
        }

        if (simulateWalking)
        {
            simulationTime += Time.deltaTime;
            ApplyPose(simulationTime / Mathf.Max(0.1f, cycleDuration));
        }
    }

    private void LateUpdate()
    {
        if (showJointSpheres && markerRoot == null)
        {
            RebuildJointSpheres();
        }
        if (!showJointSpheres && markerRoot != null)
        {
            ClearJointSpheres();
        }

        for (int index = 0; index < markers.Count; index++)
        {
            if (markers[index].Joint != null && markers[index].Marker != null)
            {
                markers[index].Marker.position = markers[index].Joint.position;
                SetWorldDiameter(markers[index].Marker, sphereDiameter);
            }
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            RestoreRestPose();
        }
        ClearJointSpheres();
    }

    private void OnDestroy()
    {
        ClearJointSpheres();
    }

    [ContextMenu("Start Walking Simulation")]
    public void StartSimulation()
    {
        if (Initialize())
        {
            simulateWalking = true;
            status = "Walking simulation running on " + drivenJointCount + " joints.";
        }
    }

    [ContextMenu("Stop And Restore Rest Pose")]
    public void StopAndRestore()
    {
        simulateWalking = false;
        RestoreRestPose();
        status = "Walking simulation stopped; rest pose restored.";
    }

    [ContextMenu("Rebuild Kinematic Joint Spheres")]
    public void RebuildJointSpheres()
    {
        ClearJointSpheres();
        if (!Initialize())
        {
            return;
        }

        GameObject rootObject = new GameObject("SKEL Kinematic Joints (Generated)");
        rootObject.hideFlags = HideFlags.DontSave;
        markerRoot = rootObject.transform;
        markerRoot.SetParent(transform, false);
        markerMaterial = CreateMarkerMaterial(jointColor);

        Transform[] joints = skeletonRoot.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < joints.Length; index++)
        {
            Transform joint = joints[index];
            if (!IsKinematicJoint(joint))
            {
                continue;
            }

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Joint @ " + joint.name;
            sphere.hideFlags = HideFlags.DontSave;
            sphere.transform.SetParent(markerRoot, false);
            sphere.transform.position = joint.position;
            SetWorldDiameter(sphere.transform, sphereDiameter);

            Renderer renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = markerMaterial;
            }
            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyGeneratedObject(collider);
            }
            markers.Add(new JointMarker(joint, sphere.transform));
        }

        visualizedJointCount = markers.Count;
        status = "Visualizing " + visualizedJointCount + " kinematic joints; walking drives "
            + drivenJointCount + " joints.";
    }

    private bool Initialize()
    {
        if (initialized && skeletonRoot != null)
        {
            return true;
        }

        skeletonRoot = FindDescendant(transform, "SKEL_Root");
        if (skeletonRoot == null)
        {
            status = "Could not find SKEL_Root below skel_female.";
            return false;
        }

        pelvis = FindDescendant(transform, "pelvis");
        lumbar = FindDescendant(transform, "lumbar_body");
        thorax = FindFirstDescendant(transform, "thorax", "thoracic6", "spine");
        leftHip = FindDescendant(transform, "femur_l");
        rightHip = FindDescendant(transform, "femur_r");
        leftKnee = FindDescendant(transform, "tibia_l");
        rightKnee = FindDescendant(transform, "tibia_r");
        leftAnkle = FindFirstDescendant(transform, "talus_l", "calcn_l", "foot_l");
        rightAnkle = FindFirstDescendant(transform, "talus_r", "calcn_r", "foot_r");
        leftShoulder = FindDescendant(transform, "humerus_l");
        rightShoulder = FindDescendant(transform, "humerus_r");

        restRootLocalPosition = skeletonRoot.localPosition;
        restRotations.Clear();
        CaptureRestRotation(skeletonRoot);
        CaptureRestRotation(pelvis);
        CaptureRestRotation(lumbar);
        CaptureRestRotation(thorax);
        CaptureRestRotation(leftHip);
        CaptureRestRotation(rightHip);
        CaptureRestRotation(leftKnee);
        CaptureRestRotation(rightKnee);
        CaptureRestRotation(leftAnkle);
        CaptureRestRotation(rightAnkle);
        CaptureRestRotation(leftShoulder);
        CaptureRestRotation(rightShoulder);
        drivenJointCount = restRotations.Count;
        initialized = true;
        status = "Ready: " + drivenJointCount + " walking joints resolved.";

        if (showJointSpheres && markerRoot == null)
        {
            RebuildJointSpheres();
        }
        return true;
    }

    private void ApplyPose(float cycle)
    {
        float phase = Mathf.Repeat(cycle, 1.0f);
        float oppositePhase = Mathf.Repeat(phase + 0.5f, 1.0f);
        float leftHipAngle = SampleCycle(HipFlexion, phase) * motionScale;
        float rightHipAngle = SampleCycle(HipFlexion, oppositePhase) * motionScale;

        SetRestRelativeRotation(leftHip, new Vector3(leftHipAngle, 0.0f, 0.0f));
        SetRestRelativeRotation(rightHip, new Vector3(rightHipAngle, 0.0f, 0.0f));
        float leftKneeAngle = -SampleCycle(KneeFlexion, phase) * kneeFlexionScale * motionScale;
        float rightKneeAngle = -SampleCycle(KneeFlexion, oppositePhase) * kneeFlexionScale * motionScale;
        SetRestRelativeRotation(leftKnee, new Vector3(leftKneeAngle, 0.0f, 0.0f));
        SetRestRelativeRotation(rightKnee, new Vector3(rightKneeAngle, 0.0f, 0.0f));
        SetRestRelativeRotation(leftAnkle, new Vector3(
            SampleCycle(AnkleFlexion, phase) * ankleMotionScale * motionScale,
            0.0f,
            0.0f));
        SetRestRelativeRotation(rightAnkle, new Vector3(
            SampleCycle(AnkleFlexion, oppositePhase) * ankleMotionScale * motionScale,
            0.0f,
            0.0f));

        SetRestRelativeRotation(leftShoulder, new Vector3(-rightHipAngle * 0.75f, 0.0f, 3.0f * motionScale));
        SetRestRelativeRotation(rightShoulder, new Vector3(-leftHipAngle * 0.75f, 0.0f, -3.0f * motionScale));
        float torsoTwist = Mathf.Sin(phase * Mathf.PI * 2.0f) * 4.0f * motionScale;
        float bendAngle = 0.0f;
        float chestAngle = 0.0f;
        if (addTorsoBending)
        {
            float bendPhase = simulationTime / Mathf.Max(1.0f, bendCycleDuration);
            float bendWave = Mathf.Sin(bendPhase * Mathf.PI * 2.0f);
            bendAngle = bendWave >= 0.0f
                ? bendWave * forwardBendDegrees
                : bendWave * backwardBendDegrees;
            chestAngle = Mathf.Sin(bendPhase * Mathf.PI * 4.0f + Mathf.PI * 0.35f)
                * chestMotionDegrees;
        }
        SetRestRelativeRotation(pelvis, new Vector3(0.0f, torsoTwist * 0.35f, 0.0f));
        SetRestRelativeRotation(lumbar, new Vector3(
            (1.5f + bendAngle * 0.6f) * motionScale,
            -torsoTwist * 0.45f,
            0.0f));
        SetRestRelativeRotation(thorax, new Vector3(
            (-1.0f + bendAngle * 0.4f + chestAngle) * motionScale,
            torsoTwist * 0.55f,
            Mathf.Sin(phase * Mathf.PI * 2.0f) * 2.0f * motionScale));

        Vector3 rootOffset = new Vector3(
            SampleCycle(RootSway, phase) * motionScale,
            SampleCycle(RootBob, phase) * motionScale,
            translateForward ? simulationTime * forwardSpeed : 0.0f);
        skeletonRoot.localPosition = restRootLocalPosition + rootOffset;
    }

    private void RestoreRestPose()
    {
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            if (pair.Key != null)
            {
                pair.Key.localRotation = pair.Value;
            }
        }
        if (skeletonRoot != null)
        {
            skeletonRoot.localPosition = restRootLocalPosition;
        }
        simulationTime = 0.0f;
    }

    private void CaptureRestRotation(Transform joint)
    {
        if (joint != null && !restRotations.ContainsKey(joint))
        {
            restRotations.Add(joint, joint.localRotation);
        }
    }

    private void SetRestRelativeRotation(Transform joint, Vector3 eulerOffset)
    {
        Quaternion rest;
        if (joint != null && restRotations.TryGetValue(joint, out rest))
        {
            joint.localRotation = rest * Quaternion.Euler(eulerOffset);
        }
    }

    private static float SampleCycle(float[] values, float phase)
    {
        float sample = Mathf.Repeat(phase, 1.0f) * (values.Length - 1);
        int index = Mathf.FloorToInt(sample);
        int next = Mathf.Min(index + 1, values.Length - 1);
        return Mathf.Lerp(values[index], values[next], sample - index);
    }

    private static bool IsKinematicJoint(Transform candidate)
    {
        if (candidate.GetComponent<Renderer>() != null || candidate.GetComponent<MeshFilter>() != null)
        {
            return false;
        }
        return !candidate.name.StartsWith("Biological_", StringComparison.OrdinalIgnoreCase)
            && !candidate.name.EndsWith("_mesh", StringComparison.OrdinalIgnoreCase);
    }

    private static Transform FindFirstDescendant(Transform root, params string[] names)
    {
        for (int index = 0; index < names.Length; index++)
        {
            Transform match = FindDescendant(root, names[index]);
            if (match != null)
            {
                return match;
            }
        }
        return null;
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
        for (int index = 0; index < descendants.Length; index++)
        {
            if (string.Equals(descendants[index].name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return descendants[index];
            }
        }
        return null;
    }

    private static Material CreateMarkerMaterial(Color color)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        Material material = new Material(shader)
        {
            name = "SKEL kinematic joint material",
            color = color,
            hideFlags = HideFlags.DontSave
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        return material;
    }

    private static void SetWorldDiameter(Transform marker, float diameter)
    {
        Vector3 parentScale = marker.parent == null ? Vector3.one : marker.parent.lossyScale;
        marker.localScale = new Vector3(
            SafeDivide(diameter, parentScale.x),
            SafeDivide(diameter, parentScale.y),
            SafeDivide(diameter, parentScale.z));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) < 0.00001f ? value : value / Mathf.Abs(divisor);
    }

    private void ClearJointSpheres()
    {
        markers.Clear();
        visualizedJointCount = 0;
        if (markerRoot != null)
        {
            DestroyGeneratedObject(markerRoot.gameObject);
        }
        markerRoot = null;
        if (markerMaterial != null)
        {
            DestroyGeneratedObject(markerMaterial);
        }
        markerMaterial = null;
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

    private readonly struct JointMarker
    {
        public readonly Transform Joint;
        public readonly Transform Marker;

        public JointMarker(Transform joint, Transform marker)
        {
            Joint = joint;
            Marker = marker;
        }
    }
}
