using UnityEngine;
using UnityEngine.Video;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(VideoPlayer))]
public class TrailerPlayer : MonoBehaviour
{
    private VideoPlayer vp;

    void Start()
    {
        vp = GetComponent<VideoPlayer>();
        vp.loopPointReached += EndReached;
        vp.Play();
    }

    // Se llama cuando el video termina
    void EndReached(VideoPlayer vp)
    {
        RestartVideo(); // Reinicia automáticamente el trailer al terminar
    }

    // --- Funciones para los botones ---

    public void PlayVideo()
    {
        if (!vp.isPlaying)
            vp.Play();
        else
            vp.Pause();
    }

    public void Forward5Sec()
    {
        if (vp.canSetTime)
            vp.time += 5.0f;
    }

    public void Backward5Sec()
    {
        if (vp.canSetTime)
            vp.time = Mathf.Max(0f, (float)vp.time - 5f);
    }

    public void RestartVideo()
    {
        if (vp.canSetTime)
        {
            vp.time = 0f;
            vp.Play();
        }
    }

    // ✅ Botón que vuelve al menú (cambia el nombre por el de tu escena)
    public void GoToMenu()
    {
        SceneManager.LoadScene("SampleScene");
    }
}
