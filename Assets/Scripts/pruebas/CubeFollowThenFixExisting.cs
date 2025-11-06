using UnityEngine;
using Vuforia;
using System.Collections;

public class CubeFollowThenFixExisting : MonoBehaviour
{
    public GameObject cube; // Arrastra aquí tu cubo existente en la escena
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
            StartCoroutine(FollowThenFix());
            hasStarted = true;
        }
    }

    private IEnumerator FollowThenFix()
    {
        // Hacer el cubo hijo del target para que siga su movimiento
        cube.transform.parent = transform;

        // Ajustar posición relativa para que quede encima
        cube.transform.localPosition = Vector3.up * 0.05f;
        cube.transform.localRotation = Quaternion.identity;

        // Esperar 3 segundos
        yield return new WaitForSeconds(3f);

        // Desvincular del target y mantener posición global
        cube.transform.parent = null;
        cube.transform.position = cube.transform.position;
        cube.transform.rotation = cube.transform.rotation;
    }
}
