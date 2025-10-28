using System;
using System.Collections.Generic;
using UnityEngine;

/**
 * @file DetectionOrderManager.cs
 * @brief Gestiona los targets detectados y mantiene un orden según el momento de detección.
 *
 * Uso típico: Asigna el mismo targetsRoot que usas en RoadFromTargets y referencia este
 * componente desde la carretera para que construya la ruta siguiendo el orden de detección.
 *
 * Reglas por defecto:
 * - Cada TargetX se añade al orden la primera vez que pasa a visible (flanco de subida).
 * - No se elimina del orden aunque después pierda visibilidad (puedes limpiarlo manualmente o
 *   activar reset cuando todos están perdidos).
 * - El elemento añadido más recientemente es el "último" del orden; si detectas 1 y luego 4
 *   el orden es [1,4]; si después aparece 2, el orden es [1,4,2] y la carretera nueva irá 4→2.
 */
public class DetectionOrderManager : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Raíz que contiene los TargetX con un hijo '_tgt_point'.")]
    public Transform targetsRoot;
    [Tooltip("Nombre del hijo dentro de cada TargetX que representa el punto.")]
    public string pointChildName = "_tgt_point";

    [Header("Actualización y reglas")]
    [Tooltip("Si es true, evalúa el estado de los targets cada frame.")]
    public bool updateEveryFrame = true;
    [Tooltip("Si todos los targets están invisibles, limpia el orden automáticamente.")]
    public bool resetOrderWhenAllLost = false;
    [Tooltip("Escribe mensajes de diagnóstico en consola.")]
    public bool logDebug = false;

    public enum VisibilitySource { AutoPreferVuforia, VuforiaOnly, ActiveHierarchy }
    [Header("Tracking source")]
    [Tooltip("AutoPreferVuforia: usa Vuforia si hay Observer, si no, usa activeInHierarchy. VuforiaOnly: sólo Vuforia cuenta. ActiveHierarchy: sólo activeInHierarchy.")]
    public VisibilitySource visibilitySource = VisibilitySource.VuforiaOnly;

    [Tooltip("Si está activo, el ORDEN y la visibilidad sólo se basan en Vuforia (ignora activeInHierarchy). Recomendado.")]
    public bool requireVuforiaForOrder = true;

    [Header("Gizmos (orden)")]
    [Tooltip("Dibuja gizmos para visualizar el orden de detección en la escena.")]
    public bool drawGizmos = true;
    [Tooltip("Dibuja también cuando el objeto no está seleccionado.")]
    public bool drawWhenNotSelected = true;
    [Tooltip("Dibuja líneas conectando los puntos en orden.")]
    public bool drawLines = true;
    [Tooltip("Muestra índices sobre cada punto (1,2,3,...).")]
    public bool drawIndices = true;
    public Color gizmoPointColor = new Color(1f, 0.9f, 0.1f, 1f);
    public Color gizmoLineColor = new Color(1f, 0.85f, 0.1f, 1f);
    [Min(0f)] public float pointRadius = 0.006f;
    [Min(0f)] public float labelUpOffset = 0.015f;

    [Header("Eventos Vuforia")]
    [Tooltip("Usar eventos de Vuforia (OnTargetStatusChanged) para capturar el orden temporal real de detección.")]
    public bool useObserverEvents = true;

    [Header("Frozen points")]
    [Tooltip("Crea un punto 'congelado' independiente cuando se detecta por primera vez (no se mueve aunque se pierda el tracking).")]
    public bool freezeOnDetection = true;
    [Tooltip("Raíz donde se instanciarán los puntos congelados; si está vacío y se permite, se crea automáticamente.")]
    public Transform frozenRoot;
    [Tooltip("Crear automáticamente un hijo 'FrozenPoints' si no hay raíz asignada.")]
    public bool autoCreateFrozenRoot = true;
    public enum FrozenParent { WorldRoot, UnderManager, Custom }
    [Tooltip("Dónde anclar los puntos congelados para que NO se muevan con la AR: WorldRoot (recomendado), bajo este manager, o un padre custom.")]
    public FrozenParent frozenParent = FrozenParent.WorldRoot;
    [Tooltip("Padre custom para puntos congelados si eliges 'Custom'.")]
    public Transform customFrozenParent;
    [Tooltip("Dibuja gizmos para los puntos congelados.")]
    public bool drawFrozenGizmos = true;
    public Color frozenPointColor = new Color(0.1f, 0.9f, 1f, 1f);
    [Tooltip("Dibuja líneas entre puntos congelados en orden.")]
    public bool drawFrozenLines = true;
    public Color frozenLineColor = new Color(0.1f, 0.9f, 1f, 1f);

    [Header("Resets")]
    [Tooltip("Si se pierde todo el tracking, conservar el orden (recomendado con freeze).")]
    public bool keepOrderWhenAllLost = true;

    /// <summary>Evento que se dispara cuando cambia el orden (por ejemplo, una nueva detección).</summary>
    public event Action OnOrderChanged;
    /// <summary>Evento por nueva detección individual; entrega el _tgt_point añadido.</summary>
   public event Action<Transform> OnNewDetection;

    class Entry
    {
        public Transform realTarget;
        public Transform point; // TargetX/_tgt_point
        public bool wasVisible;
        public int siblingIndex;
        public string name;         // Unity object name (fallback)
        public string vuforiaName;  // Vuforia ObserverBehaviour.TargetName (si disponible)
    }

    readonly List<Entry> entries = new();
    readonly List<Transform> orderedPoints = new();
    readonly List<string> orderedNames = new();
    readonly List<Transform> frozenPoints = new();
#if VUFORIA_PRESENT
    readonly System.Collections.Generic.Dictionary<Vuforia.ObserverBehaviour, Entry> observerMap = new();
#endif
    readonly System.Collections.Generic.Dictionary<Entry, Transform> frozenMap = new();

    void Awake()
    {
        if (!targetsRoot)
        {
            var go = GameObject.Find("Targets");
            if (go) targetsRoot = go.transform;
        }
        RebuildEntries();
    }

    void OnEnable()
    {
#if VUFORIA_PRESENT
    if (useObserverEvents) SubscribeObserverEvents();
    // Si no hay observers (o no usamos eventos), añadimos ya en el primer muestreo
    bool addNow = !useObserverEvents || observerMap.Count == 0;
    SampleVisibility(updateOrder: addNow);
#else
    // Sin Vuforia: añadir inmediatamente según la fuente de visibilidad
    SampleVisibility(updateOrder: true);
#endif
    }

    void Update()
    {
        if (updateEveryFrame) SampleVisibility(updateOrder: true);
    }

    void OnDisable()
    {
#if VUFORIA_PRESENT
        UnsubscribeObserverEvents();
#endif
    }

    /// <summary>
    /// Reconstruye la lista interna de candidatos desde <c>targetsRoot</c>.
    /// Llama a esto si añades/eliminas hijos en tiempo de ejecución.
    /// </summary>
    public void RebuildEntries()
    {
#if VUFORIA_PRESENT
        UnsubscribeObserverEvents();
#endif
        entries.Clear();
        if (!targetsRoot) return;
        foreach (Transform child in targetsRoot)
        {
            if (!child) continue;
            Transform p = child.Find(pointChildName);
            if (!p)
            {
                // Búsqueda laxa
                foreach (Transform sub in child)
                {
                    string n = sub.name.ToLowerInvariant();
                    if (n == pointChildName.ToLowerInvariant() || n.Contains("tgt") || n.Contains("targetpoint"))
                    { p = sub; break; }
                }
            }
            if (!p) continue;
            string vname = child.name;
#if VUFORIA_PRESENT
            {
                var ob = child.GetComponent<Vuforia.ObserverBehaviour>();
                if (!ob && p) ob = p.GetComponentInParent<Vuforia.ObserverBehaviour>();
                if (ob != null && !string.IsNullOrEmpty(ob.TargetName)) vname = ob.TargetName;
            }
#endif
            entries.Add(new Entry
            {
                realTarget = child,
                point = p,
                wasVisible = false,
                siblingIndex = child.GetSiblingIndex(),
                name = child.name,
                vuforiaName = vname
            });
        }
        if (logDebug) Debug.Log($"[DetectMgr] Entradas reconstruidas: {entries.Count}");
#if VUFORIA_PRESENT
    if (useObserverEvents) SubscribeObserverEvents();
#endif
    }

    /// <summary>
    /// Obtiene la lista en orden de detección (copia) de los <c>_tgt_point</c>.
    /// </summary>
    public List<Transform> GetOrderedPoints()
    {
        return new List<Transform>(orderedPoints);
    }

    /// <summary>
    /// Obtiene la lista de nombres (Vuforia si existe; si no, nombre del objeto) en orden de detección.
    /// </summary>
    public List<string> GetOrderedNames()
    {
        return new List<string>(orderedNames);
    }

    /// <summary>Devuelve los puntos congelados en orden de detección.</summary>
    public List<Transform> GetFrozenPoints()
    {
        return new List<Transform>(frozenPoints);
    }

    /// <summary>Limpia todos los puntos congelados (no afecta a entradas ni orden).</summary>
    public void ClearFrozen()
    {
        foreach (var t in frozenPoints) if (t) DestroyImmediateSafe(t.gameObject);
        frozenPoints.Clear();
        frozenMap.Clear();
    }

    /// <summary>
    /// Limpia el orden y estados de visibilidad acumulados (no toca las entradas).
    /// </summary>
    public void ClearOrder()
    {
        orderedPoints.Clear();
        orderedNames.Clear();
        if (logDebug) Debug.Log("[DetectMgr] Orden limpiado");
        OnOrderChanged?.Invoke();
    }

    /// <summary>
    /// Devuelve el último punto detectado (o null si no hay).
    /// </summary>
    public Transform GetLastDetectedPoint()
    {
        return orderedPoints.Count > 0 ? orderedPoints[^1] : null;
    }

    void SampleVisibility(bool updateOrder)
    {
        if (targetsRoot && targetsRoot.childCount != entries.Count)
        {
            RebuildEntries();
        }

    bool anyVisible = false;
    bool changed = false;
    bool usingEvents = false;
#if VUFORIA_PRESENT
    usingEvents = useObserverEvents && observerMap.Count > 0; // si no hay observers, caer a polling
#endif

        foreach (var e in entries)
        {
            bool nowVisible = IsVisible(e.realTarget, e.point);
            anyVisible |= nowVisible;

            // flanco de subida = nueva detección si no estaba antes visible
            if (updateOrder && nowVisible && !e.wasVisible && !usingEvents)
            {
                if (!orderedPoints.Contains(e.point))
                {
                    orderedPoints.Add(e.point);
                    orderedNames.Add(string.IsNullOrEmpty(e.vuforiaName) ? e.name : e.vuforiaName);
                    if (freezeOnDetection) CreateFrozenIfNeeded(e);
                    changed = true;
                    if (logDebug) Debug.Log($"[DetectMgr] Nueva detección: {e.name}");
                    OnNewDetection?.Invoke(e.point);
                }
            }
            e.wasVisible = nowVisible;
        }

        if (resetOrderWhenAllLost && !anyVisible && orderedPoints.Count > 0 && !keepOrderWhenAllLost)
        {
            orderedPoints.Clear();
            orderedNames.Clear();
            changed = true;
            if (logDebug) Debug.Log("[DetectMgr] Reset por todos perdidos");
        }

        if (changed) OnOrderChanged?.Invoke();
    }

#if VUFORIA_PRESENT
    void SubscribeObserverEvents()
    {
        observerMap.Clear();
        foreach (var e in entries)
        {
            var ob = e.realTarget ? e.realTarget.GetComponent<Vuforia.ObserverBehaviour>() : null;
            if (!ob && e.point) ob = e.point.GetComponentInParent<Vuforia.ObserverBehaviour>();
            if (ob != null && !observerMap.ContainsKey(ob))
            {
                observerMap.Add(ob, e);
                ob.OnTargetStatusChanged += OnTargetStatusChanged;
            }
        }
        if (logDebug) Debug.Log($"[DetectMgr] Suscritos {observerMap.Count} observers");
    }

    void UnsubscribeObserverEvents()
    {
        foreach (var kv in observerMap)
        {
            if (kv.Key != null) kv.Key.OnTargetStatusChanged -= OnTargetStatusChanged;
        }
        observerMap.Clear();
    }

    void OnTargetStatusChanged(Vuforia.ObserverBehaviour behaviour, Vuforia.TargetStatus status)
    {
        if (!useObserverEvents) return;
        // robusto frente a versiones de Vuforia que no definen DETECTED
        var s = status.Status;
        bool isTracked = s == Vuforia.Status.TRACKED || s == Vuforia.Status.EXTENDED_TRACKED;
        bool isDetectedLike = (s != Vuforia.Status.NO_POSE);
        if (!(isTracked || isDetectedLike)) return;
        if (observerMap.TryGetValue(behaviour, out var e))
        {
            if (!orderedPoints.Contains(e.point))
            {
                orderedPoints.Add(e.point);
                orderedNames.Add(string.IsNullOrEmpty(e.vuforiaName) ? e.name : e.vuforiaName);
                if (freezeOnDetection) CreateFrozenIfNeeded(e);
                e.wasVisible = true;
                if (logDebug) Debug.Log($"[DetectMgr] Nueva detección (evento): {e.name}");
                OnNewDetection?.Invoke(e.point);
                OnOrderChanged?.Invoke();
            }
        }
    }
#endif

    void CreateFrozenIfNeeded(Entry e)
    {
        if (frozenMap.ContainsKey(e) && frozenMap[e]) return;
        EnsureFrozenRoot();
        var go = new GameObject($"frozen_{(string.IsNullOrEmpty(e.vuforiaName) ? e.name : e.vuforiaName)}");
        var t = go.transform;
        t.SetPositionAndRotation(e.point.position, e.point.rotation);
        // Parent según preferencia (mantener posición mundial)
        t.SetParent(frozenRoot, worldPositionStays: true);
        frozenMap[e] = t;
        frozenPoints.Add(t);
    }

    void EnsureFrozenRoot()
    {
        if (frozenRoot) return;
        // Determinar padre preferido
        Transform pref = null;
        switch (frozenParent)
        {
            case FrozenParent.UnderManager: pref = this.transform; break;
            case FrozenParent.Custom: pref = customFrozenParent; break;
            case FrozenParent.WorldRoot:
            default: pref = null; break;
        }

        if (!autoCreateFrozenRoot)
        {
            // Usar el preferido (puede ser null => mundo)
            frozenRoot = pref; // puede quedar null (SetParent(null) usará raíz del mundo)
            return;
        }

        var go = new GameObject("FrozenPoints");
        if (pref)
            go.transform.SetParent(pref, false);
        else
            go.transform.SetParent(null, false); // raíz del mundo
        frozenRoot = go.transform;
    }

    static void DestroyImmediateSafe(GameObject go)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(go);
        else UnityEngine.Object.Destroy(go);
#else
        UnityEngine.Object.Destroy(go);
#endif
    }

    bool IsVisible(Transform realTarget, Transform point)
    {
#if VUFORIA_PRESENT
    Vuforia.ObserverBehaviour ob = null;
    if (realTarget) ob = realTarget.GetComponent<Vuforia.ObserverBehaviour>();
    if (!ob && point) ob = point.GetComponentInParent<Vuforia.ObserverBehaviour>();
    if (!ob && realTarget) ob = realTarget.GetComponentInChildren<Vuforia.ObserverBehaviour>();
        if (requireVuforiaForOrder)
        {
            // Requiere observer y estado de Vuforia
            if (ob == null) return false;
            var s = ob.TargetStatus.Status;
            if (s == Vuforia.Status.TRACKED || s == Vuforia.Status.EXTENDED_TRACKED) return true;
            // admitir cualquier estado distinto de NO_POSE como "detectado" en versiones antiguas/nuevas
            if (s != Vuforia.Status.NO_POSE) return true;
            return false;
        }

        if (visibilitySource == VisibilitySource.ActiveHierarchy)
            return point && point.gameObject.activeInHierarchy;
        if (ob != null)
        {
            var s = ob.TargetStatus.Status;
            if (s == Vuforia.Status.TRACKED || s == Vuforia.Status.EXTENDED_TRACKED) return true;
            if (visibilitySource == VisibilitySource.AutoPreferVuforia && s != Vuforia.Status.NO_POSE) return true; // ayuda en fallback
            return false;
        }
        // Sin Observer
        if (visibilitySource == VisibilitySource.AutoPreferVuforia)
            return point && point.gameObject.activeInHierarchy;
        return false; // VuforiaOnly sin Observer => no visible
#else
        // Sin Vuforia compilada
        if (visibilitySource == VisibilitySource.VuforiaOnly || requireVuforiaForOrder) return false;
        return point && point.gameObject.activeInHierarchy;
#endif
    }

    void OnDrawGizmosSelected() => DrawGizmosImpl(selected: true);
    void OnDrawGizmos() => DrawGizmosImpl(selected: false);

    void DrawGizmosImpl(bool selected)
    {
        if (!drawGizmos) return;
        if (!selected && !drawWhenNotSelected) return;
        if (orderedPoints == null || orderedPoints.Count == 0) return;

        // Dibuja esferas e índices
        Gizmos.color = gizmoPointColor;
        for (int i = 0; i < orderedPoints.Count; i++)
        {
            var pt = orderedPoints[i];
            if (!pt) continue;
            var pos = pt.position + Vector3.up * (pointRadius * 0.8f);
            Gizmos.DrawSphere(pos, Mathf.Max(1e-4f, pointRadius));
#if UNITY_EDITOR
            if (drawIndices)
            {
                UnityEditor.Handles.color = gizmoPointColor;
                UnityEditor.Handles.Label(pt.position + Vector3.up * Mathf.Max(1e-4f, labelUpOffset), (i + 1).ToString());
            }
#endif
        }

        // Dibuja líneas entre puntos consecutivos
        if (drawLines && orderedPoints.Count >= 2)
        {
#if UNITY_EDITOR
            UnityEditor.Handles.color = gizmoLineColor;
            for (int i = 0; i < orderedPoints.Count - 1; i++)
            {
                var a = orderedPoints[i];
                var b = orderedPoints[i + 1];
                if (!a || !b) continue;
                UnityEditor.Handles.DrawAAPolyLine(2.5f, new Vector3[] { a.position, b.position });
            }
#else
            Gizmos.color = gizmoLineColor;
            for (int i = 0; i < orderedPoints.Count - 1; i++)
            {
                var a = orderedPoints[i];
                var b = orderedPoints[i + 1];
                if (!a || !b) continue;
                Gizmos.DrawLine(a.position, b.position);
            }
#endif
        }

        if (drawFrozenGizmos && frozenPoints.Count > 0)
        {
            Gizmos.color = frozenPointColor;
            foreach (var fp in frozenPoints)
            {
                if (!fp) continue;
                Gizmos.DrawCube(fp.position + Vector3.up * (pointRadius * 0.5f), Vector3.one * (pointRadius * 0.9f));
            }
            if (drawFrozenLines && frozenPoints.Count >= 2)
            {
#if UNITY_EDITOR
                UnityEditor.Handles.color = frozenLineColor;
                for (int i = 0; i < frozenPoints.Count - 1; i++)
                {
                    var a = frozenPoints[i];
                    var b = frozenPoints[i + 1];
                    if (!a || !b) continue;
                    UnityEditor.Handles.DrawAAPolyLine(2.5f, new Vector3[] { a.position, b.position });
                }
#else
                Gizmos.color = frozenLineColor;
                for (int i = 0; i < frozenPoints.Count - 1; i++)
                {
                    var a = frozenPoints[i];
                    var b = frozenPoints[i + 1];
                    if (!a || !b) continue;
                    Gizmos.DrawLine(a.position, b.position);
                }
#endif
            }
        }
    }
}
