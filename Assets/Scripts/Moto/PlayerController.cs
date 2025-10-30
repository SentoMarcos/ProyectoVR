using UnityEngine;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Movimiento")]
    public float moveDistance = 3.5f;
    public float moveDuration = 0.3f;

    [Header("Carriles")]
    public int maxLane = 1; // -1 = izquierda, 0 = centro, 1 = derecha
    private int currentLane = 0;

    public Animator animator;

    private bool isMoving = false;
    private bool ready = false; // evita movimientos en el arranque

    void Awake()
    {
        // 🔹 Asegura que el Animator no empuje al personaje
        if (animator != null)
            animator.applyRootMotion = false;
    }

    IEnumerator Start()
    {
        // 🔹 Centra el personaje en el carril 0
        Vector3 p = transform.position;
        p.x = 0f; // si tus carriles son en Z, cambia por p.z = 0f;
        transform.position = p;
        currentLane = 0;

        // 🔹 Reproduce Idle como estado inicial
        if (animator != null)
        {
            animator.Play("BikeRig|Idle", 0, 0f);
            animator.Update(0f);
        }

        // Espera un frame para evitar inputs o triggers automáticos
        yield return null;
        ready = true;
    }

    public void MoveLeft()
    {
        if (!ready || isMoving) return;

        if (currentLane > -1 * maxLane)
        {
            currentLane--;
            StartCoroutine(MoveSmooth(Vector3.left * moveDistance));

            // 🔹 Reinicia animación izquierda desde frame 0
            animator.Play("BikeRig|movIzquierda", 0, 0f);
            animator.Update(0f);
        }
    }

    public void MoveRight()
    {
        if (!ready || isMoving) return;

        if (currentLane < maxLane)
        {
            currentLane++;
            StartCoroutine(MoveSmooth(Vector3.right * moveDistance));

            // 🔹 Reinicia animación derecha desde frame 0
            animator.Play("BikeRig|movDerecha", 0, 0f);
            animator.Update(0f);
        }
    }

    public void MoveForward()
    {
        if (!ready || isMoving) return;
        StartCoroutine(MoveSmooth(Vector3.forward * moveDistance));
    }

    private IEnumerator MoveSmooth(Vector3 offset)
    {
        isMoving = true;
        Vector3 start = transform.position;
        Vector3 end = start + offset;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / 0.9f;
            transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        transform.position = end;
        isMoving = false;
    }
}