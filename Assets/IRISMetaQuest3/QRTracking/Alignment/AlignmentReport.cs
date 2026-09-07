using System.Collections.Generic;
using System.Text;

namespace IRIS.MetaQuest3.Alignment
{
    /// <summary>
    /// Formats a solve for the in-headset debug panel. Kept separate from the
    /// MonoBehaviour so the wording can be tested without a device, and so the
    /// same text can be logged to adb.
    /// </summary>
    public static class AlignmentReport
    {
        /// <summary>Multi-line quality readout. Pass -1 to omit the sample count.</summary>
        public static string Detail(AlignmentResult result, int sampleCount = -1)
        {
            var text = new StringBuilder();

            if (!result.Succeeded)
            {
                text.AppendLine($"Not aligned - {result.Status}");
                text.Append(string.IsNullOrEmpty(result.Message) ? "No detail." : result.Message);
                return text.ToString();
            }

            text.Append($"Aligned from {result.MarkerCount} markers");
            if (sampleCount >= 0)
            {
                text.Append($", {sampleCount} sightings");
            }

            text.AppendLine();
            text.AppendLine($"Tilt vs gravity  {result.TiltFromGravityDeg:F2} deg");
            text.AppendLine(
                $"Plane residual   {result.PlaneResidualRms * 1000f:F1} mm rms " +
                $"(max {result.MaxPlaneResidual * 1000f:F1} mm)");

            if (!string.IsNullOrEmpty(result.WorstMarkerId))
            {
                text.AppendLine($"Furthest marker  {result.WorstMarkerId}");
            }

            text.Append($"Geometry drift   {result.WorstGeometryDriftRatio * 100f:F1}%");
            if (!string.IsNullOrEmpty(result.WorstDriftPair))
            {
                text.Append($" ({result.WorstDriftPair})");
            }

            if (!result.WasCrossChecked)
            {
                text.AppendLine();
                text.Append("Unverified - 3 markers and no reference; add a 4th to cross-check.");
            }

            return text.ToString();
        }

        /// <summary>
        /// Live calibration progress, naming the markers still short of
        /// sightings so the wearer knows where to look. Markers never have to
        /// share a frame, so this is the only cue that one is being missed.
        /// </summary>
        public static string Progress(
            IReadOnlyList<MarkerProgress> markers, float elapsedSeconds, int minSamples)
        {
            var ready = new List<string>();
            var waiting = new List<string>();

            foreach (MarkerProgress marker in markers)
            {
                if (marker.Samples >= minSamples)
                {
                    ready.Add(marker.Id);
                }
                else
                {
                    waiting.Add($"{marker.Id} ({marker.Samples}/{minSamples})");
                }
            }

            var text = new StringBuilder();
            text.AppendLine($"Calibrating {elapsedSeconds:F0}s - Found {markers.Count} marker(s) ({ready.Count} of {markers.Count} ready)");

            if (waiting.Count > 0)
            {
                text.Append("Look at: ");
                text.Append(string.Join(", ", waiting));
            }
            else
            {
                text.Append("All markers seen - solving.");
            }

            return text.ToString();
        }

        /// <summary>Single line, for a status bar or a log entry.</summary>
        public static string Summary(AlignmentResult result)
        {
            return result.Succeeded
                ? $"Aligned - tilt {result.TiltFromGravityDeg:F2} deg, " +
                  $"residual {result.PlaneResidualRms * 1000f:F1} mm, {result.MarkerCount} markers"
                : $"Not aligned - {result.Status}";
        }
    }
}
