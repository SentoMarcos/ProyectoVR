using UnityEngine;
using System.Collections.Generic;

public class TrafficSpawner : MonoBehaviour
{
    [Header("Vehicle Settings")]
    public GameObject vanPrefab;
    public float speed = 2f;             // velocidad de las furgonetas
    public float spawnInterval = 1f;     // tiempo mínimo entre intentos de spawn por carril
    public int maxVansPerLane = 2;       // máximo de furgonetas activas por carril
    public float minDistanceBetweenVans = 8f; // distancia mínima entre furgonetas

    [Header("Road Settings")]
    public Transform roadCenter;
    public float laneWidth = 2.5f;
    public int laneCount = 3;

    private float[] lastSpawnTime;
    private List<GameObject>[] laneVans;

    void Start()
    {
        lastSpawnTime = new float[laneCount];
        laneVans = new List<GameObject>[laneCount];

        for (int i = 0; i < laneCount; i++)
        {
            lastSpawnTime[i] = -spawnInterval;
            laneVans[i] = new List<GameObject>();
        }
    }

    void Update()
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            // Spawn aleatorio si no se ha alcanzado el máximo
            if (laneVans[lane].Count < maxVansPerLane && Time.time - lastSpawnTime[lane] > spawnInterval)
            {
                // Comprobar si hay espacio para una nueva furgoneta
                bool canSpawn = true;
                foreach (GameObject van in laneVans[lane])
                {
                    if (van != null && Vector3.Distance(van.transform.position, roadCenter.position) < minDistanceBetweenVans)
                    {
                        canSpawn = false;
                        break;
                    }
                }

                if (canSpawn && Random.value > 0.5f)
                {
                    SpawnVan(lane);
                    lastSpawnTime[lane] = Time.time;
                }
            }

            // Mover furgonetas hacia adelante (Z)
            for (int i = laneVans[lane].Count - 1; i >= 0; i--)
            {
                GameObject van = laneVans[lane][i];
                if (van != null)
                {
                    van.transform.position += Vector3.forward * speed * Time.deltaTime;

                    // destruir si se sale del road (ejemplo Z > 100)
                    if (van.transform.position.z > 100f)
                    {
                        Destroy(van);
                        laneVans[lane].RemoveAt(i);
                    }
                }
                else
                {
                    laneVans[lane].RemoveAt(i);
                }
            }
        }
    }

    void SpawnVan(int lane)
    {
        float laneOffsetX = (lane - 1) * laneWidth;
        Vector3 pos = new Vector3(
            roadCenter.position.x + laneOffsetX,
            roadCenter.position.y,
            roadCenter.position.z - 10f
        );

        GameObject van = Instantiate(vanPrefab, pos, Quaternion.identity);
        van.name = "Van_L" + lane + "_Rand";
        van.tag = "Van";
        laneVans[lane].Add(van);

        // Collider simple
        if (van.GetComponent<Collider>() == null)
        {
            BoxCollider bc = van.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.75f, 0f); // ajustar al centro
            bc.size = new Vector3(1f, 1.5f, 3.5f);   // aprox ancho, alto, largo
        }

        // Rigidbody
        if (van.GetComponent<Rigidbody>() == null)
        {
            Rigidbody rb = van.AddComponent<Rigidbody>();
            rb.isKinematic = true; // no queremos que la física lo mueva
        }
    }

}
