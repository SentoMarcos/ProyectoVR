using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public string instruccionesScene = "Instrucciones";
    public string trailerScene = "Trailer";
    public string modeloScene = "Modelo";

    public void LoadInstrucciones()   => SceneManager.LoadScene(instruccionesScene);
    public void LoadTrailer()         => SceneManager.LoadScene(trailerScene);
    public void LoadModelo()          => SceneManager.LoadScene(modeloScene);

    public void QuitApp()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}
