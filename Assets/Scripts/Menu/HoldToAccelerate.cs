using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Mantiene la aceleración mientras el botón UI está presionado.
/// Úsalo en el botón de acelerar: asigna el RoadLaneFollower o el PlayerController.
/// </summary>
public class HoldToAccelerate : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IDisableHandler
{
    [Tooltip("Si se asigna, se usará directamente el RoadLaneFollower")] 
    public RoadLaneFollower follower;
    [Tooltip("Alternativa: si se asigna, se invocará al PlayerController para acelerar")] 
    public PlayerController playerController;

    bool isDown;

    void Awake()
    {
        if (!follower && !playerController)
        {
            // Intenta encontrar en padres por comodidad
            follower = GetComponentInParent<RoadLaneFollower>();
            if (!follower)
                playerController = GetComponentInParent<PlayerController>();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isDown = true;
        if (follower) follower.AccelerateOn();
        else if (playerController) playerController.AccelerateOn();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isDown = false;
        if (follower) follower.AccelerateOff();
        else if (playerController) playerController.AccelerateOff();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isDown) return;
        isDown = false;
        if (follower) follower.AccelerateOff();
        else if (playerController) playerController.AccelerateOff();
    }

    public void OnDisable()
    {
        // Seguridad: si el botón se desactiva mientras estaba pulsado, corta aceleración
        if (isDown)
        {
            isDown = false;
            if (follower) follower.AccelerateOff();
            else if (playerController) playerController.AccelerateOff();
        }
    }
}

// Interfaz pequeña para que OnDisable sea capturada por el sistema de eventos GUI
public interface IDisableHandler { }