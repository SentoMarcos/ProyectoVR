using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

public class MouseTouchRotateScale : MonoBehaviour
{
    [Header("Rotation Settings")]
    public float rotateSpeedMouse = 0.3f;
    public float rotateSpeedTouch = 0.15f;

    [Header("Scale Zoom Settings")]
    public float minScale = 0.2f;
    public float maxScale = 3f;
    public float wheelZoomSpeed = 0.1f; 
    public float pinchZoomSpeed = 0.01f;

    private Vector3 originalScale;

#if !ENABLE_INPUT_SYSTEM
    private Vector3 lastMousePos;
    private bool dragging;
#endif

    void Awake()
    {
        originalScale = transform.localScale;

#if ENABLE_INPUT_SYSTEM
        EnhancedTouchSupport.Enable();
#endif
    }

    void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        EnhancedTouchSupport.Disable();
#endif
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        HandleMouseInputSystem();
        HandleTouchInputSystem();
#else
        HandleMouseLegacy();
        HandleTouchLegacy();
#endif
    }

    // ✅ Reset scale (UI button)
    public void ResetBike()
    {
        transform.localScale = originalScale;
        transform.rotation = Quaternion.identity;
    }

#if ENABLE_INPUT_SYSTEM
    // ---------------- Mouse (New Input System) ----------------
    void HandleMouseInputSystem()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        // Rotate
        if (mouse.leftButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            transform.Rotate(-delta.y * rotateSpeedMouse, delta.x * rotateSpeedMouse, 0, Space.World);
        }

        // Scroll to scale
        if (mouse.scroll.IsActuated())
        {
            float s = mouse.scroll.ReadValue().y * wheelZoomSpeed;
            Vector3 newScale = transform.localScale + Vector3.one * s;
            transform.localScale = ClampScale(newScale);
        }
    }

    // ---------------- Touch (New Input System) ----------------
    void HandleTouchInputSystem()
    {
        int count = ETouch.activeTouches.Count;

        if (count == 1)
        {
            var t = ETouch.activeTouches[0];
            if (t.phase == ETouchPhase.Moved)
            {
                Vector2 d = t.delta;
                transform.Rotate(-d.y * rotateSpeedTouch, d.x * rotateSpeedTouch, 0, Space.World);
            }
        }
        else if (count >= 2)
        {
            var t0 = ETouch.activeTouches[0];
            var t1 = ETouch.activeTouches[1];

            float prev = Vector2.Distance(t0.screenPosition - t0.delta, t1.screenPosition - t1.delta);
            float curr = Vector2.Distance(t0.screenPosition, t1.screenPosition);
            float delta = (curr - prev) * pinchZoomSpeed;

            Vector3 newScale = transform.localScale + Vector3.one * delta;
            transform.localScale = ClampScale(newScale);
        }
    }

#else
    // --------------- Mouse Legacy Input ---------------
    void HandleMouseLegacy()
    {
        if (Input.GetMouseButtonDown(0))
        {
            dragging = true;
            lastMousePos = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(0)) dragging = false;

        if (dragging)
        {
            Vector3 delta = Input.mousePosition - lastMousePos;
            lastMousePos = Input.mousePosition;
            transform.Rotate(-delta.y * rotateSpeedMouse, delta.x * rotateSpeedMouse, 0, Space.World);
        }

        float scroll = Input.mouseScrollDelta.y * wheelZoomSpeed;
        Vector3 newScale = transform.localScale + Vector3.one * scroll;
        transform.localScale = ClampScale(newScale);
    }

    // --------------- Touch Legacy Input ---------------
    void HandleTouchLegacy()
    {
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved)
                transform.Rotate(-t.deltaPosition.y * rotateSpeedTouch, t.deltaPosition.x * rotateSpeedTouch, 0, Space.World);
        }
        else if (Input.touchCount >= 2)
        {
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);

            float prev = (t0.position - t0.deltaPosition - (t1.position - t1.deltaPosition)).magnitude;
            float curr = (t0.position - t1.position).magnitude;
            float delta = (curr - prev) * pinchZoomSpeed;

            Vector3 newScale = transform.localScale + Vector3.one * delta;
            transform.localScale = ClampScale(newScale);
        }
    }
#endif

    Vector3 ClampScale(Vector3 s)
    {
        float clamped = Mathf.Clamp(s.x, minScale, maxScale);
        return new Vector3(clamped, clamped, clamped);
    }
}