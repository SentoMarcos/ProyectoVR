using UnityEngine;

public partial class DetectionOrderManager : MonoBehaviour
{
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
