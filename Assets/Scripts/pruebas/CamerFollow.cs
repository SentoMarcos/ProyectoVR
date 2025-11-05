using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target;         // la moto a seguir
    public Vector3 offset = new Vector3(0f, 5f, -10f); // posición relativa a la moto

    [Header("Follow Settings")]
    public float smoothSpeed = 5f;   // suavidad al seguir

    void LateUpdate()
    {
        if (target == null) return;

        // Posición deseada
        Vector3 desiredPosition = target.position + offset;

        // Mantener Y del offset + suavizado
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        transform.position = smoothedPosition;

        // Mirar siempre al objetivo
        transform.LookAt(target.position);
    }
}
