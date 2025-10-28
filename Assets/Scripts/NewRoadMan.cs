using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Vuforia;
using Unity.Mathematics; // para BezierKnot (float3)
using System.Linq;

public class NewRoadMan : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Transform TargetsParent;       // GameObject "Targets" con tus Image/ModelTargets
    [SerializeField] private SplineContainer CenterLane;    // Roads/Lane_Center (SplineContainer)
    [SerializeField] private SplineContainer LeftLane;      // Roads/Lane_Left
    [SerializeField] private SplineContainer RightLane;     // Roads/Lane_Right

    [Header("Auto Create Lanes")]
    [Tooltip("Si está activo, creará automáticamente los objetos de carril (SplineContainer) si no están asignados.")]
    [SerializeField] private bool autoCreateLanes = true;
    [Tooltip("Padre opcional para los objetos de carril. Si está vacío, se creará un GameObject 'Roads' como hijo de este componente.")]
    [SerializeField] private Transform lanesParent;

    [Header("Road Settings")]
    [SerializeField] private float laneOffset = 1.75f;      // separación lateral entre carriles
    [SerializeField] private bool closeLoopWhenAllSeen = false;
    [SerializeField] private Vector3 upAxis = Vector3.up;   // cambia si tu escena usa otro "arriba"
    [SerializeField] private float smoothTangent = 0.25f;   // 0 = poligonal, 0.2–0.35 = suave

    [Header("Planar Constraints")]
    [Tooltip("Fuerza que todos los puntos y la malla estén en un plano horizontal (Y constante en mundo).")]
    [SerializeField] private bool forcePlanar = true;
    [Tooltip("Si está activo, usa el valor de 'fixedPlaneY'. Si no, promedia la Y de las detecciones.")]
    [SerializeField] private bool useFixedPlaneY = false;
    [SerializeField] private float fixedPlaneY = 0f;

    private readonly List<ObserverBehaviour> _allObservers = new();
    private readonly List<ObserverBehaviour> _orderedDetections = new();

    [Header("Road Mesh (Spline -> Mesh)")]
    [Tooltip("Anchura total de la calzada generada a partir del carril central.")]
    [SerializeField] private float roadWidth = 3.5f;
    [Tooltip("Longitud aproximada de cada segmento a lo largo de la spline (mientras más pequeño, más suave y más polígonos).")]
    [SerializeField] private float segmentLength = 0.5f;
    [Tooltip("Escala vertical del UV (V = distancia * uvTiling).")]
    [SerializeField] private float uvTiling = 0.2f;
    [Tooltip("Material del asfalto para el MeshRenderer del camino.")]
    [SerializeField] private Material roadMaterial;
    [SerializeField] private bool generateRoadCollider = true;
    [SerializeField] private string roadMeshObjectName = "Road_Mesh";

    // --- Unity lifecycle ---
    private void Awake()
    {
        if (TargetsParent == null)
        {
            Debug.LogError("[NewRoadMan] Asigna TargetsParent en el inspector.");
            return;
        }

        // Crea los carriles automáticamente si hace falta
        if (autoCreateLanes)
        {
            EnsureLanesCreated();
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

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (autoCreateLanes && (CenterLane == null || LeftLane == null || RightLane == null))
        {
            // Evita nullref si el objeto aún no está en escena
            if (isActiveAndEnabled || gameObject.scene.IsValid())
            {
                EnsureLanesCreated();
            }
        }
    }
#endif

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

    // --- Auto-setup lanes ---
    [ContextMenu("Auto-Create Lanes Now")] // útil en editor
    private void EnsureLanesCreated()
    {
        // Determina el padre de los carriles
        Transform parent = lanesParent;
        if (parent == null)
        {
            // Busca un hijo existente llamado "Roads" para reutilizarlo
            var existing = transform.Find("Roads");
            parent = existing != null ? existing : new GameObject("Roads").transform;
            if (existing == null)
            {
                parent.SetParent(transform, false);
            }
            lanesParent = parent; // guarda referencia
        }

        CenterLane = CreateLaneIfMissing(CenterLane, parent, "Lane_Center");
        LeftLane   = CreateLaneIfMissing(LeftLane,   parent, "Lane_Left");
        RightLane  = CreateLaneIfMissing(RightLane,  parent, "Lane_Right");
    }

    private SplineContainer CreateLaneIfMissing(SplineContainer existing, Transform parent, string name)
    {
        if (existing != null) return existing;

        // Intenta encontrar uno existente por nombre bajo el padre
        var found = parent.Find(name);
        if (found != null && found.TryGetComponent<SplineContainer>(out var scFound))
            return scFound;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sc = go.AddComponent<SplineContainer>();
        // La spline se crea/limpia en BuildSplineFromWorldPoints cuando sea necesario
        return sc;
    }

    // --- Build lanes ---
    private void RebuildAllLanes()
    {
        if (CenterLane == null || LeftLane == null || RightLane == null) return;
        if (_orderedDetections.Count < 2) return;

        // 1) puntos mundo en orden de detección
        var pts = new List<Vector3>(_orderedDetections.Count);
        foreach (var o in _orderedDetections) pts.Add(o.transform.position);

        // 1.1) Fuerza planitud en Y (mundo) si procede
        if (forcePlanar)
        {
            float planeY;
            if (useFixedPlaneY)
                planeY = fixedPlaneY;
            else
                planeY = pts.Count > 0 ? pts.Average(p => p.y) : 0f;

            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                pts[i] = new Vector3(p.x, planeY, p.z);
            }
        }

    // 2) determina si debe cerrarse el circuito: si hay > 3 puntos o si está forzado por la bandera antigua
    bool willClose = closeLoopWhenAllSeen || _orderedDetections.Count > 3;

    // 2) carril central
    BuildSplineFromWorldPoints(CenterLane, pts, willClose);

        // 3) carriles paralelos por offset lateral usando aproximación de tangente
        var leftPts = new List<Vector3>(pts.Count);
        var rightPts = new List<Vector3>(pts.Count);

        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            int iPrev = willClose ? (i - 1 + n) % n : Mathf.Max(i - 1, 0);
            int iNext = willClose ? (i + 1) % n : Mathf.Min(i + 1, n - 1);
            Vector3 prev = pts[iPrev];
            Vector3 next = pts[iNext];
            // Proyecta la dirección al plano horizontal definido por upAxis
            Vector3 dir = next - prev;
            dir -= Vector3.Project(dir, upAxis); // quita componente vertical
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward; // fallback
            dir.Normalize();
            Vector3 side = Vector3.Cross(upAxis.normalized, dir).normalized;  // derecha en el plano

            leftPts.Add(pts[i] - side * laneOffset);
            rightPts.Add(pts[i] + side * laneOffset);
        }

        BuildSplineFromWorldPoints(LeftLane, leftPts, willClose);
        BuildSplineFromWorldPoints(RightLane, rightPts, willClose);

        // 4) (nuevo) Generar malla de carretera a partir del carril central
        TryBuildRoadMeshFromCenter();
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

        int n = worldPts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 local = trs.InverseTransformPoint(worldPts[i]);

            int iPrev = closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0);
            int iNext = closed ? (i + 1) % n : Mathf.Min(i + 1, n - 1);
            Vector3 prevLocal = trs.InverseTransformPoint(worldPts[iPrev]);
            Vector3 nextLocal = trs.InverseTransformPoint(worldPts[iNext]);
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

        // Borra también la malla si existe
        if (CenterLane != null)
        {
            var t = CenterLane.transform;
            var found = t.Find(roadMeshObjectName);
            if (found != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(found.gameObject);
                else
                    Destroy(found.gameObject);
                #else
                Destroy(found.gameObject);
                #endif
            }
        }
    }

    private void ClearContainer(SplineContainer c)
    {
        if (c == null) return;
        if (c.Splines.Count == 0) return;
        c.Splines[0].Clear();
    }

    [ContextMenu("Rebuild Road Mesh (from Center)")]
    public void RebuildRoadMeshNow()
    {
        TryBuildRoadMeshFromCenter();
    }

    // =====================
    // Road mesh generation
    // =====================
    private void TryBuildRoadMeshFromCenter()
    {
        if (CenterLane == null) return;
        if (CenterLane.Splines.Count == 0) return;

        var spline = CenterLane.Splines[0];
        if (spline.Count < 2) return;

        // Asegura un GO hijo bajo el mismo transform que la spline central
        var parent = CenterLane.transform;
        Transform meshTf = parent.Find(roadMeshObjectName);
        if (meshTf == null)
        {
            var go = new GameObject(roadMeshObjectName);
            meshTf = go.transform;
            meshTf.SetParent(parent, false);
        }

        // Componentes requeridos
        var mf = meshTf.GetComponent<MeshFilter>();
        if (mf == null) mf = meshTf.gameObject.AddComponent<MeshFilter>();

        var mr = meshTf.GetComponent<MeshRenderer>();
        if (mr == null) mr = meshTf.gameObject.AddComponent<MeshRenderer>();
        if (roadMaterial != null) mr.sharedMaterial = roadMaterial;

        // Determina la Y del plano para la malla (en espacio local del contenedor)
        float meshPlaneYLocal = 0f;
        if (forcePlanar)
        {
            float worldY = useFixedPlaneY ? fixedPlaneY : CenterLane.transform.position.y;
            meshPlaneYLocal = CenterLane.transform.InverseTransformPoint(new Vector3(0f, worldY, 0f)).y;
        }

        var mesh = GenerateRoadMesh(spline,
            Mathf.Max(0.01f, roadWidth),
            Mathf.Max(0.02f, segmentLength),
            parent.InverseTransformDirection(upAxis).normalized,
            spline.Closed,
            Mathf.Max(0.0001f, uvTiling),
            forcePlanar,
            meshPlaneYLocal);

        var oldMesh = mf.sharedMesh;
        mf.sharedMesh = mesh;
        if (oldMesh != null && oldMesh.name == "RoadMeshGenerated")
        {
            #if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(oldMesh);
            else
                Destroy(oldMesh);
            #else
            Destroy(oldMesh);
            #endif
        }

        var mc = meshTf.GetComponent<MeshCollider>();
        if (generateRoadCollider)
        {
            if (mc == null) mc = meshTf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }
        else if (mc != null)
        {
            #if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(mc);
            else
                Destroy(mc);
            #else
            Destroy(mc);
            #endif
        }
    }

    private static Mesh GenerateRoadMesh(Spline spline, float width, float segLen, Vector3 upLocal, bool closed, float uvTile, bool forcePlanar, float planeYLocal)
    {
        // Aproxima la longitud mediante muestreo para evitar depender de overloads con matrices
        float length = ApproximateSplineLength(spline, 128);
        int steps = Mathf.Max(2, Mathf.CeilToInt(length / segLen) + 1);

        var verts = new List<Vector3>(steps * 2);
        var norms = new List<Vector3>(steps * 2);
        var tans = new List<Vector4>(steps * 2);
        var uvs = new List<Vector2>(steps * 2);
        var tris = new List<int>((steps - 1) * 6);

        float vDist = 0f;
        Vector3 prevCenter = Vector3.zero;
        bool hasPrev = false;
        float half = width * 0.5f;

        for (int i = 0; i < steps; i++)
        {
            float t = (steps == 1) ? 0f : (float)i / (steps - 1);

            float3 p3 = SplineUtility.EvaluatePosition(spline, t);
            float3 tg3 = SplineUtility.EvaluateTangent(spline, t);
            Vector3 center = (Vector3)p3;
            Vector3 tangent = ((Vector3)tg3);
            // Proyecta la tangente al plano si procede, para mantener la carretera plana
            if (forcePlanar)
            {
                tangent -= Vector3.Project(tangent, upLocal);
                center.y = planeYLocal;
            }
            tangent = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;

            // Asegura un sistema ortonormal (tangent, side, normal)
            Vector3 side = Vector3.Cross(upLocal, tangent);
            if (side.sqrMagnitude < 1e-6f)
            {
                // fallback si up ≈ tangent
                side = Vector3.Cross(Vector3.up, tangent);
                if (side.sqrMagnitude < 1e-6f)
                    side = Vector3.Cross(Vector3.right, tangent);
            }
            side.Normalize();
            Vector3 normal = Vector3.Cross(tangent, side).normalized;

            Vector3 vL = center - side * half;
            Vector3 vR = center + side * half;

            if (hasPrev)
                vDist += Vector3.Distance(center, prevCenter);

            verts.Add(vL);
            verts.Add(vR);
            norms.Add(normal);
            norms.Add(normal);
            tans.Add(new Vector4(side.x, side.y, side.z, 1f));
            tans.Add(new Vector4(side.x, side.y, side.z, 1f));
            uvs.Add(new Vector2(0f, vDist * uvTile));
            uvs.Add(new Vector2(1f, vDist * uvTile));

            if (i < steps - 1)
            {
                int i0 = i * 2;
                int i1 = i0 + 1;
                int i2 = i0 + 2;
                int i3 = i0 + 3;
                // Triángulos con winding horario (puede ajustarse según tus normales)
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }

            prevCenter = center;
            hasPrev = true;
        }

        var mesh = new Mesh();
        mesh.name = "RoadMeshGenerated";
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTangents(tans);
        mesh.SetUVs(0, uvs);
        // Cierra el bucle si es necesario
        if (closed && verts.Count >= 4)
        {
            int last = verts.Count - 2;   // par izquierdo del último segmento
            int lastR = verts.Count - 1;  // par derecho
            int i0 = last;     // L last
            int i1 = lastR;    // R last
            int i2 = 0;        // L first
            int i3 = 1;        // R first
            tris.Add(i0); tris.Add(i2); tris.Add(i1);
            tris.Add(i1); tris.Add(i2); tris.Add(i3);
        }

        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float ApproximateSplineLength(Spline spline, int samples = 64)
    {
        samples = Mathf.Max(2, samples);
        float3 prev = SplineUtility.EvaluatePosition(spline, 0f);
        float length = 0f;
        for (int i = 1; i < samples; i++)
        {
            float t = (float)i / (samples - 1);
            float3 p = SplineUtility.EvaluatePosition(spline, t);
            length += Vector3.Distance((Vector3)prev, (Vector3)p);
            prev = p;
        }
        return length;
    }
}
