using UnityEngine;

/// <summary>
/// Hace que un objeto recorra la carretera generada por RoadFromTargetsSticky, con selección de carril.
///</summary>
[RequireComponent(typeof(Transform))]
public class RoadLaneFollower : MonoBehaviour
{
    [Header("Referencia")]
    public RoadFromTargetsSticky road;

    [Header("Movimiento")]
    [Tooltip("Velocidad a lo largo de la carretera (m/s)")]
    public float speed = 0.5f;
    [Tooltip("Comienza a esta distancia a lo largo del camino (m)")]
    public float startDistance = 0f;
    [Tooltip("Si está activo y el camino es cerrado, se repite en bucle")]
    public bool loopOnClosed = true;

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

    // Estado interno
    float currentS;              // distancia acumulada actual
    float targetLaneT;           // objetivo lateral en [-0.5, 0.5]
    float currentLaneT;          // valor suavizado actual en [-0.5, 0.5]

    void Start()
    {
        currentS = Mathf.Max(0f, startDistance);
        RecomputeLaneT();
        currentLaneT = targetLaneT;
    }

    void Update()
    {
        if (!road || !road.PathReady) return;

        float dt = Mathf.Max(0f, Time.deltaTime);
        // Avanza
        currentS += speed * dt;
        float len = road.PathLength;
        if (len > 1e-5f)
        {
            if (road.IsClosedPath && loopOnClosed) currentS = Mathf.Repeat(currentS, len);
            else currentS = Mathf.Clamp(currentS, 0f, len);
        }

        // Suaviza cambio de carril
        RecomputeLaneT();
        if (laneChangeTime > 1e-4f)
        {
            float k = Mathf.Clamp01(dt / laneChangeTime);
            currentLaneT = Mathf.Lerp(currentLaneT, targetLaneT, k);
        }
        else currentLaneT = targetLaneT;

        // Muestra camino
        if (road.SampleAtDistance(currentS, out var pos, out var tan))
        {
            Vector3 up = road.PathUp;
            Vector3 right = Vector3.Cross(tan.normalized, up).normalized;
            float half = road.TotalWidthPublic * 0.5f;
            Vector3 lateral = right * (currentLaneT * 2f * half);
            Vector3 finalPos = pos + lateral + up * verticalOffset;
            transform.position = finalPos;

            if (alignToPath && tan.sqrMagnitude > 1e-8f)
            {
                Quaternion rot = Quaternion.LookRotation(tan, up);
                transform.rotation = rot;
            }
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
        // Mapea índice de carril a [-0.5, 0.5] uniformemente
        if (lanes == 1) targetLaneT = 0f;
        else targetLaneT = (laneIndex / (float)(lanes - 1)) - 0.5f;
    }
}
