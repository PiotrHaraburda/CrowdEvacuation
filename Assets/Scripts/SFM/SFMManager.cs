using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Metrics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SFM
{
    public class SFMManager : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject sfmAgentPrefab;

        [Header("References")]
        public EvacuationMetricsLogger metricsLogger;

        [Header("Agent variation")]
        public float meanDesiredSpeed = 1.34f; // (Weidmann 1993)
        public float stdDesiredSpeed = 0.26f; // (Weidmann 1993)
        public float minDesiredSpeed = 0.5f;
        public float maxDesiredSpeed = 2.0f;
        public float meanRadius = 0.2f;
        public float stdRadius = 0.02f;

        [Header("Spawn streams")]
        public SpawnStream[] streams;

        private readonly List<SocialForceAgent> _agents = new();
        private int _nextId = 1;

        [Serializable]
        public class SpawnStream
        {
            public string name;
            public Transform goal;
            public int agentCount;
            public float spawnRate;
            public float minX, maxX;
            public float minZ, maxZ;
            public float minSpawnDist = 0.41f;
        }

        private void Start()
        {
            var totalAgents = streams.Sum(stream => stream.agentCount);

            if (metricsLogger != null)
                metricsLogger.totalAgents = totalAgents;

            foreach (var stream in streams)
            {
                if (stream.spawnRate <= 0f)
                    SpawnAllInStream(stream);
                else
                    StartCoroutine(SpawnGradually(stream));
            }
        }

        private void SpawnAllInStream(SpawnStream stream)
        {
            for (var i = 0; i < stream.agentCount; i++)
            {
                var pos = FindSpawnPosition(stream);
                if (pos.HasValue)
                    SpawnAgent(pos.Value, stream.goal);
            }

            if (metricsLogger != null)
                metricsLogger.RegisterAgents();
        }

        private IEnumerator SpawnGradually(SpawnStream stream)
        {
            var delay = 1f / stream.spawnRate;
            for (var i = 0; i < stream.agentCount; i++)
            {
                var pos = FindSpawnPosition(stream);
                if (pos.HasValue)
                    SpawnAgent(pos.Value, stream.goal);

                yield return new WaitForSeconds(delay);
            }
        }
        
        private Vector3? FindSpawnPosition(SpawnStream stream)
        {
            var attempts = 0;
            Vector3 pos;
            do
            {
                pos = new Vector3(
                    Random.Range(stream.minX, stream.maxX),
                    1f,
                    Random.Range(stream.minZ, stream.maxZ)
                );
                attempts++;
            } while (!IsSpawnClear(pos, stream.minSpawnDist) && attempts < 200);

            if (attempts >= 200)
            {
                Debug.LogWarning($"[SFM] Could not find clear spawn in stream '{stream.name}'");
                return null;
            }

            return pos;
        }
        
        private bool IsSpawnClear(Vector3 pos, float minDist)
        {
            foreach (var agent in _agents)
            {
                if (!agent)
                {
                    continue;
                }
                if (Vector3.Distance(agent.transform.position, pos) < minDist)
                    return false;
            }
            return true;
        }

        private void SpawnAgent(Vector3 pos, Transform agentGoal)
        {
            var go = Instantiate(sfmAgentPrefab, pos, Quaternion.identity, transform);
            go.name = $"SFM_Agent_{_nextId}";

            var agent = go.GetComponent<SocialForceAgent>();
            agent.goal = agentGoal;
            agent.desiredSpeed = SampleGaussian(meanDesiredSpeed, stdDesiredSpeed, minDesiredSpeed, maxDesiredSpeed);
            agent.radius = SampleGaussian(meanRadius, stdRadius, 0.15f, 0.25f);

            var capsule = go.GetComponent<CapsuleCollider>();
            if (capsule)
            {
                capsule.radius = agent.radius;
            }

            var ma = go.GetComponent<MetricsAgent>();
            if (ma)
            {
                ma.agentId = _nextId;
                if (metricsLogger)
                    ma.RegisterLogger(metricsLogger);
            }

            _agents.Add(agent);
            _nextId++;
            metricsLogger.RegisterAgents();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                var logger = FindObjectOfType<EvacuationMetricsLogger>();
                Debug.Log($"Evacuated: {logger?.evacuatedAgents} / {logger?.totalAgents}");
                logger?.ForceExport();
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