using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IRIS.MetaQuest3.Alignment
{
    /// <summary>One site's remembered adjustment, keyed by marker prefix.</summary>
    [Serializable]
    public class EnvironmentProfile
    {
        public string EnvironmentId;

        /// Manual nudge applied after alignment, expressed in the aligned
        /// frame, so it stays correct wherever the table sits in the room.
        public Vector3 Offset;

        public string LastAlignedUtc;
    }

    [Serializable]
    internal class EnvironmentProfileList
    {
        public List<EnvironmentProfile> Profiles = new List<EnvironmentProfile>();
    }

    /// <summary>
    /// Remembers each environment's offset across sessions, so a site that has
    /// been adjusted once never has to be adjusted again.
    ///
    /// Takes an explicit path rather than reaching for
    /// Application.persistentDataPath, so it can be exercised in tests.
    /// </summary>
    public class EnvironmentStore
    {
        private readonly string filePath;
        private readonly Dictionary<string, EnvironmentProfile> profiles =
            new Dictionary<string, EnvironmentProfile>(StringComparer.Ordinal);

        public EnvironmentStore(string filePath)
        {
            this.filePath = filePath;
            Load();
        }

        public int Count => profiles.Count;

        public IEnumerable<string> KnownEnvironments => profiles.Keys;

        public bool TryGetOffset(string environmentId, out Vector3 offset)
        {
            if (!string.IsNullOrEmpty(environmentId) &&
                profiles.TryGetValue(environmentId, out EnvironmentProfile profile))
            {
                offset = profile.Offset;
                return true;
            }

            offset = Vector3.zero;
            return false;
        }

        public void SetOffset(string environmentId, Vector3 offset)
        {
            if (string.IsNullOrEmpty(environmentId))
            {
                return;
            }

            if (!profiles.TryGetValue(environmentId, out EnvironmentProfile profile))
            {
                profile = new EnvironmentProfile { EnvironmentId = environmentId };
                profiles.Add(environmentId, profile);
            }

            profile.Offset = offset;
            profile.LastAlignedUtc = DateTime.UtcNow.ToString("o");
            Save();
        }

        public void Forget(string environmentId)
        {
            if (!string.IsNullOrEmpty(environmentId) && profiles.Remove(environmentId))
            {
                Save();
            }
        }

        public void Save()
        {
            try
            {
                var list = new EnvironmentProfileList();
                foreach (EnvironmentProfile profile in profiles.Values)
                {
                    list.Profiles.Add(profile);
                }

                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, JsonUtility.ToJson(list, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EnvironmentStore] could not save '{filePath}': {e.Message}");
            }
        }

        private void Load()
        {
            profiles.Clear();

            try
            {
                if (!File.Exists(filePath))
                {
                    return;
                }

                var list = JsonUtility.FromJson<EnvironmentProfileList>(File.ReadAllText(filePath));
                if (list?.Profiles == null)
                {
                    return;
                }

                foreach (EnvironmentProfile profile in list.Profiles)
                {
                    if (profile != null && !string.IsNullOrEmpty(profile.EnvironmentId))
                    {
                        profiles[profile.EnvironmentId] = profile;
                    }
                }
            }
            catch (Exception e)
            {
                // A corrupt file must not stop the app aligning; start fresh.
                Debug.LogWarning($"[EnvironmentStore] could not read '{filePath}': {e.Message}");
                profiles.Clear();
            }
        }
    }
}
