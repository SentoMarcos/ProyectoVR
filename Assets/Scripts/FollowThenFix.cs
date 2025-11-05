using UnityEngine;
using Vuforia;
using System.Collections;

public class FollowThenFix : MonoBehaviour
{
    public GameObject cube; // Arrastra tu cubo existente aquí
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
            StartCoroutine(FollowThenFixCoroutine());
            hasStarted = true;
        }
    }

    private IEnumerator FollowThenFixCoroutine()
    {
        // Seguir el target durante 3 segundos
        cube.transform.parent = transform;
        cube.transform.localPosition = Vector3.up * 0.05f;
        cube.transform.localRotation = Quaternion.identity;

        yield return new WaitForSeconds(3f);

        // Guardar posición global y desvincular del target
        Vector3 globalPos = cube.transform.position;
        Quaternion globalRot = cube.transform.rotation;

        // Mostrar las coordenadas en consola
        Debug.Log($"?? Posición del cubo: X={globalPos.x:F3}, Y={globalPos.y:F3}, Z={globalPos.z:F3}");

        cube.transform.parent = null;
        cube.transform.SetPositionAndRotation(globalPos, globalRot);
    }
}
