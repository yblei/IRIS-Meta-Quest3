using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace IRIS.MetaQuest3.Alignment.Tests
{
    public class MarkerPayloadParserTests
    {
        [TestCase("VENTION_1", "VENTION", 1)]
        [TestCase("VENTION_12", "VENTION", 12)]
        [TestCase("IRIS_0", "IRIS", 0)]
        [TestCase("CELL_A_3", "CELL_A", 3)]
        public void TryParse_SplitsEnvironmentFromIndex(string payload, string env, int index)
        {
            Assert.IsTrue(MarkerPayloadParser.TryParse(payload, out string parsedEnv, out int parsedIndex));
            Assert.AreEqual(env, parsedEnv);
            Assert.AreEqual(index, parsedIndex);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("NOINDEX")]
        [TestCase("TRAILING_")]
        [TestCase("_1")]
        [TestCase("VENTION_X")]
        public void TryParse_RejectsAnythingElse(string payload)
        {
            Assert.IsFalse(MarkerPayloadParser.TryParse(payload, out _, out _));
        }

        [Test]
        public void Compose_RoundTrips()
        {
            string payload = MarkerPayloadParser.Compose("VENTION", 3);
            Assert.AreEqual("VENTION_3", payload);
            Assert.IsTrue(MarkerPayloadParser.TryParse(payload, out string env, out int index));
            Assert.AreEqual("VENTION", env);
            Assert.AreEqual(3, index);
        }
    }

    public class EnvironmentStoreTests
    {
        private string path;

        [SetUp]
        public void SetUp()
        {
            path = Path.Combine(Path.GetTempPath(), $"iris-env-{Path.GetRandomFileName()}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Offsets_SurviveAReload()
        {
            var store = new EnvironmentStore(path);
            store.SetOffset("VENTION", new Vector3(0.1f, -0.25f, 0.4f));
            store.SetOffset("CELL_A", new Vector3(1f, 2f, 3f));

            var reopened = new EnvironmentStore(path);

            Assert.AreEqual(2, reopened.Count);
            Assert.IsTrue(reopened.TryGetOffset("VENTION", out Vector3 vention));
            Assert.AreEqual(0.1f, vention.x, 1e-5f);
            Assert.AreEqual(-0.25f, vention.y, 1e-5f);
            Assert.AreEqual(0.4f, vention.z, 1e-5f);

            Assert.IsTrue(reopened.TryGetOffset("CELL_A", out Vector3 cell));
            Assert.AreEqual(2f, cell.y, 1e-5f);
        }

        [Test]
        public void EnvironmentsAreIndependent()
        {
            var store = new EnvironmentStore(path);
            store.SetOffset("VENTION", Vector3.one);

            Assert.IsFalse(store.TryGetOffset("SOMEWHERE_ELSE", out Vector3 missing));
            Assert.AreEqual(Vector3.zero, missing);
        }

        [Test]
        public void Forget_RemovesOnlyThatEnvironment()
        {
            var store = new EnvironmentStore(path);
            store.SetOffset("A", Vector3.one);
            store.SetOffset("B", Vector3.up);

            store.Forget("A");

            Assert.IsFalse(store.TryGetOffset("A", out _));
            Assert.IsTrue(store.TryGetOffset("B", out _));
            Assert.AreEqual(1, new EnvironmentStore(path).Count);
        }

        [Test]
        public void MissingFile_StartsEmptyRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => new EnvironmentStore(path));
            Assert.AreEqual(0, new EnvironmentStore(path).Count);
        }

        [Test]
        public void CorruptFile_StartsEmptyRatherThanThrowing()
        {
            File.WriteAllText(path, "{ this is not json");
            EnvironmentStore store = null;

            Assert.DoesNotThrow(() => store = new EnvironmentStore(path));
            Assert.AreEqual(0, store.Count, "a corrupt file must not stop the app aligning");
        }
    }
}
