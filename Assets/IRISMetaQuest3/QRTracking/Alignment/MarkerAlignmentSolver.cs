using System;
using System.Collections.Generic;
using UnityEngine;

namespace IRIS.MetaQuest3.Alignment
{
    /// <summary>
    /// Turns a set of observed marker positions into a scene-root pose whose
    /// up-axis is the table's normal.
    ///
    /// Deliberately free of MonoBehaviour, MRUK and any other Unity lifecycle:
    /// the geometry is the part worth testing, and it should be testable at a
    /// desk rather than in a headset holding a printout.
    ///
    /// The normal comes from a least-squares plane through every marker, so
    /// three markers give an exact fit and more than three average out noise.
    /// This is the whole point of the multi-marker approach: angular error in
    /// the normal falls roughly as atan(sigma / baseline), so spreading the
    /// markers wide buys accuracy that no single-marker pose can reach.
    /// </summary>
    public static class MarkerAlignmentSolver
    {
        public const int MinimumMarkers = 3;

        /// <summary>
        /// How far the largest plane residual must stand above the next largest
        /// before we are willing to blame a specific marker for it.
        /// </summary>
        public const float OutlierDominanceRatio = 1.5f;

        public static AlignmentResult Solve(
            IReadOnlyList<MarkerObservation> observations,
            MarkerSet markers,
            AlignmentTolerances tolerances,
            MarkerGeometry reference = null)
        {
            if (markers == null)
            {
                throw new ArgumentNullException(nameof(markers));
            }

            if (observations == null || observations.Count < MinimumMarkers)
            {
                int count = observations?.Count ?? 0;
                return Failure(
                    AlignmentStatus.TooFewMarkers,
                    $"Need {MinimumMarkers} markers, saw {count}.",
                    count);
            }

            // --- index observations, rejecting duplicates -------------------

            var byId = new Dictionary<string, Vector3>(observations.Count);
            foreach (MarkerObservation observation in observations)
            {
                // A QR code elsewhere in the room must never join the fit.
                if (!markers.Contains(observation.Id))
                {
                    continue;
                }

                if (byId.ContainsKey(observation.Id))
                {
                    return Failure(
                        AlignmentStatus.DuplicateMarker,
                        $"Marker '{observation.Id}' was observed more than once.",
                        observations.Count);
                }

                byId.Add(observation.Id, observation.Position);
            }

            if (byId.Count < MinimumMarkers)
            {
                return Failure(
                    AlignmentStatus.TooFewMarkers,
                    $"Need {MinimumMarkers} markers from the set, have {byId.Count}.",
                    byId.Count);
            }

            if (!byId.TryGetValue(markers.FirstAxisMarkerId, out Vector3 firstAxisPoint))
            {
                return Failure(
                    AlignmentStatus.MissingRequiredMarker,
                    $"Axis marker '{markers.FirstAxisMarkerId}' not visible.",
                    byId.Count);
            }

            if (!byId.TryGetValue(markers.SecondAxisMarkerId, out Vector3 secondAxisPoint))
            {
                return Failure(
                    AlignmentStatus.MissingRequiredMarker,
                    $"Axis marker '{markers.SecondAxisMarkerId}' not visible.",
                    byId.Count);
            }

            // --- drift against previously observed geometry -----------------
            // Nothing here is measured by hand. The first accepted solve
            // becomes the reference; afterwards a marker that has been knocked
            // or misdetected shows up as a changed separation.

            float worstRatio = 0f;
            string worstPair = string.Empty;

            var ids = new List<string>(byId.Keys);
            ids.Sort(StringComparer.Ordinal);

            if (reference != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    for (int j = i + 1; j < ids.Count; j++)
                    {
                        if (!reference.TryGetDistance(ids[i], ids[j], out float expected) ||
                            expected < 1e-4f)
                        {
                            continue;
                        }

                        float measured = Vector3.Distance(byId[ids[i]], byId[ids[j]]);
                        float ratio = Mathf.Abs(measured - expected) / expected;

                        if (ratio > worstRatio)
                        {
                            worstRatio = ratio;
                            worstPair = $"{ids[i]}-{ids[j]}";
                        }
                    }
                }

                if (worstRatio > tolerances.MaxGeometryDriftRatio)
                {
                    AlignmentResult drifted = Failure(
                        AlignmentStatus.GeometryChanged,
                        $"Separation {worstPair} has changed by {worstRatio * 100f:F1}% since " +
                        $"calibration (limit {tolerances.MaxGeometryDriftRatio * 100f:F1}%). " +
                        "A marker has moved, or one was misread.",
                        byId.Count);
                    drifted.WorstGeometryDriftRatio = worstRatio;
                    drifted.WorstDriftPair = worstPair;
                    return drifted;
                }
            }

            // --- least-squares plane through every marker -------------------

            var points = new Vector3[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                points[i] = byId[ids[i]];
            }

            FitPlane(points, out Vector3 centroid, out Vector3 normal, out float spreadRatio);

            if (spreadRatio < tolerances.MinSpreadRatio)
            {
                AlignmentResult degenerate = Failure(
                    AlignmentStatus.DegenerateGeometry,
                    $"Markers are too close to a straight line (spread {spreadRatio:F3}, " +
                    $"need {tolerances.MinSpreadRatio:F3}). Move one marker off the line.",
                    byId.Count);
                degenerate.SpreadRatio = spreadRatio;
                return degenerate;
            }

            // Point the normal away from the floor. Safe for markers lying on a
            // roughly horizontal surface, which is the case this solves for.
            if (Vector3.Dot(normal, Vector3.up) < 0f)
            {
                normal = -normal;
            }

            // --- residuals --------------------------------------------------

            float sumSquared = 0f;
            float maxResidual = 0f;
            float secondResidual = 0f;
            string worstMarker = string.Empty;

            for (int i = 0; i < points.Length; i++)
            {
                float distance = Mathf.Abs(Vector3.Dot(points[i] - centroid, normal));
                sumSquared += distance * distance;

                if (distance > maxResidual)
                {
                    secondResidual = maxResidual;
                    maxResidual = distance;
                    worstMarker = ids[i];
                }
                else if (distance > secondResidual)
                {
                    secondResidual = distance;
                }
            }

            float residualRms = Mathf.Sqrt(sumSquared / points.Length);

            // Only name an offender when the data can actually single one out.
            // A symmetric layout with one marker off-plane tilts the fitted
            // plane until every marker is equally far from it, and then "worst"
            // is an artefact of iteration order. Sending someone to re-seat the
            // wrong marker is worse than admitting we cannot tell.
            string namedOutlier =
                maxResidual >= OutlierDominanceRatio * secondResidual ? worstMarker : string.Empty;

            if (residualRms > tolerances.MaxPlaneResidualMeters)
            {
                string advice = namedOutlier.Length > 0
                    ? $"Check '{namedOutlier}' is flat on the surface."
                    : "One marker is not flat on the surface; with this layout the fit " +
                      "cannot tell which. Check all of them, or add a marker.";

                AlignmentResult notFlat = Failure(
                    AlignmentStatus.NotCoplanar,
                    $"Markers are not coplanar (RMS {residualRms * 1000f:F1} mm, " +
                    $"limit {tolerances.MaxPlaneResidualMeters * 1000f:F1} mm). " + advice,
                    byId.Count);
                notFlat.PlaneResidualRms = residualRms;
                notFlat.MaxPlaneResidual = maxResidual;
                notFlat.WorstMarkerId = namedOutlier;
                notFlat.SpreadRatio = spreadRatio;
                return notFlat;
            }

            // --- basis ------------------------------------------------------

            Vector3 baseline = secondAxisPoint - firstAxisPoint;
            if (baseline.magnitude < tolerances.MinBaselineMeters)
            {
                return Failure(
                    AlignmentStatus.BaselineTooShort,
                    $"Origin to X-axis distance is {baseline.magnitude * 100f:F0} cm, " +
                    $"need at least {tolerances.MinBaselineMeters * 100f:F0} cm. " +
                    "Move the markers further apart.",
                    byId.Count);
            }

            // Project the baseline into the fitted plane so X is exactly
            // perpendicular to the normal rather than merely close to it.
            Vector3 x = baseline - normal * Vector3.Dot(baseline, normal);
            if (x.sqrMagnitude < 1e-10f)
            {
                return Failure(
                    AlignmentStatus.DegenerateGeometry,
                    "The origin-to-X baseline is parallel to the table normal.",
                    byId.Count);
            }

            x.Normalize();

            // Unity is left-handed with Y up: Cross(right, up) == forward.
            Vector3 forward = Vector3.Cross(x, normal);

            // Midpoint puts the origin halfway along the axis-marker pair, so
            // with those two on the ends of a table edge the robot lands at the
            // middle of that edge.
            Vector3 rawOrigin = markers.OriginMode == FrameOrigin.MidpointOfAxisMarkers
                ? (firstAxisPoint + secondAxisPoint) * 0.5f
                : firstAxisPoint;

            // Snap onto the fitted plane: the plane is averaged evidence from
            // every marker, so it beats any single marker's own reading.
            Vector3 origin = rawOrigin - normal * Vector3.Dot(rawOrigin - centroid, normal);

            return new AlignmentResult
            {
                Status = AlignmentStatus.Success,
                Pose = new Pose(origin, Quaternion.LookRotation(forward, normal)),
                Normal = normal,
                TiltFromGravityDeg = Vector3.Angle(normal, Vector3.up),
                PlaneResidualRms = residualRms,
                MaxPlaneResidual = maxResidual,
                WorstMarkerId = namedOutlier,
                WorstGeometryDriftRatio = worstRatio,
                WorstDriftPair = worstPair,
                SpreadRatio = spreadRatio,
                MarkerCount = points.Length,
                EnvironmentId = markers.EnvironmentId,
                ObservedGeometry = MarkerGeometry.FromObservations(BuildObservations(ids, points)),
                WasCrossChecked = reference != null || points.Length >= 4,
                Message =
                    $"Aligned from {points.Length} markers. " +
                    $"Tilt {Vector3.Angle(normal, Vector3.up):F2} deg, " +
                    $"residual {residualRms * 1000f:F1} mm.",
            };
        }

        /// <summary>
        /// Least-squares plane through the points: the centroid plus the
        /// direction of least variance. Exact for three points, averaging for
        /// more. <paramref name="spreadRatio"/> reports how much the points
        /// spread across the plane relative to along it, which is what tells
        /// us whether the normal is trustworthy or the points are in a line.
        /// </summary>
        public static void FitPlane(
            IReadOnlyList<Vector3> points,
            out Vector3 centroid,
            out Vector3 normal,
            out float spreadRatio)
        {
            centroid = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                centroid += points[i];
            }

            centroid /= points.Count;

            var covariance = new double[3, 3];
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 d = points[i] - centroid;
                covariance[0, 0] += (double)d.x * d.x;
                covariance[0, 1] += (double)d.x * d.y;
                covariance[0, 2] += (double)d.x * d.z;
                covariance[1, 1] += (double)d.y * d.y;
                covariance[1, 2] += (double)d.y * d.z;
                covariance[2, 2] += (double)d.z * d.z;
            }

            covariance[1, 0] = covariance[0, 1];
            covariance[2, 0] = covariance[0, 2];
            covariance[2, 1] = covariance[1, 2];

            SymmetricEigen(covariance, out double[] values, out Vector3[] vectors);

            // Descending: values[0] is the longest axis, values[2] the normal.
            SortDescending(values, vectors);

            normal = vectors[2].normalized;

            double largest = Math.Max(values[0], 1e-18);
            spreadRatio = (float)Math.Sqrt(Math.Max(values[1], 0.0) / largest);
        }

        private static void SortDescending(double[] values, Vector3[] vectors)
        {
            for (int i = 0; i < 2; i++)
            {
                int best = i;
                for (int j = i + 1; j < 3; j++)
                {
                    if (values[j] > values[best])
                    {
                        best = j;
                    }
                }

                if (best == i)
                {
                    continue;
                }

                (values[i], values[best]) = (values[best], values[i]);
                (vectors[i], vectors[best]) = (vectors[best], vectors[i]);
            }
        }

        /// <summary>
        /// Cyclic Jacobi eigendecomposition of a symmetric 3x3 matrix. Unity
        /// ships no SVD, and this is short, dependency-free and numerically
        /// well behaved at this size.
        /// </summary>
        private static void SymmetricEigen(double[,] matrix, out double[] values, out Vector3[] vectors)
        {
            var a = new double[3, 3];
            Array.Copy(matrix, a, 9);

            var v = new double[3, 3];
            v[0, 0] = v[1, 1] = v[2, 2] = 1.0;

            for (int sweep = 0; sweep < 64; sweep++)
            {
                double off = (a[0, 1] * a[0, 1]) + (a[0, 2] * a[0, 2]) + (a[1, 2] * a[1, 2]);
                if (off <= 1e-30)
                {
                    break;
                }

                for (int p = 0; p < 2; p++)
                {
                    for (int q = p + 1; q < 3; q++)
                    {
                        double apq = a[p, q];
                        if (Math.Abs(apq) <= 1e-30)
                        {
                            continue;
                        }

                        double theta = (a[q, q] - a[p, p]) / (2.0 * apq);
                        double t = theta >= 0.0
                            ? 1.0 / (theta + Math.Sqrt((theta * theta) + 1.0))
                            : -1.0 / (-theta + Math.Sqrt((theta * theta) + 1.0));

                        double c = 1.0 / Math.Sqrt((t * t) + 1.0);
                        double s = t * c;

                        for (int k = 0; k < 3; k++)
                        {
                            double akp = a[k, p];
                            double akq = a[k, q];
                            a[k, p] = (c * akp) - (s * akq);
                            a[k, q] = (s * akp) + (c * akq);
                        }

                        for (int k = 0; k < 3; k++)
                        {
                            double apk = a[p, k];
                            double aqk = a[q, k];
                            a[p, k] = (c * apk) - (s * aqk);
                            a[q, k] = (s * apk) + (c * aqk);
                        }

                        for (int k = 0; k < 3; k++)
                        {
                            double vkp = v[k, p];
                            double vkq = v[k, q];
                            v[k, p] = (c * vkp) - (s * vkq);
                            v[k, q] = (s * vkp) + (c * vkq);
                        }
                    }
                }
            }

            values = new[] { a[0, 0], a[1, 1], a[2, 2] };
            vectors = new[]
            {
                new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]).normalized,
                new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]).normalized,
                new Vector3((float)v[0, 2], (float)v[1, 2], (float)v[2, 2]).normalized,
            };
        }

        private static List<MarkerObservation> BuildObservations(List<string> ids, Vector3[] points)
        {
            var list = new List<MarkerObservation>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                list.Add(new MarkerObservation(ids[i], points[i]));
            }

            return list;
        }

        private static AlignmentResult Failure(AlignmentStatus status, string message, int markerCount)
        {
            return new AlignmentResult
            {
                Status = status,
                Message = message,
                MarkerCount = markerCount,
                WorstMarkerId = string.Empty,
                WorstDriftPair = string.Empty,
            };
        }
    }
}
