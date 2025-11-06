using UnityEngine;

/// <summary>
/// Genera en tiempo de ejecución una carretera recta de 3 carriles con marcas discontinuas entre carriles.
/// Pegarlo en un GameObject vacío y asignar materials en el Inspector.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ThreeLaneRoadGenerator : MonoBehaviour
{
    [Header("Road settings")]
    public int laneCount = 3;                 // fijo a 3 en nuestra intención
    public float laneWidth = 2.5f;           // ancho por carril (m)
    public float roadLength = 30f;           // longitud total de la carretera (m)
    public Material roadMaterial;            // material del asfalto
    public bool generateCollider = true;

    [Header("Lane marker settings")]
    public Material markerMaterial;          // material de la marca (blanco/amarillo)
    public float markerWidth = 0.12f;        // grosor de la marca
    public float markerLength = 1.0f;        // longitud de cada dash
    public float markerSpacing = 1.5f;       // espacio entre dashes

    // internal
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
    }

    void Start()
    {
        if (laneCount != 3)
        {
            Debug.LogWarning("Este generador está pensado para 3 carriles pero laneCount está en " + laneCount);
        }

        GenerateRoad();
        GenerateLaneMarkers();
    }

    void GenerateRoad()
    {
        float totalWidth = laneWidth * laneCount;
        float halfWidth = totalWidth / 2f;
        float halfLength = roadLength / 2f;

        // Crear mesh simple (un rectángulo)
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[4];
        Vector2[] uvs = new Vector2[4];
        int[] tris = new int[6];

        // vertices: (x,z) en el plano XZ, y = 0
        vertices[0] = new Vector3(-halfWidth, 0f, -halfLength); // bottom-left
        vertices[1] = new Vector3(halfWidth, 0f, -halfLength);  // bottom-right
        vertices[2] = new Vector3(-halfWidth, 0f, halfLength);  // top-left
        vertices[3] = new Vector3(halfWidth, 0f, halfLength);   // top-right

        // UV
        uvs[0] = new Vector2(0, 0);
        uvs[1] = new Vector2(1, 0);
        uvs[2] = new Vector2(0, 1);
        uvs[3] = new Vector2(1, 1);

        // Triangles
        tris[0] = 0;
        tris[1] = 2;
        tris[2] = 1;
        tris[3] = 2;
        tris[4] = 3;
        tris[5] = 1;

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
        if (roadMaterial != null) meshRenderer.material = roadMaterial;

        // Añadir collider para interacción/fisicas
        if (generateCollider)
        {
            BoxCollider bc = GetComponent<BoxCollider>();
            if (bc == null) bc = gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, -0.01f, 0f); // pequeño offset hacia abajo
            bc.size = new Vector3(totalWidth, 0.02f, roadLength);
        }
    }

    void GenerateLaneMarkers()
    {
        if (markerMaterial == null)
        {
            Debug.LogWarning("Asignar un material para markerMaterial si quieres marcas de carril.");
            return;
        }

        // posiciones X de las divisiones entre carriles (para 3 carriles hay 2 divisiones)
        float totalWidth = laneWidth * laneCount;
        float halfWidth = totalWidth / 2f;

        // divisiones: -half + laneWidth, -half + 2*laneWidth, ... pero queremos solo entre carriles
        // para 3 carriles, las X son: -laneWidth/2 y +laneWidth/2 (centros de las divisiones)
        float leftDivisionX = -laneWidth / 2f;
        float rightDivisionX = laneWidth / 2f;

        CreateDashesAlongLine(leftDivisionX);
        CreateDashesAlongLine(rightDivisionX);
    }

    void CreateDashesAlongLine(float localX)
    {
        // desde -roadLength/2 hasta +roadLength/2
        float zStart = -roadLength / 2f;
        float zEnd = roadLength / 2f;

        float step = markerLength + markerSpacing;
        int count = Mathf.CeilToInt((zEnd - zStart) / step) + 2;

        for (int i = 0; i < count; i++)
        {
            float z = zStart + i * step;
            // Ajuste para centrar dashes alternos (opcional)
            Vector3 pos = new Vector3(localX, 0.01f, z); // un poco por encima del asfalto
            GameObject dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dash.name = "LaneMarker";
            dash.transform.SetParent(this.transform, false);
            dash.transform.localPosition = pos;
            dash.transform.localRotation = Quaternion.identity;
            dash.transform.localScale = new Vector3(markerWidth, 0.02f, markerLength);

            // Asignar material
            var rend = dash.GetComponent<Renderer>();
            if (rend != null) rend.material = markerMaterial;

            // Eliminar collider de las marcas para que no interfieran con la interacción (opcional)
            Collider c = dash.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }
    }

#if UNITY_EDITOR
    // Esto permite regenerar en Editor cuando cambias parámetros (opcional)
    void OnValidate()
    {
        if (Application.isPlaying) return;
        // no hacemos nada automático en editor para evitar conflictos,
        // pero podríamos añadir herramientas Editor si lo quieres.
    }
#endif
}
