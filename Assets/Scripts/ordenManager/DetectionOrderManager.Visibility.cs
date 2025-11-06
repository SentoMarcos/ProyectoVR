using System;
using System.Collections.Generic;
using UnityEngine;

public partial class DetectionOrderManager : MonoBehaviour
{
#if VUFORIA_PRESENT
    // Mapa observer -> Entry para eventos
    readonly System.Collections.Generic.Dictionary<Vuforia.ObserverBehaviour, Entry> observerMap = new();
#endif

    // SampleVisibility revisa entradas y actualiza orden (llama CreateFrozenIfNeeded definido en Frozen partial)
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
}
