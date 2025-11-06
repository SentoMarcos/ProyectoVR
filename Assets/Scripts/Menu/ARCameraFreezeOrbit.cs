using System.Collections.Generic;
using UnityEngine;

// Use the ARCamera itself as an orbit camera while the road is frozen.
// Disables AR pose/manager behaviours temporarily so camera transform can be driven manually.
public class ARCameraFreezeOrbit : MonoBehaviour
{
    [Header("References")]
    public RoadFromTargetsSticky road;
    public Camera arCamera; // assign your ARCamera (or the same GameObject this script is on)

    [Header("Behavior")]
    public bool orbitWhenFrozen = true;
    public bool disableARTrackingWhileFrozen = true;

    [Header("Orbit Controls")]
    public float distance = 1.5f;
    public float minDistance = 0.2f;
    public float maxDistance = 10f;
    public float orbitSpeed = 120f; // deg/sec per mouse unit
    public float zoomSpeed = 2.0f;  // scroll wheel
    public Vector2 pitchLimits = new Vector2(-80f, 80f);

    float yaw;
    float pitch = 20f;
    Vector3 pivotWorld;

    // Track temporarily disabled behaviours to re-enable later
    readonly List<Behaviour> _disabledBehaviours = new();

    void Reset()
    {
        if (!arCamera) arCamera = GetComponent<Camera>();
        if (!road) road = FindObjectOfType<RoadFromTargetsSticky>();
    }

    void LateUpdate()
    {
        if (!road || !arCamera) return;

        if (orbitWhenFrozen && road.manualFreeze)
        {
            EnsureARDisabled();
            UpdateOrbitTarget();
            DriveCamera();
        }
        else
        {
            RestoreARIfNeeded();
        }
    }

    void EnsureARDisabled()
    {
        if (!disableARTrackingWhileFrozen) return;
        // Disable common AR behaviours by type name (no hard dependency on packages)
        string[] typeNames = { "TrackedPoseDriver", "ARPoseDriver", "ARCameraManager", "VuforiaBehaviour" };
        foreach (var name in typeNames)
        {
            var comp = arCamera.GetComponent(name) as Behaviour;
            if (comp && comp.enabled)
            {
                comp.enabled = false;
                if (!_disabledBehaviours.Contains(comp)) _disabledBehaviours.Add(comp);
            }
        }
        // If camera is parented under objects that AR uses, it is still fine; pose driver is disabled so transform won't be driven each frame
    }

    void RestoreARIfNeeded()
    {
        if (_disabledBehaviours.Count == 0) return;
        foreach (var b in _disabledBehaviours)
        {
            if (b) b.enabled = true;
        }
        _disabledBehaviours.Clear();
    }

    void UpdateOrbitTarget()
    {
        // Compute pivot once at the beginning of freezing (or when distance not initialized)
        if (pivotWorld == Vector3.zero)
        {
            pivotWorld = ComputeRoadCenter();
            // Initialize yaw/pitch to look at pivot from current camera pose
            Vector3 dir = (arCamera.transform.position - pivotWorld).normalized;
            if (dir.sqrMagnitude < 1e-6f) dir = arCamera.transform.forward;
            Quaternion look = Quaternion.LookRotation(-dir, Vector3.up);
            Vector3 e = look.eulerAngles;
            yaw = e.y;
            pitch = ClampPitch(e.x);
            float d = Vector3.Distance(arCamera.transform.position, pivotWorld);
            if (d > 1e-4f) distance = Mathf.Clamp(d, minDistance, maxDistance);
        }
#if UNITY_EDITOR
        // Right mouse to orbit
        if (Input.GetMouseButton(1))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = -Input.GetAxis("Mouse Y");
            yaw += dx * orbitSpeed * Time.unscaledDeltaTime;
            pitch = ClampPitch(pitch + dy * orbitSpeed * Time.unscaledDeltaTime);
        }
        // Scroll to zoom
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 1e-3f)
            distance = Mathf.Clamp(distance * Mathf.Exp(-scroll * zoomSpeed * 0.1f), minDistance, maxDistance);
#endif
    }

    void DriveCamera()
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rot * (Vector3.back * distance);
        arCamera.transform.position = pivotWorld + offset;
        arCamera.transform.rotation = rot;
        arCamera.transform.LookAt(pivotWorld, Vector3.up);
    }

    Vector3 ComputeRoadCenter()
    {
        if (!road) return arCamera.transform.position + arCamera.transform.forward;
        var mf = road.GetComponent<MeshFilter>();
        if (mf && mf.sharedMesh)
        {
            Bounds b = mf.sharedMesh.bounds;
            return road.transform.TransformPoint(b.center);
        }
        return road.transform.position;
    }

    float ClampPitch(float p)
    {
        // Convert Unity's 0..360 to signed -180..180 for clamping
        if (p > 180f) p -= 360f;
        p = Mathf.Clamp(p, pitchLimits.x, pitchLimits.y);
        if (p < 0f) p += 360f;
        return p;
    }
}
