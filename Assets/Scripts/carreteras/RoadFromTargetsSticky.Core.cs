using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// Nota: sin namespace para no romper referencias de Unity al componente existente
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public partial class RoadFromTargetsSticky : MonoBehaviour
{
        // -------------------- Inspector (escena / entrada) --------------------
        [Header("Raíz de targets en la escena")]
        public Transform targetsRoot;
        public string pointChildName = "_tgt_point";

        [Header("Actualización")]
        [FormerlySerializedAs("updateEveryFrame")]
        [Tooltip("Actualiza automáticamente solo si detecta cambios (posiciones o parámetros)")]
        public bool updateIfChange = true;
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

        public enum ConnectMode { Sequential, FirstToLastOnly, LastTwoOnly }
        [Header("Modo de conexión entre puntos")]
        public ConnectMode connectMode = ConnectMode.LastTwoOnly;

        [Header("Plano y UVs")]
        public bool flattenToTargetsPlane = true;
        public bool lockPlaneAfterTwoStable = true;
        public float uvTilesPerMeter = 0.15f;
        public bool ignoreTargetRotations = false;
        public bool derivePlaneFromPointOrientation = true;
        public enum OrientationAxis { Up, Forward, Right }
        public OrientationAxis planeNormalAxis = OrientationAxis.Up;
        public bool autoChoosePlaneAxis = true;
        public bool snapNormalToWorldUp = true;
        [Range(0f, 60f)] public float snapUpMaxAngle = 30f;

        [Header("Altura constante (opcional)")]
        public bool forceTargetsSameHeight = false;
        public enum HeightReferenceMode { FirstPoint, AveragePoints, ThisObjectY, CustomY }
        public HeightReferenceMode heightReferenceMode = HeightReferenceMode.AveragePoints;
        public Transform heightReferenceTransform;
        public float customHeightY = 0f;

        [Header("Carretera (ancho auto en AR)")]
        public int laneCount = 1;
        public enum WidthMode { AbsoluteMeters, FitToSpacing }
        public WidthMode widthMode = WidthMode.FitToSpacing;
        public float laneWidthMeters = 3f;
        [Range(0.05f, 0.6f)] public float laneWidthFraction = 0.08f;
        [Range(0.2f, 1.0f)] public float maxWidthVsMinSeg = 0.5f;
        [Range(0.005f, 2f)] public float minTotalWidthMeters = 0.05f;
        [Range(0f, 2f)] public float maxTotalWidthMeters = 0.08f;

        [Header("Ancho adaptativo en curvas")]
        public bool adaptiveWidthInCurves = true;
        [Range(0.2f, 1f)] public float minWidthScaleAtSharpTurn = 0.5f;
        [Range(5f, 80f)] public float angleForMinWidth = 50f;
        [Range(0f, 40f)] public float angleStartNarrow = 15f;

        [Header("Curvas y joins")]
        public bool refineByCurvature = true;
        [Range(1f, 30f)] public float maxCurveAngleDeg = 6f;
        [Range(0.01f, 0.25f)] public float maxSegmentLen = 0.03f;
        [Range(1, 4)] public int maxRefinePasses = 3;
        public bool useMiterJoins = false;
        [Range(1f, 5f)] public float miterLimit = 1.05f;
        [Range(5f, 80f)] public float bevelAtAngleDeg = 18f;
        public bool useRoundedJoins = true;
        [Range(1, 12)] public int roundSegmentsPer90 = 5;
        public bool rotateSeamToLowestCurvature = true;

        [Header("Pegajosidad / Anti-parpadeo")]
        public int minVisibleFramesToUpdate = 1;
        public int minInvisibleFramesToHold = 2;
        public float maxReacquireJump = 0.15f;
        [Range(0f, 1f)] public float updateLerp = 0.35f;
        public float minDeltaToUpdate = 0.003f;

        [Header("Filtro de temblor de mano")]
        public bool tremorFilter = true;
        [Range(0f, 0.05f)] public float tremorDeadzoneMeters = 0.01f;
        [Range(0, 10)] public int tremorHoldFrames = 2;
        [Range(0f, 0.1f)] public float tremorMaxStepMeters = 0.02f;
        [Range(0f, 1f)] public float tremorLerp = 0.25f;

        [Header("Congelar tras estabilidad")]
        public bool freezeProxyAfterFirstStable = false;
        public bool freezeRoadAfterPlaneLocked = false;

        [Header("Visibilidad")]
        public float surfaceOffset = 0.002f;
        public Material asphaltMaterial;

        [Header("Marcas de carril (pintura)")]
        public bool enableLaneLines = true;
        public Material laneLineMaterial;
        public Texture2D laneLineTexture;
        public Color laneLineColor = Color.white;
        public float lineUvTilesPerMeter = 0.5f;
        [Range(0.005f, 0.2f)] public float centerLineWidthMeters = 0.06f;
        [Range(0.005f, 0.2f)] public float edgeLineWidthMeters = 0.08f;
        public bool drawEdgeLines = true;
        public bool drawCenterLines = true;
        public bool centerLinesDashed = true;
        [Range(0.02f, 2f)] public float dashLengthMeters = 0.35f;
        [Range(0.02f, 2f)] public float gapLengthMeters = 0.35f;
        public float dashOffsetMeters = 0f;
        [Range(0f, 0.01f)] public float linesLiftOffsetMeters = 0.0015f;

        [Header("Sombra bajo la carretera")]
        public bool addUnderShadow = true;
        [Range(0f, 0.5f)] public float shadowExtraWidthMeters = 0.02f;
        [Range(-0.01f, 0.01f)] public float shadowUnderOffset = -0.0015f;
        public Color shadowColor = new Color(0f, 0f, 0f, 0.35f);
        public Material shadowMaterial;

        [Header("Vuforia (requisito de tracking)")]
        public bool requireVuforiaTracking = true;
        public bool countDetectedAsTracked = true;

        [Header("Debug")]
        public bool logWhenNoPoints = false;

        [Header("Testing y Gizmos")]
        public bool hideRoadMesh = false;
        public bool drawUsedPolylineGizmo = true;
        public Color usedPolylineColor = new Color(0f, 1f, 1f, 0.9f);
        public bool drawLoopClosureGizmo = true;
        public Color loopClosureColor = new Color(1f, 0.9f, 0f, 0.9f);
        public bool drawGizmosWhenNotSelected = true;

        [Header("Gizmos de carriles (debug)")]
        public bool drawLaneCenterlinesGizmo = true;
        [Range(1, 10)] public int gizmoLaneSampleStep = 1;
        public Color laneCenterColor = new Color(0.1f, 0.8f, 0.1f, 0.9f);
        public bool drawEdgesGizmo = true;
        public Color leftEdgeColor = new Color(0.9f, 0.1f, 0.1f, 0.9f);
        public Color rightEdgeColor = new Color(0.1f, 0.1f, 0.9f, 0.9f);

        // -------------------- Estado --------------------
        MeshFilter mf;
        Mesh mesh;
        MeshRenderer mr;
        MeshFilter linesMf; MeshRenderer linesMr; Mesh linesMesh;
        MeshFilter shadowMf; MeshRenderer shadowMr; Mesh shadowMesh;

        readonly List<Transform> lastUsedControlPoints = new();
        List<Vector3> lastCenterline = new();
        List<float> lastCumulative = new();
        Vector3 lastUpVec = Vector3.up;
        bool lastClosed = false;
        float lastTotalWidth = 0f;
        int lastHash = 0;

        [Header("Actualización condicional")]
        [Range(0f, 0.2f)] public float minRebuildPosDeltaMeters = 0.05f;
        List<Vector3> lastCtrlPositionsCache = new();
        int lastParamHash = 0;

        public bool PathReady => lastCenterline != null && lastCenterline.Count >= 2;
    public float PathLength => (lastCumulative != null && lastCumulative.Count > 0) ? lastCumulative[lastCumulative.Count - 1] : 0f;
        public Vector3 PathUp => lastUpVec;
        public bool IsClosedPath => lastClosed;
        public int LaneCountPublic => laneCount;
        public float TotalWidthPublic => lastTotalWidth;
    [Range(0f, 0.5f)] public float outerLaneEdgeMargin = 0.1f;
}
