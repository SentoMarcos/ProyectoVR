using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI; // ¡Necesario para la clase Button!

public class GameOver : MonoBehaviour
{
    [Header("Botones de Menú")]
    [Tooltip("Asigna el componente Button de Reiniciar")]
    public Button restartButton;

    [Tooltip("Asigna el componente Button de Salir")]
    public Button quitButton;

    void Start()
    {
        // 1. Verificar si los botones están asignados y suscribir las funciones

        if (restartButton != null)
        {
            // Añadir un "listener" (suscriptor) que llama a RestartScene() cuando se hace clic.
            restartButton.onClick.AddListener(RestartScene);
        }
        else
        {
            Debug.LogError("El botón de Reiniciar no está asignado en el Inspector de GameOver.cs");
        }

        if (quitButton != null)
        {
            // Añadir un "listener" que llama a QuitGame() cuando se hace clic.
            quitButton.onClick.AddListener(QuitGame);
        }
        else
        {
            Debug.LogError("El botón de Salir no está asignado en el Inspector de GameOver.cs");
        }
    }

    // Este método se llama cuando se hace clic en restartButton
    public void RestartScene()
    {
        // 1. Asegurarse de que el tiempo se reanude
        Time.timeScale = 1f;

        // 2. Cargar la escena actual (reiniciarla)
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Este método se llama cuando se hace clic en quitButton
    public void QuitGame()
    {
        Debug.Log("Saliendo del juego...");

        // 1. Asegurarse de que el tiempo se reanude (buena práctica)
        Time.timeScale = 1f;

        // 2. Salir de la aplicación
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }

    void OnDestroy()
    {
        // ¡Buena práctica! Eliminar los listeners para evitar errores cuando el objeto se destruye
        if (restartButton != null)
            restartButton.onClick.RemoveListener(RestartScene);

        if (quitButton != null)
            quitButton.onClick.RemoveListener(QuitGame);
    }
}