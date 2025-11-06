using UnityEngine;
using UnityEngine.InputSystem;

public class MotoController : MonoBehaviour
{
    [Header("Road & Lane Settings")]
    public Transform roadCenter;   // centro de la carretera
    public float laneWidth = 2.5f; // ancho de carril
    public int laneCount = 3;

    [Header("Movement Settings")]
    public float moveSpeed = 5f;       // velocidad adelante/atrás
    public float laneChangeSpeed = 5f; // suavidad para cambiar de carril

    private int currentLane = 1;  // 0 = izquierda, 1 = centro, 2 = derecha
    private float targetX;        // X deseada para interpolar

    void Start()
    {
        if (roadCenter != null)
        {
            transform.position = new Vector3(roadCenter.position.x, transform.position.y, roadCenter.position.z);
        }
        currentLane = 1;
        targetX = transform.position.x;
    }

    void Update()
    {
        HandleInput();

        // Movimiento lateral suave
        Vector3 pos = transform.position;
        pos.x = Mathf.Lerp(pos.x, targetX, Time.deltaTime * laneChangeSpeed);

        transform.position = pos; // solo X y Z cambiarán, Y permanece igual
    }

    void HandleInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Cambiar carril (X)
        if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
        {
            if (currentLane > 0)
            {
                currentLane--;
                UpdateTargetX();
            }
        }
        if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
        {
            if (currentLane < laneCount - 1)
            {
                currentLane++;
                UpdateTargetX();
            }
        }

        // Mover adelante/atrás (Z)
        Vector3 forward = new Vector3(0f, 0f, moveSpeed * Time.deltaTime);
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            transform.position += forward; // adelante
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            transform.position -= forward; // atrás
    }

    void UpdateTargetX()
    {
        float offsetFromCenter = (currentLane - 1) * laneWidth;
        targetX = roadCenter.position.x + offsetFromCenter;
    }
}
