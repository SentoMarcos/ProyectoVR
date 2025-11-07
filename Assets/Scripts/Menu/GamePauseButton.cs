using UnityEngine;

// Asigna este script a cualquier GameObject (puede ser el propio Button).
// En el Button (Inspector) -> OnClick, arrastra el GameObject con este componente
// y selecciona el método que quieras: Pause(), Resume() o TogglePause().
public class GamePauseButton : MonoBehaviour
{
    [Tooltip("Si está activo, el juego empieza en pausa.")]
    public bool startPaused = false;
    [Tooltip("También modificar Time.timeScale al pausar (recomendado para pausar TODO: físicas, animaciones, coroutines con WaitForSeconds)")]
    public bool affectTimeScale = true;
    [Tooltip("Pausar AudioListener al pausar")]
    public bool affectAudio = true;
    [Header("Debug")]
    [Tooltip("Imprimir logs de cada transición de pausa")] public bool logTransitions = true;
    [Tooltip("Texto UI opcional para mostrar estado (asigna un TextMeshProUGUI o Text)")] public UnityEngine.Object statusText;
    [Tooltip("Prefijo para el texto de estado")] public string statusPrefix = "Estado";
    [Tooltip("Refrescar texto cada frame aunque no cambie (útil para comprobar deltaTime)")] public bool refreshTextContinuously = false;

    float _fixedDeltaDefault;

    void Awake()
    {
        _fixedDeltaDefault = Time.fixedDeltaTime;
        if (startPaused) ApplyPause(true);
    }

    void Update()
    {
        if (refreshTextContinuously) UpdateStatusText();
    }

    // Pausar el juego (para usar desde OnClick)
    public void Pause()
    {
        ApplyPause(true);
    }

    // Reanudar el juego (para usar desde OnClick)
    public void Resume()
    {
        ApplyPause(false);
    }

    // Alternar pausa (para usar desde OnClick con un único botón)
    public void TogglePause()
    {
        ApplyPause(!GameTime.Paused);
    }

    void ApplyPause(bool paused)
    {
        GameTime.SetPaused(paused);
        if (affectTimeScale)
        {
            Time.timeScale = paused ? 0f : 1f;
            // Mantener la relación de fixedDeltaTime con timeScale para físicas consistentes
            Time.fixedDeltaTime = _fixedDeltaDefault * (paused ? 0f : 1f);
        }
#if !UNITY_WEBGL
        if (affectAudio) AudioListener.pause = paused;
#endif
        if (logTransitions)
        {
            Debug.Log($"[GamePauseButton] ApplyPause => paused={paused} timeScale={Time.timeScale} fixedDelta={Time.fixedDeltaTime} deltaTime(ThisFrame)={Time.deltaTime:F4} unscaledDelta={Time.unscaledDeltaTime:F4}");
        }
        UpdateStatusText();
    }

    void UpdateStatusText()
    {
        if (!statusText) return;
        string msg = $"{statusPrefix}: {(GameTime.Paused ? "PAUSADO" : "CORRIENDO")} | timeScale={Time.timeScale:F2}";
        if (statusText is UnityEngine.UI.Text uiText)
        {
            uiText.text = msg;
        }
#if TMP_PRESENT
        else if (statusText is TMPro.TextMeshProUGUI tmpText)
        {
            tmpText.text = msg;
        }
#endif
    }
}
