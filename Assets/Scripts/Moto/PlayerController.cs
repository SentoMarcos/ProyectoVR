using UnityEngine;
using System.Collections;
using UnityEngine.UI; // Para el texto de UI
// Nuevo Input System
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerController : MonoBehaviour
{
    [Header("Carriles")]
    private int currentLane = 0; // Carril actual (0 = izquierda, centro depende de la carretera)

    [Header("Referencias")]
    public Animator animator;
    [Tooltip("Componente que sigue la carretera. Debe estar en la moto")]
    public RoadLaneFollower laneFollower;
    [Tooltip("Centrar en el carril medio al iniciar")]
    public bool autoCenterOnStart = true;
    
    [Header("VR Controls")]
    [Tooltip("Referencia a la cámara VR de Vuforia")]
    public Camera vuforiaCamera;
    [Tooltip("Umbral de inclinación para cambiar de carril con método Vuforia (0..1 normalizado)")]
    public float tiltThreshold = 0.10f; // un poco menos sensible
    [Tooltip("Suavizado del movimiento de inclinación")]
    public float tiltSmoothing = 5f;
    [Tooltip("Tiempo entre cambios de carril permitidos")]
    public float laneChangeCooldown = 0.3f;

    [Header("Sensibilidad")]
    [Tooltip("Multiplicador de sensibilidad para la inclinación medida con Vuforia ( >1 = más sensible )")]
    public float cameraTiltGain = 1.3f;

    [Header("Inclinación Visual Moto")]
    [Tooltip("Transform que se inclina visualmente (si es null usa este GameObject)")]
    public Transform leanVisual;
    [Tooltip("Ángulo máximo de inclinación visual en grados")]
    public float maxLeanAngle = 25f;
    [Tooltip("Velocidad de suavizado visual de la inclinación")]
    public float leanSmooth = 8f;
    [Tooltip("Aplicar inclinación visual también cuando se simula con teclado")]
    public bool leanDuringSimulation = true;

    [Header("Device Motion (opcional)")]
    [Tooltip("Usar sensores del teléfono (acelerómetro/giroscopio) en vez de la rotación de la cámara Vuforia")]
    public bool useDeviceMotion = false;
    [Tooltip("Umbral en grados para cambiar de carril con sensores del teléfono")]
    public float deviceTiltThresholdDeg = 9f;
    [Tooltip("Ganancia para el tilt detectado por sensores")]
    public float deviceTiltGain = 1.15f;
    [Tooltip("Invertir dirección detectada por sensores")]
    public bool invertDeviceTilt = false;

    [Header("UI Debug")]
    [Tooltip("Texto para mostrar la inclinación")]
    public Text tiltDebugText;
    [Tooltip("Mostrar información de debug en pantalla")]
    public bool showDebugInfo = true;

    [Header("Editor/PC Testing")]
    [Tooltip("Permite probar cambios de carril con teclado/axis en Editor/PC")]
    public bool enableEditorSimulation = true;
    [Tooltip("Usar teclas para cambiar de carril (discreto)")]
    public bool simulateWithKeys = true;
    public KeyCode leftKey = KeyCode.LeftArrow;
    public KeyCode rightKey = KeyCode.RightArrow;
    [Tooltip("Overlay en juego para ajustar sensibilidad en caliente (F1)")]
    public bool enableTuningOverlay = true;

    [Header("Animaciones")]
    public string animIdle = "BikeRig|Idle";
    public string animLeft = "BikeRig|movIzquierda";
    public string animRight = "BikeRig|movDerecha";
    public bool useCrossFade = true;
    public float crossFadeDuration = 0.08f;
    public bool returnToIdleAfterLaneChange = true;

    [Header("Animator (Triggers opcionales)")]
    public bool useAnimatorTriggers = false;
    public string leftTriggerName = "TurnLeft";
    public string rightTriggerName = "TurnRight";

    [Header("Restricciones de control")]
    [Tooltip("Si está activo, sólo permitirá cambiar de carril mientras se está acelerando")]
    public bool requireAcceleratingForLaneChange = false;

    [Header("Velocidad por vuelta")]
    [Tooltip("Cuánto se incrementa la velocidad por cada vuelta completada")]
    public float speedIncrementPerLap = 0.1f;

    // Estado
    private bool isMoving = false; // evita spam de movimientos
    private bool ready = false;    // espera a que la carretera cargue
    private float lastProgress = 0f;
    private int lapCount = 0;
    
    // Control por inclinación
    private Vector3 currentTilt;
    private Vector3 smoothedTilt;
    private float lastLaneChangeTime;
    private bool tiltInitialized = false;
    private Vector3 initialTilt;
    // Sensores del dispositivo
    private bool gyroSupported = false;
    private bool accelSupported = false;
    private float accelZero = 0f; // calibración del eje horizontal
    private bool accelCalibrated = false;
    private float deviceSmoothedTiltDeg = 0f;
    private bool showTuningOverlay = false;
    private float currentLeanAngle = 0f;

    void Awake()
    {
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        if (!laneFollower)
            laneFollower = GetComponent<RoadLaneFollower>();
            
    // Buscar cámara Vuforia si no está asignada
        if (!vuforiaCamera)
            vuforiaCamera = Camera.main;

    // Sensores
    gyroSupported = SystemInfo.supportsGyroscope;
    accelSupported = SystemInfo.supportsAccelerometer;
    if (gyroSupported) Input.gyro.enabled = true;
    }

IEnumerator Start()
{
    SafePlay(animIdle);
    yield return StartCoroutine(WaitForRoadReady());
    CenterLaneOnStart();
    ready = true;
    laneFollower?.AccelerateOn();
    
    // Inicializar calibración de inclinación
    StartCoroutine(InitializeTiltCalibration());
    if (useDeviceMotion && accelSupported)
        StartCoroutine(CalibrateAccelerometer());
}



    IEnumerator InitializeTiltCalibration()
    {
        yield return new WaitForSeconds(1f); // Esperar a que se estabilice
        if (vuforiaCamera != null)
        {
            initialTilt = vuforiaCamera.transform.rotation.eulerAngles;
            tiltInitialized = true;
            Debug.Log("Calibración de inclinación completada: " + initialTilt);
        }
    }

    IEnumerator WaitForRoadReady()
    {
        if (!laneFollower || !laneFollower.road) yield break;

        float timeout = 2f;
        while (!laneFollower.road.PathReady && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
    }

    void CenterLaneOnStart()
    {
        if (!autoCenterOnStart || laneFollower?.road == null) return;

        int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
        currentLane = (lanes - 1) / 2;
        laneFollower.laneIndex = currentLane;
    }

    void Update()
    {
        if (!laneFollower || laneFollower.road == null) return;

        // Detecta vuelta completada
        float progress = laneFollower.CurrentS / laneFollower.road.PathLength;
        if (lastProgress > 0.9f && progress < 0.1f)
        {
            lapCount++;
            laneFollower.speed += speedIncrementPerLap;
            Debug.Log($"Vuelta completada: {lapCount} | Nueva velocidad: {laneFollower.speed}");
        }
        lastProgress = progress;
        
        // Simulación en editor/PC con teclas (opcional, no interfiere con móvil)
        if (enableEditorSimulation && (Application.isEditor || Application.platform == RuntimePlatform.WindowsPlayer))
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (simulateWithKeys && !isMoving && Time.time - lastLaneChangeTime >= laneChangeCooldown)
                {
                    if (IsPressedThisFrame(keyboard, leftKey)) { TryChangeLane(-1); lastLaneChangeTime = Time.time; }
                    else if (IsPressedThisFrame(keyboard, rightKey)) { TryChangeLane(+1); lastLaneChangeTime = Time.time; }
                }
                if (enableTuningOverlay && keyboard[Key.F1].wasPressedThisFrame)
                    showTuningOverlay = !showTuningOverlay;
            }
#else
            // Fallback a sistema antiguo si compilado sin Input System nuevo
            if (simulateWithKeys && !isMoving && Time.time - lastLaneChangeTime >= laneChangeCooldown)
            {
                if (Input.GetKeyDown(leftKey)) { TryChangeLane(-1); lastLaneChangeTime = Time.time; }
                else if (Input.GetKeyDown(rightKey)) { TryChangeLane(+1); lastLaneChangeTime = Time.time; }
            }
            if (enableTuningOverlay && Input.GetKeyDown(KeyCode.F1))
                showTuningOverlay = !showTuningOverlay;
#endif
        }

        // Control por inclinación
        if (useDeviceMotion)
            HandleDeviceTiltControls();
        else
            HandleTiltControls();

        ApplyVisualLean();
        
        // Actualizar UI de debug
        UpdateTiltDebugUI();
    }

    void OnGUI()
    {
        if (!(enableTuningOverlay && showTuningOverlay)) return;

        const float w = 320f; const float h = 240f;
        Rect r = new Rect(10, 10, w, h);
        GUI.Box(r, "Tuning Sensibilidad (F1 para ocultar)");
        GUILayout.BeginArea(new Rect(20, 35, w - 20, h - 45));
        GUILayout.Label($"Vuforia gain: {cameraTiltGain:F2}");
        cameraTiltGain = GUILayout.HorizontalSlider(cameraTiltGain, 0.5f, 3.0f);
        GUILayout.Label($"Vuforia umbral: {tiltThreshold:F2}");
        tiltThreshold = GUILayout.HorizontalSlider(tiltThreshold, 0.02f, 0.3f);
        GUILayout.Label($"Cooldown: {laneChangeCooldown:F2} s");
        laneChangeCooldown = GUILayout.HorizontalSlider(laneChangeCooldown, 0.1f, 1.0f);

        GUILayout.Space(6);
        GUILayout.Label("Sensores del teléfono");
        GUILayout.Label($"Tilt gain: {deviceTiltGain:F2}");
        deviceTiltGain = GUILayout.HorizontalSlider(deviceTiltGain, 0.8f, 2.0f);
        GUILayout.Label($"Umbral (deg): {deviceTiltThresholdDeg:F1}");
        deviceTiltThresholdDeg = GUILayout.HorizontalSlider(deviceTiltThresholdDeg, 3f, 20f);

        GUILayout.Space(6);
        if (GUILayout.Button("Recalibrar (cam/sensores)"))
            RecalibrateTilt();
        GUILayout.EndArea();
    }

    void HandleTiltControls()
    {
        if (!ready || !tiltInitialized || vuforiaCamera == null) return;
        
        // Obtener rotación actual de la cámara
        Vector3 currentRotation = vuforiaCamera.transform.rotation.eulerAngles;
        
        // Normalizar ángulos a -180 a 180
        currentTilt.x = NormalizeAngle(currentRotation.x - initialTilt.x);
        currentTilt.y = NormalizeAngle(currentRotation.y - initialTilt.y);
        currentTilt.z = NormalizeAngle(currentRotation.z - initialTilt.z);
        
        // Suavizar el movimiento
        smoothedTilt = Vector3.Lerp(smoothedTilt, currentTilt, tiltSmoothing * Time.deltaTime);
        
        // Verificar si puede cambiar de carril
        if (Time.time - lastLaneChangeTime >= laneChangeCooldown && !isMoving)
        {
            // Usar la inclinación en Z por defecto (rotación roll del dispositivo)
            float rawTilt = smoothedTilt.z;
            float tiltValue = rawTilt * Mathf.Max(0.1f, cameraTiltGain);

            if (tiltValue > tiltThreshold)
            {
                TryChangeLane(+1);
                lastLaneChangeTime = Time.time;
            }
            else if (tiltValue < -tiltThreshold)
            {
                TryChangeLane(-1);
                lastLaneChangeTime = Time.time;
            }
        }
        // Guardar para inclinación visual
        currentLeanAngle = Mathf.Clamp(smoothedTilt.z * cameraTiltGain, -1f, 1f) * maxLeanAngle;
    }

    void HandleDeviceTiltControls()
    {
        if (!ready) return;
        float tiltDeg = GetDeviceTiltDegrees();
        // suavizado independiente (usa tiltSmoothing como constante de tiempo aproximada)
        deviceSmoothedTiltDeg = Mathf.Lerp(deviceSmoothedTiltDeg, tiltDeg * Mathf.Max(0.1f, deviceTiltGain), tiltSmoothing * Time.deltaTime);

        if (Time.time - lastLaneChangeTime >= laneChangeCooldown && !isMoving)
        {
            int dir = 0;
            if (deviceSmoothedTiltDeg > deviceTiltThresholdDeg) dir = invertDeviceTilt ? -1 : +1;
            else if (deviceSmoothedTiltDeg < -deviceTiltThresholdDeg) dir = invertDeviceTilt ? +1 : -1;
            if (dir != 0)
            {
                TryChangeLane(dir);
                lastLaneChangeTime = Time.time;
            }
        }
        // Guardar para inclinación visual
        currentLeanAngle = Mathf.Clamp(deviceSmoothedTiltDeg / deviceTiltThresholdDeg, -1.5f, 1.5f);
        currentLeanAngle = Mathf.Clamp(currentLeanAngle, -1f, 1f) * maxLeanAngle;
    }

    float GetDeviceTiltDegrees()
    {
        // Preferir acelerómetro (más estable), con fallback a gyro si es débil
        float deg = 0f;
        if (accelSupported)
        {
            Vector3 a = Input.acceleration;
            float h;
            switch (Input.deviceOrientation)
            {
                case DeviceOrientation.Portrait: h = a.x; break;
                case DeviceOrientation.PortraitUpsideDown: h = -a.x; break;
                case DeviceOrientation.LandscapeLeft: h = a.y; break;
                case DeviceOrientation.LandscapeRight: h = -a.y; break;
                default: h = Mathf.Abs(a.x) > Mathf.Abs(a.y) ? a.x : a.y; break;
            }
            float x = h - (accelCalibrated ? accelZero : 0f);
            x = Mathf.Clamp(x, -1f, 1f);
            deg = Mathf.Asin(x) * Mathf.Rad2Deg;
        }
        if (gyroSupported && Mathf.Abs(deg) < 1.0f)
        {
            var q = Input.gyro.attitude; q = new Quaternion(q.x, q.y, -q.z, -q.w);
            float z = q.eulerAngles.z; if (z > 180f) z -= 360f;
            deg = z;
        }
        return deg;
    }

    void ApplyVisualLean()
    {
        if (!leanVisual) leanVisual = transform;
        if (!leanDuringSimulation && enableEditorSimulation && Application.isEditor) return;
        // Interpolar hacia el ángulo deseado alrededor del eje forward (Z rot en local X? Depende del modelo; usar Z en euler local Y? Ajustamos roll en local Z).
        // Suponemos que el modelo mira hacia adelante en +Z o +X; aplicamos rotación roll sobre eje local forward.
        // Para simplicidad ajustaremos rotación local sobre eje Z (estándar para roll).
        Quaternion target = Quaternion.Euler(leanVisual.localEulerAngles.x, leanVisual.localEulerAngles.y, -currentLeanAngle);
        leanVisual.localRotation = Quaternion.Slerp(leanVisual.localRotation, target, leanSmooth * Time.deltaTime);
    }

#if ENABLE_INPUT_SYSTEM
    bool IsPressedThisFrame(Keyboard keyboard, KeyCode code)
    {
        switch (code)
        {
            case KeyCode.LeftArrow: return keyboard.leftArrowKey.wasPressedThisFrame;
            case KeyCode.RightArrow: return keyboard.rightArrowKey.wasPressedThisFrame;
            case KeyCode.UpArrow: return keyboard.upArrowKey.wasPressedThisFrame;
            case KeyCode.DownArrow: return keyboard.downArrowKey.wasPressedThisFrame;
            case KeyCode.A: return keyboard.aKey.wasPressedThisFrame;
            case KeyCode.D: return keyboard.dKey.wasPressedThisFrame;
            case KeyCode.W: return keyboard.wKey.wasPressedThisFrame;
            case KeyCode.S: return keyboard.sKey.wasPressedThisFrame;
            case KeyCode.Q: return keyboard.qKey.wasPressedThisFrame;
            case KeyCode.E: return keyboard.eKey.wasPressedThisFrame;
            case KeyCode.F1: return keyboard[Key.F1].wasPressedThisFrame;
            default:
                // Fallback: intentar convertir enum por nombre (limitado)
                return false;
        }
    }
#endif

    float NormalizeAngle(float angle)
    {
        // Normalizar ángulo a rango -180 a 180
        angle = angle % 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle / 180f; // Normalizar a -1 a 1
    }

    void UpdateTiltDebugUI()
    {
        if (tiltDebugText != null && showDebugInfo)
        {
            tiltDebugText.text = $"Inclinación:\n" +
                               $"X: {smoothedTilt.x:F2}\n" +
                               $"Y: {smoothedTilt.y:F2}\n" +
                               $"Z: {smoothedTilt.z:F2}\n" +
                               $"Carril: {currentLane}\n" +
                               $"Vueltas: {lapCount}";
        }
    }

    public void MoveLeft() => TryChangeLane(-1);
    public void MoveRight() => TryChangeLane(+1);

    void TryChangeLane(int direction)
    {
        if (!ready || isMoving || laneFollower?.road == null) return;

        if (requireAcceleratingForLaneChange && !laneFollower.IsAccelerating)
            return;

        int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
        int newLane = Mathf.Clamp(currentLane + direction, 0, lanes - 1);
        if (newLane == currentLane) return;

        currentLane = newLane;
        laneFollower.laneIndex = currentLane;

        PlayLaneAnim(direction < 0 ? animLeft : animRight);

        float lockTime = Mathf.Max(0.05f, laneFollower.laneChangeTime);
        StartCoroutine(LaneChangeCooldown(lockTime));

        if (returnToIdleAfterLaneChange)
            StartCoroutine(ReturnToIdleAfter(lockTime));
    }

    IEnumerator LaneChangeCooldown(float duration)
    {
        isMoving = true;
        yield return new WaitForSeconds(duration);
        isMoving = false;
    }

    IEnumerator ReturnToIdleAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        SafeCrossFade(animIdle);
    }

    void PlayLaneAnim(string stateName)
    {
        if (!animator) return;

        if (useAnimatorTriggers)
            TriggerAnim(stateName);
        else
        {
            if (useCrossFade) SafeCrossFade(stateName);
            else SafePlay(stateName);
        }

        animator.Update(0f);
    }

    void TriggerAnim(string stateName)
    {
        animator.ResetTrigger(leftTriggerName);
        animator.ResetTrigger(rightTriggerName);

        if (stateName == animLeft) animator.SetTrigger(leftTriggerName);
        else if (stateName == animRight) animator.SetTrigger(rightTriggerName);
    }

    void SafePlay(string stateName)
    {
        if (!string.IsNullOrEmpty(stateName))
            animator.Play(stateName, 0, 0f);
    }

    void SafeCrossFade(string stateName)
    {
        if (!string.IsNullOrEmpty(stateName))
            animator.CrossFadeInFixedTime(stateName, crossFadeDuration);
    }

    public void AccelerateOn() => laneFollower?.AccelerateOn();
    public void AccelerateOff() => laneFollower?.AccelerateOff();
    
    // Método para recalibrar la inclinación en tiempo de ejecución
    public void RecalibrateTilt()
    {
        if (vuforiaCamera != null)
        {
            initialTilt = vuforiaCamera.transform.rotation.eulerAngles;
            smoothedTilt = Vector3.zero;
            Debug.Log("Inclinación recalibrada: " + initialTilt);
        }
        if (useDeviceMotion && accelSupported)
        {
            StartCoroutine(CalibrateAccelerometer());
        }
    }

    IEnumerator CalibrateAccelerometer()
    {
        float t = 0f; float sum = 0f; int n = 0;
        while (t < 0.25f)
        {
            Vector3 a = Input.acceleration;
            float h;
            switch (Input.deviceOrientation)
            {
                case DeviceOrientation.Portrait: h = a.x; break;
                case DeviceOrientation.PortraitUpsideDown: h = -a.x; break;
                case DeviceOrientation.LandscapeLeft: h = a.y; break;
                case DeviceOrientation.LandscapeRight: h = -a.y; break;
                default: h = Mathf.Abs(a.x) > Mathf.Abs(a.y) ? a.x : a.y; break;
            }
            sum += h; n++; t += Time.deltaTime; yield return null;
        }
        accelZero = (n > 0) ? sum / n : 0f;
        accelCalibrated = true;
        Debug.Log($"Acelerómetro calibrado. Zero={accelZero:F3}");
    }
}