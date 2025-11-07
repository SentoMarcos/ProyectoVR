using UnityEngine;

/// <summary>
/// Hace que un objeto recorra la carretera generada por RoadFromTargetsSticky, con selección de carril.
///</summary>
[RequireComponent(typeof(Transform))]
public class RoadLaneFollower : MonoBehaviour
{
    [Header("Update")]
    [Tooltip("Aplica el seguimiento en LateUpdate para ejecutarse después del Animator (evita que la animación sobrescriba la posición/rotación)")]
    public bool applyInLateUpdate = true;
    [Header("Referencia")]
    public RoadFromTargetsSticky road;
    [Tooltip("Transform que se moverá/rotará. Si está vacío, se usa este mismo transform")]
    public Transform motionTarget;

    [Header("Movimiento")]
    [Tooltip("Velocidad a lo largo de la carretera (m/s)")]
    public float speed = 0.5f;
    [Tooltip("Comienza a esta distancia a lo largo del camino (m)")]
    public float startDistance = 0f;
    [Tooltip("Si está activo y el camino es cerrado, se repite en bucle")]
    public bool loopOnClosed = true;
    [Tooltip("Si está activo, SOLO avanza cuando AccelerateOn() esté activo")]
    public bool requireAccelerate = true;

    [Header("Carriles")]
    [Tooltip("Índice de carril (0..laneCount-1). 0 = borde izq, laneCount-1 = borde dcha, centro = medio.")]
    public int laneIndex = 0;
    [Tooltip("Suavizado del cambio de carril (segundos para alcanzar el offset deseado)")]
    public float laneChangeTime = 0.3f;

    [Header("Orientación")]
    [Tooltip("Alinear el objeto con la dirección del camino (forward) y up del plano")]
    public bool alignToPath = true;
    [Tooltip("Offset vertical adicional (m) para evitar z-fighting o hundirse en la carretera")]
    public float verticalOffset = 0.01f;
    [Tooltip("Mantener el localScale del objeto igual al inicial (evita escalados indeseados por animaciones)")]
    public bool lockScaleToInitial = true;

    [Header("Colocación sobre la carretera")]
    [Tooltip("Distancia desde el pivote del modelo hasta el punto de contacto con el suelo (m). Añade este valor hacia 'up' para apoyar las ruedas.")]
    public float pivotToGround = 0f;
    [Tooltip("Offset extra por si quieres levantarlo más de la carretera (m)")]
    public float extraSurfaceOffset = 0f;

    [Header("Ajuste de orientación del modelo")]
    [Tooltip("Aplica un offset de Euler a la orientación siguiendo el camino, para corregir modelos importados (por ejemplo X=-90)")]
    public bool useOrientationOffset = true;
    public Vector3 orientationOffsetEuler = Vector3.zero;

    [Tooltip("Mapea qué ejes locales del modelo representan su Forward y Up. Útil si el FBX no usa Z+ y Y+ como en Unity.")]
    public bool useAxisMapping = true;
    public enum Axis { X, Y, Z, NegativeX, NegativeY, NegativeZ }
    [Tooltip("Eje local del modelo que apunta hacia delante (forward)")]
    public Axis modelForwardAxis = Axis.Z;
    [Tooltip("Eje local del modelo que apunta hacia arriba (up)")]
    public Axis modelUpAxis = Axis.Y;

    [Header("Debug")]
    [Tooltip("Si > 0, usa este ancho total en metros para el cálculo lateral (ignora el de la carretera)")]
    public float debugOverrideTotalWidth = 0f;
    [Tooltip("Imprime logs periódicos con el estado del seguidor (PathReady, Len, S, LaneIndex, Width)")]
    public bool debugLogs = false;
    [Tooltip("Intervalo mínimo entre logs de debug (segundos)")]
    public float logInterval = 0.5f;

    // Estado interno
    float currentS;              // distancia acumulada actual
    float targetLaneT;           // objetivo lateral en [-0.5, 0.5]
    float currentLaneT;          // valor suavizado actual en [-0.5, 0.5]
    bool accelerating;           // gate de aceleración
    float logElapsed;
    Vector3 initialLocalScale;

    void Start()
    {
        currentS = Mathf.Max(0f, startDistance);
        RecomputeLaneT();
        currentLaneT = targetLaneT;
        var t = motionTarget ? motionTarget : transform;
        initialLocalScale = t.localScale;
    }

    void Update()
    {
        if (!applyInLateUpdate)
            Tick();
    }

    void LateUpdate()
    {
        if (applyInLateUpdate)
            Tick();
    }

    void Tick()
    {
        if (!road || !road.PathReady) return;

    float dt = Mathf.Max(0f, GameTime.DeltaTime);
        // Avanza
        float effSpeed = (requireAccelerate && !accelerating) ? 0f : Mathf.Max(0f, speed);
        currentS += effSpeed * dt;
        float len = road.PathLength;
        if (len > 1e-5f)
        {
            if (road.IsClosedPath && loopOnClosed) currentS = Mathf.Repeat(currentS, len);
            else currentS = Mathf.Clamp(currentS, 0f, len);
        }

        // Suaviza cambio de carril (alcanza EXACTO en laneChangeTime)
        RecomputeLaneT();
        if (laneChangeTime > 1e-4f)
        {
            float delta = Mathf.Abs(targetLaneT - currentLaneT);
            float step = (delta / laneChangeTime) * dt; // cubrir el delta exactamente en laneChangeTime
            currentLaneT = Mathf.MoveTowards(currentLaneT, targetLaneT, step);
        }
        else currentLaneT = targetLaneT;

        // Muestra camino
        bool sampleOk = road.SampleAtDistance(currentS, out var pos, out var tan);
        if (sampleOk)
        {
            Vector3 up = road.PathUp;
            // Usa convención Unity: right = cross(up, forward)
            Vector3 right = Vector3.Cross(up, tan.normalized).normalized;
            float totalW = (debugOverrideTotalWidth > 1e-6f) ? debugOverrideTotalWidth : road.TotalWidthPublic;
            float half = totalW * 0.5f;
            Vector3 lateral = right * (currentLaneT * 2f * half);
            float upOffset = verticalOffset + pivotToGround + extraSurfaceOffset;
            Vector3 finalPos = pos + lateral + up * upOffset;
            var t = motionTarget ? motionTarget : transform;
            t.position = finalPos;

            if (alignToPath && tan.sqrMagnitude > 1e-8f)
            {
                Quaternion rot = ComputeAlignedRotation(tan, up);
                t.rotation = rot;
            }

            if (lockScaleToInitial && t.localScale != initialLocalScale)
            {
                t.localScale = initialLocalScale;
            }
        }

        if (debugLogs)
        {
            logElapsed += Mathf.Max(0f, dt);
            if (logElapsed >= Mathf.Max(0.1f, logInterval))
            {
                logElapsed = 0f;
                float pathLen = road.PathLength;
                float totalW = (debugOverrideTotalWidth > 1e-6f) ? debugOverrideTotalWidth : road.TotalWidthPublic;
                Debug.Log($"[RoadLaneFollower] PathReady={road.PathReady} Closed={road.IsClosedPath} Len={pathLen:F2} S={currentS:F2} LaneIndex={laneIndex} LaneT={currentLaneT:F2} Width={totalW:F2} sampleOk={sampleOk}", this);
            }
        }
    }

    Quaternion ComputeAlignedRotation(Vector3 forwardW, Vector3 upW)
    {
        forwardW = forwardW.normalized;
        upW = upW.normalized;
        Quaternion worldRot = Quaternion.LookRotation(forwardW, upW);

        if (useAxisMapping)
        {
            Vector3 mf = AxisToVector(modelForwardAxis);
            Vector3 mu = AxisToVector(modelUpAxis);
            // Rotación que llevaría (mf, mu) a (Z+, Y+) en espacio local
            Quaternion modelBasis = Quaternion.LookRotation(mf, mu);
            worldRot = worldRot * Quaternion.Inverse(modelBasis);
        }

        if (useOrientationOffset)
        {
            worldRot = worldRot * Quaternion.Euler(orientationOffsetEuler);
        }

        return worldRot;
    }

    static Vector3 AxisToVector(Axis a)
    {
        switch (a)
        {
            case Axis.X: return Vector3.right;
            case Axis.Y: return Vector3.up;
            case Axis.Z: return Vector3.forward;
            case Axis.NegativeX: return Vector3.left;
            case Axis.NegativeY: return Vector3.down;
            case Axis.NegativeZ: return Vector3.back;
            default: return Vector3.forward;
        }
    }

    void OnValidate()
    {
        RecomputeLaneT();
    }

    void RecomputeLaneT()
    {
        int lanes = (road ? Mathf.Max(1, road.LaneCountPublic) : 1);
        laneIndex = Mathf.Clamp(laneIndex, 0, lanes - 1);
        // Mapea índice de carril a [-0.5, 0.5]
        bool canUseRoadMap = road && road.PathReady && road.TotalWidthPublic > 1e-4f;
        if (lanes == 1) targetLaneT = 0f;
        else targetLaneT = canUseRoadMap ? road.LaneIndexToTRel(laneIndex)
                                          : (laneIndex / (float)(lanes - 1)) - 0.5f;
    }

    // API pública para el botón de acelerar
    public void SetAccelerating(bool value) => accelerating = value;
    public void AccelerateOn() => accelerating = true;
    public void AccelerateOff() => accelerating = false;
    public bool IsAccelerating => accelerating;
    public float CurrentS => currentS;

}
