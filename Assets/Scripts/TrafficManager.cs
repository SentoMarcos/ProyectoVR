using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
/// Spawner y gestor de tráfico. Genera coches en distintos carriles
/// y gestiona su movimiento y comportamiento.
/// </summary>
public class TrafficManager : MonoBehaviour
{
    [Header("Referencias")]
    public RoadFromTargetsSticky road;
    [Tooltip("Seguidor de la moto para tener en cuenta su carril y posición")]
    public RoadLaneFollower motoFollower;
    [Tooltip("Transform de la moto (opcional), usado para gizmos si no hay follower")]
    public Transform motoTransform;

    [Header("Spawn")]
    public int initialCars = 6;
    public float minSpacingMeters = 1.0f;
    public Vector2 speedRange = new Vector2(0.8f, 1.6f);
    public int reservedLaneIndex = 0; // carril para la moto
    [Tooltip("Radio (metros) alrededor de la moto donde NO se spawnearán NPCs al inicio")]
    public float spawnExclusionRadiusMeters = 5f;
    [Tooltip("Si la pista es muy corta comparada con el radio, permite desactivar la exclusión")]
    public bool allowDisableExclusionOnShortTrack = true;

    [Header("Prefabs de coches")]
    [Tooltip("Puedes asignar varios modelos de coche aquí")]
    public GameObject[] carPrefabs;
    public Vector3 carBoxSize = new Vector3(0.3f, 0.15f, 0.6f);

    private readonly List<TrafficAgent> agents = new();
    private bool _spawned;

    [Header("Debug")]
    public bool debugSpawnLogs = false;

    void Start()
    {
        if (!road) road = FindFirstObjectByType<RoadFromTargetsSticky>();
        if (!road)
        {
            if (debugSpawnLogs) Debug.Log("[Traffic] No road found at Start");
            return;
        }

        if (road.PathReady)
        {
            if (debugSpawnLogs) Debug.Log("[Traffic] Road ready. Spawning next frame to sync with moto.");
            StartCoroutine(SpawnInitialDeferred());
            _spawned = true;
        }
        else if (debugSpawnLogs)
        {
            Debug.Log("[Traffic] Road not ready at Start. Will wait until PathReady.");
        }
    }

    void Update()
    {
        if (!_spawned && road && road.PathReady)
        {
            if (debugSpawnLogs) Debug.Log("[Traffic] Road became ready. Spawning next frame to sync with moto.");
            StartCoroutine(SpawnInitialDeferred());
            _spawned = true;
        }
    }

    IEnumerator SpawnInitialDeferred()
    {
        // Espera un frame para asegurar que RoadLaneFollower de la moto actualice su CurrentS
        yield return null;
        SpawnInitial();
    }

    void SpawnInitial()
    {
        int lanes = Mathf.Max(1, road.LaneCountPublic);
        float len = road.PathLength;
        bool exclusionActive = spawnExclusionRadiusMeters > 0.01f && (!allowDisableExclusionOnShortTrack || len > spawnExclusionRadiusMeters * 1.2f);
        float motoS = -1f;
        if (motoFollower)
        {
            motoS = motoFollower.CurrentS;
        }

        for (int i = 0; i < initialCars; i++)
        {
            bool placed = false;
            const int MAX_TRIES_PER_CAR = 12;
            for (int attempt = 0; attempt < MAX_TRIES_PER_CAR; attempt++)
            {
                int lane = Random.Range(0, lanes);
                float s = Random.Range(0f, Mathf.Max(1f, len - 0.1f));

                // 1) Evitar spawn cerca de la moto (distancia sobre el camino)
                if (exclusionActive && motoS >= 0f)
                {
                    float gap = Mathf.Abs(s - motoS);
                    if (road.IsClosedPath && gap > len * 0.5f) gap = len - gap;
                    if (gap < spawnExclusionRadiusMeters)
                    {
                        continue; // demasiado cerca de la moto
                    }
                    // Opción adicional: si es el mismo carril, también evita dentro del radio
                    if (motoFollower && lane == motoFollower.laneIndex && gap < spawnExclusionRadiusMeters)
                    {
                        continue;
                    }
                }

                // 2) Respetar separación mínima entre coches ya creados en ese carril
                if (!IsLaneSegmentClear(lane, s, Mathf.Max(0f, minSpacingMeters), Mathf.Max(0f, minSpacingMeters), null))
                {
                    continue; // muy cerca de otro coche
                }

                float speed = Random.Range(speedRange.x, speedRange.y);
                CreateCar(lane, s, speed);
                placed = true;
                break;
            }
            if (!placed && debugSpawnLogs)
            {
                Debug.Log($"[Traffic] No se pudo ubicar el coche {i} respetando exclusión y separación. Se omite.");
            }
        }

        if (debugSpawnLogs)
            Debug.Log($"[Traffic] Spawned {initialCars} cars. lanes={lanes} len={len:F2}");
    }

    void CreateCar(int laneIndex, float startS, float desiredSpeed)
    {
        GameObject go;

        // ✅ Selecciona un prefab aleatorio si hay varios
        if (carPrefabs != null && carPrefabs.Length > 0)
        {
            GameObject chosenPrefab = carPrefabs[Random.Range(0, carPrefabs.Length)];
            go = Instantiate(chosenPrefab, transform);
        }
        else
        {
            // Si no hay prefabs, crea una caja por defecto
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(transform, false);
            var coll = go.GetComponent<Collider>();
            if (coll) Destroy(coll);
            go.transform.localScale = carBoxSize;
        }

        go.tag = "NPC";

        // Añadir componentes
        var follower = go.AddComponent<RoadLaneFollower>();
        follower.road = road;
        follower.applyInLateUpdate = true;
        follower.requireAccelerate = false;
        follower.laneIndex = laneIndex;
        follower.startDistance = startS;
        follower.speed = desiredSpeed;
        follower.lockScaleToInitial = true;
        follower.alignToPath = true;

        var agent = go.AddComponent<TrafficAgent>();
        agent.follower = follower;
        agent.road = road;
        agent.manager = this;
        agent.desiredSpeed = desiredSpeed;
        agent.reservedLaneIndex = reservedLaneIndex;

        agents.Add(agent);
    }

    public TrafficAgent FindFrontAgent(TrafficAgent me)
    {
        TrafficAgent candidate = null;
        float bestGap = float.MaxValue;
        int lane = me.follower.laneIndex;
        float myS = me.follower.CurrentS;
        float len = road.PathLength;
        foreach (var a in agents)
        {
            if (a == null || a == me) continue;
            if (a.follower.laneIndex != lane) continue;
            float gap = a.follower.CurrentS - myS;
            if (road.IsClosedPath && gap < 0f) gap += len;
            if (gap > 0f && gap < bestGap)
            {
                bestGap = gap; candidate = a;
            }
        }
        return candidate;
    }

    public TrafficAgent FindFrontAgentInLane(int lane, float myS)
    {
        TrafficAgent candidate = null;
        float bestGap = float.MaxValue;
        float len = road.PathLength;
        foreach (var a in agents)
        {
            if (a == null) continue;
            if (a.follower.laneIndex != lane) continue;
            float gap = a.follower.CurrentS - myS;
            if (road.IsClosedPath && gap < 0f) gap += len;
            if (gap > 0f && gap < bestGap)
            {
                bestGap = gap; candidate = a;
            }
        }
        return candidate;
    }

    public bool IsLaneOccupiedClose(int laneIndex, float s, float radius = 1.0f)
    {
        float len = road.PathLength;
        foreach (var a in agents)
        {
            if (a == null) continue;
            if (a.follower.laneIndex != laneIndex) continue;
            float gap = Mathf.Abs(a.follower.CurrentS - s);
            if (gap > len * 0.5f) gap = len - gap; // camino cerrado
            if (gap < radius) return true;
        }
        return false;
    }

    // Comprueba que el segmento [s - behind, s + forward] en un carril está libre de otros agentes (excluyendo 'exclude')
    public bool IsLaneSegmentClear(int laneIndex, float sRef, float forward, float behind, TrafficAgent exclude = null)
    {
        if (!road) return false;
        float len = road.PathLength;
        float sStart = sRef - Mathf.Max(0f, behind);
        float sEnd = sRef + Mathf.Max(0f, forward);
        foreach (var a in agents)
        {
            if (a == null || a == exclude) continue;
            if (a.follower.laneIndex != laneIndex) continue;
            float sA = a.follower.CurrentS;
            // manejo de wrap para circuito cerrado
            // normaliza comparando distancia mínima
            bool overlap = SegmentContains(len, sStart, sEnd, sA);
            if (overlap) return false;
        }
        // También evita choque con la moto si ocupa ese carril y está dentro del segmento
        if (motoFollower && motoFollower.laneIndex == laneIndex)
        {
            if (SegmentContains(len, sStart, sEnd, motoFollower.CurrentS)) return false;
        }
        return true;
    }

    bool SegmentContains(float len, float sStart, float sEnd, float sPoint)
    {
        // Ajusta a rango [0,len)
        if (len <= 0f) return false;
        float wrapStart = Mod(sStart, len);
        float wrapEnd = Mod(sEnd, len);
        float p = Mod(sPoint, len);
        if (wrapStart <= wrapEnd) return p >= wrapStart && p <= wrapEnd;
        // cruza el cero (wrap)
        return p >= wrapStart || p <= wrapEnd;
    }

    float Mod(float x, float m)
    {
        if (m <= 0f) return 0f;
        float r = x % m;
        if (r < 0f) r += m;
        return r;
    }

    void OnDrawGizmosSelected()
    {
        if (spawnExclusionRadiusMeters <= 0.01f) return;
        Transform refT = null;
        if (motoFollower) refT = motoFollower.transform;
        else if (motoTransform) refT = motoTransform;
        if (!refT) return;

        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.3f);
        Gizmos.DrawSphere(refT.position, spawnExclusionRadiusMeters);
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(refT.position, spawnExclusionRadiusMeters);
    }
}
