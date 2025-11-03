using System.Collections.Generic;
using UnityEngine;

public class AutoBikeVuforia : MonoBehaviour
{
    [Header("Targets y ruta")]
    public List<Transform> targets;     // Los targets sobre los que se genera la carretera
    public bool useSmoothInterpolation = true;
    public int samplesPerSegment = 10;  // Para suavizar el camino

    [Header("Velocidad")]
    public float speed = 5f;
    public float speedIncrement = 2f;
    public float maxSpeed = 20f;

    [Header("Proximidad")]
    public float pointThreshold = 0.1f; // Distancia para considerar que se alcanzó un target

    private List<Vector3> path;         // Ruta generada
    private int currentIndex = 0;       // Índice del target actual

    void UpdatePath()
    {
        path = new List<Vector3>();
        if (targets.Count == 0) return;

        // Generar la ruta a partir de los targets
        if (useSmoothInterpolation && targets.Count >= 2)
        {
            for (int i = 0; i < targets.Count - 1; i++)
            {
                Vector3 p0 = i == 0 ? targets[i].position : targets[i - 1].position;
                Vector3 p1 = targets[i].position;
                Vector3 p2 = targets[i + 1].position;
                Vector3 p3 = (i + 2 < targets.Count) ? targets[i + 2].position : targets[i + 1].position;

                for (int s = 0; s <= samplesPerSegment; s++)
                {
                    float t = s / (float)samplesPerSegment;
                    path.Add(CatmullRomCentripetal(p0, p1, p2, p3, t));
                }
            }
        }
        else
        {
            foreach (var t in targets) path.Add(t.position);
        }
    }

    void Start()
    {
        UpdatePath();
        if (path.Count > 0)
            transform.position = path[0]; // Coloca la moto en el primer target
    }

    void Update()
    {
        if (targets.Count == 0) return;

        // Actualizar la ruta cada frame, por si los targets se mueven
        UpdatePath();

        if (path.Count == 0) return;

        Vector3 targetPos = path[currentIndex];
        Vector3 dir = (targetPos - transform.position).normalized;

        // Movimiento
        transform.position += dir * speed * Time.deltaTime;
        transform.forward = dir;

        // Comprobar si se alcanzó el target actual
        if (Vector3.Distance(transform.position, targetPos) < pointThreshold)
        {
            currentIndex++;

            // Si llegamos al final de la ruta
            if (currentIndex >= path.Count)
            {
                currentIndex = 0; // Volver al primer target
                speed = Mathf.Min(speed + speedIncrement, maxSpeed); // Aumentar velocidad
            }
        }
    }

    // -------------------- Catmull-Rom centrípeta --------------------
    Vector3 CatmullRomCentripetal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        const float alpha = 0.5f;
        float t0 = 0f;
        float t1 = t0 + Mathf.Pow((p1 - p0).magnitude, alpha);
        float t2 = t1 + Mathf.Pow((p2 - p1).magnitude, alpha);
        float t3 = t2 + Mathf.Pow((p3 - p2).magnitude, alpha);
        float tt = Mathf.Lerp(t1, t2, t);

        Vector3 A1 = ((t1 - tt) / Mathf.Max(1e-6f, t1 - t0)) * p0 + ((tt - t0) / Mathf.Max(1e-6f, t1 - t0)) * p1;
        Vector3 A2 = ((t2 - tt) / Mathf.Max(1e-6f, t2 - t1)) * p1 + ((tt - t1) / Mathf.Max(1e-6f, t2 - t1)) * p2;
        Vector3 A3 = ((t3 - tt) / Mathf.Max(1e-6f, t3 - t2)) * p2 + ((tt - t2) / Mathf.Max(1e-6f, t3 - t2)) * p3;

        Vector3 B1 = ((t2 - tt) / Mathf.Max(1e-6f, t2 - t0)) * A1 + ((tt - t0) / Mathf.Max(1e-6f, t2 - t0)) * A2;
        Vector3 B2 = ((t3 - tt) / Mathf.Max(1e-6f, t3 - t1)) * A2 + ((tt - t1) / Mathf.Max(1e-6f, t3 - t1)) * A3;

        return ((t2 - tt) / Mathf.Max(1e-6f, t2 - t1)) * B1 + ((tt - t1) / Mathf.Max(1e-6f, t2 - t1)) * B2;
    }
}
