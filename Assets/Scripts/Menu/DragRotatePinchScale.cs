using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
// Aliases to avoid "Touch is ambiguous" errors
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

public class MouseTouchRotateScale : MonoBehaviour
{
    [Header("Rotation")]
    public float rotateSpeedMouse = 0.3f;
    public float rotateSpeedTouch = 0.15f;

    [Header("Scaling")]
    public float minScale = 0.3f;
    public float maxScale = 3f;
    public float scrollScaleSpeed = 0.5f;

    private float pinchStartDistance;
    private Vector3 pinchStartScale;
    private bool pinching;

#if !ENABLE_INPUT_SYSTEM
    private Vector3 lastMousePos;
    private bool dragging;
#endif

    void Awake()
    {
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
        HandleMouse_InputSystem();
        HandleTouch_InputSystem();
#else
        HandleMouse_Legacy();
        HandleTouch_Legacy();
#endif
    }

#if ENABLE_INPUT_SYSTEM
    // -------- New Input System: Mouse --------
    void HandleMouse_InputSystem()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            float rotX = -delta.y * rotateSpeedMouse;
            float rotY =  delta.x * rotateSpeedMouse;
            transform.Rotate(rotX, rotY, 0f, Space.World);
        }

        if (mouse.scroll.IsActuated())
        {
            float scrollY = mouse.scroll.ReadValue().y; // often ±120
            float factor = 1f + (scrollY * 0.001f * scrollScaleSpeed);
            transform.localScale = ClampUniformScale(transform.localScale * factor);
        }
    }

    // -------- New Input System: Touch (EnhancedTouch) --------
    void HandleTouch_InputSystem()
    {
        int count = ETouch.activeTouches.Count;

        if (count == 1)
        {
            pinching = false;
            var t = ETouch.activeTouches[0];
            if (t.phase == ETouchPhase.Moved)
            {
                Vector2 d = t.delta;
                float rotX = -d.y * rotateSpeedTouch;
                float rotY =  d.x * rotateSpeedTouch;
                transform.Rotate(rotX, rotY, 0f, Space.World);
            }
        }
        else if (count >= 2)
        {
            var t0 = ETouch.activeTouches[0];
            var t1 = ETouch.activeTouches[1];

            if (!pinching || t0.phase == ETouchPhase.Began || t1.phase == ETouchPhase.Began)
            {
                pinching = true;
                pinchStartDistance = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                pinchStartScale = transform.localScale;
            }
            else
            {
                float curDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                if (pinchStartDistance > 0.001f)
                {
                    float ratio = curDist / pinchStartDistance;
                    transform.localScale = ClampUniformScale(pinchStartScale * ratio);
                }
            }
        }
        else
        {
            pinching = false;
        }
    }
#else
    // -------- Legacy Input: Mouse --------
    void HandleMouse_Legacy()
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

            float rotX = -delta.y * rotateSpeedMouse;
            float rotY = delta.x * rotateSpeedMouse;
            transform.Rotate(rotX, rotY, 0f, Space.World);
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float factor = 1f + scroll * scrollScaleSpeed * Time.deltaTime;
            transform.localScale = ClampUniformScale(transform.localScale * factor);
        }
    }

    // -------- Legacy Input: Touch --------
    void HandleTouch_Legacy()
    {
        if (Input.touchCount == 1)
        {
            pinching = false;
            UnityEngine.Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved)
            {
                Vector2 d = t.deltaPosition;
                float rotX = -d.y * rotateSpeedTouch;
                float rotY = d.x * rotateSpeedTouch;
                transform.Rotate(rotX, rotY, 0f, Space.World);
            }
        }
        else if (Input.touchCount >= 2)
        {
            UnityEngine.Touch t0 = Input.GetTouch(0);
            UnityEngine.Touch t1 = Input.GetTouch(1);

            if (!pinching || t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
            {
                pinching = true;
                pinchStartDistance = Vector2.Distance(t0.position, t1.position);
                pinchStartScale = transform.localScale;
            }
            else
            {
                float curDist = Vector2.Distance(t0.position, t1.position);
                float ratio = curDist / pinchStartDistance;
                transform.localScale = ClampUniformScale(pinchStartScale * ratio);
            }
        }
        else
        {
            pinching = false;
        }
    }
#endif

    // -------- Helpers --------
    Vector3 ClampUniformScale(Vector3 s)
    {
        float u = Mathf.Clamp(s.x, minScale, maxScale);
        return new Vector3(u, u, u);
    }
}
