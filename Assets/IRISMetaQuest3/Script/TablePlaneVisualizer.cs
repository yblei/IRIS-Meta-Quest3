using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace IRIS.MetaQuest3
{
    /// <summary>
    /// Draws a wireframe outline around every TABLE surface in the scene model.
    ///
    /// The alignment now takes the scene origin's height from that surface, so
    /// being able to see where it actually is matters: a table anchor left over
    /// from an old room scan is confidently wrong in a way no number reveals.
    /// Shown while QR alignment runs and removed when it stops.
    /// </summary>
    public class TablePlaneVisualizer : MonoBehaviour
    {
        private const string MaterialResource = "IRISTablePlane";
        private const float LineWidth = 0.006f;

        private readonly List<LineRenderer> _outlines = new List<LineRenderer>();
        private Material _material;
        private bool _visible;

        /// <summary>How many table outlines are currently drawn.</summary>
        public int OutlineCount => _outlines.Count;

        public void Show()
        {
            _visible = true;
            Rebuild();
        }

        public void Hide()
        {
            _visible = false;
            Clear();
        }

        private void Clear()
        {
            foreach (LineRenderer outline in _outlines)
            {
                if (outline != null)
                {
                    Destroy(outline.gameObject);
                }
            }

            _outlines.Clear();
        }

        /// <summary>
        /// Rebuilds the outlines from the current room. Cheap enough to call
        /// whenever the room model may have changed.
        /// </summary>
        public void Rebuild()
        {
            Clear();

            if (!_visible)
            {
                return;
            }

            MRUK mruk = MRUK.Instance;
            MRUKRoom room = mruk != null ? mruk.GetCurrentRoom() : null;
            if (room == null)
            {
                Debug.LogWarning($"[{nameof(TablePlaneVisualizer)}] no room loaded; nothing to draw.");
                return;
            }

            if (_material == null)
            {
                _material = Resources.Load<Material>(MaterialResource);
                if (_material == null)
                {
                    Debug.LogWarning(
                        $"[{nameof(TablePlaneVisualizer)}] material '{MaterialResource}' missing from Resources.");
                    return;
                }
            }

            foreach (MRUKAnchor anchor in room.Anchors)
            {
                if (anchor == null ||
                    !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) ||
                    !anchor.PlaneRect.HasValue)
                {
                    continue;
                }

                AddOutline(anchor, anchor.PlaneRect.Value);
            }

            Debug.Log($"[{nameof(TablePlaneVisualizer)}] drew {_outlines.Count} table outline(s).");
        }

        private void AddOutline(MRUKAnchor anchor, Rect rect)
        {
            var host = new GameObject($"TableOutline_{anchor.name}");
            host.transform.SetParent(anchor.transform, false);

            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            line.widthMultiplier = LineWidth;
            line.material = _material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            // MRUK plane anchors lie in their local XY with the normal along
            // local Z, so the rect corners map straight onto local space.
            line.SetPositions(new[]
            {
                new Vector3(rect.xMin, rect.yMin, 0f),
                new Vector3(rect.xMax, rect.yMin, 0f),
                new Vector3(rect.xMax, rect.yMax, 0f),
                new Vector3(rect.xMin, rect.yMax, 0f),
            });

            _outlines.Add(line);
        }
    }
}
