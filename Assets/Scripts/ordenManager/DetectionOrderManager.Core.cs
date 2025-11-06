using System;
using System.Collections.Generic;
using UnityEngine;

// Núcleo: campos públicos, eventos, Entry, listas y funciones básicas.
public partial class DetectionOrderManager : MonoBehaviour
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

    // Entrada interna para cada Target
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

    // Mapas relacionados con frozen points se mantienen en el partial Frozen

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
        bool addNow = !useObserverEvents || (observerMap != null && observerMap.Count == 0);
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

    /// <summary>
    /// Devuelve el último punto detectado (o null si no hay).
    /// </summary>
    public Transform GetLastDetectedPoint()
    {
        return orderedPoints.Count > 0 ? orderedPoints[^1] : null;
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

    // SampleVisibility está implementado en el partial Visibility
}
