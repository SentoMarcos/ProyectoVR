using UnityEngine;

// Simple orbit camera preview to inspect the frozen road from any angle in Editor.
// Enable this if you use ARCamera and want to freely orbit when the road is fixed.
public class RoadOrbitPreview : MonoBehaviour
{
    [Header("References")]
    public RoadFromTargetsSticky road;
    public Camera arCamera;    // Your ARCamera (can be left null if not used)
    public Camera orbitCamera; // A secondary camera placed anywhere in the scene

    [Header("Behavior")]
    public bool enableWhenFrozen = true;
    public float distance = 1.5f;
    public float minDistance = 0.2f;
    public float maxDistance = 10f;
    public float orbitSpeed = 120f; // deg/sec per mouse unit
    public float zoomSpeed = 2.0f;  // scroll wheel
    public Vector2 pitchLimits = new Vector2(-80f, 80f);

    float yaw;
    float pitch = 20f;

    void Reset()
    {
        if (!road) road = FindFirstObjectByType<RoadFromTargetsSticky>();
        if (!orbitCamera)
        {
            // Try to find any secondary camera in scene (not the AR one)
            var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in cams) { if (!c.gameObject.name.ToLower().Contains("ar")) { orbitCamera = c; break; } }
        }
        if (!arCamera)
        {
            var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in cams) { if (c.gameObject.name.ToLower().Contains("ar")) { arCamera = c; break; } }
        }
    }

    void LateUpdate()
    {
        if (!road || !orbitCamera) return;

        bool frozen = road.manualFreeze;
        if (enableWhenFrozen && frozen)
        {
            if (arCamera) arCamera.enabled = false;
            orbitCamera.enabled = true;
            UpdateOrbit();
        }
        else
        {
            if (arCamera) arCamera.enabled = true;
            orbitCamera.enabled = false;
        }
    }

    void UpdateOrbit()
    {
#if UNITY_EDITOR
        // Mouse drag to orbit (right mouse)
        if (Input.GetMouseButton(1))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = -Input.GetAxis("Mouse Y");
            yaw += dx * orbitSpeed * Time.unscaledDeltaTime;
            pitch = Mathf.Clamp(pitch + dy * orbitSpeed * Time.unscaledDeltaTime, pitchLimits.x, pitchLimits.y);
        }
        // Scroll to zoom
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 1e-3f)
            distance = Mathf.Clamp(distance * Mathf.Exp(-scroll * zoomSpeed * 0.1f), minDistance, maxDistance);
#endif
        Vector3 target = ComputeRoadCenter();
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rot * (Vector3.back * distance);
        orbitCamera.transform.position = target + offset;
        orbitCamera.transform.rotation = rot;
        orbitCamera.transform.LookAt(target, Vector3.up);
    }

    Vector3 ComputeRoadCenter()
    {
        // Try mesh bounds center in world space
        if (road)
        {
            var mf = road.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh)
            {
                Bounds b = mf.sharedMesh.bounds;
                // bounds are in local space of the road object
                return road.transform.TransformPoint(b.center);
            }
        }
        return road ? road.transform.position : Vector3.zero;
    }
}
