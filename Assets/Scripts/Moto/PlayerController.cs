using UnityEngine;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Movimiento")]
    // Obsoletos para traslación directa, se mantienen por compatibilidad de inspector
    public float moveDistance = 3.5f;
    public float moveDuration = 0.3f;

    [Header("Carriles")]
    // Obsoleto: el conteo de carriles real lo define RoadFromTargetsSticky.laneCount
    public int maxLane = 1;
    private int currentLane = 0; // Índice discreto del carril usado por RoadLaneFollower

    [Header("Referencias")]
    public Animator animator;
    [Tooltip("Componente que sigue la carretera. Debe estar en la moto")]
    public RoadLaneFollower laneFollower;
    [Tooltip("Centrar en el carril medio al iniciar")]
    public bool autoCenterOnStart = true;

    [Header("Animaciones")]
    [Tooltip("Nombre del estado Idle en el Animator")] public string animIdle = "BikeRig|Idle";
    [Tooltip("Nombre del estado de giro a la izquierda")] public string animLeft = "BikeRig|movIzquierda";
    [Tooltip("Nombre del estado de giro a la derecha")] public string animRight = "BikeRig|movDerecha";
    [Tooltip("Usar CrossFade en lugar de Play para transiciones suaves")] public bool useCrossFade = true;
    [Tooltip("Duración de CrossFade")] public float crossFadeDuration = 0.08f;
    [Tooltip("Volver a Idle automáticamente después del cambio de carril")] public bool returnToIdleAfterLaneChange = true;
    [Header("Animator (Triggers opcionales)")]
    [Tooltip("Si está activo, usará parámetros Trigger del Animator en lugar de nombres de estado")] public bool useAnimatorTriggers = false;
    [Tooltip("Nombre del Trigger para animación de giro a la izquierda")] public string leftTriggerName = "TurnLeft";
    [Tooltip("Nombre del Trigger para animación de giro a la derecha")] public string rightTriggerName = "TurnRight";

    [Header("Restricciones de control")]
    [Tooltip("Si está activo, sólo permitirá cambiar de carril mientras se está acelerando")] public bool requireAcceleratingForLaneChange = false;

    // Estado
    private bool isMoving = false; // cooldown de cambio de carril
    private bool ready = false;

    void Awake()
    {
        if (animator != null)
        {
            animator.applyRootMotion = false;
            // Asegura que el Animator no pare por estar fuera de cámara ni cambie el orden de actualización
            animator.updateMode = AnimatorUpdateMode.Normal; // anima en Update
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // no se pausa al no estar visible
        }
        if (!laneFollower)
            laneFollower = GetComponent<RoadLaneFollower>();
    }

    IEnumerator Start()
    {
        // Idle inicial
        if (animator != null)
        {
            SafePlay(animIdle, 0f);
            animator.Update(0f);
        }

        // Espera a que el camino esté listo y centra al medio
        if (laneFollower && laneFollower.road)
        {
            yield return null; // un frame para inicializar
            float timeout = 2f;
            while (!laneFollower.road.PathReady && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (autoCenterOnStart && laneFollower.road)
            {
                int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
                currentLane = (lanes - 1) / 2;
                laneFollower.laneIndex = currentLane;
            }
        }
        else
        {
            yield return null;
        }

        ready = true;
    }

    // Botón IZQUIERDA
    public void MoveLeft()
    {
        if (!ready || isMoving) return;
        if (!laneFollower || !laneFollower.road) return;
        if (requireAcceleratingForLaneChange && laneFollower && !laneFollower.IsAccelerating) return;

        int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
        int newLane = Mathf.Clamp(currentLane - 1, 0, lanes - 1);
        if (newLane != currentLane)
        {
            currentLane = newLane;
            laneFollower.laneIndex = currentLane;
            PlayLaneAnim(animLeft);
            float lockTime = laneFollower ? Mathf.Max(0.05f, laneFollower.laneChangeTime) : 0.2f;
            StartCoroutine(LaneChangeCooldown(lockTime));
            if (returnToIdleAfterLaneChange) StartCoroutine(ReturnToIdleAfter(lockTime));
        }
    }

    // Botón DERECHA
    public void MoveRight()
    {
        if (!ready || isMoving) return;
        if (!laneFollower || !laneFollower.road) return;
        if (requireAcceleratingForLaneChange && laneFollower && !laneFollower.IsAccelerating) return;

        int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
        int newLane = Mathf.Clamp(currentLane + 1, 0, lanes - 1);
        if (newLane != currentLane)
        {
            currentLane = newLane;
            laneFollower.laneIndex = currentLane;
            PlayLaneAnim(animRight);
            float lockTime = laneFollower ? Mathf.Max(0.05f, laneFollower.laneChangeTime) : 0.2f;
            StartCoroutine(LaneChangeCooldown(lockTime));
            if (returnToIdleAfterLaneChange) StartCoroutine(ReturnToIdleAfter(lockTime));
        }
    }

    // Botón ACELERAR (mantener pulsado)
    public void MoveForward()
    {
        if (!ready) return;
        if (laneFollower) laneFollower.AccelerateOn();
    }

    // UI hooks para hold explícito
    public void AccelerateOn()
    {
        if (laneFollower) laneFollower.AccelerateOn();
    }
    public void AccelerateOff()
    {
        if (laneFollower) laneFollower.AccelerateOff();
    }

    private IEnumerator LaneChangeCooldown(float duration)
    {
        isMoving = true;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            yield return null;
        }
        isMoving = false;
    }

    private IEnumerator ReturnToIdleAfter(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay));
        SafeCrossFade(animIdle, crossFadeDuration);
    }

    void PlayLaneAnim(string stateName)
    {
        if (!animator) return;
        if (useAnimatorTriggers)
        {
            string trig = null;
            if (!string.IsNullOrEmpty(animLeft) && stateName == animLeft) trig = leftTriggerName;
            else if (!string.IsNullOrEmpty(animRight) && stateName == animRight) trig = rightTriggerName;

            if (!string.IsNullOrEmpty(trig))
            {
                // Evita encadenar triggers
                if (!string.IsNullOrEmpty(leftTriggerName)) animator.ResetTrigger(leftTriggerName);
                if (!string.IsNullOrEmpty(rightTriggerName)) animator.ResetTrigger(rightTriggerName);
                animator.SetTrigger(trig);
            }
            else
            {
                // Fallback a estados
                if (useCrossFade) SafeCrossFade(stateName, crossFadeDuration);
                else SafePlay(stateName, 0f);
            }
        }
        else
        {
            if (useCrossFade) SafeCrossFade(stateName, crossFadeDuration);
            else SafePlay(stateName, 0f);
        }
        animator.Update(0f);
    }

    void SafePlay(string stateName, float normalizedTime)
    {
        if (!animator || string.IsNullOrEmpty(stateName)) return;
        animator.Play(stateName, 0, normalizedTime);
    }

    void SafeCrossFade(string stateName, float duration)
    {
        if (!animator || string.IsNullOrEmpty(stateName)) return;
        animator.CrossFadeInFixedTime(stateName, Mathf.Max(0f, duration));
    }
}