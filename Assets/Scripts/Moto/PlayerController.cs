using UnityEngine;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Carriles")]
    private int currentLane = 0; // Carril actual (0 = izquierda, centro depende de la carretera)

    [Header("Referencias")]
    public Animator animator;
    [Tooltip("Componente que sigue la carretera. Debe estar en la moto")]
    public RoadLaneFollower laneFollower;
    [Tooltip("Centrar en el carril medio al iniciar")]
    public bool autoCenterOnStart = true;

    [Header("Animaciones")]
    public string animIdle = "BikeRig|Idle";
    public string animLeft = "BikeRig|movIzquierda";
    public string animRight = "BikeRig|movDerecha";
    public bool useCrossFade = true;
    public float crossFadeDuration = 0.08f;
    public bool returnToIdleAfterLaneChange = true;

    [Header("Animator (Triggers opcionales)")]
    public bool useAnimatorTriggers = false;
    public string leftTriggerName = "TurnLeft";
    public string rightTriggerName = "TurnRight";

    [Header("Restricciones de control")]
    [Tooltip("Si está activo, sólo permitirá cambiar de carril mientras se está acelerando")]
    public bool requireAcceleratingForLaneChange = false;

    [Header("Velocidad por vuelta")]
    [Tooltip("Cuánto se incrementa la velocidad por cada vuelta completada")]
    public float speedIncrementPerLap = 0.1f;

    // Estado
    private bool isMoving = false; // evita spam de movimientos
    private bool ready = false;    // espera a que la carretera cargue
    private float lastProgress = 0f;
    private int lapCount = 0;

    void Awake()
    {
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        if (!laneFollower)
            laneFollower = GetComponent<RoadLaneFollower>();
    }

    IEnumerator Start()
    {
        SafePlay(animIdle);
        yield return StartCoroutine(WaitForRoadReady());
        CenterLaneOnStart();
        ready = true;
        laneFollower?.AccelerateOn();
    }

    IEnumerator WaitForRoadReady()
    {
        if (!laneFollower || !laneFollower.road) yield break;

        float timeout = 2f;
        while (!laneFollower.road.PathReady && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
    }

    void CenterLaneOnStart()
    {
        if (!autoCenterOnStart || laneFollower?.road == null) return;

        int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
        currentLane = (lanes - 1) / 2;
        laneFollower.laneIndex = currentLane;
    }

    void Update()
    {
        if (!laneFollower || laneFollower.road == null) return;

        // Detecta vuelta completada
        float progress = laneFollower.CurrentS / laneFollower.road.PathLength;
        if (lastProgress > 0.9f && progress < 0.1f)
        {
            lapCount++;
            laneFollower.speed += speedIncrementPerLap;
            Debug.Log($"Vuelta completada: {lapCount} | Nueva velocidad: {laneFollower.speed}");
        }
        lastProgress = progress;
    }

    public void MoveLeft() => TryChangeLane(-1);
    public void MoveRight() => TryChangeLane(+1);

    void TryChangeLane(int direction)
    {
        if (!ready || isMoving || laneFollower?.road == null) return;

        if (requireAcceleratingForLaneChange && !laneFollower.IsAccelerating)
            return;

        int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
        int newLane = Mathf.Clamp(currentLane + direction, 0, lanes - 1);
        if (newLane == currentLane) return;

        currentLane = newLane;
        laneFollower.laneIndex = currentLane;

        PlayLaneAnim(direction < 0 ? animLeft : animRight);

        float lockTime = Mathf.Max(0.05f, laneFollower.laneChangeTime);
        StartCoroutine(LaneChangeCooldown(lockTime));

        if (returnToIdleAfterLaneChange)
            StartCoroutine(ReturnToIdleAfter(lockTime));
    }

    IEnumerator LaneChangeCooldown(float duration)
    {
        isMoving = true;
        yield return new WaitForSeconds(duration);
        isMoving = false;
    }

    IEnumerator ReturnToIdleAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        SafeCrossFade(animIdle);
    }

    void PlayLaneAnim(string stateName)
    {
        if (!animator) return;

        if (useAnimatorTriggers)
            TriggerAnim(stateName);
        else
        {
            if (useCrossFade) SafeCrossFade(stateName);
            else SafePlay(stateName);
        }

        animator.Update(0f);
    }

    void TriggerAnim(string stateName)
    {
        animator.ResetTrigger(leftTriggerName);
        animator.ResetTrigger(rightTriggerName);

        if (stateName == animLeft) animator.SetTrigger(leftTriggerName);
        else if (stateName == animRight) animator.SetTrigger(rightTriggerName);
    }

    void SafePlay(string stateName)
    {
        if (!string.IsNullOrEmpty(stateName))
            animator.Play(stateName, 0, 0f);
    }

    void SafeCrossFade(string stateName)
    {
        if (!string.IsNullOrEmpty(stateName))
            animator.CrossFadeInFixedTime(stateName, crossFadeDuration);
    }

    public void AccelerateOn() => laneFollower?.AccelerateOn();
    public void AccelerateOff() => laneFollower?.AccelerateOff();
}
