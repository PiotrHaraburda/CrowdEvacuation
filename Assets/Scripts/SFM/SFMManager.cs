using System.Collections.Generic;
using Metrics;
using UnityEngine;

namespace SFM
{
    public class SFMManager : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject sfmAgentPrefab;

        [Header("Spawning")]
        public Transform goal;
        public int agentCount = 93;
        public float spawnMinX = -3.0f;
        public float spawnMaxX = 3.0f;
        public float spawnMinZ = -0.8f;
        public float spawnMaxZ = 1.6f;

        [Header("Agent Variation")]
        public float meanDesiredSpeed = 1.34f; // (Weidmann 1993)
        public float stdDesiredSpeed = 0.26f; // (Weidmann 1993)
        public float minDesiredSpeed = 0.5f;
        public float maxDesiredSpeed = 2.0f;
        public float meanRadius = 0.2f;
        public float stdRadius = 0.02f;

        [Header("References")]
        public EvacuationMetricsLogger metricsLogger;

        [Header("State")]
        [SerializeField] private int spawnedCount;
        [SerializeField] private int evacuatedCount;
        [SerializeField] private bool simulationRunning;

        private readonly List<SocialForceAgent> _agents = new();
        private int _nextId = 1;

        private void Start()
        {
            if (metricsLogger != null)
                metricsLogger.totalAgents = agentCount;
            
            SpawnAll();
        }

        private void SpawnAll()
        {
            for (var i = 0; i < agentCount; i++)
            {
                Vector3 pos;
                var attempts = 0;
                do
                {
                    pos = new Vector3(
                        Random.Range(spawnMinX, spawnMaxX),
                        1f,
                        Random.Range(spawnMinZ, spawnMaxZ));
                    attempts++;
                } while (!IsSpawnClear(pos, 0.45f) && attempts < 200);

                if (attempts >= 200)
                {
                    Debug.LogWarning($"[SFM] Could not find clear spawn for agent {i}");
                    continue;
                }

                SpawnAgent(pos);
            }

            simulationRunning = true;

            if (metricsLogger != null)
                metricsLogger.RegisterAgents();
        }
        
        private bool IsSpawnClear(Vector3 pos, float minDist)
        {
            foreach (var agent in _agents)
            {
                if (agent == null) continue;
                if (Vector3.Distance(agent.transform.position, pos) < minDist)
                    return false;
            }
            return true;
        }

        private void SpawnAgent(Vector3 pos)
        {
            var go = Instantiate(sfmAgentPrefab, pos, Quaternion.identity, transform);
            go.name = $"SFM_Agent_{_nextId}";

            var agent = go.GetComponent<SocialForceAgent>();
            agent.goal = goal;
            agent.desiredSpeed = SampleGaussian(meanDesiredSpeed, stdDesiredSpeed, minDesiredSpeed, maxDesiredSpeed);
            agent.radius = SampleGaussian(meanRadius, stdRadius, 0.15f, 0.25f);

            var capsule = go.GetComponent<CapsuleCollider>();
            if (capsule != null) capsule.radius = agent.radius;

            var ma = go.GetComponent<MetricsAgent>();
            if (ma != null)
            {
                ma.agentId = _nextId;
                if (metricsLogger != null)
                    ma.RegisterLogger(metricsLogger);
            }

            _agents.Add(agent);
            _nextId++;
            spawnedCount++;
        }

        private void Update()
        {
            if (!simulationRunning) return;

            if (Input.GetKeyDown(KeyCode.E))
            {
                Debug.Log($"[SFM] Evacuated: {evacuatedCount} / {agentCount}");
                metricsLogger?.ForceExport();
            }
        }

        private static float SampleGaussian(float mean, float std, float min, float max)
        {
            var u1 = Random.value;
            var u2 = Random.value;
            var z = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(u1, 1e-6f))) * Mathf.Cos(2f * Mathf.PI * u2);
            return Mathf.Clamp(mean + std * z, min, max);
        }
    }
}