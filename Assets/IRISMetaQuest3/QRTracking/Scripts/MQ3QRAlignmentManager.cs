using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using Meta.XR.MRUtilityKit;
using IRIS.Node;
using IRIS.MetaQuest3.Alignment;
using IRIS.MetaQuest3.QRCodeDetection;

/// <summary>
/// Aligns the IRIS scene to a sheet of QR markers lying on the work surface.
///
/// Several environments are told apart by marker payload alone: markers read
/// "ENVIRONMENT_INDEX" (VENTION_1, VENTION_2, ...), so walking into a different
/// cell picks up that cell's frame and its remembered offset with no
/// configuration. The origin sits halfway between the two axis markers, which
/// with those markers on the ends of a table edge places the robot at the
/// middle of that edge.
///
/// While QR tracking is enabled, fresh sample windows continuously refine the
/// scene pose. Turning tracking off freezes the last accepted pose.
/// </summary>
public class MQ3QRAlignmentManager : Singleton<MQ3QRAlignmentManager>
{
    public enum AlignmentState
    {
        /// Not calibrating. Any previous alignment stays where it was.
        Idle,

        /// Gathering marker sightings.
        Collecting,

        /// Solved and frozen because QR tracking was stopped.
        Locked,
    }

    /// <summary>
    /// Unity cannot serialize an open generic UnityEvent, so the string-carrying
    /// event needs a concrete subclass to be inspector-wirable and non-null.
    /// </summary>
    [System.Serializable]
    public class AlignmentStatusEvent : UnityEvent<string> { }

    [Header("Wiring")]
    [SerializeField] private QRCodeManager qrCodeManager;

    [Tooltip("Transform moved to align the scene. Falls back to this object's own transform.")]
    [SerializeField] private Transform sceneRoot;

    [Header("Marker sheet")]
    [Tooltip("Marker index that starts the axis. The robot sits between this and the next.")]
    [SerializeField] private int firstAxisIndex = 1;

    [Tooltip("Marker index that ends the axis. +X runs from the first towards this one.")]
    [SerializeField] private int secondAxisIndex = 2;

    [Tooltip("Where the origin sits relative to the two axis markers.")]
    [SerializeField] private FrameOrigin originMode = FrameOrigin.MidpointOfAxisMarkers;

    [SerializeField] private AlignmentTolerances tolerances = AlignmentTolerances.Default;

    [Header("Calibration")]
    [Tooltip("Length of one collection window before the solver is run.")]
    [SerializeField] private float collectionSeconds = 4f;

    [Tooltip("Length of each sample window after the first successful alignment.")]
    [SerializeField] private float refinementSeconds = 0.5f;

    [Range(0.01f, 1f)]
    [Tooltip("Fraction of each refined pose applied per window. Lower values reduce jitter.")]
    [SerializeField] private float refinementBlend = 0.25f;

    [Tooltip("Give up if the markers still have not been seen after this long.")]
    [SerializeField] private float timeoutSeconds = 60f;

    [Tooltip("Sightings a marker needs before its position is trusted.")]
    [SerializeField] private int minSamplesPerMarker = 3;

    [Tooltip("Sightings further than this many RMS deviations from a marker's mean are discarded.")]
    [SerializeField] private float outlierSigma = 2.5f;

    [Tooltip("How often the live progress readout refreshes while calibrating.")]
    [SerializeField] private float progressRefreshSeconds = 0.5f;

    public AlignmentStatusEvent onStatusChanged = new AlignmentStatusEvent();
    public UnityEvent onAligned = new UnityEvent();

    public AlignmentState State { get; private set; } = AlignmentState.Idle;

    public AlignmentResult LastResult { get; private set; }

    /// <summary>Environment the last accepted alignment belonged to.</summary>
    public string CurrentEnvironmentId { get; private set; }

    public string StatusLine { get; private set; } = "Idle";

    /// <summary>Marker sightings gathered for the current or most recent solve.</summary>
    public int SampleCount => accumulator.TotalSamples;

    /// <summary>Remembered nudge for the current environment, in the aligned frame.</summary>
    public Vector3 CurrentOffset { get; private set; }

    private readonly MarkerSampleAccumulator accumulator = new MarkerSampleAccumulator();

    // Diagnostics: what MRUK is holding, so a failure says where it stopped.
    private int lastTrackableTotal;
    private int lastQrCodeCount;
    private int lastPayloadCount;

    /// Geometry learned per environment from its first accepted alignment.
    private readonly Dictionary<string, MarkerGeometry> referenceGeometry =
        new Dictionary<string, MarkerGeometry>();

    private EnvironmentStore store;
    private Pose solvedPose;
    private bool hasSolvedPose;
    private float windowEndsAt;
    private float collectionStartedAt;
    private float nextProgressAt;
    private bool hasAlignedThisSession;

    private Transform Target => sceneRoot != null ? sceneRoot : transform;

    private EnvironmentStore Store =>
        store ??= new EnvironmentStore(
            Path.Combine(Application.persistentDataPath, "iris-environments.json"));

    private void Update()
    {
        if (State != AlignmentState.Collecting)
        {
            return;
        }

        CollectSightings();
        ReportProgress();

        if (Time.time >= windowEndsAt)
        {
            AttemptSolve();
        }
    }

    // ---- public API, kept stable for the existing menu wiring --------------

    public void StartQRAlignment()
    {
        if (qrCodeManager == null)
        {
            SetStatus("No QR code manager assigned.");
            return;
        }

        if (!qrCodeManager.TrackingEnabled)
        {
            qrCodeManager.TrackingEnabled = true;
        }

        BeginCollecting();
    }

    public void StopQRAlignment()
    {
        if (qrCodeManager != null && qrCodeManager.TrackingEnabled)
        {
            qrCodeManager.TrackingEnabled = false;
        }

        // Keep a completed alignment's readout rather than wiping what the
        // operator just earned.
        string closing = hasSolvedPose
            ? $"Tracking off. {AlignmentReport.Summary(LastResult)}"
            : "QR alignment stopped.";

        State = hasSolvedPose ? AlignmentState.Locked : AlignmentState.Idle;
        accumulator.Clear();
        SetStatus(closing);
    }

    public void Recalibrate()
    {
        ResetDetections();
    }

    /// <summary>Discards every marker sample and camera ray, then starts a fresh scan.</summary>
    public void ResetDetections()
    {
        if (qrCodeManager == null)
        {
            SetStatus("No QR code manager assigned.");
            return;
        }

        if (!qrCodeManager.TrackingEnabled)
        {
            qrCodeManager.TrackingEnabled = true;
        }

        BeginCollecting();
    }

    /// <summary>
    /// Discards the learned geometry for the current environment, for when its
    /// markers have genuinely been rearranged rather than knocked.
    /// </summary>
    public void ForgetMarkerGeometry()
    {
        if (!string.IsNullOrEmpty(CurrentEnvironmentId))
        {
            referenceGeometry.Remove(CurrentEnvironmentId);
        }

        SetStatus("Marker geometry forgotten; next alignment will relearn it.");
    }

    public void ToggleQRTracking()
    {
        if (qrCodeManager == null)
        {
            return;
        }

        if (qrCodeManager.TrackingEnabled)
        {
            StopQRAlignment();
        }
        else
        {
            StartQRAlignment();
        }
    }

    // ---- remembered offsets -----------------------------------------------

    /// <summary>
    /// Stores wherever the operator has dragged the scene as this
    /// environment's offset, so the same site never has to be nudged twice.
    /// </summary>
    public void SaveCurrentOffset()
    {
        if (!hasSolvedPose || string.IsNullOrEmpty(CurrentEnvironmentId))
        {
            SetStatus("Nothing to save: align to a marker sheet first.");
            return;
        }

        Vector3 offset = Quaternion.Inverse(solvedPose.rotation) *
                         (Target.position - solvedPose.position);

        CurrentOffset = offset;
        Store.SetOffset(CurrentEnvironmentId, offset);
        SetStatus($"Saved offset for {CurrentEnvironmentId}: " +
                  $"({offset.x:F3}, {offset.y:F3}, {offset.z:F3}) m.");
    }

    /// <summary>Nudges the scene along the aligned frame's axes and remembers it.</summary>
    public void NudgeOffset(Vector3 delta)
    {
        if (!hasSolvedPose || string.IsNullOrEmpty(CurrentEnvironmentId))
        {
            return;
        }

        CurrentOffset += delta;
        Store.SetOffset(CurrentEnvironmentId, CurrentOffset);
        ApplyPose();
        SetStatus($"{CurrentEnvironmentId} offset: " +
                  $"({CurrentOffset.x:F3}, {CurrentOffset.y:F3}, {CurrentOffset.z:F3}) m.");
    }

    public void ClearOffset()
    {
        if (string.IsNullOrEmpty(CurrentEnvironmentId))
        {
            return;
        }

        CurrentOffset = Vector3.zero;
        Store.Forget(CurrentEnvironmentId);

        if (hasSolvedPose)
        {
            ApplyPose();
        }

        SetStatus($"Cleared saved offset for {CurrentEnvironmentId}.");
    }

    // ---- calibration ------------------------------------------------------

    private void BeginCollecting()
    {
        accumulator.Clear();
        collectionStartedAt = Time.time;
        windowEndsAt = Time.time + collectionSeconds;
        nextProgressAt = 0f;
        hasAlignedThisSession = false;
        State = AlignmentState.Collecting;
        SetStatus("Calibrating: look over the markers.");
    }

    private void CollectSightings()
    {
        // QRCodeManager owns the MRUK instance configured in the scene and
        // retains each QR trackable it has localized. This lets observations
        // accumulate as the wearer looks at markers one at a time.
        if (qrCodeManager == null)
        {
            return;
        }

        Dictionary<string, MRUKTrackable> trackedCodes = qrCodeManager.GetTrackedQRCodes();

        int qrCodes = 0;
        int withPayload = 0;

        foreach (KeyValuePair<string, MRUKTrackable> entry in trackedCodes)
        {
            MRUKTrackable trackable = entry.Value;
            if (trackable == null || trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            {
                continue;
            }

            qrCodes++;

            string payload = trackable.MarkerPayloadString;
            if (string.IsNullOrEmpty(payload))
            {
                continue;
            }

            withPayload++;

            // Anything shaped like ENVIRONMENT_INDEX is a candidate; which
            // environment wins is decided at solve time by marker count.
            if (MarkerPayloadParser.TryParse(payload, out _, out _))
            {
                accumulator.Add(payload, trackable.transform.position);
            }
        }

        lastTrackableTotal = trackedCodes.Count;
        lastQrCodeCount = qrCodes;
        lastPayloadCount = withPayload;
    }

    private void ReportProgress()
    {
        if (Time.time < nextProgressAt)
        {
            return;
        }

        nextProgressAt = Time.time + Mathf.Max(0.1f, progressRefreshSeconds);

        var progress = new List<MarkerProgress>();
        foreach (string id in accumulator.SeenMarkerIds)
        {
            progress.Add(new MarkerProgress(id, accumulator.SamplesFor(id)));
        }

        StatusLine = progress.Count == 0
            ? $"Calibrating {Time.time - collectionStartedAt:F0}s - Found 0 markers (look directly at markers)"
            : AlignmentReport.Progress(progress, Time.time - collectionStartedAt, minSamplesPerMarker);

        Debug.Log($"[{nameof(MQ3QRAlignmentManager)}] {StatusLine}");
        onStatusChanged?.Invoke(StatusLine);
    }

    private void AttemptSolve()
    {
        List<MarkerObservation> observations =
            accumulator.Reduce(minSamplesPerMarker, outlierSigma);

        if (!TryPickEnvironment(observations, out string environmentId,
                out List<MarkerObservation> forEnvironment))
        {
            KeepWaitingOrGiveUp($"Seen {observations.Count} markers, need "
                                + $"{MarkerAlignmentSolver.MinimumMarkers} from one environment.");
            return;
        }

        var ids = new List<string>(forEnvironment.Count);
        foreach (MarkerObservation observation in forEnvironment)
        {
            ids.Add(observation.Id);
        }

        MarkerSet set = MarkerSet.FromPayloads(
            environmentId, ids, firstAxisIndex, secondAxisIndex, originMode);

        referenceGeometry.TryGetValue(environmentId, out MarkerGeometry reference);

        AlignmentResult result =
            MarkerAlignmentSolver.Solve(forEnvironment, set, tolerances, reference);
        LastResult = result;

        if (result.Succeeded)
        {
            bool isRefinement = hasAlignedThisSession &&
                                CurrentEnvironmentId == environmentId;

            CurrentEnvironmentId = environmentId;
            solvedPose = isRefinement
                ? new Pose(
                    Vector3.Lerp(solvedPose.position, result.Pose.position, refinementBlend),
                    Quaternion.Slerp(solvedPose.rotation, result.Pose.rotation, refinementBlend))
                : result.Pose;
            hasSolvedPose = true;

            if (!isRefinement)
            {
                CurrentOffset = Store.TryGetOffset(environmentId, out Vector3 saved)
                    ? saved
                    : Vector3.zero;
            }

            ApplyPose();

            if (!referenceGeometry.ContainsKey(environmentId))
            {
                referenceGeometry[environmentId] = result.ObservedGeometry;
            }

            hasAlignedThisSession = true;
            accumulator.Clear();
            collectionStartedAt = Time.time;
            windowEndsAt = Time.time + Mathf.Max(0.1f, refinementSeconds);
            State = AlignmentState.Collecting;

            string offsetNote = CurrentOffset == Vector3.zero
                ? string.Empty
                : $" Offset ({CurrentOffset.x:F2}, {CurrentOffset.y:F2}, {CurrentOffset.z:F2}) m restored.";

            SetStatus($"Refining {environmentId}: {AlignmentReport.Summary(result)}{offsetNote}");
            Debug.Log($"[{nameof(MQ3QRAlignmentManager)}] {environmentId}\n" +
                      AlignmentReport.Detail(result, SampleCount));
            onAligned?.Invoke();
            return;
        }

        bool stillWaiting =
            result.Status == AlignmentStatus.TooFewMarkers ||
            result.Status == AlignmentStatus.MissingRequiredMarker;

        if (stillWaiting)
        {
            KeepWaitingOrGiveUp(result.Message);
            return;
        }

        accumulator.Clear();
        windowEndsAt = Time.time + collectionSeconds;
        SetStatus($"Bad detections discarded. {result.Message} Rescanning.");
    }

    private void KeepWaitingOrGiveUp(string message)
    {
        if (hasAlignedThisSession)
        {
            windowEndsAt = Time.time + Mathf.Max(0.1f, refinementSeconds);
            return;
        }

        if (Time.time - collectionStartedAt < timeoutSeconds)
        {
            windowEndsAt = Time.time + collectionSeconds;
            return;
        }

        State = AlignmentState.Idle;
        SetStatus($"Gave up after {timeoutSeconds:F0}s. {message} " +
                  $"MRUK held {lastTrackableTotal} trackables, {lastQrCodeCount} QR codes, " +
                  $"{lastPayloadCount} with a readable payload.");
    }

    /// <summary>
    /// Picks the environment with the most markers currently resolved, so
    /// walking between cells selects the right sheet without configuration.
    /// </summary>
    private static bool TryPickEnvironment(
        List<MarkerObservation> observations,
        out string environmentId,
        out List<MarkerObservation> forEnvironment)
    {
        var grouped = new Dictionary<string, List<MarkerObservation>>();

        foreach (MarkerObservation observation in observations)
        {
            if (!MarkerPayloadParser.TryParse(observation.Id, out string id, out _))
            {
                continue;
            }

            if (!grouped.TryGetValue(id, out List<MarkerObservation> list))
            {
                list = new List<MarkerObservation>();
                grouped.Add(id, list);
            }

            list.Add(observation);
        }

        environmentId = null;
        forEnvironment = null;

        foreach (KeyValuePair<string, List<MarkerObservation>> entry in grouped)
        {
            if (entry.Value.Count >= MarkerAlignmentSolver.MinimumMarkers &&
                (forEnvironment == null || entry.Value.Count > forEnvironment.Count))
            {
                environmentId = entry.Key;
                forEnvironment = entry.Value;
            }
        }

        return forEnvironment != null;
    }

    private void ApplyPose()
    {
        Target.SetPositionAndRotation(
            solvedPose.position + (solvedPose.rotation * CurrentOffset),
            solvedPose.rotation);
    }

    private void SetStatus(string message)
    {
        StatusLine = message;
        Debug.Log($"[{nameof(MQ3QRAlignmentManager)}] {message}");
        onStatusChanged?.Invoke(message);
    }
}
