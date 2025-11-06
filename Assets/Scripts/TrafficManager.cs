using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawner y gestor de tráfico muy simple. Genera cajas "coches" en distintos carriles
/// y gestiona consultas de vecindad para car-following y cambios de carril.
/// </summary>
public class TrafficManager : MonoBehaviour
{
    [Header("Referencias")]
    public RoadFromTargetsSticky road;
    [Tooltip("Seguidor de la moto para tener en cuenta su carril y posición")]
    public RoadLaneFollower motoFollower;

    [Header("Spawn")]
    public int initialCars = 6;
    public float minSpacingMeters = 1.0f;
    public Vector2 speedRange = new Vector2(0.8f, 1.6f);
    public int reservedLaneIndex = 0; // carril para la moto

    [Header("Prefab (si se deja vacío, se crea una caja)")]
    public GameObject[] carPrefabs;
    public Vector3 carBoxSize = new Vector3(0.3f, 0.15f, 0.6f);

    readonly List<TrafficAgent> agents = new();
    bool _spawned;
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
            SpawnInitial();
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
            if (debugSpawnLogs) Debug.Log("[Traffic] Road became ready. Spawning cars now.");
            SpawnInitial();
            _spawned = true;
        }
    }

    void SpawnInitial()
    {
        int lanes = Mathf.Max(1, road.LaneCountPublic);
        float len = road.PathLength;
        for (int i = 0; i < initialCars; i++)
        {
            int lane = Random.Range(0, lanes); // permitimos usar reservado; se apartarán si la moto llega
            float s = Random.Range(0f, Mathf.Max(1f, len - 0.1f));
            CreateCar(lane, s, Random.Range(speedRange.x, speedRange.y));
        }
        if (debugSpawnLogs) Debug.Log($"[Traffic] Spawned {initialCars} cars. lanes={lanes} len={len:F2}");
    }

    void CreateCar(int laneIndex, float startS, float desiredSpeed)
    {
        GameObject go;
        // **CAMBIO AQUÍ: Selecciona un prefab aleatorio si el array no está vacío**
        if (carPrefabs != null && carPrefabs.Length > 0)
        {
            GameObject selectedPrefab = carPrefabs[Random.Range(0, carPrefabs.Length)];
            go = Instantiate(selectedPrefab, transform);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(transform, false);
            var coll = go.GetComponent<Collider>(); if (coll) Destroy(coll);
            go.transform.localScale = carBoxSize;
        }
        go.tag = "NPC";

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
}
