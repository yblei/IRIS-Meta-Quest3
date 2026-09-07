using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace IRIS.MetaQuest3.Alignment.Tests
{
    public class MarkerAlignmentSolverTests
    {
        private const string M0 = "IRIS_0";
        private const string M1 = "IRIS_1";
        private const string M2 = "IRIS_2";
        private const string M3 = "IRIS_3";
        private const string Centre = "IRIS_C";

        private static MarkerSet Set(params string[] ids)
        {
            return new MarkerSet
            {
                EnvironmentId = "IRIS",
                FirstAxisMarkerId = ids[0],
                SecondAxisMarkerId = ids[1],
                MarkerIds = new List<string>(ids),
                OriginMode = FrameOrigin.AtFirstMarker,
            };
        }

        private static AlignmentTolerances Permissive => new AlignmentTolerances
        {
            MaxGeometryDriftRatio = 0.25f,
            MinBaselineMeters = 0.05f,
            MinSpreadRatio = 0.01f,
            MaxPlaneResidualMeters = 1.0f,
        };

        /// <summary>
        /// Places markers from in-surface coordinates. Nothing here is a
        /// "known layout" - the solver never sees these numbers, they only
        /// describe where the test puts the markers in the world.
        /// </summary>
        private static List<MarkerObservation> Place(
            Quaternion rotation, Vector3 translation, params (string id, float u, float v)[] points)
        {
            var observations = new List<MarkerObservation>(points.Length);
            foreach ((string id, float u, float v) in points)
            {
                Vector3 onSurface = new Vector3(u, 0f, v);
                observations.Add(new MarkerObservation(id, (rotation * onSurface) + translation));
            }

            return observations;
        }

        private static (string, float, float)[] Rectangle()
        {
            return new[] { (M0, 0f, 0f), (M1, 1f, 0f), (M2, 0f, 0.8f), (M3, 1f, 0.8f) };
        }

        // ---- happy paths ---------------------------------------------------

        [Test]
        public void LevelSurface_ProducesUprightIdentityFrame()
        {
            var height = new Vector3(0f, 0.75f, 0f);
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, height, Rectangle()),
                Set(M0, M1, M2, M3),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
            Assert.AreEqual(0f, result.TiltFromGravityDeg, 1e-3f);
            Assert.AreEqual(0f, Vector3.Distance(result.Pose.position, height), 1e-4f);
            Assert.AreEqual(0f, Quaternion.Angle(result.Pose.rotation, Quaternion.identity), 1e-3f);
            Assert.AreEqual(4, result.MarkerCount);
        }

        [Test]
        public void TiltedSurface_RecoversTheTiltAngle()
        {
            const float tilt = 7.5f;
            Quaternion rotation = Quaternion.Euler(tilt, 33f, 0f);

            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(rotation, new Vector3(1f, 0.7f, -2f), Rectangle()),
                Set(M0, M1, M2, M3),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
            Assert.AreEqual(tilt, result.TiltFromGravityDeg, 1e-2f);
            Assert.AreEqual(0f, Vector3.Angle(result.Normal, rotation * Vector3.up), 1e-2f);
        }

        [Test]
        public void Normal_PointsAwayFromTheFloor_WhateverTheMarkerOrder()
        {
            // Third marker on the far side, flipping the raw cross-product sign.
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero,
                    (M0, 0f, 0f), (M1, 1f, 0f), (M2, 0f, -0.8f)),
                Set(M0, M1, M2),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
            Assert.Greater(Vector3.Dot(result.Normal, Vector3.up), 0.99f);
        }

        [Test]
        public void StrayMarkerOutsideTheSet_IsIgnored()
        {
            List<MarkerObservation> observations = Place(Quaternion.identity, Vector3.zero, Rectangle());
            // A QR code on a poster across the room, well off the surface.
            observations.Add(new MarkerObservation("SOME_OTHER_CODE", new Vector3(3f, 2.2f, 4f)));

            AlignmentResult result = MarkerAlignmentSolver.Solve(
                observations, Set(M0, M1, M2, M3), AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
            Assert.AreEqual(4, result.MarkerCount, "the stray code must not join the fit");
            Assert.AreEqual(0f, result.TiltFromGravityDeg, 1e-3f);
        }

        [Test]
        public void MidpointOrigin_PutsTheRobotBetweenTheTwoAxisMarkers()
        {
            // Axis markers on the ends of a table edge; origin should land
            // halfway along it, which is where the robot is mounted.
            var set = new MarkerSet
            {
                EnvironmentId = "VENTION",
                FirstAxisMarkerId = "VENTION_1",
                SecondAxisMarkerId = "VENTION_2",
                MarkerIds = new List<string> { "VENTION_1", "VENTION_2", "VENTION_3", "VENTION_4" },
                OriginMode = FrameOrigin.MidpointOfAxisMarkers,
            };

            var height = new Vector3(0f, 0.75f, 0f);
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, height,
                    ("VENTION_1", 0f, 0f), ("VENTION_2", 1.4f, 0f),
                    ("VENTION_3", 0f, 0.9f), ("VENTION_4", 1.4f, 0.9f)),
                set,
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
            Assert.AreEqual(0.7f, result.Pose.position.x, 1e-4f, "origin should sit mid-edge");
            Assert.AreEqual(0f, result.Pose.position.z, 1e-4f, "origin should stay on the edge");
            Assert.AreEqual(0.75f, result.Pose.position.y, 1e-4f);
            Assert.AreEqual("VENTION", result.EnvironmentId);
        }

        [Test]
        public void AtFirstMarkerOrigin_PutsTheOriginOnMarkerOne()
        {
            var set = new MarkerSet
            {
                EnvironmentId = "VENTION",
                FirstAxisMarkerId = "VENTION_1",
                SecondAxisMarkerId = "VENTION_2",
                MarkerIds = new List<string> { "VENTION_1", "VENTION_2", "VENTION_3" },
                OriginMode = FrameOrigin.AtFirstMarker,
            };

            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero,
                    ("VENTION_1", 0f, 0f), ("VENTION_2", 1.4f, 0f), ("VENTION_3", 0f, 0.9f)),
                set,
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
            Assert.AreEqual(0f, result.Pose.position.x, 1e-4f);
        }

        // ---- self-verification without any measured sheet ------------------

        [Test]
        public void ThreeMarkers_WithNoReference_AreFlaggedAsUnverified()
        {
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero, (M0, 0f, 0f), (M1, 1f, 0f), (M2, 0f, 0.8f)),
                Set(M0, M1, M2),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
            Assert.IsFalse(result.WasCrossChecked,
                "three points fit a plane exactly, so nothing was actually verified");
        }

        [Test]
        public void FourMarkers_AreCrossChecked()
        {
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero, Rectangle()),
                Set(M0, M1, M2, M3),
                AlignmentTolerances.Default);

            Assert.IsTrue(result.WasCrossChecked);
        }

        [Test]
        public void KnockedMarker_IsCaughtAgainstTheLearnedGeometry()
        {
            MarkerSet set = Set(M0, M1, M2, M3);
            List<MarkerObservation> first = Place(Quaternion.identity, Vector3.zero, Rectangle());

            AlignmentResult calibration =
                MarkerAlignmentSolver.Solve(first, set, AlignmentTolerances.Default);
            Assert.AreEqual(AlignmentStatus.Success, calibration.Status, calibration.Message);
            Assert.IsNotNull(calibration.ObservedGeometry);

            // Later someone slides one marker 12 cm along the surface.
            List<MarkerObservation> later = Place(Quaternion.identity, Vector3.zero,
                (M0, 0f, 0f), (M1, 1f, 0f), (M2, 0f, 0.92f), (M3, 1f, 0.8f));

            AlignmentResult result = MarkerAlignmentSolver.Solve(
                later, set, AlignmentTolerances.Default, calibration.ObservedGeometry);

            Assert.AreEqual(AlignmentStatus.GeometryChanged, result.Status, result.Message);
            Assert.Greater(result.WorstGeometryDriftRatio, AlignmentTolerances.Default.MaxGeometryDriftRatio);
            StringAssert.Contains(M2, result.WorstDriftPair);
        }

        [Test]
        public void WithoutAReference_TheSameMoveIsAccepted()
        {
            // Nothing to compare against on a first calibration: the solver
            // has no way to know the sheet used to look different.
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero,
                    (M0, 0f, 0f), (M1, 1f, 0f), (M2, 0f, 0.92f), (M3, 1f, 0.8f)),
                Set(M0, M1, M2, M3),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
        }

        [Test]
        public void UnchangedMarkers_PassTheDriftCheck()
        {
            MarkerSet set = Set(M0, M1, M2, M3);
            List<MarkerObservation> observations = Place(Quaternion.identity, Vector3.zero, Rectangle());

            AlignmentResult first = MarkerAlignmentSolver.Solve(observations, set, AlignmentTolerances.Default);
            AlignmentResult second = MarkerAlignmentSolver.Solve(
                observations, set, AlignmentTolerances.Default, first.ObservedGeometry);

            Assert.AreEqual(AlignmentStatus.Success, second.Status, second.Message);
            Assert.AreEqual(0f, second.WorstGeometryDriftRatio, 1e-4f);
            Assert.IsTrue(second.WasCrossChecked);
        }

        // ---- rejections ----------------------------------------------------

        [Test]
        public void TwoMarkers_AreRejected()
        {
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero, (M0, 0f, 0f), (M1, 1f, 0f)),
                Set(M0, M1, M2),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.TooFewMarkers, result.Status);
        }

        [Test]
        public void MissingOriginMarker_IsRejected()
        {
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero, (M1, 1f, 0f), (M2, 0f, 0.8f), (M3, 1f, 0.8f)),
                Set(M0, M1, M2, M3),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.MissingRequiredMarker, result.Status);
            StringAssert.Contains(M0, result.Message);
        }

        [Test]
        public void DuplicateMarker_IsRejected()
        {
            List<MarkerObservation> observations = Place(Quaternion.identity, Vector3.zero, Rectangle());
            observations.Add(new MarkerObservation(M0, Vector3.one));

            AlignmentResult result = MarkerAlignmentSolver.Solve(
                observations, Set(M0, M1, M2, M3), AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.DuplicateMarker, result.Status);
        }

        [Test]
        public void NearCollinearMarkers_AreRejected()
        {
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero,
                    (M0, 0f, 0f), (M1, 1f, 0f), (M2, 0.5f, 0.004f)),
                Set(M0, M1, M2),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.DegenerateGeometry, result.Status, result.Message);
            Assert.Less(result.SpreadRatio, AlignmentTolerances.Default.MinSpreadRatio);
        }

        [Test]
        public void ShortBaseline_IsRejected()
        {
            AlignmentResult result = MarkerAlignmentSolver.Solve(
                Place(Quaternion.identity, Vector3.zero,
                    (M0, 0f, 0f), (M1, 0.10f, 0f), (M2, 0f, 0.10f)),
                Set(M0, M1, M2),
                AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.BaselineTooShort, result.Status, result.Message);
        }

        [Test]
        public void MarkerLiftedOffTheSurface_IsRejectedAsNotCoplanar()
        {
            List<MarkerObservation> observations = Place(Quaternion.identity, Vector3.zero, Rectangle());
            int index = observations.FindIndex(o => o.Id == M3);
            observations[index] = new MarkerObservation(
                M3, observations[index].Position + new Vector3(0f, 0.08f, 0f));

            AlignmentResult result = MarkerAlignmentSolver.Solve(
                observations, Set(M0, M1, M2, M3), AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.NotCoplanar, result.Status, result.Message);

            // Four markers on a rectangle cannot say WHICH one is off: the
            // fitted plane tilts until all four sit equally far from it.
            Assert.IsEmpty(result.WorstMarkerId);
            StringAssert.Contains("cannot tell which", result.Message);
        }

        [Test]
        public void LiftedCentreMarker_IsNamed_WhenTheArrangementCanSingleItOut()
        {
            // Tilting the plane cannot absorb a raised centre the way it
            // absorbs a raised corner, so the offender stands out 4:1.
            List<MarkerObservation> observations = Place(Quaternion.identity, Vector3.zero,
                (M0, 0f, 0f), (M1, 1f, 0f), (M2, 0f, 0.8f), (M3, 1f, 0.8f), (Centre, 0.5f, 0.4f));

            int index = observations.FindIndex(o => o.Id == Centre);
            observations[index] = new MarkerObservation(
                Centre, observations[index].Position + new Vector3(0f, 0.08f, 0f));

            AlignmentResult result = MarkerAlignmentSolver.Solve(
                observations, Set(M0, M1, M2, M3, Centre), AlignmentTolerances.Default);

            Assert.AreEqual(AlignmentStatus.NotCoplanar, result.Status, result.Message);
            Assert.AreEqual(Centre, result.WorstMarkerId);
        }

        // ---- the reason this design exists ---------------------------------

        [Test]
        public void WiderBaseline_ReducesTiltError_UnderIdenticalNoise()
        {
            const float sigma = 0.003f;
            const int trials = 400;

            float narrow = MeanTiltError(0.30f, sigma, trials, seed: 12345);
            float wide = MeanTiltError(1.20f, sigma, trials, seed: 12345);

            Assert.Less(wide, narrow * 0.5f,
                $"wide baseline {wide:F3} deg should beat narrow {narrow:F3} deg");
            Assert.Less(wide, 0.5f);
        }

        private static float MeanTiltError(float baseline, float sigma, int trials, int seed)
        {
            var random = new System.Random(seed);
            MarkerSet set = Set(M0, M1, M2);
            List<MarkerObservation> clean = Place(
                Quaternion.identity, new Vector3(0f, 0.75f, 0f),
                (M0, 0f, 0f), (M1, baseline, 0f), (M2, 0f, baseline * 0.8f));

            double total = 0.0;
            for (int trial = 0; trial < trials; trial++)
            {
                var noisy = new List<MarkerObservation>(clean.Count);
                foreach (MarkerObservation observation in clean)
                {
                    noisy.Add(new MarkerObservation(
                        observation.Id, observation.Position + Gaussian(random, sigma)));
                }

                AlignmentResult result = MarkerAlignmentSolver.Solve(noisy, set, Permissive);
                Assert.AreEqual(AlignmentStatus.Success, result.Status, result.Message);
                total += result.TiltFromGravityDeg;
            }

            return (float)(total / trials);
        }

        private static Vector3 Gaussian(System.Random random, float sigma)
        {
            return new Vector3(
                NextNormal(random) * sigma, NextNormal(random) * sigma, NextNormal(random) * sigma);
        }

        private static float NextNormal(System.Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
