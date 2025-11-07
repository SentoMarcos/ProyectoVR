using System.Collections.Generic;
using UnityEngine;

public partial class RoadFromTargetsSticky
{
    void OnDrawGizmos()
    {
        if (!drawGizmosWhenNotSelected) return;
        DrawGizmosInternal(false);
    }

    void OnDrawGizmosSelected()
    {
        DrawGizmosInternal(true);
    }

    void DrawGizmosInternal(bool selected)
    {
        Color prev = Gizmos.color;

        // Centroide y plano bloqueado
        if (planeLocked)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.9f);
            Vector3 p = lockedPoint;
            Vector3 n = lockedUp.normalized;
            Gizmos.DrawLine(p, p + n * 0.3f);
            Vector3 r = Vector3.Cross(n, Vector3.forward);
            if (r.sqrMagnitude < 1e-6f) r = Vector3.right;
            r.Normalize();
            Vector3 f = Vector3.Cross(r, n).normalized;
            float s = 0.08f;
            Vector3 c0 = p + r * s + f * s;
            Vector3 c1 = p - r * s + f * s;
            Vector3 c2 = p - r * s - f * s;
            Vector3 c3 = p + r * s - f * s;
            Gizmos.DrawLine(c0, c1);
            Gizmos.DrawLine(c1, c2);
            Gizmos.DrawLine(c2, c3);
            Gizmos.DrawLine(c3, c0);
        }

        // Polilínea usada
        if (drawUsedPolylineGizmo && lastCenterline != null && lastCenterline.Count > 1)
        {
            Gizmos.color = usedPolylineColor;
            for (int i = 0; i < lastCenterline.Count - 1; i++)
                Gizmos.DrawLine(lastCenterline[i], lastCenterline[i + 1]);
            if (lastClosed && drawLoopClosureGizmo)
            {
                Gizmos.color = loopClosureColor;
                Gizmos.DrawLine(lastCenterline[lastCenterline.Count - 1], lastCenterline[0]);
            }
        }

        Gizmos.color = prev;
    }
}
