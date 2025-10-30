using UnityEngine;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Movimiento")]
    public float moveDistance = 3.5f; // Obsoleto para traslación directa
    public float moveDuration = 0.3f; // Obsoleto para traslación directa

    [Header("Carriles")]
    public int maxLane = 1; // Obsoleto, lo controla RoadFromTargetsSticky.laneCount
    private int currentLane = 0; // índice discreto usado por RoadLaneFollower

    public Animator animator;

    [Header("Seguir carretera")]
    public RoadLaneFollower laneFollower; // Asignar en la moto
    public bool autoCenterOnStart = true;

    private bool isMoving = false;
    private bool ready = false; // evita movimientos en el arranque

    void Awake()
    {
        // 🔹 Asegura que el Animator no empuje al personaje
        if (animator != null)
            animator.applyRootMotion = false;

        if (!laneFollower)
            laneFollower = GetComponent<RoadLaneFollower>();
    }

    IEnumerator Start()
    {
        // 🔹 Reproduce Idle como estado inicial
        if (animator != null)
        {
            animator.Play("BikeRig|Idle", 0, 0f);
            animator.Update(0f);
        }

        if (laneFollower && laneFollower.road)
        {
            // Espera un frame e intenta esperar a PathReady un tiempo prudente
            yield return null;
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

    public void MoveLeft()
    {
        if (!ready || isMoving) return;
        if (!laneFollower || !laneFollower.road) return;

        int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
        int newLane = Mathf.Clamp(currentLane - 1, 0, lanes - 1);
        if (newLane != currentLane)
        {
            currentLane = newLane;
            laneFollower.laneIndex = currentLane;

            if (animator)
            {
                animator.Play("BikeRig|movIzquierda", 0, 0f);
                animator.Update(0f);
            }

            StartCoroutine(LaneChangeCooldown(laneFollower ? Mathf.Max(0.05f, laneFollower.laneChangeTime) : 0.2f));
        }
    }

    public void MoveRight()
    {
        if (!ready || isMoving) return;
        if (!laneFollower || !laneFollower.road) return;

        int lanes = Mathf.Max(1, laneFollower.road.LaneCountPublic);
        int newLane = Mathf.Clamp(currentLane + 1, 0, lanes - 1);
        if (newLane != currentLane)
        {
            currentLane = newLane;
            laneFollower.laneIndex = currentLane;

            if (animator)
            {
                animator.Play("BikeRig|movDerecha", 0, 0f);
                animator.Update(0f);
            }

            StartCoroutine(LaneChangeCooldown(laneFollower ? Mathf.Max(0.05f, laneFollower.laneChangeTime) : 0.2f));
        }
    }

    public void MoveForward()
    {
        // Botón de acelerar (mantener presionado)
        if (!ready) return;
        if (laneFollower) laneFollower.AccelerateOn();
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

    // UI hooks
    public void AccelerateOn()
    {
        if (laneFollower) laneFollower.AccelerateOn();
    }
    public void AccelerateOff()
    {
        if (laneFollower) laneFollower.AccelerateOff();
    }
}