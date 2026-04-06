using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Metrics;
using UnityEngine;
using Utility;
using Random = UnityEngine.Random;

namespace SFM
{
    public class SFMManager : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject sfmAgentPrefab;

        [Header("References")]
        public EvacuationMetricsLogger metricsLogger;

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
            public float minSpawnDist = 0.38f;
        }

        private void Start()
        {
            metricsLogger.totalAgents = streams.Sum(s => s.agentCount);

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
                if (!agent) continue;
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
            agent.DesiredSpeed = AgentConfig.SampleDesiredSpeed();
            agent.Radius = AgentConfig.SampleRadius();

            var d = agent.Radius * 2f;
            go.transform.localScale = new Vector3(d, go.transform.localScale.y, d);
            var capsule = go.GetComponent<CapsuleCollider>();
            capsule.isTrigger = true;

            var ma = go.GetComponent<MetricsAgent>();
            ma.agentId = _nextId;
            ma.RegisterLogger(metricsLogger);
            metricsLogger.RegisterAgent(ma);

            _agents.Add(agent);
            _nextId++;
        }
    }
}