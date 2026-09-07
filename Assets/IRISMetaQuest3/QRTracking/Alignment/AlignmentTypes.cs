using System;
using System.Collections.Generic;
using UnityEngine;

namespace IRIS.MetaQuest3.Alignment
{
    /// <summary>
    /// Why a solve succeeded or was rejected. Anything other than
    /// <see cref="Success"/> means the caller should keep the alignment it
    /// already has and surface the reason, rather than apply a bad pose.
    /// </summary>
    public enum AlignmentStatus
    {
        Success = 0,

        /// Fewer than three usable marker observations.
        TooFewMarkers,

        /// The origin or the X-axis marker was not observed.
        MissingRequiredMarker,

        /// The same marker id was observed more than once.
        DuplicateMarker,

        /// Origin and X-axis markers are too close together to define a direction.
        BaselineTooShort,

        /// The markers have moved relative to each other since the reference
        /// geometry was captured, so at least one has been knocked or
        /// misdetected. Only reachable once a reference exists.
        GeometryChanged,

        /// The markers are too close to collinear for the plane normal to be
        /// well conditioned.
        DegenerateGeometry,

        /// The markers do not lie on a common plane, e.g. one is sitting on
        /// something rather than flat on the surface.
        NotCoplanar,
    }

    /// <summary>One marker seen in headset world space. Positions are metres.</summary>
    public struct MarkerObservation
    {
        public string Id;
        public Vector3 Position;

        public MarkerObservation(string id, Vector3 position)
        {
            Id = id;
            Position = position;
        }
    }

    /// <summary>Where the scene origin sits relative to the two axis markers.</summary>
    public enum FrameOrigin
    {
        /// Origin sits on the first axis marker.
        AtFirstMarker,

        /// Origin sits halfway between the two axis markers. With the markers
        /// on opposite ends of a table edge this puts the robot at the middle
        /// of that edge, which is where it is usually mounted.
        MidpointOfAxisMarkers,
    }

    /// <summary>
    /// Which markers make up one environment's sheet and which two define the
    /// frame.
    ///
    /// Carries no distances: markers only need to be coplanar and not in a
    /// line, and nothing has to be measured or printed to spec.
    ///
    /// Marker payloads are "ENVIRONMENT_INDEX" (VENTION_1, VENTION_2, ...), so
    /// several sites can be told apart by what is printed on them alone.
    /// </summary>
    [Serializable]
    public class MarkerSet
    {
        [Tooltip("Environment these markers belong to, e.g. VENTION.")]
        public string EnvironmentId;

        [Tooltip("Payload strings of the markers in this environment.")]
        public List<string> MarkerIds = new List<string>();

        [Tooltip("First axis marker. With midpoint origin, one end of the table edge.")]
        public string FirstAxisMarkerId;

        [Tooltip("Second axis marker. +X runs from the first towards this one.")]
        public string SecondAxisMarkerId;

        [Tooltip("Where the origin sits relative to the two axis markers.")]
        public FrameOrigin OriginMode = FrameOrigin.MidpointOfAxisMarkers;

        public bool Contains(string id)
        {
            return MarkerIds != null && MarkerIds.Contains(id);
        }

        /// <summary>
        /// Builds a set from payloads actually seen for one environment. The
        /// two axis markers are chosen by index, so the sheet defines the frame
        /// and no per-site configuration is needed.
        /// </summary>
        public static MarkerSet FromPayloads(
            string environmentId,
            IEnumerable<string> payloads,
            int firstAxisIndex,
            int secondAxisIndex,
            FrameOrigin originMode = FrameOrigin.MidpointOfAxisMarkers)
        {
            var ids = new List<string>(payloads);
            ids.Sort(StringComparer.Ordinal);

            return new MarkerSet
            {
                EnvironmentId = environmentId,
                MarkerIds = ids,
                FirstAxisMarkerId = MarkerPayloadParser.Compose(environmentId, firstAxisIndex),
                SecondAxisMarkerId = MarkerPayloadParser.Compose(environmentId, secondAxisIndex),
                OriginMode = originMode,
            };
        }

        /// <summary>Four markers of one environment, indices 1 to 4.</summary>
        public static MarkerSet CreateDefault(string environmentId = "VENTION")
        {
            return FromPayloads(
                environmentId,
                new[]
                {
                    MarkerPayloadParser.Compose(environmentId, 1),
                    MarkerPayloadParser.Compose(environmentId, 2),
                    MarkerPayloadParser.Compose(environmentId, 3),
                    MarkerPayloadParser.Compose(environmentId, 4),
                },
                1, 2);
        }
    }

    /// <summary>
    /// Distances between markers as actually observed, captured from a solve
    /// the operator accepted. Later solves compare against it, so a marker that
    /// is later nudged or misdetected is caught — without anyone ever having
    /// measured the sheet. The first accepted solve defines the truth.
    /// </summary>
    public class MarkerGeometry
    {
        private readonly Dictionary<string, float> distances = new Dictionary<string, float>();

        public int PairCount => distances.Count;

        private static string Key(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        public static MarkerGeometry FromObservations(IReadOnlyList<MarkerObservation> observations)
        {
            var geometry = new MarkerGeometry();
            for (int i = 0; i < observations.Count; i++)
            {
                for (int j = i + 1; j < observations.Count; j++)
                {
                    geometry.distances[Key(observations[i].Id, observations[j].Id)] =
                        Vector3.Distance(observations[i].Position, observations[j].Position);
                }
            }

            return geometry;
        }

        public bool TryGetDistance(string a, string b, out float distance)
        {
            return distances.TryGetValue(Key(a, b), out distance);
        }
    }

    /// <summary>How many sightings one marker has gathered so far.</summary>
    public struct MarkerProgress
    {
        public string Id;
        public int Samples;

        public MarkerProgress(string id, int samples)
        {
            Id = id;
            Samples = samples;
        }
    }

    /// <summary>Rejection thresholds. None of them require a measured sheet.</summary>
    [Serializable]
    public struct AlignmentTolerances
    {
        [Tooltip("Permitted relative change in a marker separation since the reference was captured.")]
        public float MaxGeometryDriftRatio;

        [Tooltip("Shortest usable origin-to-X-axis distance, in metres.")]
        public float MinBaselineMeters;

        [Tooltip("Lower bound on spread across the plane relative to along it. Guards against collinearity.")]
        public float MinSpreadRatio;

        [Tooltip("Largest permitted RMS distance from the markers to the fitted plane, in metres.")]
        public float MaxPlaneResidualMeters;

        public static AlignmentTolerances Default => new AlignmentTolerances
        {
            MaxGeometryDriftRatio = 0.03f,
            MinBaselineMeters = 0.20f,
            MinSpreadRatio = 0.08f,
            MaxPlaneResidualMeters = 0.010f,
        };
    }

    /// <summary>Everything one solve produced, including the numbers worth showing the operator.</summary>
    public struct AlignmentResult
    {
        public AlignmentStatus Status;

        /// Pose to apply to the scene root. Only meaningful when <see cref="Succeeded"/>.
        public Pose Pose;

        /// Fitted plane normal in world space, oriented away from the floor.
        public Vector3 Normal;

        /// Angle between the fitted normal and gravity, in degrees. Meta's
        /// tracking space is gravity-aligned, so world up is the gravity vector
        /// and this needs no IMU of its own. This is the headline quality number.
        public float TiltFromGravityDeg;

        /// RMS distance from the markers to the fitted plane, in metres.
        public float PlaneResidualRms;

        /// Largest single distance from a marker to the fitted plane, in metres.
        public float MaxPlaneResidual;

        /// Marker responsible for <see cref="MaxPlaneResidual"/>, or empty when
        /// the arrangement cannot single one out. A symmetric set with one
        /// marker off-plane tilts the fitted plane until every marker is
        /// equally far from it, and naming one then would be a guess.
        public string WorstMarkerId;

        /// Largest relative change against the reference geometry, if one was supplied.
        public float WorstGeometryDriftRatio;

        /// Marker pair responsible for <see cref="WorstGeometryDriftRatio"/>.
        public string WorstDriftPair;

        /// How far the markers spread across their longest axis, as a fraction
        /// of it. Near zero means near-collinear.
        public float SpreadRatio;

        public int MarkerCount;

        /// Environment this alignment belongs to, from the marker payloads.
        public string EnvironmentId;

        /// Observed separations from this solve. Cache this after an accepted
        /// alignment to become the reference for later ones.
        public MarkerGeometry ObservedGeometry;

        /// Whether the solve had anything to check itself against. Three markers
        /// with no reference geometry fit a plane exactly and can validate
        /// nothing, so the fit has to be taken on trust.
        public bool WasCrossChecked;

        /// Human-readable explanation, suitable for the in-headset debug text.
        public string Message;

        public bool Succeeded => Status == AlignmentStatus.Success;
    }
}
