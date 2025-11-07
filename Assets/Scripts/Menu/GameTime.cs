using UnityEngine;

// Centraliza el estado de pausa y ofrece DeltaTime que respeta la pausa.
public static class GameTime
{
    // Estado de pausa global
    public static bool Paused { get; private set; }

    // DeltaTime "seguro": si está en pausa devuelve 0; si no, Time.deltaTime
    public static float DeltaTime => Paused ? 0f : Time.deltaTime;

    // Útil para UI, cámaras u overlays que deben moverse aunque el juego esté en pausa
    public static float UnscaledDeltaTime => Time.unscaledDeltaTime;

    // Aplicar/consultar pausa sin tocar Time.timeScale, por si hay sistemas que dependen de él
    public static void SetPaused(bool paused)
    {
        Paused = paused;
        // Opcional: si además quieres congelar físicas/animaciones basadas en timeScale, descomenta:
        // Time.timeScale = paused ? 0f : 1f;
        // AudioListener.pause = paused; // si quieres pausar audio global
    }

    public static void TogglePaused() => SetPaused(!Paused);
}
