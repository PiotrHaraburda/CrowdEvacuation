using System;
using System.Collections;
using System.Collections.Generic;
using Metrics;
using UnityEngine;
using Utility;
using Random = UnityEngine.Random;

namespace RL
{
    public class RLManager : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject agentPrefab;

        [Header("References")]
        public EvacuationMetricsLogger metricsLogger;

        [Header("Spawn streams")]
        public SpawnStream[] streams;

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

        private readonly List<GameObject> _spawnedAgents = new();
        private int _nextId = 1;

        private void Start()
        {
            var total = 0;
            foreach (var s in streams)
            {
                total += s.agentCount;
            }
            metricsLogger.totalAgents = total;

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
                SpawnOne(stream);

            metricsLogger.RegisterAgents();
        }

        private IEnumerator SpawnGradually(SpawnStream stream)
        {
            var delay = 1f / stream.spawnRate;
            for (var i = 0; i < stream.agentCount; i++)
            {
                SpawnOne(stream);
                yield return new WaitForSeconds(delay);
            }
        }

        private void SpawnOne(SpawnStream stream)
        {
            var pos = FindSpawnPosition(stream);
            if (!pos.HasValue) return;

            var dirToGoal = (stream.goal.position - pos.Value).normalized;
            dirToGoal.y = 0;
            var rotation = Quaternion.LookRotation(dirToGoal);
            var go = Instantiate(agentPrefab, pos.Value, rotation, transform);
            go.name = $"RL_Agent_{_nextId}";
            go.layer = LayerMask.NameToLayer("Agent");

            var agent = go.GetComponent<RLAgent>();
            agent.goal = stream.goal;
            agent.Radius = AgentConfig.SampleRadius();
            agent.MaxAgentSpeed = AgentConfig.SampleDesiredSpeed() * AgentConfig.VelocityClamp;

            var d = agent.Radius * 2f;
            go.transform.localScale = new Vector3(d, go.transform.localScale.y, d);
            go.GetComponent<CapsuleCollider>().isTrigger = true;

            var ma = go.GetComponent<MetricsAgent>();
            ma.agentId = _nextId;
            ma.RegisterLogger(metricsLogger);
            metricsLogger.RegisterAgent(ma);

            _spawnedAgents.Add(go);
            _nextId++;
        }

        private Vector3? FindSpawnPosition(SpawnStream stream)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var pos = new Vector3(
                    Random.Range(stream.minX, stream.maxX), 1f,
                    Random.Range(stream.minZ, stream.maxZ));

                var clear = true;
                foreach (var go in _spawnedAgents)
                {
                    if (!go || !go.activeSelf) continue;
                    if (Vector3.Distance(go.transform.position, pos) < stream.minSpawnDist)
                    {
                        clear = false;
                        break;
                    }
                }

                if (clear)
                    return pos;
            }

            return null;
        }
    }
}


