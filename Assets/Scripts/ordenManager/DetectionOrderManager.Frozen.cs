using System.Collections.Generic;
using UnityEngine;

public partial class DetectionOrderManager : MonoBehaviour
{
    readonly List<Transform> frozenPoints = new();
    readonly System.Collections.Generic.Dictionary<Entry, Transform> frozenMap = new();

    public List<Transform> GetFrozenPoints()
    {
        return new List<Transform>(frozenPoints);
    }

    public void ClearFrozen()
    {
        foreach (var t in frozenPoints) if (t) DestroyImmediateSafe(t.gameObject);
        frozenPoints.Clear();
        frozenMap.Clear();
    }

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
}
