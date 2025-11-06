using UnityEngine;
using UnityEngine.SceneManagement;

public class MotoCollision : MonoBehaviour
{
    [Header("Referencias")]
    public MotoController controller;     // Referencia al script de movimiento
    public GameObject crashEffectPrefab;  // Prefab de explosión o chispas (asígnalo en el inspector)
    public AudioClip crashSound;          // Sonido del choque (opcional)

    private bool hasCrashed = false;      // Evita múltiples colisiones

    void OnCollisionEnter(Collision collision)
    {
        if (hasCrashed) return;

        if (collision.gameObject.CompareTag("Van"))
        {
            hasCrashed = true;
            Debug.Log("💥 ¡Has chocado! Game Over");

            // Desactivar control de la moto
            if (controller != null)
                controller.enabled = false;

            // Detener movimiento
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = Vector3.zero;

            // Reproducir efecto visual
            if (crashEffectPrefab != null)
            {
                GameObject effect = Instantiate(crashEffectPrefab, transform.position, Quaternion.identity);
                Destroy(effect, 3f); // destruir el efecto tras 3 segundos
            }

            // Reproducir sonido de choque
            if (crashSound != null)
                AudioSource.PlayClipAtPoint(crashSound, transform.position);

            // Desactivar visualmente la moto (opcional)
            GetComponent<MeshRenderer>().enabled = false;

            // Reiniciar tras 2 segundos
            Invoke(nameof(GameOver), 2f);
        }
    }

    void GameOver()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        // O si prefieres mostrar UI:
        // UIManager.Instance.ShowGameOver();
    }
}
