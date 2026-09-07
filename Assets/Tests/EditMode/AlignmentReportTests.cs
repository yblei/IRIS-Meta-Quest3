using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace IRIS.MetaQuest3.Alignment.Tests
{
    public class AlignmentReportTests
    {
        private static AlignmentResult Success()
        {
            return new AlignmentResult
            {
                Status = AlignmentStatus.Success,
                Pose = new Pose(Vector3.zero, Quaternion.identity),
                Normal = Vector3.up,
                TiltFromGravityDeg = 0.284f,
                PlaneResidualRms = 0.0014f,
                MaxPlaneResidual = 0.0021f,
                WorstMarkerId = "IRIS_2",
                WorstGeometryDriftRatio = 0.006f,
                WorstDriftPair = "IRIS_0-IRIS_1",
                SpreadRatio = 0.8f,
                MarkerCount = 4,
                WasCrossChecked = true,
                Message = "ok",
            };
        }

        [Test]
        public void Detail_ReportsTheNumbersAnOperatorActsOn()
        {
            string text = AlignmentReport.Detail(Success(), sampleCount: 240);

            StringAssert.Contains("0.28 deg", text);
            StringAssert.Contains("1.4 mm rms", text);
            StringAssert.Contains("max 2.1 mm", text);
            StringAssert.Contains("IRIS_2", text);
            StringAssert.Contains("0.6%", text);
            StringAssert.Contains("240 sightings", text);
        }

        [Test]
        public void Detail_OmitsSampleCount_WhenNotSupplied()
        {
            StringAssert.DoesNotContain("sightings", AlignmentReport.Detail(Success()));
        }

        [Test]
        public void Detail_OmitsTheFurthestMarker_WhenNoneCouldBeSingledOut()
        {
            AlignmentResult ambiguous = Success();
            ambiguous.WorstMarkerId = string.Empty;

            StringAssert.DoesNotContain("Furthest marker", AlignmentReport.Detail(ambiguous));
        }

        [Test]
        public void Detail_OnRejection_LeadsWithTheReason()
        {
            var rejected = new AlignmentResult
            {
                Status = AlignmentStatus.GeometryChanged,
                Message = "Separation IRIS_0-IRIS_2 has changed by 12.0% since calibration.",
            };

            string text = AlignmentReport.Detail(rejected);

            StringAssert.Contains("GeometryChanged", text);
            StringAssert.Contains("12.0%", text);
        }

        [Test]
        public void Detail_SurvivesADefaultResult_WithNullStrings()
        {
            Assert.DoesNotThrow(() => AlignmentReport.Detail(default));
            Assert.IsNotEmpty(AlignmentReport.Detail(default));
        }

        [Test]
        public void Progress_NamesTheMarkersStillNeeded()
        {
            var markers = new List<MarkerProgress>
            {
                new MarkerProgress("IRIS_0", 14),
                new MarkerProgress("IRIS_1", 10),
                new MarkerProgress("IRIS_2", 3),
                new MarkerProgress("IRIS_3", 0),
            };

            string text = AlignmentReport.Progress(markers, 8f, minSamples: 10);

            StringAssert.Contains("2 of 4 ready", text);
            StringAssert.Contains("Look at:", text);
            StringAssert.Contains("IRIS_2 (3/10)", text);
            StringAssert.Contains("IRIS_3 (0/10)", text);
            StringAssert.DoesNotContain("IRIS_0 (", text);
        }

        [Test]
        public void Progress_SaysSolving_WhenEveryMarkerIsReady()
        {
            var markers = new List<MarkerProgress>
            {
                new MarkerProgress("IRIS_0", 12),
                new MarkerProgress("IRIS_1", 11),
                new MarkerProgress("IRIS_2", 10),
            };

            string text = AlignmentReport.Progress(markers, 4f, minSamples: 10);

            StringAssert.Contains("3 of 3 ready", text);
            StringAssert.Contains("solving", text);
        }

        [Test]
        public void Summary_IsOneLine()
        {
            string summary = AlignmentReport.Summary(Success());

            Assert.IsFalse(summary.Contains("\n"), "summary must fit a single status line");
            StringAssert.Contains("0.28 deg", summary);
        }
    }
}
