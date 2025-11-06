using UnityEngine;
using Vuforia;

public class FixedCubeOnTarget : MonoBehaviour
{
    public GameObject cubePrefab;
    private GameObject cubeInstance;
    private ImageTargetBehaviour imageTarget;
    private bool isFixed = false;

    void Start()
    {
        imageTarget = GetComponent<ImageTargetBehaviour>();
        if (imageTarget != null)
        {
            imageTarget.OnTargetStatusChanged += OnTargetStatusChanged;
            Debug.Log("[FixedCubeOnTarget] ImageTarget detectado. Esperando cambios de estado...");
        }
        else
        {
            Debug.LogWarning("[FixedCubeOnTarget] No se encontró el componente ImageTargetBehaviour.");
        }
    }

    private void OnDestroy()
    {
        if (imageTarget != null)
        {
            imageTarget.OnTargetStatusChanged -= OnTargetStatusChanged;
        }
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        if (status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED)
        {
            OnTargetFound();
        }
        else if (status.Status == Status.NO_POSE)
        {
            OnTargetLost();
        }
    }

    private void OnTargetFound()
    {
        if (isFixed) return; // Si ya está fijo, no hacer nada

        if (cubeInstance == null)
        {
            // Crear cubo en la posición actual del target
            Vector3 cubePos = transform.position + Vector3.up * 0.05f;
            Quaternion cubeRot = transform.rotation;

            cubeInstance = Instantiate(cubePrefab, cubePos, cubeRot);

            // Desvincularlo del target
            cubeInstance.transform.parent = null;

            Debug.Log($"[FixedCubeOnTarget] Cubo instanciado en posición: {cubeInstance.transform.position}");
        }
        else
        {
            // Actualizar posición mientras el target esté visible
            cubeInstance.transform.position = transform.position + Vector3.up * 0.05f;
            cubeInstance.transform.rotation = transform.rotation;

            Debug.Log($"[FixedCubeOnTarget] Cubo actualizado a posición: {cubeInstance.transform.position}");
        }
    }

    private void OnTargetLost()
    {
        if (cubeInstance != null)
        {
            isFixed = true;
            Debug.Log($"[FixedCubeOnTarget] Target perdido. Cubo fijado en posición final: {cubeInstance.transform.position}");
        }
    }
}
