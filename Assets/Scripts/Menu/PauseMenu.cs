using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

/// <summary>
/// Gestiona el menú de pausa y sus funcionalidades asociadas.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject pauseMenuUI;
    
    [Header("AR Elements")]
    [SerializeField] private GameObject arObject;
    [SerializeField] private Behaviour[] scriptsToPause;
    [SerializeField] private Animator[] animatorsToPause;

    [Header("Settings")]
    [SerializeField] private bool enableTapToShow = false;
    [Tooltip("Tiempo mínimo entre taps consecutivos")]
    [SerializeField] private float tapCooldown = 0.2f;

    private bool isPaused;
    private bool waitingForTapToShow;
    private float lastTapTime;
    private EventSystem eventSystem;

    private void Awake()
    {
        eventSystem = EventSystem.current;
    }

    private void Start()
    {
        SetPauseState(true);
    }

    private void Update()
    {
        if (!isPaused && waitingForTapToShow)
        {
            CheckForTapToShow();
        }
    }

    private void CheckForTapToShow()
    {
        if (Input.touchCount <= 0 || Input.GetTouch(0).phase != TouchPhase.Began) return;
        
        float timeSinceLastTap = Time.time - lastTapTime;
        if (timeSinceLastTap < tapCooldown) return;

        var touch = Input.GetTouch(0);
        if (eventSystem && !eventSystem.IsPointerOverGameObject(touch.fingerId))
        {
            lastTapTime = Time.time;
            SetPauseState(true);
        }
    }

    private void SetPauseState(bool paused)
    {
        isPaused = paused;
        
        if (pauseMenuUI)
        {
            pauseMenuUI.SetActive(paused);
        }

        // Pausar/reanudar scripts
        foreach (var script in scriptsToPause)
        {
            if (script)
            {
                script.enabled = !paused;
            }
        }

        // Pausar/reanudar animadores
        foreach (var animator in animatorsToPause)
        {
            if (animator)
            {
                animator.speed = paused ? 0f : 1f;
            }
        }

        waitingForTapToShow = !paused && enableTapToShow;
    }

    #region Public Menu Actions

    public void Resume() => SetPauseState(false);

    public void Pause() => SetPauseState(true);

    public void ShowInstructions() => LoadScene("Instrucciones");

    public void ShowTrailer() => LoadScene("Trailer");

    public void ShowModel() => LoadScene("Modelo");

    public void ReturnToMenu() => LoadScene("SampleScene");

    public void EnableFreeView()
    {
        SetPauseState(false);
        waitingForTapToShow = true;
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    private void LoadScene(string sceneName)
    {
        // Asegurarse de que el tiempo está normal antes de cambiar escena
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }
}