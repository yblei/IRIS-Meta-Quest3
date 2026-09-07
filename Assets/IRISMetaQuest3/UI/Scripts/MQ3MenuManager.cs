using UnityEngine;
using System;
using TMPro;
using UnityEngine.Events;
using IRIS.Node;
using IRIS.MetaQuest3.Alignment;
using NUnit.Framework.Internal;
using System.Collections.Generic;
using System.Collections.Concurrent;

public class MQ3MenuManager : Singleton<MQ3MenuManager>
{
    [SerializeField] private TMP_Text debugText;
    [SerializeField] private TMP_InputField appNameInput;
    [SerializeField] private MQ3QRAlignmentManager qrAlignmentManager;
    [SerializeField] private IRISMetaQuest3Grabbable sceneGrabbable;
    // [SerializeField] OffsetConfigMenuManager offsetConfigMenuManagerPrefab;
    // [SerializeField] GameObject OffsetConfigMenuManagerParent;
    public UnityEvent onQRTrackingStarted;
    public UnityEvent onQRTrackingStopped;
    public UnityEvent onAlignmentStarted;
    public UnityEvent onAlignmentStopped;
    public UnityEvent<string> onChangeName;

    // private Dictionary<string, OffsetConfigMenuManager> offsetConfigMenuManagers = new Dictionary<string, OffsetConfigMenuManager>();
    // private ConcurrentQueue<Dictionary<string, MQ3QRAlignmentManager.SceneData>> _pendingConfigs = new ConcurrentQueue<Dictionary<string, MQ3QRAlignmentManager.SceneData>>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (debugText == null)
        {
            Debug.LogError("Debug Text is not assigned in the inspector.");
        }
        if (appNameInput == null)
        {
            Debug.LogError("App Name Input is not assigned in the inspector.");
        }
        if (qrAlignmentManager == null)
        {
            Debug.LogError("QR Alignment Manager is not assigned in the inspector.");
        }
        if (sceneGrabbable == null)
        {
            Debug.LogError("Scene Grabbable is not assigned in the inspector.");
        }
        onQRTrackingStarted.AddListener(() => qrAlignmentManager.StartQRAlignment());
        onQRTrackingStopped.AddListener(() => qrAlignmentManager.StopQRAlignment());

        // The alignment manager reports progress, rejections and the quality
        // numbers itself, so let it drive the panel rather than overwriting it
        // with a fixed "QR Tracking Started".
        if (qrAlignmentManager != null)
        {
            qrAlignmentManager.onStatusChanged.AddListener(ShowAlignmentStatus);
        }
        onAlignmentStarted.AddListener(() => debugText.text = "Alignment Started");
        onAlignmentStarted.AddListener(() => sceneGrabbable.EnableGrab());
        onAlignmentStopped.AddListener(() => debugText.text = "Alignment Stopped");
        onAlignmentStopped.AddListener(() => sceneGrabbable.DisableGrab());
        UpdateDisplayName();
    }

    public void QRTrackingToggled(bool isTracking)
    {
        if (isTracking)
        {
            onQRTrackingStarted?.Invoke();
        }
        else
        {
            onQRTrackingStopped?.Invoke();
        }
    }

    public void AlignmentToggled(bool isAligning)
    {
        if (isAligning)
        {
            onAlignmentStarted?.Invoke();
        }
        else
        {
            onAlignmentStopped?.Invoke();
        }
    }

    /// <summary>Hook for a Recalibrate button: discards the lock and solves again.</summary>
    public void RecalibrateAlignment()
    {
        if (qrAlignmentManager == null)
        {
            return;
        }

        qrAlignmentManager.Recalibrate();
    }

    /// <summary>Hook for a Details button: full quality readout for the last solve.</summary>
    public void ShowAlignmentDetail()
    {
        if (qrAlignmentManager == null || debugText == null)
        {
            return;
        }

        debugText.text = AlignmentReport.Detail(
            qrAlignmentManager.LastResult, qrAlignmentManager.SampleCount);
    }

    private void ShowAlignmentStatus(string status)
    {
        if (debugText != null)
        {
            debugText.text = status;
        }
    }

    private void UpdateDisplayName()
    {
        debugText.text = "UpdateDisplayName called. ";
        name = IRISXRNode.Instance.localInfo.nodeInfo.Name;
        if (appNameInput != null)
        {
            debugText.text = "App Name: " + name;
            appNameInput.text = name;
        }
        else
        {
            debugText.text = "App Name Input is null, " + "App Name: " + name;
        }
    }


}
