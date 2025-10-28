using System.Collections.Generic;
using UnityEngine;

/**
 * @file RoadFromTargets.cs
 * @brief Genera una malla de "carretera" que conecta una secuencia de objetivos (_tgt_point) detectados en AR.
 *
 * El sistema construye una línea central muestreada (Catmull–Rom centrípeta) a partir de los puntos detectados
 * y extruye una banda con un ancho configurable alrededor de dicha línea, generando la malla (vértices/UVs/triángulos).
 * Puede operar directamente con los puntos reales o mediante "proxies" suavizados/estables.
 *
 * Principales fórmulas utilizadas (notación ASCII):
 * - Proyección de un punto P sobre un plano (n, p0):  P' = P - n * dot(P - p0, n)
 * - Producto vectorial (normal de dos vectores):      n = cross(a, b)
 * - Catmull–Rom centrípeta (parámetro ti):            ti = t(i-1) + ||pi - p(i-1)||^alpha   (alpha = 0.5)
 * - Interpolación lineal:                             LERP(a,b,t) = (1 - t) * a + t * b  (Slerp para cuaterniones)
 * - Cálculo de ancho:                                 W = max(minTotal, min(limitSeg, perLane * laneCount))
 *   - en FitToSpacing:  perLane = frac * meanSegmentDistance
 *   - en AbsoluteMeters: perLane = absMeters
 */

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RoadFromTargetsSticky : MonoBehaviour
{
    // -------------------- Inspector --------------------
    [Header("Raíz de targets en la escena")]
    /// <summary>Transform raíz que contiene hijos del tipo TargetX con un hijo <c>_tgt_point</c>.</summary>
    public Transform targetsRoot;                 // Arrastra el objeto "Targets" aquí
    /// <summary>Nombre del hijo buscado dentro de cada TargetX.</summary>
    public string pointChildName = "_tgt_point";  // Hijo dentro de cada TargetX

    [Header("Actualización")]
    /// <summary>Si es true, se regenera cada frame; si es false, sólo en Start() o cuando lo invoques manualmente.</summary>
    public bool updateEveryFrame = true;
    /// <summary>Número de muestras por tramo Catmull-Rom. Controla densidad de vértices.</summary>
    [Range(4, 64)] public int samplesPerSegment = 24;
    [Tooltip("Usar directamente los _tgt_point de 'Targets' sin lógica de estabilidad/visibilidad.")]
    public bool useRealPointsDirectly = true;
    [Tooltip("Si se asigna, usará el orden de detección proporcionado por este manager.")]
    public DetectionOrderManager detectionOrderManager;
    [Tooltip("Usar puntos CONGELADOS del manager (estables en mundo) en lugar de los _tgt_point vivos.")]
    public bool useFrozenPointsFromManager = true;
    [Tooltip("Regenerar la carretera inmediatamente cuando cambie el orden en el manager.")]
    public bool regenerateOnOrderChanged = true;
    [Tooltip("Si hay ≥3 puntos, conecta el último con el primero (circuito cerrado). El nuevo detectado pasa a ser el último.")]
    public bool closeLoopWhenAtLeast3 = true;

    public enum ConnectMode
    {
        // Comportamiento actual: conecta todos los puntos en orden (y opcionalmente cierra el bucle)
        Sequential,
        // Conecta solo el primero detectado con el último (2 puntos)
        FirstToLastOnly,
        // Conecta únicamente los dos últimos detectados (cuando llega uno nuevo, pasa a unirse con el anterior último)
        LastTwoOnly,
    }
    [Header("Modo de conexión entre puntos")]
    [Tooltip("Cómo se conectan los puntos: Sequential (todos), FirstToLastOnly (solo primero-último), LastTwoOnly (solo los dos últimos).")]
    public ConnectMode connectMode = ConnectMode.LastTwoOnly;

    [Header("Plano y UVs")]
    /// <summary>Proyecta la línea central sobre un plano estimado para evitar desniveles no deseados.</summary>
    public bool flattenToTargetsPlane = true;
    /// <summary>Bloquea la normal del plano tras obtener suficientes puntos estables (reduce jitter).</summary>
    public bool lockPlaneAfterTwoStable = true;
    /// <summary>Escala UV a lo largo del eje longitudinal en tiles por metro.</summary>
    public float uvTilesPerMeter = 0.15f;
    [Tooltip("Ignora las rotaciones/orientaciones locales de los _tgt_point al calcular la normal del plano (usa sólo geometría/snap a vertical).")]
    /// <summary>Si está activo, no se usan las orientaciones de los puntos para derivar la normal del plano.</summary>
    public bool ignoreTargetRotations = false;
    [Tooltip("Usa la orientación de los _tgt_point para definir la normal del plano (mejor con 2 puntos en superficies planas).")]
    /// <summary>Deriva la normal del plano de la orientación local de los puntos (útil en superficies planas).</summary>
    public bool derivePlaneFromPointOrientation = true;
    /// <summary>Ejes candidatos del <c>_tgt_point</c> usados como normal de superficie.</summary>
    public enum OrientationAxis { Up, Forward, Right }
    [Tooltip("Eje local del _tgt_point que se considerará como normal de la superficie.")]
    /// <summary>Eje local del <c>_tgt_point</c> que se considera normal de la superficie.</summary>
    public OrientationAxis planeNormalAxis = OrientationAxis.Up;
    [Tooltip("Elegir automáticamente el eje local más cercano a World Up (Up/Forward/Right) para cada punto.")]
    /// <summary>Elegir automáticamente el eje más alineado con Vector3.up por cada punto.</summary>
    public bool autoChoosePlaneAxis = true;
    [Tooltip("Intenta fijar la normal del plano a World Up cuando sea casi horizontal (mesas, suelos).")]
    /// <summary>Si la normal está cerca del eje vertical mundial, se "encaja" a Vector3.up/Vector3.down.</summary>
    public bool snapNormalToWorldUp = true;
    /// <summary>Ángulo máximo para aplicar el snap a vertical (en grados).</summary>
    [Range(0f, 60f)] public float snapUpMaxAngle = 30f;

    [Header("Altura constante (opcional)")]
    [Tooltip("Fuerza que todos los puntos de entrada estén a la misma altura Y mundial (carretera totalmente plana).")]
    public bool forceTargetsSameHeight = false;
    public enum HeightReferenceMode { FirstPoint, AveragePoints, ThisObjectY, CustomY }
    [Tooltip("Cómo se elige la altura Y de referencia cuando se fuerza altura constante.")]
    public HeightReferenceMode heightReferenceMode = HeightReferenceMode.AveragePoints;
    [Tooltip("Transform de referencia para el modo ThisObjectY (si está vacío, usa este componente).")]
    public Transform heightReferenceTransform;
    [Tooltip("Valor Y absoluto a usar cuando el modo es CustomY.")]
    public float customHeightY = 0f;

    [Header("Carretera (ancho auto en AR)")]
    /// <summary>Número de carriles (multiplicador del ancho por carril).</summary>
    public int laneCount = 1;
    /// <summary>Modo de cálculo del ancho total (absoluto o proporcional al espaciamiento medio).</summary>
    public enum WidthMode { AbsoluteMeters, FitToSpacing }
    public WidthMode widthMode = WidthMode.FitToSpacing;
    /// <summary>Ancho por carril en metros cuando <c>WidthMode=AbsoluteMeters</c>.</summary>
    public float laneWidthMeters = 3f;                 // Si usas metros reales
    /// <summary>Fracción del espaciamiento medio usada como ancho por carril en <c>FitToSpacing</c>.</summary>
    [Range(0.05f, 0.6f)] public float laneWidthFraction = 0.08f;  // % del espaciamiento medio (más fino por defecto)
    /// <summary>Límite relativo del ancho total con respecto al tramo mínimo (evita solapes fuertes).</summary>
    [Range(0.2f, 1.0f)] public float maxWidthVsMinSeg = 0.5f;     // límite relativo vs tramo mínimo (más conservador)
    [Tooltip("Anchura total mínima para asegurar visibilidad (metros).")]
    /// <summary>Ancho total mínimo para asegurar visibilidad.</summary>
    [Range(0.005f, 2f)] public float minTotalWidthMeters = 0.05f;   // más fino por defecto

    [Tooltip("Límite ABSOLUTO del ancho total de la carretera (m). 0 = sin límite.")]
    /// <summary>Tope de seguridad para evitar carreteras excesivamente gordas en AR.</summary>
    [Range(0f, 2f)] public float maxTotalWidthMeters = 0.08f;

    [Header("Ancho adaptativo en curvas")]
    [Tooltip("Reduce el ancho en curvas cerradas para evitar auto-intersecciones.")]
    public bool adaptiveWidthInCurves = true;
    [Tooltip("Factor mínimo de ancho en una curva muy cerrada (0.2..1.0)")]
    [Range(0.2f, 1f)] public float minWidthScaleAtSharpTurn = 0.5f;
    [Tooltip("Ángulo (grados) a partir del cual se alcanza el factor mínimo de ancho.")]
    [Range(5f, 80f)] public float angleForMinWidth = 50f;
    [Tooltip("Ángulo (grados) desde el que empieza a reducirse el ancho.")]
    [Range(0f, 40f)] public float angleStartNarrow = 15f;

    [Header("Curvas y joins")]
    [Tooltip("Refinar muestreo según curvatura y longitud de tramo para curvas suaves.")]
    public bool refineByCurvature = true;
    [Range(1f, 30f)] public float maxCurveAngleDeg = 6f;
    [Tooltip("Longitud máxima deseada entre puntos consecutivos tras refinar (m)")]
    [Range(0.01f, 0.25f)] public float maxSegmentLen = 0.03f;
    [Range(1, 4)] public int maxRefinePasses = 3;
    [Tooltip("Usar joins tipo 'miter' limitados para evitar picos en giros bruscos.")]
    public bool useMiterJoins = false;
    [Tooltip("Límite del factor miter: 1=sin miter extra, 2=duplica a lo sumo, etc.")]
    [Range(1f, 5f)] public float miterLimit = 1.05f;
    [Tooltip("Por encima de este ángulo (deg) se fuerza bevel (evita picos agudos).")]
    [Range(5f, 80f)] public float bevelAtAngleDeg = 18f;
    [Tooltip("Usar joins redondeados (arco) para giros suaves.")]
    public bool useRoundedJoins = true;
    [Tooltip("Muestras del arco por cada 90º de giro (redondeo de joins)")]
    [Range(1, 12)] public int roundSegmentsPer90 = 5;
    [Tooltip("En circuitos cerrados, mueve la 'seam' al vértice de menor curvatura para que sea imperceptible.")]
    public bool rotateSeamToLowestCurvature = true;

    [Header("Pegajosidad / Anti-parpadeo")]
    /// <summary>Frames mínimos visibles para considerar un punto como "estable".</summary>
    public int minVisibleFramesToUpdate = 1;    // 1 = comienza rápido
    /// <summary>Frames mínimos invisibles antes de soltar la pose.</summary>
    public int minInvisibleFramesToHold = 2;    // mantiene pose estable al perderlo
    /// <summary>Distancia máxima permitida al recuperar un punto para evitar saltos bruscos (m).</summary>
    public float maxReacquireJump = 0.15f;      // ignora reapariciones con salto grande (m)
    /// <summary>Factor de suavizado exponencial al actualizar poses estables (0..1).</summary>
    [Range(0f, 1f)] public float updateLerp = 0.35f; // suavizado al actualizar
    /// <summary>Desplazamiento mínimo para aplicar actualización (m).</summary>
    public float minDeltaToUpdate = 0.003f;     // umbral de movimiento (m)

    [Header("Visibilidad")]
    [Tooltip("Levanta la carretera del plano para evitar z-fighting (metros).")]
    /// <summary>Offset en metros para elevar la malla y evitar z-fighting con el feed de cámara.</summary>
    public float surfaceOffset = 0.002f;
    [Tooltip("Si no hay material, se crea uno oscuro y doble cara.")]
    /// <summary>Material utilizado para renderizar la carretera (se crea uno Unlit si no hay).</summary>
    public Material asphaltMaterial;

    [Header("Vuforia (requisito de tracking)")]
    [Tooltip("Si está activo, SOLO se conectan puntos que estén detectados por Vuforia (ignora activeInHierarchy).")]
    public bool requireVuforiaTracking = true;
    [Tooltip("Cuenta el estado DETECTED como válido (además de TRACKED/EXTENDED_TRACKED).")]
    public bool countDetectedAsTracked = true;

    [Header("Debug")]
    /// <summary>Activa logs informativos cuando falten puntos o no se genere la malla.</summary>
    public bool logWhenNoPoints = false;

    [Header("Testing y Gizmos")]
    [Tooltip("Oculta temporalmente la malla de carretera para centrarse en gizmos/depuración.")]
    public bool hideRoadMesh = false;
    [Tooltip("Dibuja la polilínea de puntos usados (en orden) como gizmo.")]
    public bool drawUsedPolylineGizmo = true;
    public Color usedPolylineColor = new Color(0f, 1f, 1f, 0.9f);
    [Tooltip("Dibuja un gizmo específico para el cierre del bucle (último->primero) cuando esté activo.")]
    public bool drawLoopClosureGizmo = true;
    public Color loopClosureColor = new Color(1f, 0.9f, 0f, 0.9f);
    [Tooltip("Si está activo, los gizmos se dibujan aunque el objeto no esté seleccionado.")]
    public bool drawGizmosWhenNotSelected = true;

    [Header("Gizmos de carriles (debug)")]
    [Tooltip("Dibuja las líneas de cada carril a lo largo de la carretera (requiere que el camino esté generado).")]
    public bool drawLaneCenterlinesGizmo = true;
    [Range(1, 10)] public int gizmoLaneSampleStep = 1; // saltar muestras para no saturar
    public Color laneCenterColor = new Color(0.1f, 0.8f, 0.1f, 0.9f);
    [Tooltip("Dibuja los bordes izquierdo/derecho de la carretera.")]
    public bool drawEdgesGizmo = true;
    public Color leftEdgeColor = new Color(0.9f, 0.1f, 0.1f, 0.9f);
    public Color rightEdgeColor = new Color(0.1f, 0.1f, 0.9f, 0.9f);

    // -------------------- Estado --------------------
    /// <summary>Referencia al MeshFilter del objeto.</summary>
    MeshFilter mf;
    /// <summary>Malla generada de la carretera.</summary>
    Mesh mesh;
    /// <summary>Renderer para poder ocultar/mostrar la malla fácilmente.</summary>
    MeshRenderer mr;
    /// <summary>Últimos puntos de control usados (tras aplicar cierre si procede) para dibujar gizmos.</summary>
    readonly List<Transform> lastUsedControlPoints = new();

    // Último camino generado (para seguidores)
    List<Vector3> lastCenterline = new();
    List<float> lastCumulative = new();
    Vector3 lastUpVec = Vector3.up;
    bool lastClosed = false;
    float lastTotalWidth = 0f;

    // Accesores públicos mínimos para seguidores externos
    public bool PathReady => lastCenterline != null && lastCenterline.Count >= 2;
    public float PathLength => (lastCumulative != null && lastCumulative.Count > 0) ? lastCumulative[^1] : 0f;
    public Vector3 PathUp => lastUpVec;
    public bool IsClosedPath => lastClosed;
    public int LaneCountPublic => laneCount;
    public float TotalWidthPublic => lastTotalWidth;

    /// <summary>
    /// Muestra posición y tangente a lo largo del camino por distancia acumulada (m).
    /// </summary>
    public bool SampleAtDistance(float s, out Vector3 pos, out Vector3 tangent)
    {
        pos = Vector3.zero; tangent = Vector3.forward;
        if (!PathReady) return false;
        float len = PathLength;
        if (len <= 1e-5f)
        {
            pos = lastCenterline[0];
            tangent = (lastCenterline[^1] - lastCenterline[0]).normalized;
            return true;
        }
        if (lastClosed) s = Mathf.Repeat(s, len); else s = Mathf.Clamp(s, 0f, len);

        // Búsqueda binaria en lastCumulative
        int lo = 0, hi = lastCumulative.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (lastCumulative[mid] < s) lo = mid + 1; else hi = mid;
        }
        int i1 = Mathf.Clamp(lo, 1, lastCumulative.Count - 1);
        int i0 = i1 - 1;
        float d0 = lastCumulative[i0];
        float d1 = lastCumulative[i1];
        float t = (d1 > d0) ? Mathf.InverseLerp(d0, d1, s) : 0f;
        pos = Vector3.Lerp(lastCenterline[i0], lastCenterline[i1], t);
        tangent = ProjectOnPlaneSafe(lastCenterline[i1] - lastCenterline[i0], lastUpVec).normalized;
        if (tangent.sqrMagnitude < 1e-8f)
        {
            // Fallback usando vecinos si el segmento es degenerado
            int inx = Mathf.Min(i1 + 1, lastCenterline.Count - 1);
            tangent = ProjectOnPlaneSafe(lastCenterline[inx] - lastCenterline[i0], lastUpVec).normalized;
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.forward;
        }
        return true;
    }

    /// <summary>Estado interno de cada target y su proxy estable.</summary>
    class ProxyState
    {
        public Transform realTarget;    // TargetX
        public Transform realPoint;     // TargetX/_tgt_point
        public Transform proxyPoint;    // Clon virtual
        public bool everStable = false;
        public int visibleFrames = 0;
        public int invisibleFrames = 0;
        public Vector3 stablePos;
        public Quaternion stableRot;
        public int orderIndex = int.MaxValue;   // índice en jerarquía
        public string name;
    }

    readonly List<ProxyState> proxies = new();
    Transform virtualRoot;

    // Plano bloqueado
    bool planeLocked = false;
    Vector3 lockedUp = Vector3.up;
    Vector3 lockedPoint = Vector3.zero;

    // -------------------- Ciclo de vida --------------------
    /**
     * @brief Inicializa la malla y el material, y descubre los targets iniciales.
     * @details Crea la malla vacía y, si no hay material, genera uno Unlit doble cara.
     */
    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mesh = new Mesh { name = "RoadMesh" };
        mf.sharedMesh = mesh;

        // Material visible y doble cara si no hay uno
        mr = GetComponent<MeshRenderer>();
        if (mr != null && (mr.sharedMaterial == null || mr.sharedMaterial.shader == null))
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) sh = Shader.Find("Unlit/Color");
            if (!sh) sh = Shader.Find("Standard");
            asphaltMaterial = new Material(sh);
            if (asphaltMaterial.HasProperty("_BaseColor")) asphaltMaterial.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.12f, 1f));
            if (asphaltMaterial.HasProperty("_Color"))     asphaltMaterial.SetColor("_Color",     new Color(0.12f, 0.12f, 0.12f, 1f));
            asphaltMaterial.SetInt("_Cull", 0);
            asphaltMaterial.SetInt("_CullMode", 0);
            mr.sharedMaterial = asphaltMaterial;
        }

        if (!targetsRoot)
        {
            var go = GameObject.Find("Targets");
            if (go) targetsRoot = go.transform;
        }

        // Reutiliza un contenedor existente para proxies si ya existe
        var existingVR = transform.Find("VirtualTargets");
        if (existingVR != null)
        {
            virtualRoot = existingVR;
        }
        else
        {
            var vr = new GameObject("VirtualTargets");
            vr.transform.SetParent(transform, false);
            virtualRoot = vr.transform;
        }

        BuildProxyList(); // inicial
    }

    /// <summary>Regenera una primera vez al empezar.</summary>
    void Start() => Tick();
    void OnEnable()
    {
        if (detectionOrderManager != null && regenerateOnOrderChanged)
            detectionOrderManager.OnOrderChanged += Tick;
    }
    void OnDisable()
    {
        if (detectionOrderManager != null)
            detectionOrderManager.OnOrderChanged -= Tick;
    }
    /// <summary>Si <c>updateEveryFrame</c> es true, regenera cada frame.</summary>
    void Update() { if (updateEveryFrame) Tick(); }

    // -------------------- Bucle principal --------------------
    /**
     * @brief Bucle principal de regeneración.
     * @details Obtiene la lista de puntos (reales o proxies), exige al menos 2 y genera la malla.
     * Condición: si |P| >= 2 => GenerateRoad(P).
     */
    void Tick()
    {
        if (!targetsRoot)
        {
            if (logWhenNoPoints) Debug.LogWarning("[Road] No se encontró 'Targets'.");
            return;
        }

        List<Transform> pts;
        // Si hay manager de orden, probar primero su lista (frozen primero, luego vivos)
        if (detectionOrderManager)
        {
            if (useFrozenPointsFromManager)
            {
                var frozen = detectionOrderManager.GetFrozenPoints();
                if (frozen != null && frozen.Count >= 2)
                {
                    pts = ApplyConnectMode(frozen);
                    RenderOrCache(pts);
                    return;
                }
            }
            var ordered = detectionOrderManager.GetOrderedPoints();
            if (ordered != null && ordered.Count >= 2)
            {
                pts = ApplyConnectMode(ordered);
                RenderOrCache(pts);
                return;
            }
        }
        if (useRealPointsDirectly)
        {
            pts = GetRealProxyPointsOrdered();
            if (pts.Count < 2)
            {
                // Fallback: intenta vía proxies si con reales no hay suficientes
                if (targetsRoot.childCount != proxies.Count) BuildProxyList();
                UpdateProxiesSticky();
                var backup = GetStableProxyPointsOrdered();
                if (backup.Count >= 2) pts = backup;
            }
        }
        else
        {
            // Si la lista cambió (añades/quitas TargetX), reconstruye
            if (targetsRoot.childCount != proxies.Count) BuildProxyList();
            UpdateProxiesSticky();
            pts = GetStableProxyPointsOrdered();
        }

        if (pts.Count < 2)
        {
            if (logWhenNoPoints) Debug.Log($"[Road] Menos de 2 puntos; encontrados={pts.Count}. Raíz={(targetsRoot? targetsRoot.name : "<null>")} childCount={(targetsRoot? targetsRoot.childCount : 0)}");
            lastUsedControlPoints.Clear();
            return; // mantiene la malla previa para evitar parpadeo
        }

        var finalPts = ApplyConnectMode(pts);
        RenderOrCache(finalPts);
    }

    // Habilita/deshabilita render según 'hideRoadMesh', y en cualquier caso cachea los puntos para gizmos
    void RenderOrCache(List<Transform> controlPoints)
    {
        lastUsedControlPoints.Clear();
        if (controlPoints != null) lastUsedControlPoints.AddRange(controlPoints);
        if (!mr) mr = GetComponent<MeshRenderer>();
        if (hideRoadMesh)
        {
            if (mr) mr.enabled = false;
            return; // no generamos malla, sólo cache para gizmos
        }
        if (mr) mr.enabled = true;
        GenerateRoad(controlPoints);
    }

    // Aplica el modo de conexión pedido desde el inspector
    List<Transform> ApplyConnectMode(List<Transform> src)
    {
        if (src == null || src.Count == 0) return src;
        switch (connectMode)
        {
            case ConnectMode.FirstToLastOnly:
                if (src.Count >= 2)
                {
                    return new List<Transform> { src[0], src[^1] };
                }
                return new List<Transform>(src);
            case ConnectMode.LastTwoOnly:
                if (src.Count >= 2)
                {
                    return new List<Transform> { src[^2], src[^1] };
                }
                return new List<Transform>(src);
            case ConnectMode.Sequential:
            default:
                return MaybeCloseLoopSequential(src);
        }
    }

    // Si hay ≥3 puntos y el toggle está activo, devolvemos una copia con el primero repetido al final.
    List<Transform> MaybeCloseLoopSequential(List<Transform> src)
    {
        if (!closeLoopWhenAtLeast3 || src == null || src.Count < 3) return src;
        var list = new List<Transform>(src.Count + 1);
        list.AddRange(src);
        list.Add(src[0]);
        return list;
    }

    // -------------------- Descubrimiento y proxies --------------------
    /**
     * @brief Reconstruye la lista de proxies internos a partir de los hijos de <c>targetsRoot</c>.
     * @details No altera la jerarquía real de los targets; sólo crea clones bajo <c>VirtualTargets</c>.
     */
    void BuildProxyList()
    {
        // Reusar proxies existentes por realTarget y eliminar los obsoletos para evitar duplicados
        var prevMap = new Dictionary<Transform, ProxyState>();
        foreach (var st in proxies)
        {
            if (st != null && st.realTarget)
                prevMap[st.realTarget] = st;
        }

        var newList = new List<ProxyState>();
        var seen = new HashSet<Transform>();

        foreach (Transform child in targetsRoot)
        {
            if (!child) continue;
            var rp = child.Find(pointChildName);
            if (!rp)
            {
                // búsqueda laxa por consistencia con el modo directo
                foreach (Transform sub in child)
                {
                    string n = sub.name.ToLowerInvariant();
                    if (n == pointChildName.ToLowerInvariant() || n.Contains("tgt") || n.Contains("targetpoint"))
                    { rp = sub; break; }
                }
            }
            if (!rp) continue;

            if (prevMap.TryGetValue(child, out var existing))
            {
                existing.realTarget = child;
                existing.realPoint = rp;
                existing.name = child.name;
                existing.orderIndex = child.GetSiblingIndex();
                if (existing.proxyPoint && existing.proxyPoint.parent != virtualRoot)
                    existing.proxyPoint.SetParent(virtualRoot, true);
                newList.Add(existing);
                seen.Add(child);
            }
            else
            {
                var tGO = new GameObject(child.name);
                tGO.transform.SetParent(virtualRoot, false);
                var pGO = new GameObject(pointChildName);
                pGO.transform.SetParent(tGO.transform, false);

                var st = new ProxyState
                {
                    realTarget = child,
                    realPoint = rp,
                    proxyPoint = pGO.transform,
                    name = child.name,
                    orderIndex = child.GetSiblingIndex(),
                    stablePos = pGO.transform.position,
                    stableRot = pGO.transform.rotation
                };
                newList.Add(st);
            }
        }

        // Destruir proxies que ya no están en la raíz de targets
        foreach (var kv in prevMap)
        {
            if (seen.Contains(kv.Key)) continue;
            var st = kv.Value;
            if (st != null && st.proxyPoint)
            {
                var parent = st.proxyPoint.parent;
                if (parent)
                {
                    if (Application.isPlaying) Destroy(parent.gameObject); else DestroyImmediate(parent.gameObject);
                }
                else
                {
                    if (Application.isPlaying) Destroy(st.proxyPoint.gameObject); else DestroyImmediate(st.proxyPoint.gameObject);
                }
            }
        }

        // Limpieza adicional: eliminar cualquier hijo huérfano en VirtualTargets no usado por 'newList'
        var keepParents = new HashSet<Transform>();
        foreach (var st in newList)
            if (st.proxyPoint && st.proxyPoint.parent) keepParents.Add(st.proxyPoint.parent);

        var toDelete = new List<Transform>();
        foreach (Transform vtChild in virtualRoot)
        {
            if (!keepParents.Contains(vtChild)) toDelete.Add(vtChild);
        }
        foreach (var tr in toDelete)
        {
            if (!tr) continue;
            if (Application.isPlaying) Destroy(tr.gameObject); else DestroyImmediate(tr.gameObject);
        }

        proxies.Clear();
        proxies.AddRange(newList);
        planeLocked = false; // al cambiar composición, desbloquea plano
    }

    /**
     * @brief Actualiza los proxies con lógica de estabilidad/antiparpadeo.
     * @details Se usa un umbral de movimiento y un LERP exponencial para suavizar:
     *          p(t+1) = (1 - lambda) * p(t) + lambda * p_hat, con 0 < lambda <= 1.
     */
    void UpdateProxiesSticky()
    {
        foreach (var st in proxies)
        {
            if (!st.realTarget) continue;
            if (!st.realPoint) { st.realPoint = st.realTarget.Find(pointChildName); }
            bool isVisible = IsTargetVisible(st.realTarget, st.realPoint);

            if (isVisible)
            {
                st.visibleFrames++;
                st.invisibleFrames = 0;

                Vector3 candPos = st.realPoint.position;
                Quaternion candRot = st.realPoint.rotation;

                if (!st.everStable)
                {
                    if (st.visibleFrames >= minVisibleFramesToUpdate)
                    {
                        st.everStable = true;
                        st.stablePos = candPos;
                        st.stableRot = candRot;
                        st.proxyPoint.SetPositionAndRotation(st.stablePos, st.stableRot);
                        MaybeLockPlane();
                    }
                }
                else
                {
                    if ((candPos - st.stablePos).magnitude <= maxReacquireJump)
                    {
                        float delta = (candPos - st.stablePos).magnitude;
                        bool firstUpdate = st.proxyPoint.position == Vector3.zero; // Heurística para la primera actualización
                        if (delta >= minDeltaToUpdate || firstUpdate)
                        {
                            st.stablePos = Vector3.Lerp(st.stablePos, candPos, updateLerp);
                            st.stableRot = Quaternion.Slerp(st.stableRot, candRot, updateLerp);
                            st.proxyPoint.SetPositionAndRotation(st.stablePos, st.stableRot);
                        }
                    }
                }
            }
            else
            {
                st.invisibleFrames++;
                // mantenemos su última pose estable
            }
        }
    }

    /**
     * @brief Determina si un target está visible/trackeado.
     * @return true si está trackeado por Vuforia (si presente) o si el punto está activo en la jerarquía.
     */
    bool IsTargetVisible(Transform realTarget, Transform realPoint)
    {
#if VUFORIA_PRESENT
        Vuforia.ObserverBehaviour ob = null;
        if (realTarget) ob = realTarget.GetComponent<Vuforia.ObserverBehaviour>();
        if (!ob && realPoint) ob = realPoint.GetComponentInParent<Vuforia.ObserverBehaviour>();
        if (!ob && realTarget) ob = realTarget.GetComponentInChildren<Vuforia.ObserverBehaviour>();
        if (requireVuforiaTracking)
        {
            if (ob == null) return false; // sin observer no cuenta
            var s = ob.TargetStatus.Status;
            if (s == Vuforia.Status.TRACKED || s == Vuforia.Status.EXTENDED_TRACKED) return true;
            // versiones de Vuforia antiguas/nuevas pueden no definir DETECTED; considerar cualquier estado distinto de NO_POSE
            if (countDetectedAsTracked && s != Vuforia.Status.NO_POSE) return true;
            return false;
        }
        else
        {
            if (ob != null)
            {
                var s = ob.TargetStatus.Status;
                if (s == Vuforia.Status.TRACKED || s == Vuforia.Status.EXTENDED_TRACKED) return true;
                if (countDetectedAsTracked && s != Vuforia.Status.NO_POSE) return true;
            }
        }
#else
        if (requireVuforiaTracking) return false; // compilación sin Vuforia: no conectar
#endif
        return realPoint && realPoint.gameObject.activeInHierarchy;
    }

    /**
     * @brief Devuelve la lista de proxies estables en orden de jerarquía.
     */
    List<Transform> GetStableProxyPointsOrdered()
    {
        proxies.Sort((a, b) => a.orderIndex.CompareTo(b.orderIndex));
        var list = new List<Transform>();
        foreach (var st in proxies) if (st.everStable) list.Add(st.proxyPoint);
        return list;
    }

        // Usar directamente los puntos reales (TargetX/_tgt_point) en el orden de la jerarquía
    /**
     * @brief Devuelve directamente los <c>_tgt_point</c> reales en orden de jerarquía.
     * @details Aplica una búsqueda laxa por nombre si no encuentra el exacto.
     */
    List<Transform> GetRealProxyPointsOrdered()
        {
            var list = new List<Transform>();
            if (!targetsRoot) return list;
            foreach (Transform child in targetsRoot)
            {
                if (!child) continue;
                Transform rp = child.Find(pointChildName);
                if (!rp)
                {
                    // Búsqueda laxa por nombres parecidos
                    foreach (Transform sub in child)
                    {
                        string n = sub.name.ToLowerInvariant();
                        if (n == pointChildName.ToLowerInvariant() || n.Contains("tgt") || n.Contains("targetpoint"))
                        { rp = sub; break; }
                    }
                }
                if (rp)
                {
                    // Filtro por Vuforia si es requerido
                    if (!requireVuforiaTracking || IsTargetVisible(child, rp))
                        list.Add(rp);
                }
                else if (logWhenNoPoints)
                {
                    Debug.Log($"[Road] No se encontró hijo '{pointChildName}' bajo {child.name} (modo directo).");
                }
            }
            if (logWhenNoPoints) Debug.Log($"[Road] RealPoints encontrados={list.Count} (Direct={useRealPointsDirectly}).");
            return list;
        }

    /**
     * @brief Bloquea la normal del plano cuando hay suficientes puntos estables (>=3).
     * @details Normal geométrica por suma de cruces: n = sum( cross(p[i+1]-p[i], p[i+2]-p[i+1]) ).
     */
    void MaybeLockPlane()
    {
        if (!lockPlaneAfterTwoStable || planeLocked) return;

        var stables = new List<Vector3>();
        foreach (var st in proxies) if (st.everStable) stables.Add(st.stablePos);

        if (stables.Count >= 3)
        {
            Vector3 a = stables[0], b = stables[1], c = stables[2];
            lockedUp = Vector3.Cross(b - a, c - b).normalized;
            if (lockedUp.sqrMagnitude < 1e-6f) lockedUp = Vector3.up;
            lockedPoint = a;
            planeLocked = true;
        }
    }

    // -------------------- Generación de malla --------------------
    /**
     * @brief Genera la malla de carretera a partir de puntos de control.
     * @param controlPoints Lista ordenada de transforms.
     * @details
     * 1) Calcula la línea central muestreada (Catmull–Rom centrípeta).
     * 2) Opcionalmente proyecta sobre un plano: p' = p - n * dot(p - p0, n).
     * 3) Extruye lateralmente: L = p - r * (W/2), R = p + r * (W/2) con r = normalize(t) x normalize(n).
     */
    void GenerateRoad(List<Transform> controlPoints)
    {
        // Detecta si la lista de control trae el primer punto repetido al final (cierre lógico)
        bool controlClosed = false;
        if (controlPoints != null && controlPoints.Count >= 3)
        {
            var firstT = controlPoints[0];
            var lastT = controlPoints[^1];
            controlClosed = ReferenceEquals(firstT, lastT) ||
                            ((firstT != null && lastT != null) && (firstT.position - lastT.position).sqrMagnitude < 1e-10f);
        }

        // Copia posiciones y, si hay duplicado final, quítalo para muestrear como bucle cerrado real
        var ctrl = new List<Vector3>(controlPoints.Count);
        int countToCopy = controlPoints.Count;
        if (controlClosed && countToCopy >= 2) countToCopy -= 1; // elimina el duplicado final
        for (int i = 0; i < countToCopy; i++)
        {
            var t = controlPoints[i];
            if (t) ctrl.Add(t.position);
        }

        // Opcional: forzar todos los puntos a compartir la misma Y mundial
        float yRef = 0f;
        if (forceTargetsSameHeight && ctrl.Count > 0)
        {
            switch (heightReferenceMode)
            {
                case HeightReferenceMode.FirstPoint:
                    yRef = controlPoints[0] ? controlPoints[0].position.y : ctrl[0].y;
                    break;
                case HeightReferenceMode.AveragePoints:
                    float accY = 0f; for (int i = 0; i < ctrl.Count; i++) accY += ctrl[i].y; yRef = accY / ctrl.Count;
                    break;
                case HeightReferenceMode.ThisObjectY:
                    yRef = (heightReferenceTransform ? heightReferenceTransform : transform).position.y;
                    break;
                case HeightReferenceMode.CustomY:
                    yRef = customHeightY; break;
            }
            for (int i = 0; i < ctrl.Count; i++) ctrl[i] = new Vector3(ctrl[i].x, yRef, ctrl[i].z);
        }

        // Normal del plano: bloqueada, derivada de orientación (si no se ignoran rotaciones), o geométrica
        Vector3 up = planeLocked ? lockedUp : Vector3.zero;
        if (!planeLocked && derivePlaneFromPointOrientation && !ignoreTargetRotations)
        {
            up = ComputeUpFromPointOrientation(controlPoints);
        }
        if (up.sqrMagnitude < 1e-6f)
        {
            up = ComputePlaneNormal(ctrl);
        }
        if (snapNormalToWorldUp)
        {
            float aUp = Vector3.Angle(up, Vector3.up);
            float aDown = Vector3.Angle(up, Vector3.down);
            if (aUp <= snapUpMaxAngle) up = Vector3.up; else if (aDown <= snapUpMaxAngle) up = Vector3.down;
        }
        if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
        up.Normalize();
        // Centroide como referencia del plano para una proyección más neutra
        Vector3 planePoint = planeLocked ? lockedPoint : ComputeCentroid(ctrl);
        // Si se fuerza altura constante, el plano es horizontal y pasa por Y=yRef
        if (forceTargetsSameHeight && ctrl.Count > 0)
        {
            up = Vector3.up;
            planePoint.y = yRef;
        }

    List<float> cum;
    var centerline = SampleCenterline(ctrl, samplesPerSegment, out cum, controlClosed);

        if (flattenToTargetsPlane || forceTargetsSameHeight)
        {
            for (int i = 0; i < centerline.Count; i++)
            {
                float d = Vector3.Dot(centerline[i] - planePoint, up);
                centerline[i] -= up * d;
                // Si estamos forzando altura, garantizamos Y exacta (evita drift por precisión)
                if (forceTargetsSameHeight)
                {
                    var cpi = centerline[i]; cpi.y = planePoint.y; centerline[i] = cpi;
                }
            }
        }

        // Curvature-based refinement for smoother curves
        if (refineByCurvature)
        {
            centerline = RefineByCurvature(centerline, up, maxCurveAngleDeg, maxSegmentLen, maxRefinePasses);
        }
        // El centro se ha muestreado con o sin cierre según 'controlClosed'
        bool isClosed = controlClosed;

        // Opcional: rotar la seam al vértice de menor curvatura para que no se note
        if (isClosed && rotateSeamToLowestCurvature && centerline.Count >= 3)
        {
            int seam = FindLowestCurvatureIndex(centerline, up);
            if (seam > 0)
            {
                var rotated = new List<Vector3>(centerline.Count);
                for (int k = 0; k < centerline.Count; k++)
                {
                    rotated.Add(centerline[(seam + k) % centerline.Count]);
                }
                centerline = rotated;
            }
        }

    // Recompute cumulative distance after changes
    cum = ComputeCumulative(centerline);

        // ancho auto
    float avgSeg = AverageSegment(ctrl);    //!< distancia media de segmento: mean(|p[i+1]-p[i]|) para i=0..N-2
    float minSeg = MinSegment(ctrl);        //!< distancia mínima de segmento: min(|p[i+1]-p[i]|)
    float perLane = (widthMode == WidthMode.AbsoluteMeters) ? laneWidthMeters
              : Mathf.Max(0.001f, laneWidthFraction * avgSeg);
    float totalWidth = perLane * Mathf.Max(1, laneCount);
        if (widthMode == WidthMode.FitToSpacing && minSeg > 0f)
            totalWidth = Mathf.Min(totalWidth, maxWidthVsMinSeg * minSeg);
    // Clamp duro entre mínimos y máximos configurables
    float wMin = Mathf.Max(0f, minTotalWidthMeters);
    float wMax = (maxTotalWidthMeters > 0f) ? Mathf.Max(wMin, maxTotalWidthMeters) : float.PositiveInfinity;
    totalWidth = Mathf.Clamp(totalWidth, wMin, wMax);
    float half = totalWidth * 0.5f;

        var v = new List<Vector3>(centerline.Count * 2);
        var n = new List<Vector3>(centerline.Count * 2);
        var uv = new List<Vector2>(centerline.Count * 2);
        var tri = new List<int>((centerline.Count - 1) * 6);

    Vector3 localUp = transform.InverseTransformDirection(up).normalized;

    // Construye pares L/R siguiendo todo el recorrido con posibilidad de joins redondeados
    var pairsL = new List<Vector3>();
    var pairsR = new List<Vector3>();
    var pairsU = new List<float>();

        System.Func<int, float> widthScaleAtIndex = (idx) =>
        {
            if (!adaptiveWidthInCurves || centerline.Count < 3) return 1f;
            int nC = centerline.Count;
            int ip = Mathf.Max(0, idx - 1);
            int inx = Mathf.Min(nC - 1, idx + 1);
            if (isClosed)
            {
                ip = (idx - 1 + nC) % nC;
                inx = (idx + 1) % nC;
            }
            Vector3 p = centerline[idx];
            Vector3 d0 = ProjectOnPlaneSafe(p - centerline[ip], up).normalized;
            Vector3 d1 = ProjectOnPlaneSafe(centerline[inx] - p, up).normalized;
            if (d0.sqrMagnitude < 1e-8f) d0 = d1;
            if (d1.sqrMagnitude < 1e-8f) d1 = d0;
            float ang = Vector3.Angle(d0, d1); // 0 recto, 180 giro en U
            float a0 = Mathf.Max(0f, angleStartNarrow);
            float a1 = Mathf.Max(a0 + 1e-3f, angleForMinWidth);
            if (ang <= a0) return 1f;
            if (ang >= a1) return Mathf.Clamp01(minWidthScaleAtSharpTurn);
            float tA = Mathf.InverseLerp(a0, a1, ang);
            float sMin = Mathf.Clamp01(minWidthScaleAtSharpTurn);
            return Mathf.Lerp(1f, sMin, tA);
        };

        System.Action<Vector3, Vector3, float, float> addPairSimple = (p, dir, uval, scale) =>
        {
            Vector3 tdir = ProjectOnPlaneSafe(dir, up).normalized;
            if (tdir.sqrMagnitude < 1e-8f) tdir = Vector3.forward;
            Vector3 right = Vector3.Cross(tdir, up).normalized;
            if (right.sqrMagnitude < 1e-8f) right = Vector3.right;
            float h = half * Mathf.Clamp(scale, 0.1f, 1f);
            pairsL.Add(p - right * h + up * surfaceOffset);
            pairsR.Add(p + right * h + up * surfaceOffset);
            pairsU.Add(uval);
        };

        System.Action<int> addMiterAt = (iIdx) =>
        {
            Vector3 p = centerline[iIdx];
            Vector3 offsetL, offsetR;
            float scale = widthScaleAtIndex(iIdx);
            float h = half * Mathf.Clamp(scale, 0.1f, 1f);
            ComputeMiterOffsets(centerline, iIdx, up, h, miterLimit, isClosed, out offsetL, out offsetR);
            pairsL.Add(p + offsetL + up * surfaceOffset);
            pairsR.Add(p + offsetR + up * surfaceOffset);
            float u = cum[Mathf.Clamp(iIdx, 0, cum.Count - 1)] * uvTilesPerMeter;
            pairsU.Add(u);
        };

        if (isClosed)
        {
            int nC = centerline.Count;
            for (int i = 0; i < nC; i++)
            {
                int ip = (i - 1 + nC) % nC;
                int inx = (i + 1) % nC;
                Vector3 p = centerline[i];
                Vector3 d0 = ProjectOnPlaneSafe(p - centerline[ip], up).normalized;
                Vector3 d1 = ProjectOnPlaneSafe(centerline[inx] - p, up).normalized;
                if (d0.sqrMagnitude < 1e-8f) d0 = d1;
                if (d1.sqrMagnitude < 1e-8f) d1 = d0;

                if (useRoundedJoins)
                {
                    float ang = Mathf.Clamp(Vector3.SignedAngle(d0, d1, up), -180f, 180f);
                    float absAng = Mathf.Abs(ang);
                    int steps = Mathf.Max(1, Mathf.CeilToInt((absAng / 90f) * roundSegmentsPer90));
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = (steps == 0) ? 1f : (k / (float)steps);
                        Vector3 dir = Vector3.Slerp(d0, d1, t);
                        float sc = widthScaleAtIndex(i);
                        addPairSimple(p, dir, cum[Mathf.Clamp(i, 0, cum.Count - 1)] * uvTilesPerMeter, sc);
                    }
                }
                else if (useMiterJoins)
                {
                    addMiterAt(i);
                }
                else
                {
                    // simple: usa dirección media entre d0 y d1 para un centro estable
                    Vector3 dir = (d0 + d1);
                    if (dir.sqrMagnitude < 1e-8f) dir = d1;
                    float sc = widthScaleAtIndex(i);
                    addPairSimple(p, dir, cum[Mathf.Clamp(i, 0, cum.Count - 1)] * uvTilesPerMeter, sc);
                }
            }
        }
        else
        {
            // Camino abierto: primer par, interior, último par como antes
            {
                int i0 = 0; int i1 = (centerline.Count > 1) ? 1 : 0;
                Vector3 dir = (centerline[i1] - centerline[i0]);
                float sc0 = widthScaleAtIndex(i0);
                addPairSimple(centerline[i0], dir, cum[i0] * uvTilesPerMeter, sc0);
            }

            for (int i = 1; i < centerline.Count - 1; i++)
            {
                Vector3 p = centerline[i];
                Vector3 d0 = ProjectOnPlaneSafe(p - centerline[i - 1], up).normalized;
                Vector3 d1 = ProjectOnPlaneSafe(centerline[i + 1] - p, up).normalized;
                if (d0.sqrMagnitude < 1e-8f) d0 = d1;
                if (d1.sqrMagnitude < 1e-8f) d1 = d0;

                if (useRoundedJoins)
                {
                    float ang = Mathf.Clamp(Vector3.SignedAngle(d0, d1, up), -180f, 180f);
                    float absAng = Mathf.Abs(ang);
                    int steps = Mathf.Max(1, Mathf.CeilToInt((absAng / 90f) * roundSegmentsPer90));
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = (steps == 0) ? 1f : (k / (float)steps);
                        Vector3 dir = Vector3.Slerp(d0, d1, t);
                        float sc = widthScaleAtIndex(i);
                        addPairSimple(p, dir, cum[i] * uvTilesPerMeter, sc);
                    }
                }
                else if (useMiterJoins)
                {
                    addMiterAt(i);
                }
                else
                {
                    Vector3 dir = (centerline[i + 1] - centerline[i]);
                    float sc = widthScaleAtIndex(i);
                    addPairSimple(p, dir, cum[i] * uvTilesPerMeter, sc);
                }
            }

            if (centerline.Count > 1)
            {
                int last = centerline.Count - 1;
                Vector3 dir = (centerline[last] - centerline[last - 1]);
                float scl = widthScaleAtIndex(last);
                addPairSimple(centerline[last], dir, cum[last] * uvTilesPerMeter, scl);
            }
        }

        // NOTA: No duplicamos el primer par. Cerraremos la tira con triángulos explícitos (wrap-around)
        // para evitar diagonales largas/artefactos en el cierre.

        // Volcar pares a buffers locales
        for (int i = 0; i < pairsL.Count; i++)
        {
            Vector3 Ll = transform.InverseTransformPoint(pairsL[i]);
            Vector3 Rl = transform.InverseTransformPoint(pairsR[i]);
            v.Add(Ll); v.Add(Rl);
            n.Add(localUp); n.Add(localUp);
            float uval = pairsU[i];
            uv.Add(new Vector2(uval, 0f));
            uv.Add(new Vector2(uval, 1f));
        }

        // Winding automático para que siempre se vea (culling). Usar 'localUp' (espacio local).
        bool ccwUp = true;
        if (v.Count >= 4)
        {
            Vector3 a = v[1] - v[0]; // L0->R0
            Vector3 b = v[2] - v[0]; // L0->L1
            float s = Vector3.Dot(Vector3.Cross(a, b), localUp);
            ccwUp = s > 0f;
        }

        tri.Clear();
        // Tiras a lo largo (sin cerrar aún)
        for (int i = 0; i < v.Count - 2; i += 2)
        {
            if (ccwUp)
            {
                tri.Add(i);     tri.Add(i + 2); tri.Add(i + 1);
                tri.Add(i + 1); tri.Add(i + 2); tri.Add(i + 3);
            }
            else
            {
                tri.Add(i);     tri.Add(i + 1); tri.Add(i + 2);
                tri.Add(i + 1); tri.Add(i + 3); tri.Add(i + 2);
            }
        }

        // Cierre explícito si es circuito: conectar último par con el primero
        if (isClosed && v.Count >= 4)
        {
            int lastPairL = v.Count - 2;     // índice del último L
            int lastPairR = v.Count - 1;     // índice del último R
            int firstPairL = 0;              // L0
            int firstPairR = 1;              // R0
            if (ccwUp)
            {
                tri.Add(lastPairL); tri.Add(firstPairL); tri.Add(lastPairR);
                tri.Add(lastPairR); tri.Add(firstPairL); tri.Add(firstPairR);
            }
            else
            {
                tri.Add(lastPairL); tri.Add(lastPairR); tri.Add(firstPairL);
                tri.Add(lastPairR); tri.Add(firstPairR); tri.Add(firstPairL);
            }
        }

        mesh.Clear();
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();

        // Cache para seguidores
        lastCenterline.Clear();
        lastCenterline.AddRange(centerline);
        lastCumulative = cum;
        lastUpVec = up;
        lastClosed = isClosed;
        lastTotalWidth = totalWidth;

    Debug.Log($"[Road] StablePts={controlPoints.Count}  Verts={v.Count}  Tris={tri.Count / 3}  Width={totalWidth:F3}m  Mode={widthMode}");
    }

    // -------------------- Suavizado y joins --------------------
    static Vector3 ProjectOnPlaneSafe(Vector3 v, Vector3 up)
    {
        if (up.sqrMagnitude < 1e-10f) return v;
        return v - up * Vector3.Dot(v, up);
    }

    float AngleToWidthScale(Vector3 prev, Vector3 curr, Vector3 next, Vector3 up)
    {
        if (!adaptiveWidthInCurves) return 1f;
        Vector3 d0 = ProjectOnPlaneSafe(curr - prev, up).normalized;
        Vector3 d1 = ProjectOnPlaneSafe(next - curr, up).normalized;
        if (d0.sqrMagnitude < 1e-8f) d0 = d1;
        if (d1.sqrMagnitude < 1e-8f) d1 = d0;
        float ang = Vector3.Angle(d0, d1);
        float a0 = Mathf.Max(0f, angleStartNarrow);
        float a1 = Mathf.Max(a0 + 1e-3f, angleForMinWidth);
        if (ang <= a0) return 1f;
        if (ang >= a1) return Mathf.Clamp01(minWidthScaleAtSharpTurn);
        float tA = Mathf.InverseLerp(a0, a1, ang);
        float sMin = Mathf.Clamp01(minWidthScaleAtSharpTurn);
        return Mathf.Lerp(1f, sMin, tA);
    }

    List<Vector3> RefineByCurvature(List<Vector3> pts, Vector3 up, float maxAngleDeg, float maxSegLen, int passes)
    {
        if (pts == null || pts.Count < 3) return pts;
        float maxAng = Mathf.Max(1f, maxAngleDeg);
        for (int it = 0; it < passes; it++)
        {
            bool inserted = false;
            var outPts = new List<Vector3>(pts.Count * 2);
            outPts.Add(pts[0]);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i - 1];
                Vector3 b = pts[i];
                Vector3 c = pts[i + 1];
                Vector3 ab = ProjectOnPlaneSafe(b - a, up);
                Vector3 bc = ProjectOnPlaneSafe(c - b, up);
                float ang = Vector3.Angle(ab, bc);
                float len = bc.magnitude;
                outPts.Add(b);
                if ((ang > maxAng) || (len > maxSegLen))
                {
                    // Insert midpoint to increase resolution
                    outPts.Add(Vector3.Lerp(b, c, 0.5f));
                    inserted = true;
                }
            }
            outPts.Add(pts[^1]);
            pts = outPts;
            if (!inserted) break;
        }
        return pts;
    }

    List<float> ComputeCumulative(List<Vector3> pts)
    {
        var cum = new List<float>(pts.Count);
        float acc = 0f; cum.Add(0f);
        for (int i = 1; i < pts.Count; i++)
        {
            acc += Vector3.Distance(pts[i], pts[i - 1]);
            cum.Add(acc);
        }
        return cum;
    }

    int FindLowestCurvatureIndex(List<Vector3> pts, Vector3 up)
    {
        if (pts == null || pts.Count < 3) return 0;
        int n = pts.Count;
        float best = float.MaxValue; int bestIdx = 0;
        for (int i = 0; i < n; i++)
        {
            int ip = (i - 1 + n) % n;
            int inx = (i + 1) % n;
            Vector3 d0 = ProjectOnPlaneSafe(pts[i] - pts[ip], up).normalized;
            Vector3 d1 = ProjectOnPlaneSafe(pts[inx] - pts[i], up).normalized;
            if (d0.sqrMagnitude < 1e-8f || d1.sqrMagnitude < 1e-8f) continue;
            float ang = Vector3.Angle(d0, d1);
            if (ang < best) { best = ang; bestIdx = i; }
        }
        return bestIdx;
    }

    void ComputeMiterOffsets(List<Vector3> cl, int i, Vector3 up, float half, float limit, bool isClosed, out Vector3 offL, out Vector3 offR)
    {
        int last = cl.Count - 1;
        bool atStart = (i == 0);
        bool atEnd = (i == last);
        // Si es cerrado y hay duplicado del primero al final, considerar vecinos adecuados
        int prev = atStart ? (isClosed ? last - 1 : 0) : i - 1;
        int next = atEnd ? (isClosed ? 1 : last) : i + 1;

        Vector3 p = cl[i];
        Vector3 pPrev = cl[prev];
        Vector3 pNext = cl[next];

        Vector3 d0 = ProjectOnPlaneSafe(p - pPrev, up).normalized;
        Vector3 d1 = ProjectOnPlaneSafe(pNext - p, up).normalized;
        if (d0.sqrMagnitude < 1e-8f) d0 = d1;
        if (d1.sqrMagnitude < 1e-8f) d1 = d0;

        Vector3 n0 = Vector3.Cross(d0, up).normalized; // right anterior
        Vector3 n1 = Vector3.Cross(d1, up).normalized; // right siguiente
        if (n0.sqrMagnitude < 1e-8f) n0 = n1;
        if (n1.sqrMagnitude < 1e-8f) n1 = n0;

        // Ángulo en el vértice (0..180)
        float ang = Vector3.Angle(d0, d1);
        if (ang >= bevelAtAngleDeg)
        {
            // Bevel: usar normales de cada tramo sin extender
            offR = n1 * half;
            offL = -n1 * half;
            return;
        }

        // Right side miter
        Vector3 r0 = n0;
        Vector3 r1 = n1;
        Vector3 mR = (r0 + r1);
        if (mR.sqrMagnitude < 1e-8f) mR = r1; // colineal
        mR.Normalize();
    float denomR = Mathf.Max(1e-3f, Mathf.Abs(Vector3.Dot(mR, r1)));
        float scaleR = Mathf.Min(limit, 1f / denomR);
        offR = mR * (half * scaleR);

        // Left side miter
        Vector3 l0 = -n0;
        Vector3 l1 = -n1;
        Vector3 mL = (l0 + l1);
        if (mL.sqrMagnitude < 1e-8f) mL = l1;
        mL.Normalize();
        float denomL = Mathf.Max(1e-3f, Mathf.Abs(Vector3.Dot(mL, l1)));
        float scaleL = Mathf.Min(limit, 1f / denomL);
        offL = mL * (half * scaleL);
    }

    // -------------------- Gizmos de ayuda --------------------
    void OnDrawGizmos()
    {
        if (drawGizmosWhenNotSelected) DrawGizmosInternal();
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmosWhenNotSelected) DrawGizmosInternal();
    }

    void DrawGizmosInternal()
    {
        if (lastUsedControlPoints == null || lastUsedControlPoints.Count < 2) return;

        // Polilínea base
        if (drawUsedPolylineGizmo)
        {
            Gizmos.color = usedPolylineColor;
            for (int i = 0; i < lastUsedControlPoints.Count - 1; i++)
            {
                var a = lastUsedControlPoints[i];
                var b = lastUsedControlPoints[i + 1];
                if (a && b) Gizmos.DrawLine(a.position, b.position);
            }
        }

        // Segmento de cierre explícito (último -> primero) si procede
        if (connectMode == ConnectMode.Sequential && drawLoopClosureGizmo && closeLoopWhenAtLeast3 && lastUsedControlPoints.Count >= 3)
        {
            var first = lastUsedControlPoints[0];
            var last = lastUsedControlPoints[^1];
            if (first)
            {
                // Si la lista ya repite el primero al final, tomamos el penúltimo como "último distinto"
                Transform lastDistinct = (last && ReferenceEquals(first, last) && lastUsedControlPoints.Count >= 2)
                    ? lastUsedControlPoints[^2]
                    : last;
                if (lastDistinct)
                {
                    Gizmos.color = loopClosureColor;
                    Gizmos.DrawLine(lastDistinct.position, first.position);
                }
            }
        }

        // Gizmos de carriles y bordes usando el último camino generado
        if (lastCenterline != null && lastCenterline.Count >= 2)
        {
            Vector3 up = lastUpVec.sqrMagnitude > 1e-6f ? lastUpVec.normalized : Vector3.up;
            float half = Mathf.Max(0.001f, lastTotalWidth * 0.5f);
            int lanes = Mathf.Max(1, laneCount);
            int step = Mathf.Max(1, gizmoLaneSampleStep);

            // Precalcular derechos por punto
            var rights = new Vector3[lastCenterline.Count];
            for (int i = 0; i < lastCenterline.Count; i++)
            {
                int inx = (lastClosed ? (i + 1) % lastCenterline.Count : Mathf.Min(i + 1, lastCenterline.Count - 1));
                Vector3 tan = ProjectOnPlaneSafe(lastCenterline[inx] - lastCenterline[i], up).normalized;
                if (tan.sqrMagnitude < 1e-8f)
                {
                    int ip = (lastClosed ? (i - 1 + lastCenterline.Count) % lastCenterline.Count : Mathf.Max(i - 1, 0));
                    tan = ProjectOnPlaneSafe(lastCenterline[i] - lastCenterline[ip], up).normalized;
                    if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
                }
                rights[i] = Vector3.Cross(tan, up).normalized;
                if (rights[i].sqrMagnitude < 1e-8f) rights[i] = Vector3.right;
            }

            // Bordes (usar mismo ancho adaptativo visual si está activo)
            if (drawEdgesGizmo)
            {
                Gizmos.color = leftEdgeColor;
                for (int i = 0; i < lastCenterline.Count - 1; i += step)
                {
                    float sc0 = 1f, sc1 = 1f;
                    if (adaptiveWidthInCurves && lastCenterline.Count >= 3)
                    {
                        int ip0 = Mathf.Max(0, i - 1);
                        int inx0 = Mathf.Min(lastCenterline.Count - 1, i + 1);
                        int ip1 = Mathf.Max(0, (i + 1) - 1);
                        int inx1 = Mathf.Min(lastCenterline.Count - 1, (i + 1) + 1);
                        if (lastClosed)
                        {
                            ip0 = (i - 1 + lastCenterline.Count) % lastCenterline.Count;
                            inx0 = (i + 1) % lastCenterline.Count;
                            ip1 = (i % lastCenterline.Count);
                            inx1 = (i + 2) % lastCenterline.Count;
                        }
                        sc0 = AngleToWidthScale(lastCenterline[ip0], lastCenterline[i], lastCenterline[inx0], up);
                        sc1 = AngleToWidthScale(lastCenterline[ip1], lastCenterline[i + 1], lastCenterline[inx1], up);
                    }
                    Vector3 a = lastCenterline[i] - rights[i] * (half * sc0) + up * surfaceOffset;
                    Vector3 b = lastCenterline[i + 1] - rights[i + 1] * (half * sc1) + up * surfaceOffset;
                    Gizmos.DrawLine(a, b);
                }
                // cierre
                if (lastClosed)
                {
                    float scA = 1f, scB = 1f;
                    if (adaptiveWidthInCurves && lastCenterline.Count >= 3)
                    {
                        scA = AngleToWidthScale(lastCenterline[^2], lastCenterline[^1], lastCenterline[0], up);
                        scB = AngleToWidthScale(lastCenterline[^1], lastCenterline[0], lastCenterline[1], up);
                    }
                    Vector3 a = lastCenterline[^1] - rights[^1] * (half * scA) + up * surfaceOffset;
                    Vector3 b = lastCenterline[0]  - rights[0]  * (half * scB) + up * surfaceOffset;
                    Gizmos.DrawLine(a, b);
                }

                Gizmos.color = rightEdgeColor;
                for (int i = 0; i < lastCenterline.Count - 1; i += step)
                {
                    float sc0 = 1f, sc1 = 1f;
                    if (adaptiveWidthInCurves && lastCenterline.Count >= 3)
                    {
                        int ip0 = Mathf.Max(0, i - 1);
                        int inx0 = Mathf.Min(lastCenterline.Count - 1, i + 1);
                        int ip1 = Mathf.Max(0, (i + 1) - 1);
                        int inx1 = Mathf.Min(lastCenterline.Count - 1, (i + 1) + 1);
                        if (lastClosed)
                        {
                            ip0 = (i - 1 + lastCenterline.Count) % lastCenterline.Count;
                            inx0 = (i + 1) % lastCenterline.Count;
                            ip1 = (i % lastCenterline.Count);
                            inx1 = (i + 2) % lastCenterline.Count;
                        }
                        sc0 = AngleToWidthScale(lastCenterline[ip0], lastCenterline[i], lastCenterline[inx0], up);
                        sc1 = AngleToWidthScale(lastCenterline[ip1], lastCenterline[i + 1], lastCenterline[inx1], up);
                    }
                    Vector3 a = lastCenterline[i] + rights[i] * (half * sc0) + up * surfaceOffset;
                    Vector3 b = lastCenterline[i + 1] + rights[i + 1] * (half * sc1) + up * surfaceOffset;
                    Gizmos.DrawLine(a, b);
                }
                if (lastClosed)
                {
                    float scA = 1f, scB = 1f;
                    if (adaptiveWidthInCurves && lastCenterline.Count >= 3)
                    {
                        scA = AngleToWidthScale(lastCenterline[^2], lastCenterline[^1], lastCenterline[0], up);
                        scB = AngleToWidthScale(lastCenterline[^1], lastCenterline[0], lastCenterline[1], up);
                    }
                    Vector3 a = lastCenterline[^1] + rights[^1] * (half * scA) + up * surfaceOffset;
                    Vector3 b = lastCenterline[0]  + rights[0]  * (half * scB) + up * surfaceOffset;
                    Gizmos.DrawLine(a, b);
                }
            }

            // Centros de carril
            if (drawLaneCenterlinesGizmo && lanes >= 1)
            {
                for (int li = 0; li < lanes; li++)
                {
                    // mapear li a [-0.5, 0.5]
                    float t = (lanes == 1) ? 0f : (li / (float)(lanes - 1)) - 0.5f;
                    Gizmos.color = laneCenterColor;
                    for (int i = 0; i < lastCenterline.Count - 1; i += step)
                    {
                        float sc0 = 1f, sc1 = 1f;
                        if (adaptiveWidthInCurves && lastCenterline.Count >= 3)
                        {
                            int ip0 = Mathf.Max(0, i - 1);
                            int inx0 = Mathf.Min(lastCenterline.Count - 1, i + 1);
                            int ip1 = Mathf.Max(0, (i + 1) - 1);
                            int inx1 = Mathf.Min(lastCenterline.Count - 1, (i + 1) + 1);
                            if (lastClosed)
                            {
                                ip0 = (i - 1 + lastCenterline.Count) % lastCenterline.Count;
                                inx0 = (i + 1) % lastCenterline.Count;
                                ip1 = (i % lastCenterline.Count);
                                inx1 = (i + 2) % lastCenterline.Count;
                            }
                            sc0 = AngleToWidthScale(lastCenterline[ip0], lastCenterline[i], lastCenterline[inx0], up);
                            sc1 = AngleToWidthScale(lastCenterline[ip1], lastCenterline[i + 1], lastCenterline[inx1], up);
                        }
                        Vector3 a = lastCenterline[i]     + rights[i]     * (t * 2f * half * sc0) + up * surfaceOffset;
                        Vector3 b = lastCenterline[i + 1] + rights[i + 1] * (t * 2f * half * sc1) + up * surfaceOffset;
                        Gizmos.DrawLine(a, b);
                    }
                    if (lastClosed)
                    {
                        float scA = 1f, scB = 1f;
                        if (adaptiveWidthInCurves && lastCenterline.Count >= 3)
                        {
                            scA = AngleToWidthScale(lastCenterline[^2], lastCenterline[^1], lastCenterline[0], up);
                            scB = AngleToWidthScale(lastCenterline[^1], lastCenterline[0], lastCenterline[1], up);
                        }
                        Vector3 a = lastCenterline[^1] + rights[^1] * (t * 2f * half * scA) + up * surfaceOffset;
                        Vector3 b = lastCenterline[0]  + rights[0]  * (t * 2f * half * scB) + up * surfaceOffset;
                        Gizmos.DrawLine(a, b);
                    }
                }
            }
        }
    }

    // -------------------- Utilidades geométricas --------------------
    /**
     * @brief Estima la normal del plano a partir de las orientaciones locales de cada punto.
     * @details Suma de ejes normalizados (Up/Forward/Right según configuración o elección automática) y normalización.
     *          Fórmula: n_hat = normalize( sum_i (a_i_hat) ).
     */
    Vector3 ComputeUpFromPointOrientation(List<Transform> pts)
    {
        if (pts == null || pts.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var t in pts)
        {
            if (!t) continue;
            Vector3 axis;
            if (autoChoosePlaneAxis)
            {
                // Elige el eje local más vertical (alineado con world up)
                Vector3[] candidates = new[] { t.up, t.forward, t.right };
                float bestDot = -1f; Vector3 best = t.up;
                for (int i = 0; i < candidates.Length; i++)
                {
                    float d = Mathf.Abs(Vector3.Dot(candidates[i].normalized, Vector3.up));
                    if (d > bestDot) { bestDot = d; best = candidates[i]; }
                }
                axis = best;
            }
            else
            {
                axis = planeNormalAxis == OrientationAxis.Up ? t.up :
                       planeNormalAxis == OrientationAxis.Forward ? t.forward : t.right;
            }
            if (axis.sqrMagnitude > 1e-10f) sum += axis.normalized;
        }
        if (sum.sqrMagnitude < 1e-6f) return Vector3.zero;
        return sum.normalized;
    }

    /**
     * @brief Calcula el centroide de un conjunto de puntos.
     * @details c = (1/N) * sum_i p[i].
     */
    Vector3 ComputeCentroid(List<Vector3> pts)
    {
        if (pts == null || pts.Count == 0) return Vector3.zero;
        Vector3 c = Vector3.zero;
        for (int i = 0; i < pts.Count; i++) c += pts[i];
        return c / pts.Count;
    }
    /**
     * @brief Longitud media de segmentos consecutivos.
     * @details meanDistance = (1/(N-1)) * sum_i |p[i+1] - p[i]|.
     */
    float AverageSegment(List<Vector3> pts)
    {
        if (pts.Count < 2) return 0f;
        float s = 0f;
        for (int i = 0; i < pts.Count - 1; i++) s += Vector3.Distance(pts[i], pts[i + 1]);
        return s / (pts.Count - 1);
    }

    /**
     * @brief Longitud mínima entre segmentos consecutivos.
     * @details minDistance = min_i |p[i+1] - p[i]|.
     */
    float MinSegment(List<Vector3> pts)
    {
        if (pts.Count < 2) return 0f;
        float m = float.MaxValue;
        for (int i = 0; i < pts.Count - 1; i++) m = Mathf.Min(m, Vector3.Distance(pts[i], pts[i + 1]));
        return m;
    }

    // Catmull–Rom centrípeta que pasa EXACTO por los puntos
    /**
     * @brief Muestrea la línea central con Catmull–Rom centrípeta pasando exactamente por los puntos.
     * @param pts Puntos de control.
     * @param samples Muestras por tramo.
     * @param cumulativeDist Distancia acumulada a lo largo de la polilínea muestreada.
     * @details Parámetros centrípetos: t[i] = t[i-1] + |p[i] - p[i-1]|^0.5.
     */
    List<Vector3> SampleCenterline(List<Vector3> pts, int samples, out List<float> cumulativeDist, bool controlClosed = false)
    {
        cumulativeDist = new List<float>();
        var result = new List<Vector3>();

        if (pts.Count == 2)
        {
            float acc = 0f;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                Vector3 p = Vector3.Lerp(pts[0], pts[1], t);
                if (result.Count > 0) acc += Vector3.Distance(p, result[^1]);
                result.Add(p); cumulativeDist.Add(acc);
            }
            return result;
        }

        float accDist = 0f;
        if (controlClosed && pts.Count >= 3)
        {
            int n = pts.Count; // sin duplicados
            for (int i = 0; i < n; i++)
            {
                int i0 = (i - 1 + n) % n;
                int i1 = i;
                int i2 = (i + 1) % n;
                int i3 = (i + 2) % n;
                Vector3 p0 = pts[i0];
                Vector3 p1 = pts[i1];
                Vector3 p2 = pts[i2];
                Vector3 p3 = pts[i3];

                int s0 = (i == 0) ? 0 : 1; // evita duplicar el primer punto del tramo
                for (int s = s0; s <= samples; s++)
                {
                    float t = s / (float)samples;
                    Vector3 pt = CatmullRomCentripetal(p0, p1, p2, p3, t);
                    if (s == 0) pt = p1;
                    if (s == samples) pt = p2;

                    if (result.Count > 0) accDist += Vector3.Distance(pt, result[^1]);
                    result.Add(pt); cumulativeDist.Add(accDist);
                }
            }
            return result;
        }
        else
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = (i == 0) ? pts[i] : pts[i - 1];
                Vector3 p1 = pts[i];
                Vector3 p2 = pts[i + 1];
                Vector3 p3 = (i + 2 < pts.Count) ? pts[i + 2] : pts[i + 1];

                int s0 = (i == 0) ? 0 : 1; // evita duplicar el primer punto del tramo
                for (int s = s0; s <= samples; s++)
                {
                    float t = s / (float)samples;
                    Vector3 pt = CatmullRomCentripetal(p0, p1, p2, p3, t);
                    if (s == 0) pt = p1;
                    if (s == samples) pt = p2;

                    if (result.Count > 0) accDist += Vector3.Distance(pt, result[^1]);
                    result.Add(pt); cumulativeDist.Add(accDist);
                }
            }

            if (result.Count > 0)
            {
                Vector3 last = pts[^1];
                accDist += Vector3.Distance(last, result[^1]);
                result[^1] = last; cumulativeDist[^1] = accDist;
            }
            return result;
        }
    }

    /**
     * @brief Evalúa Catmull–Rom centrípeta para cuatro puntos y un parámetro t en [0,1].
     * @details Se usa la formulación de parámetros t0..t3 con alpha = 0.5 y dos interpolaciones lineales sucesivas.
     */
    Vector3 CatmullRomCentripetal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        const float alpha = 0.5f;
        float t0 = 0f;
        float t1 = t0 + Mathf.Pow((p1 - p0).magnitude, alpha);
        float t2 = t1 + Mathf.Pow((p2 - p1).magnitude, alpha);
        float t3 = t2 + Mathf.Pow((p3 - p2).magnitude, alpha);
        float tt = Mathf.Lerp(t1, t2, t);

        Vector3 A1 = ((t1 - tt) / Mathf.Max(1e-6f, t1 - t0)) * p0 + ((tt - t0) / Mathf.Max(1e-6f, t1 - t0)) * p1;
        Vector3 A2 = ((t2 - tt) / Mathf.Max(1e-6f, t2 - t1)) * p1 + ((tt - t1) / Mathf.Max(1e-6f, t2 - t1)) * p2;
        Vector3 A3 = ((t3 - tt) / Mathf.Max(1e-6f, t3 - t2)) * p2 + ((tt - t2) / Mathf.Max(1e-6f, t3 - t2)) * p3;

        Vector3 B1 = ((t2 - tt) / Mathf.Max(1e-6f, t2 - t0)) * A1 + ((tt - t0) / Mathf.Max(1e-6f, t2 - t0)) * A2;
        Vector3 B2 = ((t3 - tt) / Mathf.Max(1e-6f, t3 - t1)) * A2 + ((tt - t1) / Mathf.Max(1e-6f, t3 - t1)) * A3;

        return ((t2 - tt) / Mathf.Max(1e-6f, t2 - t1)) * B1 + ((tt - t1) / Mathf.Max(1e-6f, t2 - t1)) * B2;
    }

    /**
     * @brief Calcula una normal promedio del plano usando cruces de segmentos consecutivos.
     * @details n = normalize( sum_i normalize( cross(p[i+1]-p[i], p[i+2]-p[i+1]) ) ).
     */
    Vector3 ComputePlaneNormal(List<Vector3> pts)
    {
        if (planeLocked) return lockedUp;
        if (pts.Count < 3) return Vector3.up;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < pts.Count - 2; i++)
        {
            Vector3 a = pts[i + 1] - pts[i];
            Vector3 b = pts[i + 2] - pts[i + 1];
            Vector3 n = Vector3.Cross(a, b);
            if (n.sqrMagnitude > 1e-10f) sum += n.normalized;
        }
        return (sum.sqrMagnitude < 1e-6f) ? Vector3.up : sum.normalized;
    }
}