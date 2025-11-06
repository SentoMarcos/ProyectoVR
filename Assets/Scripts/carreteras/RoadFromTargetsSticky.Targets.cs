using System.Collections.Generic;
using UnityEngine;

public partial class RoadFromTargetsSticky
{
    class ProxyState
    {
        public Transform realTarget;
        public Transform realPoint;
        public Transform proxyPoint;
        public bool everStable = false;
        public int visibleFrames = 0;
        public int invisibleFrames = 0;
        public Vector3 stablePos;
        public Quaternion stableRot;
        public int orderIndex = int.MaxValue;
        public string name;
        public int tremorFramesAccum = 0;
    }

    void BuildProxyList()
    {
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
        planeLocked = false;
    }

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
                    if (freezeProxyAfterFirstStable) continue;
                    if ((candPos - st.stablePos).magnitude <= maxReacquireJump)
                    {
                        float delta = (candPos - st.stablePos).magnitude;
                        bool firstUpdate = st.proxyPoint.position == Vector3.zero;
                        float thr = Mathf.Max(0f, minDeltaToUpdate + (tremorFilter ? tremorDeadzoneMeters : 0f));
                        if ((delta >= thr) || firstUpdate)
                        {
                            bool allowNow = true;
                            if (tremorFilter && !firstUpdate)
                            {
                                st.tremorFramesAccum++;
                                allowNow = (st.tremorFramesAccum >= Mathf.Max(0, tremorHoldFrames));
                            }
                            if (allowNow)
                            {
                                st.tremorFramesAccum = 0;
                                float a = Mathf.Clamp01(updateLerp);
                                float b = tremorFilter ? Mathf.Clamp01(tremorLerp) : 1f;
                                float w = Mathf.Clamp01(a * b);
                                Vector3 target = Vector3.Lerp(st.stablePos, candPos, w);
                                if (tremorFilter && tremorMaxStepMeters > 0f)
                                {
                                    Vector3 step = target - st.stablePos;
                                    float maxStep = tremorMaxStepMeters;
                                    if (step.magnitude > maxStep) target = st.stablePos + step.normalized * maxStep;
                                }
                                st.stablePos = target;
                                st.stableRot = Quaternion.Slerp(st.stableRot, candRot, w);
                                st.proxyPoint.SetPositionAndRotation(st.stablePos, st.stableRot);
                            }
                        }
                        else if (tremorFilter)
                        {
                            st.tremorFramesAccum = 0;
                        }
                    }
                }
            }
            else
            {
                st.invisibleFrames++;
                if (tremorFilter) st.tremorFramesAccum = 0;
            }
        }
    }

    bool IsTargetVisible(Transform realTarget, Transform realPoint)
    {
#if VUFORIA_PRESENT
        Vuforia.ObserverBehaviour ob = null;
        if (realTarget) ob = realTarget.GetComponent<Vuforia.ObserverBehaviour>();
        if (!ob && realPoint) ob = realPoint.GetComponentInParent<Vuforia.ObserverBehaviour>();
        if (!ob && realTarget) ob = realTarget.GetComponentInChildren<Vuforia.ObserverBehaviour>();
        if (requireVuforiaTracking)
        {
            if (ob == null) return false;
            var s = ob.TargetStatus.Status;
            if (s == Vuforia.Status.TRACKED || s == Vuforia.Status.EXTENDED_TRACKED) return true;
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
        if (requireVuforiaTracking) return false;
#endif
        return realPoint && realPoint.gameObject.activeInHierarchy;
    }

    List<Transform> GetStableProxyPointsOrdered()
    {
        proxies.Sort((a, b) => a.orderIndex.CompareTo(b.orderIndex));
        var list = new List<Transform>();
        foreach (var st in proxies) if (st.everStable) list.Add(st.proxyPoint);
        return list;
    }

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
                foreach (Transform sub in child)
                {
                    string n = sub.name.ToLowerInvariant();
                    if (n == pointChildName.ToLowerInvariant() || n.Contains("tgt") || n.Contains("targetpoint"))
                    { rp = sub; break; }
                }
            }
            if (rp)
            {
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

    void RenderOrCache(List<Transform> controlPoints)
    {
        lastUsedControlPoints.Clear();
        if (controlPoints != null) lastUsedControlPoints.AddRange(controlPoints);
        if (!mr) mr = GetComponent<MeshRenderer>();
        if (hideRoadMesh)
        {
            if (mr) mr.enabled = false;
            if (shadowMr) shadowMr.enabled = false;
            return;
        }
        if (mr) mr.enabled = true;
        if (linesMr) linesMr.enabled = enableLaneLines;
        if (shadowMr) shadowMr.enabled = addUnderShadow;
        if (freezeRoadAfterPlaneLocked && planeLocked) return;
        GenerateRoad(controlPoints);
            TryPushHeightTintParams(mr);
            TryPushHeightTintParams(linesMr);
    }

    List<Transform> ApplyConnectMode(List<Transform> src)
    {
        if (src == null || src.Count == 0) return src;
        switch (connectMode)
        {
            case ConnectMode.FirstToLastOnly:
                if (src.Count >= 2) return new List<Transform> { src[0], src[src.Count - 1] };
                return new List<Transform>(src);
            case ConnectMode.LastTwoOnly:
                if (src.Count >= 2) return new List<Transform> { src[src.Count - 2], src[src.Count - 1] };
                return new List<Transform>(src);
            case ConnectMode.Sequential:
            default:
                return MaybeCloseLoopSequential(src);
        }
    }

    List<Transform> MaybeCloseLoopSequential(List<Transform> src)
    {
        if (!closeLoopWhenAtLeast3 || src == null || src.Count < 3) return src;
        var list = new List<Transform>(src.Count + 1);
        list.AddRange(src);
        list.Add(src[0]);
        return list;
    }
}
