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

        void Awake()
        {
            mf = GetComponent<MeshFilter>();
            mesh = new Mesh { name = "RoadMesh" };
            mf.sharedMesh = mesh;

            mr = GetComponent<MeshRenderer>();
            if (mr != null && (mr.sharedMaterial == null || mr.sharedMaterial.shader == null))
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (!sh) sh = Shader.Find("Unlit/Color");
                if (!sh) sh = Shader.Find("Standard");
                asphaltMaterial = new Material(sh);
                if (asphaltMaterial.HasProperty("_BaseColor")) asphaltMaterial.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.12f, 1f));
                if (asphaltMaterial.HasProperty("_Color"))     asphaltMaterial.SetColor("_Color",     new Color(0.12f, 0.12f, 0.12f, 1f));
                if (asphaltMaterial.HasProperty("_Surface"))   asphaltMaterial.SetInt("_Surface", 0); // Opaque
                if (asphaltMaterial.HasProperty("_ZWrite"))    asphaltMaterial.SetInt("_ZWrite", 1);
                asphaltMaterial.SetInt("_Cull", 0);
                asphaltMaterial.SetInt("_CullMode", 0);
                mr.sharedMaterial = asphaltMaterial;
            }

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
        }
        void OnDisable()
        {
            if (detectionOrderManager != null)
                detectionOrderManager.OnOrderChanged -= Tick;
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
}
