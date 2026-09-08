/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Meta.XR.MRUtilityKit;
using Meta.XR.Samples;

using System;
using System.Collections.Generic;

using UnityEngine;
using IRIS.Node;

namespace IRIS.MetaQuest3.QRCodeDetection
{
    [MetaCodeSample("MRUKSample-QRCodeDetection")]
    public class QRCodeManager : Singleton<QRCodeManager>
    {
        //
        // Static interface

        public const string ScenePermission = OVRPermissionsRequester.ScenePermission;

        public static bool IsSupported
            => OVRAnchor.TrackerConfiguration.QRCodeTrackingSupported;

        private Dictionary<string, MRUKTrackable> _trackedQRCodes = new Dictionary<string, MRUKTrackable>();
        private readonly List<MRUKTrackable> _mrukTrackables = new List<MRUKTrackable>();


        [SerializeField]
        QRCode _qrCodePrefab;

        [SerializeField, Tooltip("Draw the outline, label and axis gizmo on each detected marker. " +
                                 "Debug aid only - alignment does not need it.")]
        bool _showMarkerVisuals = false;

        // [SerializeField]
        // QRCodeSampleUI _uiInstance;

        [SerializeField]
        MRUK _mrukInstance;

        // non-serialized fields


        static QRCodeManager s_instance;

        void Start()
        {
        }

        void OnEnable()
        {
            s_instance = this;

            if (!_mrukInstance)
            {
                Debug.LogError($"{nameof(QRCodeManager)} requires an MRUK object in the scene!");
                return;
            }

            _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        }

        void Update()
        {
        }


        void OnDestroy()
        {
            if (_mrukInstance)
            {
                _mrukInstance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
                _mrukInstance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
            }

            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        public void OnTrackableAdded(MRUKTrackable trackable)
        {
            Debug.Log($" {nameof(OnTrackableAdded)} called.");

            if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            {
                return;
            }

            if (trackable.MarkerPayloadString == null)
            {
                return;
            }

            if (_trackedQRCodes.TryGetValue(trackable.MarkerPayloadString, out MRUKTrackable existing) &&
                existing == trackable)
            {
                return;
            }

            _trackedQRCodes[trackable.MarkerPayloadString] = trackable;
            Debug.Log($"{nameof(OnTrackableAdded)}: QRCode tracked! Text: {trackable.MarkerPayloadString}");

            // The marker overlay (outline, payload label and RGB axis gizmo) is
            // debug decoration from Meta's sample. Alignment reads marker poses
            // straight from MRUK and does not need it, so it stays off unless
            // someone is diagnosing detection.
            if (!_showMarkerVisuals || _qrCodePrefab == null)
            {
                return;
            }

            QRCode qrCode = Instantiate(_qrCodePrefab, trackable.transform);
            qrCode.Initialize(trackable);

            if (qrCode.TryGetComponent(out Bounded2DVisualizer visualizer))
            {
                visualizer.Initialize(trackable);
            }
        }

        public void OnTrackableRemoved(MRUKTrackable trackable)
        {
            if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            {
                return;
            }
            _trackedQRCodes.Remove(trackable.MarkerPayloadString);

            Debug.Log($"{nameof(OnTrackableRemoved)}: {trackable.Anchor.Uuid.ToString("N").Remove(8)}[..]");


            Destroy(trackable.gameObject);
        }

        public bool TrackingEnabled
        {
            get => s_instance && s_instance._mrukInstance && s_instance._mrukInstance.SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled;
            set
            {
                if (!s_instance || !s_instance._mrukInstance)
                {
                    return;
                }
                var config = s_instance._mrukInstance.SceneSettings.TrackerConfiguration;
                config.QRCodeTrackingEnabled = value;
                s_instance._mrukInstance.SceneSettings.TrackerConfiguration = config;
            }
        }

        /// <summary>
        /// Every QR code currently tracked, keyed by payload string. Consumed
        /// by the alignment manager, which picks out the markers named in its
        /// layout and ignores anything else in the room.
        /// </summary>
        public Dictionary<string, MRUKTrackable> GetTrackedQRCodes()
        {
            _trackedQRCodes.Clear();
            if (!_mrukInstance)
            {
                return _trackedQRCodes;
            }

            _mrukInstance.GetTrackables(_mrukTrackables);
            foreach (MRUKTrackable trackable in _mrukTrackables)
            {
                if (trackable && trackable.TrackableType == OVRAnchor.TrackableType.QRCode &&
                    !string.IsNullOrEmpty(trackable.MarkerPayloadString))
                {
                    _trackedQRCodes[trackable.MarkerPayloadString] = trackable;
                }
            }

            return _trackedQRCodes;
        }

        public static bool HasPermissions
#if UNITY_EDITOR
            => true;
#else
            => UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission);
#endif

    }
}
