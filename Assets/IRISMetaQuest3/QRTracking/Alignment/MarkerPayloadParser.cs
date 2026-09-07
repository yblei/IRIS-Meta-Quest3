using System;

namespace IRIS.MetaQuest3.Alignment
{
    /// <summary>
    /// Splits a marker payload into the environment it belongs to and its index
    /// within that environment: "VENTION_2" is marker 2 of environment
    /// "VENTION". Several environments can therefore be told apart purely by
    /// what is printed on their markers, with no per-site configuration.
    /// </summary>
    public static class MarkerPayloadParser
    {
        /// <summary>
        /// Splits on the LAST underscore, so environment names may contain
        /// underscores of their own ("CELL_A_3" is marker 3 of "CELL_A").
        /// </summary>
        public static bool TryParse(string payload, out string environmentId, out int index)
        {
            environmentId = null;
            index = 0;

            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            int split = payload.LastIndexOf('_');
            if (split <= 0 || split == payload.Length - 1)
            {
                return false;
            }

            string tail = payload.Substring(split + 1);
            if (!int.TryParse(tail, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out index))
            {
                return false;
            }

            environmentId = payload.Substring(0, split);
            return true;
        }

        /// <summary>Rebuilds a payload from its parts.</summary>
        public static string Compose(string environmentId, int index)
        {
            return $"{environmentId}_{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }
}
