using System.Collections.Generic;
using UnityEngine;

public partial class RoadFromTargetsSticky
{
    // Sample world position and tangent at distance s along the last built centerline
    public bool SampleAtDistance(float s, out Vector3 position, out Vector3 tangent)
    {
        position = Vector3.zero;
        tangent = Vector3.forward;
        if (lastCenterline == null || lastCenterline.Count < 2 || lastCumulative == null || lastCumulative.Count != lastCenterline.Count)
            return false;

    float length = lastCumulative[lastCumulative.Count - 1];
    if (length <= 1e-6f) { position = lastCenterline[0]; tangent = (lastCenterline[lastCenterline.Count - 1] - lastCenterline[0]).normalized; return true; }

        float sClamped = s;
        if (lastClosed)
        {
            sClamped = Mathf.Repeat(sClamped, length);
        }
        else
        {
            sClamped = Mathf.Clamp(sClamped, 0f, length);
        }

        // Find segment index i such that lastCumulative[i] <= sClamped <= lastCumulative[i+1]
        int i = 0;
        int hi = lastCumulative.Count - 2;
        int lo = 0;
        // Binary search
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            float a = lastCumulative[mid];
            float b = lastCumulative[mid + 1];
            if (sClamped < a) hi = mid - 1;
            else if (sClamped > b) lo = mid + 1;
            else { i = mid; break; }
            i = Mathf.Clamp(lo, 0, lastCumulative.Count - 2);
        }

        float s0 = lastCumulative[i];
        float s1 = lastCumulative[i + 1];
        float t = (s1 > s0) ? Mathf.InverseLerp(s0, s1, sClamped) : 0f;
        Vector3 p0 = lastCenterline[i];
        Vector3 p1 = lastCenterline[i + 1];
        position = Vector3.Lerp(p0, p1, t);

        // Tangent from neighbors
    Vector3 prev = (i > 0) ? lastCenterline[i - 1] : (lastClosed ? lastCenterline[lastCenterline.Count - 2] : p0);
        Vector3 next = (i + 2 < lastCenterline.Count) ? lastCenterline[i + 2] : (lastClosed ? lastCenterline[1] : p1);
        Vector3 d0 = (p1 - p0);
        Vector3 d1 = (next - prev);
        Vector3 tan = d1.sqrMagnitude > 1e-10f ? d1 : d0;
        if (tan.sqrMagnitude < 1e-10f) tan = Vector3.forward;
        tangent = tan.normalized;
        return true;
    }

    // Map lane index [0..laneCount-1] to lateral t in [-0.5, 0.5], respecting outer margins
    public float LaneIndexToTRel(int index)
    {
        int lanes = Mathf.Max(1, laneCount);
        int idx = Mathf.Clamp(index, 0, lanes - 1);
        if (lanes == 1) return 0f;
        float margin = Mathf.Clamp01(outerLaneEdgeMargin);
        float minT = -0.5f + margin;
        float maxT =  0.5f - margin;
        if (minT > maxT)
        {
            float mid = 0f;
            minT = maxT = mid;
        }
        float u = idx / (float)(lanes - 1);
        return Mathf.Lerp(minT, maxT, u);
    }
}
