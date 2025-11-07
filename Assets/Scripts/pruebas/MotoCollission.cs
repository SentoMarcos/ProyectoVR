using UnityEngine;
using UnityEngine.EventSystems;

public class MotoCollision : MonoBehaviour
{
    [Header("Referencias")]
    public GameObject crashEffect;
    public AudioClip crashSound;
    public GameObject gameOverMenuUI;  // Canvas del menú de Game Over
    public Canvas mainUICanvas;        // Canvas principal (HUD, botones, etc.)

    private bool hasCrashed = false;

    void OnCollisionEnter(Collision collision)
    {
        if (hasCrashed) return;

        if (collision.gameObject.CompareTag("NPC"))
        {
            hasCrashed = true;
            Debug.Log("💥 ¡Has chocado! Game Over");

            // Detener movimiento
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = Vector3.zero;

            // Activar efecto visual
            if (crashEffect != null)
                crashEffect.SetActive(true);

            // Reproducir sonido
            if (crashSound != null)
                AudioSource.PlayClipAtPoint(crashSound, transform.position);

            // Ocultar la malla de la moto
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                meshRenderer.enabled = false;

            GameOver();
        }
    }

    void GameOver()
    {
        // Pausar el juego
        Time.timeScale = 0f;

        // Desactivar la UI principal (para que no sea interactiva)
        if (mainUICanvas != null)
            mainUICanvas.gameObject.SetActive(false);

        // Activar el menú de Game Over
        if (gameOverMenuUI != null)
            gameOverMenuUI.SetActive(true);
        else
            Debug.LogError("El menú de Game Over no está asignado en el inspector.");
    }
}
