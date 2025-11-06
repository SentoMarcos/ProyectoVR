using UnityEngine;

/// <summary>
/// Agente de tráfico sencillo que reutiliza RoadLaneFollower para moverse por la carretera
/// añadiendo lógica de velocidad adaptativa y cambio de carril seguro para no bloquear a la moto.
/// </summary>
[RequireComponent(typeof(RoadLaneFollower))]
public class TrafficAgent : MonoBehaviour
{
    [Header("Base")]
    public RoadLaneFollower follower; // asignado en Awake
    public RoadFromTargetsSticky road; // copia de referencia para comodidad

    [Header("Velocidad dinámica")]
    [Tooltip("Velocidad deseada libre (m/s)")] public float desiredSpeed = 1.2f;
    [Tooltip("Distancia mínima de seguridad con el coche delante (m)")] public float minGap = 0.8f;
    [Tooltip("Factor de frenado cuando se invade el gap (0..1)")] [Range(0f,1f)] public float brakeFactor = 0.6f;
    [Tooltip("Suavizado de ajuste de velocidad (segundos)")] public float speedLerpTime = 0.5f;
    [Tooltip("Gap mínimo con la moto; por debajo de esto se aplica frenado fuerte")] public float motoMinGap = 1.0f;
    [Tooltip("Umbral de tiempo a colisión para aplicar frenada fuerte (s)")] public float ttcBrakeThreshold = 1.5f;
    [Tooltip("Factor de frenado fuerte cuando TTC es bajo")] [Range(0f,1f)] public float hardBrakeFactor = 0.2f;
    [Tooltip("Si el gap es menor que esto, casi detenerse (m)")] public float minStopGap = 0.35f;

    [Header("Cambio de carril")]
    [Tooltip("Habilitar cambio de carril automático si bloquea a la moto")] public bool autoLaneChange = true;
    [Tooltip("Tiempo mínimo entre intentos de cambio de carril (s)")] public float laneChangeCooldown = 3f;
    [Tooltip("Distancia que considera a la moto cercana (m)")] public float motoBehindDistance = 2f;
    [Tooltip("Tiempo de anticipación para predecir posiciones de moto y agente (s)")] public float lookaheadTimeForMoto = 0.8f;
    [Tooltip("Distancia adicional para iniciar el cambio antes (m)")] public float earlyLaneChangeDistance = 1.2f;
    [Tooltip("Gap longitudinal mínimo libre en el carril destino (m)")] public float minPredictedGap = 0.9f;
    [Tooltip("Margen lateral para considerar colisión con la moto (m)")] public float sideClearWidth = 0.4f;
    [Tooltip("Zona hacia delante a comprobar libre en carril destino (m)")] public float laneSegmentForwardClear = 1.2f;
    [Tooltip("Zona hacia detrás a comprobar libre en carril destino (m)")] public float laneSegmentBehindClear = 0.6f;
    [Tooltip("Probabilidad por segundo de iniciar un cambio de carril aleatorio si es seguro")] [Range(0f,1f)] public float randomLaneChangeRate = 0.1f;
    [Tooltip("Si la moto está delante y cerca en este carril, intenta cambiar si es seguro (m)")] public float motoAheadDistanceChange = 2.0f;

    [Header("Adelantamientos")]
    [Tooltip("Habilitar adelantamientos si el coche frontal es más lento")] public bool allowOvertake = true;
    [Tooltip("Distancia a la que se plantea adelantar si nuestra velocidad es mayor (m)")] public float overtakeConsiderDistance = 1.2f;
    [Tooltip("Ventana libre mínima en el carril destino para adelantar (m delante / m detrás)")] public Vector2 overtakeClearWindow = new Vector2(1.6f, 0.8f);
    [Tooltip("Aumento temporal de velocidad al adelantar (m/s)")] public float overtakeBoost = 0.3f;

    [Header("Reserva de carril para la moto")]
    [Tooltip("Índice de carril reservado exclusivamente para la moto (no se usa para coches)")] public int reservedLaneIndex = 0;

    [Header("Debug")]
    public bool debugDraw = false;

    float _currentSpeed;
    float _laneChangeCooldownTimer;

    public TrafficManager manager;

    void Awake()
    {
        follower = GetComponent<RoadLaneFollower>();
        if (!road) road = follower.road;
        _currentSpeed = desiredSpeed;
    }

    void OnEnable()
    {
        if (follower) follower.requireAccelerate = false; // que siempre avance
    }

    void Update()
    {
        if (!road || !road.PathReady || !follower) return;
        AdjustSpeedByGap();
        if (autoLaneChange)
        {
            MaybeLaneChange();
            if (allowOvertake) MaybeOvertake();
            MaybeRandomLaneChange();
        }
        follower.speed = _currentSpeed;
    }

    void AdjustSpeedByGap()
    {
        float target = desiredSpeed;

        // 1) coche delante
        var front = manager ? manager.FindFrontAgent(this) : null;
        if (front != null)
        {
            float gap = front.follower.CurrentS - follower.CurrentS;
            if (road.IsClosedPath && gap < 0f) gap += road.PathLength;
            if (gap < minGap)
            {
                target = Mathf.Min(target, desiredSpeed * (1f - brakeFactor));
            }
        }

        // 2) moto delante en mismo carril (seguridad más estricta)
        var moto = manager ? manager.motoFollower : null;
        if (moto && moto.laneIndex == follower.laneIndex)
        {
            float gapM = moto.CurrentS - follower.CurrentS;
            if (road.IsClosedPath && gapM < 0f) gapM += road.PathLength;
            // estimar TTC
            float relV = Mathf.Max(0f, _currentSpeed - Mathf.Max(0f, moto.speed));
            if (gapM < minStopGap)
            {
                target = Mathf.Min(target, desiredSpeed * hardBrakeFactor);
            }
            else if (gapM < motoMinGap)
            {
                target = Mathf.Min(target, desiredSpeed * (1f - brakeFactor));
            }
            if (relV > 1e-3f)
            {
                float ttc = gapM / relV;
                if (ttc < ttcBrakeThreshold)
                {
                    target = Mathf.Min(target, desiredSpeed * hardBrakeFactor);
                }
            }
        }

        // aplicar suavizado
        float tLerp = Time.deltaTime / Mathf.Max(0.01f, speedLerpTime);
        _currentSpeed = Mathf.Lerp(_currentSpeed, Mathf.Max(0.05f, target), tLerp);
    }

    void MaybeLaneChange()
    {
        if (_laneChangeCooldownTimer > 0f)
        {
            _laneChangeCooldownTimer -= Time.deltaTime;
            return;
        }
        // Si el carril reservado es el mismo que uso, intenta moverte para dejarlo libre
        if (follower.laneIndex == reservedLaneIndex)
        {
            TryMoveToBestFreeLane();
            return;
        }
        // Si la moto está detrás en este carril cerca -> muévete
        var moto = manager ? manager.motoFollower : null;
        if (moto && moto.laneIndex == follower.laneIndex)
        {
            float behind = follower.CurrentS - moto.CurrentS;
            if (road.IsClosedPath && behind < 0f) behind += road.PathLength;
            // predicción: si en lookahead la moto nos alcanza, inicia antes
            float mySFuture = follower.CurrentS + Mathf.Max(0f, _currentSpeed) * lookaheadTimeForMoto;
            float motoSpeed = Mathf.Max(0f, manager ? manager.motoFollower.speed : 0f);
            float motoSFuture = moto.CurrentS + motoSpeed * lookaheadTimeForMoto;
            float gapNow = behind;
            float gapFuture = mySFuture - motoSFuture; if (road.IsClosedPath && gapFuture < 0f) gapFuture += road.PathLength;
            bool catchingUpSoon = gapFuture < minPredictedGap;
            if ((behind < motoBehindDistance && behind > 0f) || catchingUpSoon)
            {
                TryMoveToBestFreeLane();
            }
            // Si la moto está delante y cerca, también intenta cambiar proactivamente
            float ahead = moto.CurrentS - follower.CurrentS; if (road.IsClosedPath && ahead < 0f) ahead += road.PathLength;
            if (ahead > 0f && ahead < motoAheadDistanceChange)
            {
                TryMoveToBestFreeLane();
            }
        }
    }

    void MaybeOvertake()
    {
        if (_laneChangeCooldownTimer > 0f) return;
        if (!road || manager == null) return;
        var front = manager.FindFrontAgent(this);
        if (front == null) return;
        float gap = front.follower.CurrentS - follower.CurrentS;
        if (road.IsClosedPath && gap < 0f) gap += road.PathLength;
        if (gap > overtakeConsiderDistance) return;
        // Sólo si somos más rápidos (deseada > la del frontal)
        if (desiredSpeed <= front.desiredSpeed && _currentSpeed <= front._currentSpeed + 1e-3f) return;

        int lanes = road.LaneCountPublic;
        // Preferir el carril opuesto al reservado de la moto para dejarlo libre
        int[] candidates = new int[] { follower.laneIndex + 1, follower.laneIndex - 1 };
        foreach (var c in candidates)
        {
            if (c < 0 || c >= lanes) continue;
            if (!manager.IsLaneSegmentClear(c, follower.CurrentS + earlyLaneChangeDistance, overtakeClearWindow.x, overtakeClearWindow.y, this))
                continue;
            follower.laneIndex = c;
            _laneChangeCooldownTimer = laneChangeCooldown;
            // pequeño boost para completar el adelantamiento
            _currentSpeed = Mathf.Max(_currentSpeed, desiredSpeed + overtakeBoost);
            break;
        }
    }

    void TryMoveToBestFreeLane()
    {
        if (!road) return;
        int lanes = road.LaneCountPublic;
        int best = -1;
        float bestScore = -1f;
        for (int i = 0; i < lanes; i++)
        {
            if (i == reservedLaneIndex) continue; // deja libre el reservado
            // Comprueba un segmento libre delante y detrás para no cortar a nadie
            if (!manager.IsLaneSegmentClear(i, follower.CurrentS + earlyLaneChangeDistance, laneSegmentForwardClear, laneSegmentBehindClear, this))
                continue;
            // score simple: distancia lateral al carril reservado (para apartarse más) + aleatoriedad
            float distScore = Mathf.Abs(i - reservedLaneIndex);
            float score = distScore + Random.value * 0.25f;
            if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0 && best != follower.laneIndex)
        {
            follower.laneIndex = best;
            _laneChangeCooldownTimer = laneChangeCooldown;
        }
    }

    void MaybeRandomLaneChange()
    {
        if (_laneChangeCooldownTimer > 0f) return;
        if (!road || manager == null) return;
        // evento de Poisson: probabilidad por segundo
        if (Random.value > randomLaneChangeRate * Time.deltaTime) return;
        int lanes = road.LaneCountPublic;
        if (lanes <= 1) return;
        // intenta moverte a cualquiera de los dos vecinos si está libre
        int[] candidates = new int[] { follower.laneIndex - 1, follower.laneIndex + 1 };
        System.Array.Sort(candidates, (a,b) => Random.value < 0.5f ? -1 : 1); // aleatorio
        foreach (var c in candidates)
        {
            if (c < 0 || c >= lanes) continue;
            if (!manager.IsLaneSegmentClear(c, follower.CurrentS + earlyLaneChangeDistance, laneSegmentForwardClear, laneSegmentBehindClear, this))
                continue;
            follower.laneIndex = c;
            _laneChangeCooldownTimer = laneChangeCooldown;
            break;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!debugDraw || !follower || !road) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.05f);
    }
}
