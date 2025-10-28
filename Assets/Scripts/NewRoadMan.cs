using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Vuforia;
using Unity.Mathematics; // para BezierKnot (float3)

public class NewRoadMan : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Transform TargetsParent;       // GameObject "Targets" con tus Image/ModelTargets
    [SerializeField] private SplineContainer CenterLane;    // Roads/Lane_Center (SplineContainer)
    [SerializeField] private SplineContainer LeftLane;      // Roads/Lane_Left
    [SerializeField] private SplineContainer RightLane;     // Roads/Lane_Right

    [Header("Road Settings")]
    [SerializeField] private float laneOffset = 1.75f;      // separación lateral entre carriles
    [SerializeField] private bool closeLoopWhenAllSeen = false;
    [SerializeField] private Vector3 upAxis = Vector3.up;   // cambia si tu escena usa otro "arriba"
    [SerializeField] private float smoothTangent = 0.25f;   // 0 = poligonal, 0.2–0.35 = suave

    private readonly List<ObserverBehaviour> _allObservers = new();
    private readonly List<ObserverBehaviour> _orderedDetections = new();

    // --- Unity lifecycle ---
    private void Awake()
    {
        if (TargetsParent == null)
        {
            Debug.LogError("[NewRoadMan] Asigna TargetsParent en el inspector.");
            return;
        }

        // Recoge todos los ObserverBehaviour bajo "Targets"
        foreach (Transform t in TargetsParent)
        {
            var obs = t.GetComponentInChildren<ObserverBehaviour>();
            if (obs != null) _allObservers.Add(obs);
        }
    }

    private void OnEnable()
    {
        foreach (var obs in _allObservers)
            obs.OnTargetStatusChanged += OnTargetStatusChanged;
    }

    private void OnDisable()
    {
        foreach (var obs in _allObservers)
            obs.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    // --- Vuforia callback ---
    private void OnTargetStatusChanged(ObserverBehaviour obs, TargetStatus status)
    {
        var s = status.Status;
        bool tracked = s == Status.TRACKED || s == Status.EXTENDED_TRACKED;

        if (tracked)
        {
            if (!_orderedDetections.Contains(obs))
            {
                _orderedDetections.Add(obs);
            }
            // reconstruye cada vez que hay update útil (posición puede refinarse)
            RebuildAllLanes();
        }
        // Si quieres que desaparezca al perder tracking, descomenta:
        // else if (_orderedDetections.Remove(obs))
        // {
        //     RebuildAllLanes();
        // }
    }

    // --- Build lanes ---
    private void RebuildAllLanes()
    {
        if (CenterLane == null || LeftLane == null || RightLane == null) return;
        if (_orderedDetections.Count < 2) return;

        // 1) puntos mundo en orden de detección
        var pts = new List<Vector3>(_orderedDetections.Count);
        foreach (var o in _orderedDetections) pts.Add(o.transform.position);

        // 2) carril central
        BuildSplineFromWorldPoints(CenterLane, pts, closeLoopWhenAllSeen);

        // 3) carriles paralelos por offset lateral usando aproximación de tangente
        var leftPts = new List<Vector3>(pts.Count);
        var rightPts = new List<Vector3>(pts.Count);

        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 prev = pts[Mathf.Max(i - 1, 0)];
            Vector3 next = pts[Mathf.Min(i + 1, pts.Count - 1)];
            Vector3 dir = (next - prev).normalized;
            Vector3 side = Vector3.Cross(upAxis, dir).normalized;  // “derecha” respecto a la tangente

            leftPts.Add(pts[i] - side * laneOffset);
            rightPts.Add(pts[i] + side * laneOffset);
        }

        BuildSplineFromWorldPoints(LeftLane, leftPts, closeLoopWhenAllSeen);
        BuildSplineFromWorldPoints(RightLane, rightPts, closeLoopWhenAllSeen);
    }

    private void BuildSplineFromWorldPoints(SplineContainer container, List<Vector3> worldPts, bool closed)
    {
        var trs = container.transform;

        // Asegura una única spline y límpiala
        Spline spline;
        if (container.Splines.Count == 0)
        {
            spline = new Spline();
            container.AddSpline(spline);
        }
        else
        {
            spline = container.Splines[0];
            spline.Clear();
        }

        for (int i = 0; i < worldPts.Count; i++)
        {
            Vector3 local = trs.InverseTransformPoint(worldPts[i]);

            Vector3 prevLocal = trs.InverseTransformPoint(worldPts[Mathf.Max(i - 1, 0)]);
            Vector3 nextLocal = trs.InverseTransformPoint(worldPts[Mathf.Min(i + 1, worldPts.Count - 1)]);
            Vector3 dir = (nextLocal - prevLocal) * smoothTangent;

            // BezierKnot usa float3; añadimos tangentes suaves de entrada/salida
            var knot = new BezierKnot(
                (float3)local,
                (float3)(-dir),
                (float3)dir
            );

            spline.Add(knot);
        }

        spline.Closed = closed;
        // Algunas versiones requieren refrescar el container para componentes como Extrude/Instantiate
        // (si usas Spline Extrude/Instantiate, estos se actualizarán al cambiar la spline)
    }

    // --- Helpers públicos opcionales ---
    [ContextMenu("Clear Detections & Splines")]
    public void ClearAll()
    {
        _orderedDetections.Clear();
        ClearContainer(CenterLane);
        ClearContainer(LeftLane);
        ClearContainer(RightLane);
    }

    private void ClearContainer(SplineContainer c)
    {
        if (c == null) return;
        if (c.Splines.Count == 0) return;
        c.Splines[0].Clear();
    }
}
