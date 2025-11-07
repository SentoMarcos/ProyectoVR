using System.Collections.Generic;
using UnityEngine;
using Vuforia;

public class TargetMarker : MonoBehaviour
{
    public static List<Transform> activeTargets = new List<Transform>();

    private ObserverBehaviour observer;

    void Start()
    {
        observer = GetComponent<ObserverBehaviour>();
        if (observer != null)
            observer.OnTargetStatusChanged += OnTargetStatusChanged;
    }

    private void OnDestroy()
    {
        if (observer != null)
            observer.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        if (status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED)
        {
            if (!activeTargets.Contains(transform))
            {
                activeTargets.Add(transform);
                Debug.Log($"? Target detectado: {name}");
            }
        }
        else
        {
            if (activeTargets.Contains(transform))
            {
                activeTargets.Remove(transform);
                Debug.Log($"? Target perdido: {name}");
            }
        }
    }
}
