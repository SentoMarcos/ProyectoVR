using UnityEngine;
using Vuforia;
using System.Collections;

public class CubeFollowThenFix : MonoBehaviour
{
    public GameObject cubePrefab;
    private GameObject cubeInstance;
    private ImageTargetBehaviour imageTarget;
    private bool hasStarted = false;

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
        if (!hasStarted && (status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED))
        {
            StartCoroutine(PlaceCubeCoroutine());
            hasStarted = true;
        }
    }

    private IEnumerator PlaceCubeCoroutine()
    {
        // Crear cubo como hijo del target para que siga su movimiento
        cubeInstance = Instantiate(cubePrefab, transform.position + Vector3.up * 0.05f, transform.rotation);
        cubeInstance.transform.parent = transform;

        // Esperar 3 segundos
        yield return new WaitForSeconds(3f);

        // Desvincular del target y mantener la posición global actual
        cubeInstance.transform.parent = null;
        // Esto asegura que conserva su posición exacta en el mundo
        cubeInstance.transform.position = cubeInstance.transform.position;
        cubeInstance.transform.rotation = cubeInstance.transform.rotation;
    }
}
