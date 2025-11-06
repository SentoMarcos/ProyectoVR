using System.Collections.Generic;
using UnityEngine;

public partial class RoadFromTargetsSticky
{
    void MaybeLockPlane()
    {
        if (!lockPlaneAfterTwoStable || planeLocked) return;
        var stables = new List<Vector3>();
        foreach (var st in proxies) if (st.everStable) stables.Add(st.stablePos);
        if (stables.Count >= 3)
        {
            Vector3 a = stables[0], b = stables[1], c = stables[2];
            lockedUp = Vector3.Cross(b - a, c - b).normalized;
            if (lockedUp.sqrMagnitude < 1e-6f) lockedUp = Vector3.up;
            lockedPoint = a;
            planeLocked = true;
        }
    }

    void GenerateRoad(List<Transform> controlPoints)
    {
        bool controlClosed = false;
        if (controlPoints != null && controlPoints.Count >= 3)
        {
            var firstT = controlPoints[0];
            var lastT = controlPoints[controlPoints.Count - 1];
            controlClosed = ReferenceEquals(firstT, lastT) ||
                            ((firstT != null && lastT != null) && (firstT.position - lastT.position).sqrMagnitude < 1e-10f);
        }

        var ctrl = new List<Vector3>(controlPoints.Count);
        int countToCopy = controlPoints.Count;
        if (controlClosed && countToCopy >= 2) countToCopy -= 1;
        for (int i = 0; i < countToCopy; i++)
        {
            var t = controlPoints[i];
            if (t) ctrl.Add(t.position);
        }

        float yRef = 0f;
        if (forceTargetsSameHeight && ctrl.Count > 0)
        {
            switch (heightReferenceMode)
            {
                case HeightReferenceMode.FirstPoint:
                    yRef = controlPoints[0] ? controlPoints[0].position.y : ctrl[0].y;
                    break;
                case HeightReferenceMode.AveragePoints:
                    float accY = 0f; for (int i = 0; i < ctrl.Count; i++) accY += ctrl[i].y; yRef = accY / ctrl.Count;
                    break;
                case HeightReferenceMode.ThisObjectY:
                    yRef = (heightReferenceTransform ? heightReferenceTransform : transform).position.y;
                    break;
                case HeightReferenceMode.CustomY:
                    yRef = customHeightY; break;
            }
            for (int i = 0; i < ctrl.Count; i++) ctrl[i] = new Vector3(ctrl[i].x, yRef, ctrl[i].z);
        }

        Vector3 up = planeLocked ? lockedUp : Vector3.zero;
        if (!planeLocked && derivePlaneFromPointOrientation && !ignoreTargetRotations)
        {
            up = ComputeUpFromPointOrientation(controlPoints);
        }
        if (up.sqrMagnitude < 1e-6f)
        {
            up = ComputePlaneNormal(ctrl);
        }
        if (snapNormalToWorldUp)
        {
            float aUp = Vector3.Angle(up, Vector3.up);
            float aDown = Vector3.Angle(up, Vector3.down);
            if (aUp <= snapUpMaxAngle) up = Vector3.up; else if (aDown <= snapUpMaxAngle) up = Vector3.down;
        }
        if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
        up.Normalize();
        Vector3 planePoint = planeLocked ? lockedPoint : ComputeCentroid(ctrl);
        if (forceTargetsSameHeight && ctrl.Count > 0)
        {
            up = Vector3.up;
            planePoint.y = yRef;
        }

        List<float> cum;
        var centerline = SampleCenterline(ctrl, samplesPerSegment, out cum, controlClosed);

        if (flattenToTargetsPlane || forceTargetsSameHeight)
        {
            for (int i = 0; i < centerline.Count; i++)
            {
                float d = Vector3.Dot(centerline[i] - planePoint, up);
                centerline[i] -= up * d;
                if (forceTargetsSameHeight)
                {
                    var cpi = centerline[i]; cpi.y = planePoint.y; centerline[i] = cpi;
                }
            }
        }

        // Closed flag for downstream logic
        bool isClosed = controlClosed;

        // Vertical exaggeration of relief (with adaptive damping for tilted planes)
        float usedExg = verticalExaggeration;
        if (forceNoExaggerationWhenFrozen && planeLocked) usedExg = 1f;
        // reduce exaggeration as plane tilts away from world up
        float tiltDeg = Vector3.Angle(up, Vector3.up);
        if (adaptiveTiltDamping > 0f)
        {
            float t = Mathf.InverseLerp(adaptiveTiltStartDeg, adaptiveTiltEndDeg, tiltDeg);
            float damp = Mathf.Lerp(1f, 1f - Mathf.Clamp01(adaptiveTiltDamping), Mathf.Clamp01(t));
            usedExg *= Mathf.Clamp(damp, 0f, 1f);
        }
        if (disableExaggerationAboveTiltDeg > 0f && tiltDeg >= disableExaggerationAboveTiltDeg) usedExg = 1f;

        if (usedExg > 0f && Mathf.Abs(usedExg - 1f) > 1e-3f)
        {
            Vector3 axis = (exaggerationAxis == ExaggerationAxisMode.WorldUp) ? Vector3.up : up;
            axis = axis.sqrMagnitude < 1e-6f ? Vector3.up : axis.normalized;

            if (exaggerationSource == ExaggerationSource.FromRawWorld)
            {
                // Build a raw world-height profile sampled along the centerline param
                // Map each centerline point to nearest original control segment and lerp raw heights
                var rawHeights = new float[centerline.Count];
                if (ctrl.Count >= 2)
                {
                    // cumulative distances for control points
                    var ctrlCum = ComputeCumulative(ctrl);
                    float totalCtl = ctrlCum[ctrlCum.Count - 1];
                    var clCum = ComputeCumulative(centerline);
                    float totalCl = clCum[clCum.Count - 1];
                    for (int i = 0; i < centerline.Count; i++)
                    {
                        float s = (totalCl > 1e-6f) ? clCum[i] / totalCl : 0f;
                        float sCtl = s * totalCtl;
                        // find control segment
                        int ci = 0;
                        while (ci < ctrlCum.Count - 1 && ctrlCum[ci + 1] < sCtl) ci++;
                        float s0 = ctrlCum[Mathf.Clamp(ci, 0, ctrlCum.Count - 1)];
                        float s1 = ctrlCum[Mathf.Clamp(ci + 1, 0, ctrlCum.Count - 1)];
                        float u = (s1 > s0) ? Mathf.InverseLerp(s0, s1, sCtl) : 0f;
                        Vector3 p0 = ctrl[Mathf.Clamp(ci, 0, ctrl.Count - 1)];
                        Vector3 p1 = ctrl[Mathf.Clamp(ci + 1, 0, ctrl.Count - 1)];
                        // raw height is dot against axis from planePoint baseline
                        float h0 = Vector3.Dot(p0 - planePoint, axis);
                        float h1 = Vector3.Dot(p1 - planePoint, axis);
                        rawHeights[i] = Mathf.Lerp(h0, h1, u);
                    }

                    // Seam continuity for closed paths: distribute end-start height delta along loop
                    if (isClosed && centerline.Count >= 2 && totalCl > 1e-6f)
                    {
                        float delta = rawHeights[rawHeights.Length - 1] - rawHeights[0];
                        for (int i = 0; i < centerline.Count; i++)
                        {
                            float tNorm = clCum[i] / totalCl; // 0..1
                            rawHeights[i] -= delta * tNorm;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < centerline.Count; i++) rawHeights[i] = 0f;
                }

                // Apply with baseline handling
                float baseline = 0f;
                if (exaggerationBaseline == ExaggerationBaselineMode.KeepAverage)
                {
                    float acc = 0f; for (int i = 0; i < centerline.Count; i++) acc += rawHeights[i];
                    baseline = (centerline.Count > 0) ? acc / centerline.Count : 0f;
                }
                else if (exaggerationBaseline == ExaggerationBaselineMode.KeepFirstPoint)
                {
                    baseline = (rawHeights.Length > 0) ? rawHeights[0] : 0f;
                }
                else if (exaggerationBaseline == ExaggerationBaselineMode.KeepPlanePoint)
                {
                    baseline = 0f; // planePoint is the baseline
                }
                else // KeepZero
                {
                    baseline = 0f;
                }

                for (int i = 0; i < centerline.Count; i++)
                {
                    Vector3 vec = centerline[i] - planePoint;
                    float baseH = rawHeights[i];
                    float hEx = baseline + (baseH - baseline) * usedExg;
                    // clamp applied relief
                    hEx = Mathf.Clamp(hEx, -maxAppliedReliefMeters, maxAppliedReliefMeters);
                    Vector3 flat = vec - axis * Vector3.Dot(vec, axis);
                    centerline[i] = planePoint + flat + axis * hEx;
                }
            }
            else
            {
                // Baseline from projected heights with seam continuity
                var projH = new float[centerline.Count];
                var clCum = ComputeCumulative(centerline);
                float totalCl = clCum[clCum.Count - 1];
                for (int i = 0; i < centerline.Count; i++) projH[i] = Vector3.Dot(centerline[i] - planePoint, axis);
                if (isClosed && centerline.Count >= 2 && totalCl > 1e-6f)
                {
                    float delta = projH[projH.Length - 1] - projH[0];
                    for (int i = 0; i < centerline.Count; i++)
                    {
                        float tNorm = clCum[i] / totalCl;
                        projH[i] -= delta * tNorm;
                    }
                }

                float baseline = 0f;
                if (exaggerationBaseline == ExaggerationBaselineMode.KeepAverage)
                {
                    float acc = 0f; for (int i = 0; i < projH.Length; i++) acc += projH[i];
                    baseline = (projH.Length > 0) ? acc / projH.Length : 0f;
                }
                else if (exaggerationBaseline == ExaggerationBaselineMode.KeepFirstPoint)
                {
                    baseline = (projH.Length > 0) ? projH[0] : 0f;
                }
                else if (exaggerationBaseline == ExaggerationBaselineMode.KeepPlanePoint)
                {
                    baseline = 0f;
                }
                else
                {
                    baseline = 0f;
                }

                for (int i = 0; i < centerline.Count; i++)
                {
                    Vector3 vec = centerline[i] - planePoint;
                    float h = projH[i];
                    float hEx = baseline + (h - baseline) * usedExg;
                    hEx = Mathf.Clamp(hEx, -maxAppliedReliefMeters, maxAppliedReliefMeters);
                    Vector3 flat = vec - axis * h;
                    centerline[i] = planePoint + flat + axis * hEx;
                }
            }
        }

        if (refineByCurvature)
        {
            centerline = RefineByCurvature(centerline, up, maxCurveAngleDeg, maxSegmentLen, maxRefinePasses);
        }
        

        if (isClosed && rotateSeamToLowestCurvature && centerline.Count >= 3)
        {
            int seam = FindLowestCurvatureIndex(centerline, up);
            if (seam > 0)
            {
                var rotated = new List<Vector3>(centerline.Count);
                for (int k = 0; k < centerline.Count; k++) rotated.Add(centerline[(seam + k) % centerline.Count]);
                centerline = rotated;
            }
        }

        cum = ComputeCumulative(centerline);

        float avgSeg = AverageSegment(ctrl);
        float minSeg = MinSegment(ctrl);
        float perLane = (widthMode == WidthMode.AbsoluteMeters) ? laneWidthMeters : Mathf.Max(0.001f, laneWidthFraction * avgSeg);
        float totalWidth = perLane * Mathf.Max(1, laneCount);
        if (widthMode == WidthMode.FitToSpacing && minSeg > 0f) totalWidth = Mathf.Min(totalWidth, maxWidthVsMinSeg * minSeg);
        float wMin = Mathf.Max(0f, minTotalWidthMeters);
        float wMax = (maxTotalWidthMeters > 0f) ? Mathf.Max(wMin, maxTotalWidthMeters) : float.PositiveInfinity;
        totalWidth = Mathf.Clamp(totalWidth, wMin, wMax);
        float half = totalWidth * 0.5f;

        var v = new List<Vector3>(centerline.Count * 2);
        var n = new List<Vector3>(centerline.Count * 2);
        var uv = new List<Vector2>(centerline.Count * 2);
        var tri = new List<int>((centerline.Count - 1) * 6);
        var vS = new List<Vector3>(centerline.Count * 2);
        var nS = new List<Vector3>(centerline.Count * 2);
        var uvS = new List<Vector2>(centerline.Count * 2);
        var triS = new List<int>((centerline.Count - 1) * 6);

        Vector3 localUp = transform.InverseTransformDirection(up).normalized;
        var pairsL = new List<Vector3>();
        var pairsR = new List<Vector3>();
        var pairsU = new List<float>();
        var spairsL = new List<Vector3>();
        var spairsR = new List<Vector3>();
        var spairsU = new List<float>();

        System.Func<int, float> widthScaleAtIndex = (idx) =>
        {
            if (!adaptiveWidthInCurves || centerline.Count < 3) return 1f;
            int nC = centerline.Count;
            int ip = Mathf.Max(0, idx - 1);
            int inx = Mathf.Min(nC - 1, idx + 1);
            if (isClosed)
            {
                ip = (idx - 1 + nC) % nC;
                inx = (idx + 1) % nC;
            }
            Vector3 p = centerline[idx];
            Vector3 d0 = ProjectOnPlaneSafe(p - centerline[ip], up).normalized;
            Vector3 d1 = ProjectOnPlaneSafe(centerline[inx] - p, up).normalized;
            if (d0.sqrMagnitude < 1e-8f) d0 = d1;
            if (d1.sqrMagnitude < 1e-8f) d1 = d0;
            float ang = Vector3.Angle(d0, d1);
            float a0 = Mathf.Max(0f, angleStartNarrow);
            float a1 = Mathf.Max(a0 + 1e-3f, angleForMinWidth);
            if (ang <= a0) return 1f;
            if (ang >= a1) return Mathf.Clamp01(minWidthScaleAtSharpTurn);
            float tA = Mathf.InverseLerp(a0, a1, ang);
            float sMin = Mathf.Clamp01(minWidthScaleAtSharpTurn);
            return Mathf.Lerp(1f, sMin, tA);
        };

        System.Action<Vector3, Vector3, float, float> addPairSimple = (p, dir, uval, scale) =>
        {
            Vector3 tdir = ProjectOnPlaneSafe(dir, up).normalized;
            if (tdir.sqrMagnitude < 1e-8f) tdir = Vector3.forward;
            Vector3 right = Vector3.Cross(tdir, up).normalized;
            if (right.sqrMagnitude < 1e-8f) right = Vector3.right;
            float h = half * Mathf.Clamp(scale, 0.1f, 1f);
            pairsL.Add(p - right * h + up * surfaceOffset);
            pairsR.Add(p + right * h + up * surfaceOffset);
            pairsU.Add(uval);
            if (addUnderShadow)
            {
                float hs = (h + Mathf.Max(0f, shadowExtraWidthMeters));
                float soff = surfaceOffset + shadowUnderOffset;
                spairsL.Add(p - right * hs + up * soff);
                spairsR.Add(p + right * hs + up * soff);
                spairsU.Add(uval);
            }
        };

        System.Action<int> addMiterAt = (iIdx) =>
        {
            Vector3 p = centerline[iIdx];
            Vector3 offsetL, offsetR;
            float scale = widthScaleAtIndex(iIdx);
            float h = half * Mathf.Clamp(scale, 0.1f, 1f);
            ComputeMiterOffsets(centerline, iIdx, up, h, miterLimit, isClosed, out offsetL, out offsetR);
            pairsL.Add(p + offsetL + up * surfaceOffset);
            pairsR.Add(p + offsetR + up * surfaceOffset);
            float u = cum[Mathf.Clamp(iIdx, 0, cum.Count - 1)] * uvTilesPerMeter;
            pairsU.Add(u);
            if (addUnderShadow)
            {
                Vector3 offL = offsetL.normalized * (offsetL.magnitude + Mathf.Max(0f, shadowExtraWidthMeters));
                Vector3 offR = offsetR.normalized * (offsetR.magnitude + Mathf.Max(0f, shadowExtraWidthMeters));
                float soff = surfaceOffset + shadowUnderOffset;
                spairsL.Add(p + offL + up * soff);
                spairsR.Add(p + offR + up * soff);
                spairsU.Add(u);
            }
        };

        if (isClosed)
        {
            int nC = centerline.Count;
            for (int i = 0; i < nC; i++)
            {
                int ip = (i - 1 + nC) % nC;
                int inx = (i + 1) % nC;
                Vector3 p = centerline[i];
                Vector3 d0 = ProjectOnPlaneSafe(p - centerline[ip], up).normalized;
                Vector3 d1 = ProjectOnPlaneSafe(centerline[inx] - p, up).normalized;
                if (d0.sqrMagnitude < 1e-8f) d0 = d1;
                if (d1.sqrMagnitude < 1e-8f) d1 = d0;

                if (useRoundedJoins)
                {
                    float ang = Mathf.Clamp(Vector3.SignedAngle(d0, d1, up), -180f, 180f);
                    float absAng = Mathf.Abs(ang);
                    int steps = Mathf.Max(1, Mathf.CeilToInt((absAng / 90f) * roundSegmentsPer90));
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = (steps == 0) ? 1f : (k / (float)steps);
                        Vector3 dir = Vector3.Slerp(d0, d1, t);
                        float sc = widthScaleAtIndex(i);
                        addPairSimple(p, dir, cum[Mathf.Clamp(i, 0, cum.Count - 1)] * uvTilesPerMeter, sc);
                    }
                }
                else if (useMiterJoins)
                {
                    addMiterAt(i);
                }
                else
                {
                    Vector3 dir = (d0 + d1);
                    if (dir.sqrMagnitude < 1e-8f) dir = d1;
                    float sc = widthScaleAtIndex(i);
                    addPairSimple(p, dir, cum[Mathf.Clamp(i, 0, cum.Count - 1)] * uvTilesPerMeter, sc);
                }
            }
        }
        else
        {
            int i0 = 0; int i1 = (centerline.Count > 1) ? 1 : 0;
            Vector3 dir0 = (centerline[i1] - centerline[i0]);
            float sc0 = widthScaleAtIndex(i0);
            addPairSimple(centerline[i0], dir0, cum[i0] * uvTilesPerMeter, sc0);

            for (int i = 1; i < centerline.Count - 1; i++)
            {
                Vector3 p = centerline[i];
                Vector3 d0 = ProjectOnPlaneSafe(p - centerline[i - 1], up).normalized;
                Vector3 d1 = ProjectOnPlaneSafe(centerline[i + 1] - p, up).normalized;
                if (d0.sqrMagnitude < 1e-8f) d0 = d1;
                if (d1.sqrMagnitude < 1e-8f) d1 = d0;

                if (useRoundedJoins)
                {
                    float ang = Mathf.Clamp(Vector3.SignedAngle(d0, d1, up), -180f, 180f);
                    float absAng = Mathf.Abs(ang);
                    int steps = Mathf.Max(1, Mathf.CeilToInt((absAng / 90f) * roundSegmentsPer90));
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = (steps == 0) ? 1f : (k / (float)steps);
                        Vector3 dir = Vector3.Slerp(d0, d1, t);
                        float sc = widthScaleAtIndex(i);
                        addPairSimple(p, dir, cum[i] * uvTilesPerMeter, sc);
                    }
                }
                else if (useMiterJoins)
                {
                    addMiterAt(i);
                }
                else
                {
                    Vector3 dir = (centerline[i + 1] - centerline[i]);
                    float sc = widthScaleAtIndex(i);
                    addPairSimple(p, dir, cum[i] * uvTilesPerMeter, sc);
                }
            }

            if (centerline.Count > 1)
            {
                int last = centerline.Count - 1;
                Vector3 dir = (centerline[last] - centerline[last - 1]);
                float scl = widthScaleAtIndex(last);
                addPairSimple(centerline[last], dir, cum[last] * uvTilesPerMeter, scl);
            }
        }

        for (int i = 0; i < pairsL.Count; i++)
        {
            Vector3 Ll = transform.InverseTransformPoint(pairsL[i]);
            Vector3 Rl = transform.InverseTransformPoint(pairsR[i]);
            v.Add(Ll); v.Add(Rl);
            n.Add(localUp); n.Add(localUp);
            float uval = pairsU[i];
            uv.Add(new Vector2(uval, 0f));
            uv.Add(new Vector2(uval, 1f));
        }

        if (addUnderShadow)
        {
            for (int i = 0; i < spairsL.Count; i++)
            {
                Vector3 Ll = transform.InverseTransformPoint(spairsL[i]);
                Vector3 Rl = transform.InverseTransformPoint(spairsR[i]);
                vS.Add(Ll); vS.Add(Rl);
                nS.Add(localUp); nS.Add(localUp);
                float uval = spairsU[i];
                uvS.Add(new Vector2(uval, 0f));
                uvS.Add(new Vector2(uval, 1f));
            }
        }

        bool ccwUp = true;
        if (v.Count >= 4)
        {
            Vector3 a = v[1] - v[0];
            Vector3 b = v[2] - v[0];
            float s = Vector3.Dot(Vector3.Cross(a, b), localUp);
            ccwUp = s > 0f;
        }

        tri.Clear();
        for (int i = 0; i < v.Count - 2; i += 2)
        {
            if (ccwUp)
            {
                tri.Add(i);     tri.Add(i + 2); tri.Add(i + 1);
                tri.Add(i + 1); tri.Add(i + 2); tri.Add(i + 3);
            }
            else
            {
                tri.Add(i);     tri.Add(i + 1); tri.Add(i + 2);
                tri.Add(i + 1); tri.Add(i + 3); tri.Add(i + 2);
            }
        }

        if (addUnderShadow && vS.Count >= 4)
        {
            triS.Clear();
            for (int i = 0; i < vS.Count - 2; i += 2)
            {
                if (ccwUp)
                {
                    triS.Add(i);     triS.Add(i + 2); triS.Add(i + 1);
                    triS.Add(i + 1); triS.Add(i + 2); triS.Add(i + 3);
                }
                else
                {
                    triS.Add(i);     triS.Add(i + 1); triS.Add(i + 2);
                    triS.Add(i + 1); triS.Add(i + 3); triS.Add(i + 2);
                }
            }
        }

        if (isClosed && v.Count >= 4)
        {
            int lastPairL = v.Count - 2;
            int lastPairR = v.Count - 1;
            int firstPairL = 0;
            int firstPairR = 1;
            if (ccwUp)
            {
                tri.Add(lastPairL); tri.Add(firstPairL); tri.Add(lastPairR);
                tri.Add(lastPairR); tri.Add(firstPairL); tri.Add(firstPairR);
            }
            else
            {
                tri.Add(lastPairL); tri.Add(lastPairR); tri.Add(firstPairL);
                tri.Add(lastPairR); tri.Add(firstPairR); tri.Add(firstPairL);
            }
        }

        mesh.Clear();
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();

        if (enableLaneLines && linesMesh != null)
        {
            int nC = centerline.Count;
            var rights = new Vector3[nC];
            for (int i = 0; i < nC; i++)
            {
                int inx = (isClosed ? (i + 1) % nC : Mathf.Min(i + 1, nC - 1));
                Vector3 tan = ProjectOnPlaneSafe(centerline[inx] - centerline[i], up).normalized;
                if (tan.sqrMagnitude < 1e-8f)
                {
                    int ip = (isClosed ? (i - 1 + nC) % nC : Mathf.Max(i - 1, 0));
                    tan = ProjectOnPlaneSafe(centerline[i] - centerline[ip], up).normalized;
                    if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
                }
                rights[i] = Vector3.Cross(tan, up).normalized;
                if (rights[i].sqrMagnitude < 1e-8f) rights[i] = Vector3.right;
            }

            var vL = new List<Vector3>();
            var nL = new List<Vector3>();
            var uvL = new List<Vector2>();
            var triL = new List<int>();
            float lineOffset = surfaceOffset + Mathf.Max(0f, linesLiftOffsetMeters);

            bool ShouldDrawAtS(float s)
            {
                if (!centerLinesDashed) return true;
                float period = Mathf.Max(1e-4f, dashLengthMeters + gapLengthMeters);
                float phase = (s + dashOffsetMeters) % period;
                return phase < dashLengthMeters;
            }

            System.Action<float, float, bool> addLineAtT = (tRel, width, dashed) =>
            {
                int baseIndex = vL.Count;
                for (int i = 0; i < nC; i++)
                {
                    Vector3 center = centerline[i] + rights[i] * (tRel * 2f * half) + up * lineOffset;
                    Vector3 r = rights[i];
                    float hw = Mathf.Max(0.001f, width * 0.5f);
                    Vector3 a = transform.InverseTransformPoint(center - r * hw);
                    Vector3 b = transform.InverseTransformPoint(center + r * hw);
                    vL.Add(a); vL.Add(b);
                    nL.Add(localUp); nL.Add(localUp);
                    float uval = cum[Mathf.Clamp(i, 0, cum.Count - 1)] * lineUvTilesPerMeter;
                    uvL.Add(new Vector2(uval, 0f));
                    uvL.Add(new Vector2(uval, 1f));
                }
                int stripVerts = nC * 2;
                for (int i = 0; i < stripVerts - 2; i += 2)
                {
                    int i0 = baseIndex + i;
                    int seg = i / 2;
                    bool drawThis = true;
                    if (dashed)
                    {
                        float sMid = 0f;
                        if (seg >= 0 && seg < cum.Count - 1)
                            sMid = 0.5f * (cum[seg] + cum[seg + 1]);
                        drawThis = ShouldDrawAtS(sMid);
                    }
                    if (!drawThis) continue;
                    if (ccwUp)
                    {
                        triL.Add(i0);     triL.Add(i0 + 2); triL.Add(i0 + 1);
                        triL.Add(i0 + 1); triL.Add(i0 + 2); triL.Add(i0 + 3);
                    }
                    else
                    {
                        triL.Add(i0);     triL.Add(i0 + 1); triL.Add(i0 + 2);
                        triL.Add(i0 + 1); triL.Add(i0 + 3); triL.Add(i0 + 2);
                    }
                }
                if (isClosed)
                {
                    int lastPairL = baseIndex + stripVerts - 2;
                    int lastPairR = baseIndex + stripVerts - 1;
                    int firstPairL = baseIndex + 0;
                    int firstPairR = baseIndex + 1;
                    bool drawClose = true;
                    if (dashed)
                    {
                        float sMid = 0.5f * (cum[cum.Count - 1] + 0f);
                        drawClose = ShouldDrawAtS(sMid);
                    }
                    if (drawClose && ccwUp)
                    {
                        triL.Add(lastPairL); triL.Add(firstPairL); triL.Add(lastPairR);
                        triL.Add(lastPairR); triL.Add(firstPairL); triL.Add(firstPairR);
                    }
                    else if (drawClose)
                    {
                        triL.Add(lastPairL); triL.Add(lastPairR); triL.Add(firstPairL);
                        triL.Add(lastPairR); triL.Add(firstPairR); triL.Add(firstPairL);
                    }
                }
            };

            int lanes = Mathf.Max(1, laneCount);
            if (drawCenterLines && lanes >= 2)
            {
                for (int k = 1; k <= lanes - 1; k++)
                {
                    float tRel = Mathf.Lerp(-0.5f, 0.5f, k / (float)lanes);
                    addLineAtT(tRel, centerLineWidthMeters, centerLinesDashed);
                }
            }
            if (drawEdgeLines)
            {
                addLineAtT(-0.5f, edgeLineWidthMeters, false);
                addLineAtT( 0.5f, edgeLineWidthMeters, false);
            }

            linesMesh.Clear();
            linesMesh.SetVertices(vL);
            linesMesh.SetNormals(nL);
            linesMesh.SetUVs(0, uvL);
            linesMesh.SetTriangles(triL, 0);
            linesMesh.RecalculateBounds();

            if (linesMr && linesMr.sharedMaterial)
            {
                var mat = linesMr.sharedMaterial;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", laneLineColor);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     laneLineColor);
                if (laneLineTexture)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", laneLineTexture);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", laneLineTexture);
                }
            }
        }

        if (addUnderShadow && shadowMesh != null && shadowMr != null)
        {
            shadowMesh.Clear();
            shadowMesh.SetVertices(vS);
            shadowMesh.SetNormals(nS);
            shadowMesh.SetUVs(0, uvS);
            shadowMesh.SetTriangles(triS, 0);
            shadowMesh.RecalculateBounds();
            var mat = shadowMr.sharedMaterial;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", shadowColor);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     shadowColor);
            }
        }

        lastCenterline.Clear();
        lastCenterline.AddRange(centerline);
        lastCumulative = cum;
        lastUpVec = up;
        lastClosed = isClosed;
        lastTotalWidth = totalWidth;
    }

    static Vector3 ProjectOnPlaneSafe(Vector3 v, Vector3 up)
    {
        if (up.sqrMagnitude < 1e-10f) return v;
        return v - up * Vector3.Dot(v, up);
    }

    float AngleToWidthScale(Vector3 prev, Vector3 curr, Vector3 next, Vector3 up)
    {
        if (!adaptiveWidthInCurves) return 1f;
        Vector3 d0 = ProjectOnPlaneSafe(curr - prev, up).normalized;
        Vector3 d1 = ProjectOnPlaneSafe(next - curr, up).normalized;
        if (d0.sqrMagnitude < 1e-8f) d0 = d1;
        if (d1.sqrMagnitude < 1e-8f) d1 = d0;
        float ang = Vector3.Angle(d0, d1);
        float a0 = Mathf.Max(0f, angleStartNarrow);
        float a1 = Mathf.Max(a0 + 1e-3f, angleForMinWidth);
        if (ang <= a0) return 1f;
        if (ang >= a1) return Mathf.Clamp01(minWidthScaleAtSharpTurn);
        float tA = Mathf.InverseLerp(a0, a1, ang);
        float sMin = Mathf.Clamp01(minWidthScaleAtSharpTurn);
        return Mathf.Lerp(1f, sMin, tA);
    }

    List<Vector3> RefineByCurvature(List<Vector3> pts, Vector3 up, float maxAngleDeg, float maxSegLen, int passes)
    {
        if (pts == null || pts.Count < 3) return pts;
        float maxAng = Mathf.Max(1f, maxAngleDeg);
        for (int it = 0; it < passes; it++)
        {
            bool inserted = false;
            var outPts = new List<Vector3>(pts.Count * 2);
            outPts.Add(pts[0]);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i - 1];
                Vector3 b = pts[i];
                Vector3 c = pts[i + 1];
                Vector3 ab = ProjectOnPlaneSafe(b - a, up);
                Vector3 bc = ProjectOnPlaneSafe(c - b, up);
                float ang = Vector3.Angle(ab, bc);
                float len = bc.magnitude;
                outPts.Add(b);
                if ((ang > maxAng) || (len > maxSegLen))
                {
                    outPts.Add(Vector3.Lerp(b, c, 0.5f));
                    inserted = true;
                }
            }
            outPts.Add(pts[pts.Count - 1]);
            pts = outPts;
            if (!inserted) break;
        }
        return pts;
    }

    List<float> ComputeCumulative(List<Vector3> pts)
    {
        var cum = new List<float>(pts.Count);
        float acc = 0f; cum.Add(0f);
        for (int i = 1; i < pts.Count; i++)
        {
            acc += Vector3.Distance(pts[i], pts[i - 1]);
            cum.Add(acc);
        }
        return cum;
    }

    int FindLowestCurvatureIndex(List<Vector3> pts, Vector3 up)
    {
        if (pts == null || pts.Count < 3) return 0;
        int n = pts.Count;
        float best = float.MaxValue; int bestIdx = 0;
        for (int i = 0; i < n; i++)
        {
            int ip = (i - 1 + n) % n;
            int inx = (i + 1) % n;
            Vector3 d0 = ProjectOnPlaneSafe(pts[i] - pts[ip], up).normalized;
            Vector3 d1 = ProjectOnPlaneSafe(pts[inx] - pts[i], up).normalized;
            if (d0.sqrMagnitude < 1e-8f || d1.sqrMagnitude < 1e-8f) continue;
            float ang = Vector3.Angle(d0, d1);
            if (ang < best) { best = ang; bestIdx = i; }
        }
        return bestIdx;
    }

    void ComputeMiterOffsets(List<Vector3> cl, int i, Vector3 up, float half, float limit, bool isClosed, out Vector3 offL, out Vector3 offR)
    {
        int last = cl.Count - 1;
        bool atStart = (i == 0);
        bool atEnd = (i == last);
        int prev = atStart ? (isClosed ? last - 1 : 0) : i - 1;
        int next = atEnd ? (isClosed ? 1 : last) : i + 1;

        Vector3 p = cl[i];
        Vector3 pPrev = cl[prev];
        Vector3 pNext = cl[next];

        Vector3 d0 = ProjectOnPlaneSafe(p - pPrev, up).normalized;
        Vector3 d1 = ProjectOnPlaneSafe(pNext - p, up).normalized;
        if (d0.sqrMagnitude < 1e-8f) d0 = d1;
        if (d1.sqrMagnitude < 1e-8f) d1 = d0;

        Vector3 n0 = Vector3.Cross(d0, up).normalized;
        Vector3 n1 = Vector3.Cross(d1, up).normalized;
        if (n0.sqrMagnitude < 1e-8f) n0 = n1;
        if (n1.sqrMagnitude < 1e-8f) n1 = n0;

        float ang = Vector3.Angle(d0, d1);
        if (ang >= bevelAtAngleDeg)
        {
            offR = n1 * half;
            offL = -n1 * half;
            return;
        }

        Vector3 r0 = n0; Vector3 r1 = n1; Vector3 mR = (r0 + r1);
        if (mR.sqrMagnitude < 1e-8f) mR = r1; mR.Normalize();
        float denomR = Mathf.Max(1e-3f, Mathf.Abs(Vector3.Dot(mR, r1)));
        float scaleR = Mathf.Min(limit, 1f / denomR);
        offR = mR * (half * scaleR);

        Vector3 l0 = -n0; Vector3 l1 = -n1; Vector3 mL = (l0 + l1);
        if (mL.sqrMagnitude < 1e-8f) mL = l1; mL.Normalize();
        float denomL = Mathf.Max(1e-3f, Mathf.Abs(Vector3.Dot(mL, l1)));
        float scaleL = Mathf.Min(limit, 1f / denomL);
        offL = mL * (half * scaleL);
    }

    Vector3 ComputeUpFromPointOrientation(List<Transform> pts)
    {
        if (pts == null || pts.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var t in pts)
        {
            if (!t) continue;
            Vector3 axis;
            if (autoChoosePlaneAxis)
            {
                Vector3[] candidates = new[] { t.up, t.forward, t.right };
                float bestDot = -1f; Vector3 best = t.up;
                for (int i = 0; i < candidates.Length; i++)
                {
                    float d = Mathf.Abs(Vector3.Dot(candidates[i].normalized, Vector3.up));
                    if (d > bestDot) { bestDot = d; best = candidates[i]; }
                }
                axis = best;
            }
            else
            {
                axis = planeNormalAxis == OrientationAxis.Up ? t.up :
                       planeNormalAxis == OrientationAxis.Forward ? t.forward : t.right;
            }
            if (axis.sqrMagnitude > 1e-10f) sum += axis.normalized;
        }
        if (sum.sqrMagnitude < 1e-6f) return Vector3.zero;
        return sum.normalized;
    }

    Vector3 ComputeCentroid(List<Vector3> pts)
    {
        if (pts == null || pts.Count == 0) return Vector3.zero;
        Vector3 c = Vector3.zero;
        for (int i = 0; i < pts.Count; i++) c += pts[i];
        return c / pts.Count;
    }

    float AverageSegment(List<Vector3> pts)
    {
        if (pts.Count < 2) return 0f;
        float s = 0f;
        for (int i = 0; i < pts.Count - 1; i++) s += Vector3.Distance(pts[i], pts[i + 1]);
        return s / (pts.Count - 1);
    }

    float MinSegment(List<Vector3> pts)
    {
        if (pts.Count < 2) return 0f;
        float m = float.MaxValue;
        for (int i = 0; i < pts.Count - 1; i++) m = Mathf.Min(m, Vector3.Distance(pts[i], pts[i + 1]));
        return m;
    }

    List<Vector3> SampleCenterline(List<Vector3> pts, int samples, out List<float> cumulativeDist, bool controlClosed = false)
    {
        cumulativeDist = new List<float>();
        var result = new List<Vector3>();
        if (pts.Count == 2)
        {
            float acc = 0f;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                Vector3 p = Vector3.Lerp(pts[0], pts[1], t);
                if (result.Count > 0) acc += Vector3.Distance(p, result[result.Count - 1]);
                result.Add(p); cumulativeDist.Add(acc);
            }
            return result;
        }
        float accDist = 0f;
        if (controlClosed && pts.Count >= 3)
        {
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int i0 = (i - 1 + n) % n;
                int i1 = i;
                int i2 = (i + 1) % n;
                int i3 = (i + 2) % n;
                Vector3 p0 = pts[i0];
                Vector3 p1 = pts[i1];
                Vector3 p2 = pts[i2];
                Vector3 p3 = pts[i3];
                int s0 = (i == 0) ? 0 : 1;
                for (int s = s0; s <= samples; s++)
                {
                    float t = s / (float)samples;
                    Vector3 pt = CatmullRomCentripetal(p0, p1, p2, p3, t);
                    if (s == 0) pt = p1;
                    if (s == samples) pt = p2;
                    if (result.Count > 0) accDist += Vector3.Distance(pt, result[result.Count - 1]);
                    result.Add(pt); cumulativeDist.Add(accDist);
                }
            }
            return result;
        }
        else
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = (i == 0) ? pts[i] : pts[i - 1];
                Vector3 p1 = pts[i];
                Vector3 p2 = pts[i + 1];
                Vector3 p3 = (i + 2 < pts.Count) ? pts[i + 2] : pts[i + 1];
                int s0 = (i == 0) ? 0 : 1;
                for (int s = s0; s <= samples; s++)
                {
                    float t = s / (float)samples;
                    Vector3 pt = CatmullRomCentripetal(p0, p1, p2, p3, t);
                    if (s == 0) pt = p1;
                    if (s == samples) pt = p2;
                    if (result.Count > 0) accDist += Vector3.Distance(pt, result[result.Count - 1]);
                    result.Add(pt); cumulativeDist.Add(accDist);
                }
            }
            if (result.Count > 0)
            {
                Vector3 last = pts[pts.Count - 1];
                accDist += Vector3.Distance(last, result[result.Count - 1]);
                result[result.Count - 1] = last; cumulativeDist[cumulativeDist.Count - 1] = accDist;
            }
            return result;
        }
    }

    Vector3 CatmullRomCentripetal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        const float alpha = 0.5f;
        float t0 = 0f;
        float t1 = t0 + Mathf.Pow((p1 - p0).magnitude, alpha);
        float t2 = t1 + Mathf.Pow((p2 - p1).magnitude, alpha);
        float t3 = t2 + Mathf.Pow((p3 - p2).magnitude, alpha);
        float tt = Mathf.Lerp(t1, t2, t);

        Vector3 A1 = ((t1 - tt) / Mathf.Max(1e-6f, t1 - t0)) * p0 + ((tt - t0) / Mathf.Max(1e-6f, t1 - t0)) * p1;
        Vector3 A2 = ((t2 - tt) / Mathf.Max(1e-6f, t2 - t1)) * p1 + ((tt - t1) / Mathf.Max(1e-6f, t2 - t1)) * p2;
        Vector3 A3 = ((t3 - tt) / Mathf.Max(1e-6f, t3 - t2)) * p2 + ((tt - t2) / Mathf.Max(1e-6f, t3 - t2)) * p3;

        Vector3 B1 = ((t2 - tt) / Mathf.Max(1e-6f, t2 - t0)) * A1 + ((tt - t0) / Mathf.Max(1e-6f, t2 - t0)) * A2;
        Vector3 B2 = ((t3 - tt) / Mathf.Max(1e-6f, t3 - t1)) * A2 + ((tt - t1) / Mathf.Max(1e-6f, t3 - t1)) * A3;

        return ((t2 - tt) / Mathf.Max(1e-6f, t2 - t1)) * B1 + ((tt - t1) / Mathf.Max(1e-6f, t2 - t1)) * B2;
    }

    Vector3 ComputePlaneNormal(List<Vector3> pts)
    {
        if (planeLocked) return lockedUp;
        if (pts.Count < 3) return Vector3.up;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < pts.Count - 2; i++)
        {
            Vector3 a = pts[i + 1] - pts[i];
            Vector3 b = pts[i + 2] - pts[i + 1];
            Vector3 n = Vector3.Cross(a, b);
            if (n.sqrMagnitude > 1e-10f) sum += n.normalized;
        }
        return (sum.sqrMagnitude < 1e-6f) ? Vector3.up : sum.normalized;
    }
}
