using System.Collections.Generic;
using UnityEngine;

public partial class RoadFromTargetsSticky
{
        readonly List<ProxyState> proxies = new();
        Transform virtualRoot;

        // Plano bloqueado
        bool planeLocked = false;
        Vector3 lockedUp = Vector3.up;
        Vector3 lockedPoint = Vector3.zero;
    // Tras un reset, suprime la regeneración hasta detectar al menos dos puntos válidos
    bool suppressUntilNewDetection = false;
    int _detectionsSinceReset = 0;
    bool _waitingNewDetections = false;

    // Height tint property block (for Custom/RoadHeightTintURP or compatible)
    MaterialPropertyBlock _mpb; // lazy-inited
    static readonly int _AxisId = Shader.PropertyToID("_Axis");
    static readonly int _BasePointId = Shader.PropertyToID("_BasePoint");
    static readonly int _HMinId = Shader.PropertyToID("_HeightMin");
    static readonly int _HMaxId = Shader.PropertyToID("_HeightMax");
    static readonly int _HeightContrastId = Shader.PropertyToID("_HeightContrast");
    static readonly int _ValleyDarkenId = Shader.PropertyToID("_ValleyDarken");
    static readonly int _CrestLightId = Shader.PropertyToID("_CrestLight");
    static readonly int _CrestWidthId = Shader.PropertyToID("_CrestWidth");

        void Awake()
        {
            mf = GetComponent<MeshFilter>();
            mesh = new Mesh { name = "RoadMesh" };
            mf.sharedMesh = mesh;

            mr = GetComponent<MeshRenderer>();
            EnsureRoadMaterial();

            // Helper para asegurar componentes (usa semántica null de Unity)
            T GetOrAdd<T>(Transform tr) where T : Component
            {
                var comp = tr.GetComponent<T>();
                if (comp == null) comp = tr.gameObject.AddComponent<T>();
                return comp;
            }

            // Hijo para líneas de carril
            var linesTr = transform.Find("RoadLines");
            if (!linesTr)
            {
                var go = new GameObject("RoadLines");
                go.transform.SetParent(transform, false);
                linesTr = go.transform;
            }
            linesMf = GetOrAdd<MeshFilter>(linesTr);
            linesMr = GetOrAdd<MeshRenderer>(linesTr);
            linesMesh = linesMesh ?? new Mesh { name = "RoadLinesMesh" };
            linesMf.sharedMesh = linesMesh;
            if (linesMr != null && linesMr.sharedMaterial == null)
            {
                if (!laneLineMaterial)
                {
                    Shader shL = Shader.Find("Universal Render Pipeline/Unlit");
                    if (!shL) shL = Shader.Find("Unlit/Color");
                    laneLineMaterial = new Material(shL);
                }
                if (laneLineMaterial.HasProperty("_BaseColor")) laneLineMaterial.SetColor("_BaseColor", laneLineColor);
                if (laneLineMaterial.HasProperty("_Color"))     laneLineMaterial.SetColor("_Color",     laneLineColor);
                if (laneLineMaterial.HasProperty("_Surface"))   laneLineMaterial.SetInt("_Surface", 0); // Opaque
                if (laneLineMaterial.HasProperty("_ZWrite"))    laneLineMaterial.SetInt("_ZWrite", 1);
                if (laneLineTexture)
                {
                    if (laneLineMaterial.HasProperty("_BaseMap")) laneLineMaterial.SetTexture("_BaseMap", laneLineTexture);
                    if (laneLineMaterial.HasProperty("_MainTex")) laneLineMaterial.SetTexture("_MainTex", laneLineTexture);
                }
                linesMr.sharedMaterial = laneLineMaterial;
            }

            // Sombra
            var shadowTr = transform.Find("RoadShadow");
            if (!shadowTr)
            {
                var go = new GameObject("RoadShadow");
                go.transform.SetParent(transform, false);
                shadowTr = go.transform;
            }
            shadowMf = GetOrAdd<MeshFilter>(shadowTr);
            shadowMr = GetOrAdd<MeshRenderer>(shadowTr);
            shadowMesh = shadowMesh ?? new Mesh { name = "RoadShadowMesh" };
            shadowMf.sharedMesh = shadowMesh;
            if (shadowMr != null && shadowMr.sharedMaterial == null)
            {
                if (!shadowMaterial)
                {
                    Shader shS = Shader.Find("Universal Render Pipeline/Unlit");
                    if (!shS) shS = Shader.Find("Unlit/Color");
                    shadowMaterial = new Material(shS);
                    if (shadowMaterial.HasProperty("_BaseColor")) shadowMaterial.SetColor("_BaseColor", shadowColor);
                    if (shadowMaterial.HasProperty("_Color"))     shadowMaterial.SetColor("_Color",     shadowColor);
                    shadowMaterial.SetInt("_Surface", 1); // Transparent
                    shadowMaterial.SetInt("_ZWrite", 0);  // No depth write to avoid z-fighting as overlay; se ocultará al fijar
                    shadowMaterial.SetInt("_Cull", 0);
                    shadowMaterial.SetInt("_CullMode", 0);
                    shadowMaterial.renderQueue = 2990;
                }
                shadowMr.sharedMaterial = shadowMaterial;
            }

            if (!targetsRoot)
            {
                var go = GameObject.Find("Targets");
                if (go) targetsRoot = go.transform;
            }

            // Virtual root para proxies
            var existingVR = transform.Find("VirtualTargets");
            if (existingVR != null) virtualRoot = existingVR;
            else
            {
                var vr = new GameObject("VirtualTargets");
                vr.transform.SetParent(transform, false);
                virtualRoot = vr.transform;
            }

            BuildProxyList();
        }

        void Start() => Tick();

        void OnEnable()
        {
            if (detectionOrderManager != null && regenerateOnOrderChanged)
                detectionOrderManager.OnOrderChanged += Tick;
            if (detectionOrderManager != null)
                detectionOrderManager.OnNewDetection += HandleNewDetectionAfterReset;
        }
        void OnDisable()
        {
            if (detectionOrderManager != null)
                detectionOrderManager.OnOrderChanged -= Tick;
            if (detectionOrderManager != null)
                detectionOrderManager.OnNewDetection -= HandleNewDetectionAfterReset;
        }

    // Reinicia completamente la carretera y su estado interno
    [ContextMenu("Reset Road")]
    public void ResetRoad()
        {
            // Desbloquear plano y parenting
            planeLocked = false;
            lockedUp = Vector3.up;
            lockedPoint = Vector3.zero;
            if (_prevParentOnFreeze != null)
            {
                transform.SetParent(_prevParentOnFreeze, true);
                _prevParentOnFreeze = null;
            }
            manualFreeze = false;

            // Limpiar caches y geometría
            lastUsedControlPoints.Clear();
            lastCenterline.Clear();
            lastCumulative = new List<float>();
            lastClosed = false;
            lastTotalWidth = 0f;
            lastParamHash = 0;
            lastCtrlPositionsCache.Clear();

            // Limpiar mallas
            if (mesh != null) { mesh.Clear(); }
            if (linesMesh != null) { linesMesh.Clear(); }
            if (shadowMesh != null) { shadowMesh.Clear(); }

            // Destruir proxies virtuales y reconstruir lista
            if (virtualRoot != null)
            {
                var toDel = new List<GameObject>();
                foreach (Transform c in virtualRoot) if (c) toDel.Add(c.gameObject);
                foreach (var go in toDel)
                {
                    if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
                }
            }
            proxies.Clear();

            // Limpiar estado del DetectionOrderManager (orden y puntos congelados) para permitir nueva colocación
            if (detectionOrderManager != null)
            {
                detectionOrderManager.ClearOrder();
                detectionOrderManager.ClearFrozen();
                detectionOrderManager.RebuildEntries();
            }

            BuildProxyList();

            // Reforzar material compatible
            EnsureRoadMaterial();

            // Comportamiento tras reset controlado por flag inspector
            if (waitForNewDetectionsAfterReset)
            {
                suppressUntilNewDetection = true;
                _waitingNewDetections = true;
                _detectionsSinceReset = 0;
                if (mr) mr.enabled = false;
                if (shadowMr) shadowMr.enabled = false;
                if (linesMr) linesMr.enabled = false;
            }
            else
            {
                // Fallback inmediato: si ya hay suficientes puntos, regenerar al instante; si no, esperar como antes
                if (HasAtLeastTwoValidPoints())
                {
                    if (mr) mr.enabled = !hideRoadMesh;
                    if (shadowMr) shadowMr.enabled = addUnderShadow && !hideRoadMesh;
                    if (linesMr) linesMr.enabled = enableLaneLines && !hideRoadMesh;
                    Tick();
                }
                else
                {
                    suppressUntilNewDetection = true;
                    _waitingNewDetections = true;
                    _detectionsSinceReset = 0;
                    if (mr) mr.enabled = false;
                    if (shadowMr) shadowMr.enabled = false;
                    if (linesMr) linesMr.enabled = false;
                }
            }
        }
        void Update()
        {
            // Detect toggles at runtime to perform attach/detach just once
            if (manualFreeze)
            {
                EnsureFrozenState();
                return;
            }
            else
            {
                EnsureUnfrozenState();
            }
            // Si venimos de un reset, esperar a nuevas detecciones explícitas o, en su defecto,
            // reactivar cuando haya al menos dos puntos válidos disponibles
            if (suppressUntilNewDetection)
            {
                if (mr) mr.enabled = false;
                if (shadowMr) shadowMr.enabled = false;
                if (linesMr) linesMr.enabled = false;
                // Fallback: algunos flujos no emiten OnNewDetection (p.ej. sin Vuforia o con polling)
                // Sólo aplicarlo si NO estamos esperando explícitamente nuevas detecciones
                if (!waitForNewDetectionsAfterReset && HasAtLeastTwoValidPoints())
                {
                    suppressUntilNewDetection = false;
                    _waitingNewDetections = false;
                    if (mr) mr.enabled = !hideRoadMesh;
                    if (shadowMr) shadowMr.enabled = addUnderShadow && !hideRoadMesh;
                    if (linesMr) linesMr.enabled = enableLaneLines && !hideRoadMesh;
                    Tick();
                }
                return;
            }
            if (updateIfChange) Tick();
        }

        void Tick()
        {
            if (manualFreeze) return;
            if (!targetsRoot)
            {
                if (logWhenNoPoints) Debug.LogWarning("[Road] No se encontró 'Targets'.");
                return;
            }
            List<Transform> pts;
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
                    if (targetsRoot.childCount != proxies.Count) BuildProxyList();
                    UpdateProxiesSticky();
                    var backup = GetStableProxyPointsOrdered();
                    if (backup.Count >= 2) pts = backup;
                }
            }
            else
            {
                if (targetsRoot.childCount != proxies.Count) BuildProxyList();
                UpdateProxiesSticky();
                pts = GetStableProxyPointsOrdered();
            }

            if (pts.Count < 2)
            {
                if (logWhenNoPoints) Debug.Log($"[Road] Menos de 2 puntos; encontrados={pts.Count}. Raíz={(targetsRoot? targetsRoot.name : "<null>")} childCount={(targetsRoot? targetsRoot.childCount : 0)}");
                lastUsedControlPoints.Clear();
                return;
            }

            var finalPts = ApplyConnectMode(pts);
            bool bigChange = IsBigChange(finalPts, lastCtrlPositionsCache, minRebuildPosDeltaMeters);
            int paramHash = ComputeParamHash();
            bool paramChanged = (paramHash != lastParamHash);
            if (!paramChanged && !bigChange)
            {
                lastUsedControlPoints.Clear();
                if (finalPts != null) lastUsedControlPoints.AddRange(finalPts);
                if (mr) mr.enabled = !hideRoadMesh;
                if (shadowMr) shadowMr.enabled = addUnderShadow && !hideRoadMesh;
                if (linesMr) linesMr.enabled = enableLaneLines && !hideRoadMesh;
                return;
            }
            RenderOrCache(finalPts);
            lastCtrlPositionsCache.Clear();
            if (finalPts != null)
            {
                for (int i = 0; i < finalPts.Count; i++)
                {
                    var t = finalPts[i]; if (t) lastCtrlPositionsCache.Add(t.position);
                }
            }
            lastParamHash = paramHash;
        }

        void EnsureFrozenState()
        {
            if (_prevParentOnFreeze == null)
            {
                // Primera vez: bloquear plano si procede
                if (lockPlaneOnFreeze && !planeLocked && lastCenterline != null && lastCenterline.Count >= 3)
                {
                    // Calcula normal y punto a partir de la última geometría
                    var up = lastUpVec.sqrMagnitude > 1e-6f ? lastUpVec : Vector3.up;
                    lockedUp = up.normalized;
                    lockedPoint = (lastCenterline != null && lastCenterline.Count > 0) ? lastCenterline[0] : transform.position;
                    planeLocked = true;
                }

                if (detachFromParentOnFreeze)
                {
                    _prevParentOnFreeze = transform.parent;
                    Transform newParent = freezeParentOverride ? freezeParentOverride : null; // a raíz si null
                    transform.SetParent(newParent, true); // mantiene world pose
                }

                // Para evitar apariencia de overlay, desactiva sombreado mientras está congelado
                if (shadowMr) shadowMr.enabled = false;
            }
        }

        void EnsureUnfrozenState()
        {
            if (_prevParentOnFreeze != null)
            {
                // Restaurar el parent original cuando se des-fija
                transform.SetParent(_prevParentOnFreeze, true);
                _prevParentOnFreeze = null;
            }
            if (shadowMr) shadowMr.enabled = addUnderShadow && !hideRoadMesh;
        }

        int ComputeParamHash()
        {
            int h = 17;
            unchecked
            {
                h = h * 31 + samplesPerSegment;
                h = h * 31 + (flattenToTargetsPlane ? 1 : 0);
                h = h * 31 + (forceTargetsSameHeight ? 1 : 0);
                h = h * 31 + heightReferenceMode.GetHashCode();
                h = h * 31 + laneCount;
                h = h * 31 + widthMode.GetHashCode();
                h = h * 31 + laneWidthMeters.GetHashCode();
                h = h * 31 + laneWidthFraction.GetHashCode();
                h = h * 31 + maxWidthVsMinSeg.GetHashCode();
                h = h * 31 + minTotalWidthMeters.GetHashCode();
                h = h * 31 + maxTotalWidthMeters.GetHashCode();
                h = h * 31 + (adaptiveWidthInCurves ? 1 : 0);
                h = h * 31 + minWidthScaleAtSharpTurn.GetHashCode();
                h = h * 31 + angleForMinWidth.GetHashCode();
                h = h * 31 + angleStartNarrow.GetHashCode();
                h = h * 31 + (useRoundedJoins ? 1 : 0);
                h = h * 31 + roundSegmentsPer90;
                h = h * 31 + (rotateSeamToLowestCurvature ? 1 : 0);
                h = h * 31 + surfaceOffset.GetHashCode();
                h = h * 31 + (enableLaneLines ? 1 : 0);
                h = h * 31 + centerLineWidthMeters.GetHashCode();
                h = h * 31 + edgeLineWidthMeters.GetHashCode();
                h = h * 31 + lineUvTilesPerMeter.GetHashCode();
                h = h * 31 + (centerLinesDashed ? 1 : 0);
                h = h * 31 + dashLengthMeters.GetHashCode();
                h = h * 31 + gapLengthMeters.GetHashCode();
                h = h * 31 + dashOffsetMeters.GetHashCode();
                h = h * 31 + linesLiftOffsetMeters.GetHashCode();
                h = h * 31 + (addUnderShadow ? 1 : 0);
                h = h * 31 + shadowExtraWidthMeters.GetHashCode();
                h = h * 31 + shadowUnderOffset.GetHashCode();
                h = h * 31 + shadowColor.GetHashCode();
                h = h * 31 + outerLaneEdgeMargin.GetHashCode();
            }
            return h;
        }

        static bool IsBigChange(List<Transform> a, List<Vector3> last, float threshold)
        {
            if (a == null || a.Count == 0) return false;
            if (last == null || last.Count == 0) return true;
            if (a.Count != last.Count) return true;
            float th = Mathf.Max(0f, threshold);
            float max = 0f;
            for (int i = 0; i < a.Count; i++)
            {
                var t = a[i]; if (!t) continue;
                float d = Vector3.Distance(t.position, last[i]);
                if (d > max) max = d;
                if (max >= th) return true;
            }
            return false;
        }

        void TryPushHeightTintParams(MeshRenderer targetMr)
        {
            if (!targetMr) return;
            var mat = targetMr.sharedMaterial; if (!mat) return;
            if (!(mat.HasProperty(_AxisId) && mat.HasProperty(_BasePointId) && mat.HasProperty(_HMinId) && mat.HasProperty(_HMaxId))) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            targetMr.GetPropertyBlock(_mpb);
            Vector3 axis = (exaggerationAxis == ExaggerationAxisMode.WorldUp) ? Vector3.up : (planeLocked ? lockedUp : lastUpVec);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
            _mpb.SetVector(_AxisId, new Vector4(axis.x, axis.y, axis.z, 0));
            Vector3 basePt = planeLocked ? lockedPoint : transform.position;
            _mpb.SetVector(_BasePointId, new Vector4(basePt.x, basePt.y, basePt.z, 0));
            float hMin = 0f, hMax = 1f;
            if (lastCenterline != null && lastCenterline.Count > 0)
            {
                hMin = 1e9f; hMax = -1e9f;
                for (int i = 0; i < lastCenterline.Count; i++)
                {
                    float h = Vector3.Dot(lastCenterline[i] - basePt, axis);
                    if (h < hMin) hMin = h; if (h > hMax) hMax = h;
                }
                if (Mathf.Abs(hMax - hMin) < 1e-4f) { hMin -= 0.5f; hMax += 0.5f; }
            }
            _mpb.SetFloat(_HMinId, hMin);
            _mpb.SetFloat(_HMaxId, hMax);
            if (mat.HasProperty(_HeightContrastId)) _mpb.SetFloat(_HeightContrastId, 0.6f);
            if (mat.HasProperty(_ValleyDarkenId)) _mpb.SetFloat(_ValleyDarkenId, 0.25f);
            if (mat.HasProperty(_CrestLightId)) _mpb.SetFloat(_CrestLightId, 0.15f);
            if (mat.HasProperty(_CrestWidthId)) _mpb.SetFloat(_CrestWidthId, 0.25f);
            targetMr.SetPropertyBlock(_mpb);
        }

        void EnsureRoadMaterial()
        {
            if (mr == null) return;
            bool need = (mr.sharedMaterial == null || mr.sharedMaterial.shader == null);
            if (!need && forceURPCompatibleMaterial)
            {
                // Detect magenta (broken) by sampling color if possible or missing pipeline tag
                Shader s = mr.sharedMaterial.shader;
                if (!s || s.name.Contains("Error") || s.name.Contains("Hidden/")) need = true;
            }
            // If we prefer the custom height shader, override unless it's already set
            if (preferHeightTintShader)
            {
                string sn = mr.sharedMaterial && mr.sharedMaterial.shader ? mr.sharedMaterial.shader.name : "";
                if (sn != "Custom/RoadHeightTintURP" && Shader.Find("Custom/RoadHeightTintURP")) need = true;
            }
            if (!need) return;
            Shader sh = null;
            if (preferHeightTintShader) sh = Shader.Find("Custom/RoadHeightTintURP");
            if (!sh || !sh.isSupported) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh || !sh.isSupported) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh || !sh.isSupported) sh = Shader.Find("Unlit/Color");
            if (!sh || !sh.isSupported) sh = Shader.Find("Standard");
            if (!sh || !sh.isSupported)
            {
                Debug.LogWarning("[Road] No se encontró un shader compatible URP. Usando color de respaldo.");
                return;
            }
            asphaltMaterial = new Material(sh);
            if (!asphaltMaterial.shader || !asphaltMaterial.shader.isSupported)
            {
                // Fallback a URP/Unlit si el shader resultante no está soportado
                var sh2 = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh2 && sh2.isSupported) asphaltMaterial.shader = sh2; else Debug.LogWarning("[Road] Shader no soportado. URP/Unlit no disponible.");
            }
            if (asphaltMaterial.HasProperty("_BaseColor")) asphaltMaterial.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.12f, 1f));
            if (asphaltMaterial.HasProperty("_Color"))     asphaltMaterial.SetColor("_Color",     new Color(0.12f, 0.12f, 0.12f, 1f));
            if (asphaltMaterial.HasProperty("_Surface"))   asphaltMaterial.SetInt("_Surface", 0); // Opaque
            if (asphaltMaterial.HasProperty("_ZWrite"))    asphaltMaterial.SetInt("_ZWrite", 1);
            asphaltMaterial.SetInt("_Cull", 0);
            asphaltMaterial.SetInt("_CullMode", 0);
            mr.sharedMaterial = asphaltMaterial;

            // Ensure children (lines/shadow) also use URP-compatible shaders to avoid magenta
            EnsureChildMaterial(linesMr, preferUnlit: true, tintColor: laneLineColor, tex: laneLineTexture);
            EnsureChildMaterial(shadowMr, preferUnlit: true, tintColor: shadowColor, tex: null, transparent: true);
        }

        void EnsureChildMaterial(MeshRenderer childMr, bool preferUnlit, Color? tintColor = null, Texture tex = null, bool transparent = false)
        {
            if (childMr == null) return;
            var mat = childMr.sharedMaterial;
            bool need = (mat == null || mat.shader == null);
            if (!need && forceURPCompatibleMaterial)
            {
                string sn = mat.shader ? mat.shader.name : "";
                // Common non-URP or error cases
                if (string.IsNullOrEmpty(sn) || sn.Contains("Error") || sn.Contains("Hidden/") || sn == "Standard") need = true;
            }
            if (!need) return;
            Shader sh = preferUnlit ? Shader.Find("Universal Render Pipeline/Unlit") : Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("Unlit/Color");
            if (!sh) return;
            var newMat = new Material(sh);
            if (tintColor.HasValue)
            {
                if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", tintColor.Value);
                if (newMat.HasProperty("_Color"))     newMat.SetColor("_Color",     tintColor.Value);
            }
            if (tex)
            {
                if (newMat.HasProperty("_BaseMap")) newMat.SetTexture("_BaseMap", tex);
                if (newMat.HasProperty("_MainTex")) newMat.SetTexture("_MainTex", tex);
            }
            if (newMat.HasProperty("_Surface")) newMat.SetInt("_Surface", transparent ? 1 : 0);
            if (newMat.HasProperty("_ZWrite"))  newMat.SetInt("_ZWrite", transparent ? 0 : 1);
            childMr.sharedMaterial = newMat;
        }

        void HandleNewDetectionAfterReset(Transform _)
        {
            if (!_waitingNewDetections) return;
            _detectionsSinceReset++;
            // Esperar al menos 2 nuevas detecciones para poder reconstruir un tramo
            if (_detectionsSinceReset >= 2)
            {
                suppressUntilNewDetection = false;
                _waitingNewDetections = false;
                if (mr) mr.enabled = !hideRoadMesh;
                if (shadowMr) shadowMr.enabled = addUnderShadow && !hideRoadMesh;
                if (linesMr) linesMr.enabled = enableLaneLines && !hideRoadMesh;
                Tick();
            }
        }

        bool HasAtLeastTwoValidPoints()
        {
            if (!targetsRoot) return false;
            // Prioridad: manager (congelados u ordenados)
            if (detectionOrderManager)
            {
                if (useFrozenPointsFromManager)
                {
                    var frozen = detectionOrderManager.GetFrozenPoints();
                    if (frozen != null && frozen.Count >= 2) return true;
                }
                var ordered = detectionOrderManager.GetOrderedPoints();
                if (ordered != null && ordered.Count >= 2) return true;
            }

            // Sin manager o sin suficientes, comprobar proxies reales/estables
            if (useRealPointsDirectly)
            {
                var pts = GetRealProxyPointsOrdered();
                if (pts != null && pts.Count >= 2) return true;
                if (targetsRoot.childCount != proxies.Count) BuildProxyList();
                UpdateProxiesSticky();
                var backup = GetStableProxyPointsOrdered();
                return backup != null && backup.Count >= 2;
            }
            else
            {
                if (targetsRoot.childCount != proxies.Count) BuildProxyList();
                UpdateProxiesSticky();
                var stable = GetStableProxyPointsOrdered();
                return stable != null && stable.Count >= 2;
            }
        }
}
