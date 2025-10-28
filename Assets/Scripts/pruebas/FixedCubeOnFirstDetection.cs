using UnityEngine;
using Vuforia;

public class FixedCubeOnFirstDetection : MonoBehaviour
{
    public GameObject cubePrefab;
    private GameObject cubeInstance;
    private ImageTargetBehaviour imageTarget;
    private bool hasPlacedCube = false;

    void Start()
    {
        imageTarget = GetComponent<ImageTargetBehaviour>();
        if (imageTarget != null)
        {
            imageTarget.OnTargetStatusChanged += OnTargetStatusChanged;
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
            TryPlaceCube();
        }
    }

    private void TryPlaceCube()
    {
        if (hasPlacedCube) return; // Ya lo hemos colocado, no hacer nada más

        // Crear el cubo en la posición actual del target
        Vector3 cubePos = transform.position + Vector3.up * 0.05f;
        Quaternion cubeRot = transform.rotation;

        cubeInstance = Instantiate(cubePrefab, cubePos, cubeRot);

        // Desvincularlo del target para que se quede fijo en el mundo
        cubeInstance.transform.parent = null;

        hasPlacedCube = true; // Marcar como colocado permanentemente
    }
}
