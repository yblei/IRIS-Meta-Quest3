using System.Collections.Generic;
using UnityEngine;

namespace IRIS.MetaQuest3.Alignment
{
    /// <summary>
    /// Gathers marker sightings across a calibration window and reduces each
    /// marker's samples to one robust position.
    ///
    /// Markers accumulate independently on purpose. On a wide table the headset
    /// may never see every marker in a single frame, but its world frame is
    /// stable between glances, so sightings taken at different moments still
    /// compose into one consistent set.
    /// </summary>
    public class MarkerSampleAccumulator
    {
        private readonly Dictionary<string, List<Vector3>> samples =
            new Dictionary<string, List<Vector3>>();

        /// <summary>How many distinct markers have been seen at least once.</summary>
        public int MarkerCount => samples.Count;

        public int TotalSamples { get; private set; }

        /// <summary>Every marker seen at least once this window.</summary>
        public IEnumerable<string> SeenMarkerIds => samples.Keys;

        public void Add(string id, Vector3 position)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (!samples.TryGetValue(id, out List<Vector3> list))
            {
                list = new List<Vector3>();
                samples.Add(id, list);
            }

            list.Add(position);
            TotalSamples++;
        }

        public void Clear()
        {
            samples.Clear();
            TotalSamples = 0;
        }

        public int SamplesFor(string id)
        {
            return samples.TryGetValue(id, out List<Vector3> list) ? list.Count : 0;
        }

        /// <summary>
        /// Collapses each marker with enough sightings into a single position,
        /// discarding samples more than <paramref name="outlierSigma"/> RMS
        /// deviations from that marker's mean before averaging again.
        /// Markers with too few sightings are left out entirely rather than
        /// contributing a poorly supported position.
        /// </summary>
        public List<MarkerObservation> Reduce(int minSamplesPerMarker, float outlierSigma)
        {
            var observations = new List<MarkerObservation>(samples.Count);

            foreach (KeyValuePair<string, List<Vector3>> entry in samples)
            {
                List<Vector3> list = entry.Value;
                if (list.Count < minSamplesPerMarker)
                {
                    continue;
                }

                observations.Add(new MarkerObservation(entry.Key, RobustMean(list, outlierSigma)));
            }

            return observations;
        }

        private static Vector3 RobustMean(List<Vector3> list, float outlierSigma)
        {
            Vector3 mean = Vector3.zero;
            for (int i = 0; i < list.Count; i++)
            {
                mean += list[i];
            }

            mean /= list.Count;

            if (list.Count < 3 || outlierSigma <= 0f)
            {
                return mean;
            }

            float sumSquared = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                sumSquared += (list[i] - mean).sqrMagnitude;
            }

            float sigma = Mathf.Sqrt(sumSquared / list.Count);
            if (sigma <= 1e-6f)
            {
                return mean;
            }

            float limit = outlierSigma * sigma;

            Vector3 kept = Vector3.zero;
            int keptCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i] - mean).magnitude <= limit)
                {
                    kept += list[i];
                    keptCount++;
                }
            }

            // Everything rejected means the spread is pathological; the plain
            // mean is still the best answer available.
            return keptCount > 0 ? kept / keptCount : mean;
        }
    }
}
