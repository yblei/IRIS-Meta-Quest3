using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace IRIS.MetaQuest3.Alignment.Tests
{
    public class MarkerSampleAccumulatorTests
    {
        private const float Sigma = 2.5f;

        private static MarkerObservation Find(List<MarkerObservation> list, string id)
        {
            MarkerObservation found = list.Find(o => o.Id == id);
            Assert.AreEqual(id, found.Id, $"'{id}' missing from reduced set");
            return found;
        }

        [Test]
        public void Reduce_AveragesEachMarkerSeparately()
        {
            var accumulator = new MarkerSampleAccumulator();
            for (int i = 0; i < 10; i++)
            {
                accumulator.Add("A", new Vector3(i, 0f, 0f));
                accumulator.Add("B", new Vector3(0f, 0f, 100f + i));
            }

            Assert.AreEqual(2, accumulator.MarkerCount);
            Assert.AreEqual(20, accumulator.TotalSamples);

            List<MarkerObservation> reduced = accumulator.Reduce(10, Sigma);

            Assert.AreEqual(2, reduced.Count);
            Assert.AreEqual(4.5f, Find(reduced, "A").Position.x, 1e-4f);
            Assert.AreEqual(104.5f, Find(reduced, "B").Position.z, 1e-4f);
        }

        [Test]
        public void Reduce_SkipsMarkersWithTooFewSightings()
        {
            var accumulator = new MarkerSampleAccumulator();
            for (int i = 0; i < 10; i++)
            {
                accumulator.Add("seen_often", Vector3.zero);
            }

            accumulator.Add("glimpsed_once", Vector3.one);

            List<MarkerObservation> reduced = accumulator.Reduce(10, Sigma);

            Assert.AreEqual(1, reduced.Count, "a marker seen once should not contribute a position");
            Assert.AreEqual("seen_often", reduced[0].Id);
        }

        [Test]
        public void Reduce_DiscardsAWildSighting()
        {
            var accumulator = new MarkerSampleAccumulator();
            for (int i = 0; i < 20; i++)
            {
                accumulator.Add("A", Vector3.zero);
            }

            accumulator.Add("A", new Vector3(0f, 10f, 0f));

            Vector3 position = Find(accumulator.Reduce(10, Sigma), "A").Position;

            Assert.AreEqual(0f, position.magnitude, 1e-4f,
                "the outlier should be clipped rather than dragging the mean");
        }

        [Test]
        public void Reduce_KeepsPlainMean_WhenClippingIsDisabled()
        {
            var accumulator = new MarkerSampleAccumulator();
            for (int i = 0; i < 20; i++)
            {
                accumulator.Add("A", Vector3.zero);
            }

            accumulator.Add("A", new Vector3(0f, 21f, 0f));

            Vector3 position = Find(accumulator.Reduce(10, 0f), "A").Position;

            Assert.AreEqual(1f, position.y, 1e-4f);
        }

        [Test]
        public void MarkersAccumulate_EvenWhenNeverSeenTogether()
        {
            // The wearer looks at one marker, then the other. They never share
            // a frame, which is the normal case on a wide table.
            var accumulator = new MarkerSampleAccumulator();

            for (int i = 0; i < 12; i++)
            {
                accumulator.Add("left", new Vector3(-1f, 0.75f, 0f));
            }

            for (int i = 0; i < 12; i++)
            {
                accumulator.Add("right", new Vector3(1f, 0.75f, 0f));
            }

            List<MarkerObservation> reduced = accumulator.Reduce(10, Sigma);

            Assert.AreEqual(2, reduced.Count);
            Assert.AreEqual(-1f, Find(reduced, "left").Position.x, 1e-4f);
            Assert.AreEqual(1f, Find(reduced, "right").Position.x, 1e-4f);
        }

        [Test]
        public void Clear_ResetsCounts()
        {
            var accumulator = new MarkerSampleAccumulator();
            accumulator.Add("A", Vector3.zero);
            accumulator.Add("B", Vector3.one);

            accumulator.Clear();

            Assert.AreEqual(0, accumulator.MarkerCount);
            Assert.AreEqual(0, accumulator.TotalSamples);
            Assert.AreEqual(0, accumulator.SamplesFor("A"));
            Assert.IsEmpty(accumulator.Reduce(1, Sigma));
        }
    }
}
