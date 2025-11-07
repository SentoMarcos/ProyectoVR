using UnityEngine;

public class UIRoadControls : MonoBehaviour
{
    [Tooltip("Referencia al componente de la carretera (RoadFromTargetsSticky o el wrapper RoadFromTargets)")]
    public RoadFromTargetsSticky road;
    [Tooltip("Al pulsar Reset, esperar a nuevas detecciones antes de regenerar (recomendado para borrar la carretera visible)")]
    public bool waitForNewDetections = true;

    // Botón: Reiniciar carretera
    public void ResetRoad()
    {
        if (!road) road = FindFirstObjectByType<RoadFromTargetsSticky>();
        if (road)
        {
            road.waitForNewDetectionsAfterReset = waitForNewDetections;
            road.ResetRoad();
        }
        else Debug.LogWarning("[UIRoadControls] No se encontró RoadFromTargetsSticky en la escena");
    }
}
