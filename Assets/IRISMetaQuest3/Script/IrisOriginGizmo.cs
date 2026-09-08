using UnityEngine;

namespace IRIS.MetaQuest3
{
    /// <summary>
    /// Hides the IRIS node's origin gizmo - the red/green/blue cones from
    /// IRIS-Viz's Axis prefab, nested inside IRISNode. Once the scene is
    /// aligned to the markers that origin sits on the work surface, so the red
    /// X cone ends up floating in the operator's view.
    ///
    /// Self-installing so it needs no scene wiring. The tidier permanent fix is
    /// to untick the Axis child of IRISNode in the scene, after which this file
    /// can be deleted.
    /// </summary>
    public static class IrisOriginGizmo
    {
        private const string AxisRoot = "Axis";
        private const string AxisChildX = "AxisPointerX";

        /// <summary>Set false before scene load to keep the gizmo visible.</summary>
        public static bool HideOnStartup = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!HideOnStartup)
            {
                return;
            }

            int hidden = 0;

            foreach (Transform candidate in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // Match the axis root that actually owns the coloured cones, so
                // an unrelated object called "Axis" is left alone.
                if (candidate.name != AxisRoot || candidate.Find(AxisChildX) == null)
                {
                    continue;
                }

                if (candidate.gameObject.activeSelf)
                {
                    candidate.gameObject.SetActive(false);
                    hidden++;
                }
            }

            Debug.Log($"[{nameof(IrisOriginGizmo)}] hid {hidden} origin axis gizmo(s).");
        }
    }
}
