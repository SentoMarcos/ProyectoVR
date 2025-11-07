using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class pauseMenu1 : MonoBehaviour
{
    public static bool GameIsPaused = false;
    public GameObject pauseMenuUi;          // Assign your menu Canvas or panel here
    public GameObject arObject;             // The 3D AR object you want to pause/resume
    public Behaviour[] scriptsToPause;      // Any scripts controlling motion/animation
    public Animator[] animatorsToPause;     // Optional animators to pause
    private bool waitingForTapToShow = false;
    

    void Start()
    {
        ShowMenu(false);
    }

    void Update()
    {
        // Allow tapping anywhere to reopen menu when in free view
        if (!GameIsPaused && waitingForTapToShow)
        {
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                if (!EventSystem.current || !EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                    ShowMenu(true);
            }
        }
    }

    public void Resume()
    {
        ShowMenu(false);
    }

    public void Pause()
    {
        ShowMenu(true);
    }

    public void instructions()
    {
        SceneManager.LoadScene("Instrucciones");
    }

    public void trailer()
    {
        SceneManager.LoadScene("Trailer");
    }

    public void model()
    {
        SceneManager.LoadScene("Modelo");
    }

    public void ReturnToMenu()
    {
        SceneManager.LoadScene("SampleScene");
    }

    public void quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void freeView()
    {
        // Hide the menu and enable tap-to-show behavior
        ShowMenu(false);
        waitingForTapToShow = true;
    }

    // --- Helper to pause/resume everything ---
    void ShowMenu(bool show)
    {
        if (pauseMenuUi != null)
            pauseMenuUi.SetActive(show);

        GameIsPaused = show;

        // Pause/resume any scripts controlling AR object motion
        foreach (var s in scriptsToPause)
            if (s != null) s.enabled = !show;

        // Pause/resume any animators
        foreach (var a in animatorsToPause)
            if (a != null) a.speed = show ? 0f : 1f;

        waitingForTapToShow = !show;  // If menu is hidden, enable tap detection
    }
}
