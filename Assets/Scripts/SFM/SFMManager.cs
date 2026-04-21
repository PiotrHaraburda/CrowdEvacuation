using System;
using System.Collections.Generic;
using System.IO;
using Ghost;
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

        [Header("Empirical data")]
        public string dataFolderPath = "";
        public string fileName = "";
        public Vector2 dataOffset = Vector2.zero;

        [Header("Spawn streams")]
        public SpawnStream[] streams;

        [Serializable]
        public class SpawnStream
        {
            public string name;
            public Transform goal;
            public float filterMinX, filterMaxX;
            public float filterMinZ, filterMaxZ;
        }

        private struct PendingSpawn
        {
            public int AgentId;
            public float Time;
            public Vector3 Position;
            public Vector3 InitialVelocity;
            public Transform Goal;
        }

        [Header("Replication")]
        public int runSeed;

        private readonly List<SocialForceAgent> _agents = new();
        private readonly List<PendingSpawn> _pending = new();
        private float _simStart;

        private void Start()
        {
            _simStart = Time.time;
            if (runSeed != 0) 
                Random.InitState(runSeed);
            LoadEmpiricalSpawns();
        }

        private void LoadEmpiricalSpawns()
        {
            var filePath = Path.Combine(dataFolderPath, fileName);
            if (!File.Exists(filePath))
            {
                return;
            }

            var json = File.ReadAllText(filePath);
            var wrapped = "{\"agents\":" + json + "}";
            var episode = JsonUtility.FromJson<EpisodeData>(wrapped);

            if (episode?.agents == null)
            {
                return;
            }

            var unassigned = 0;
            foreach (var agent in episode.agents)
            {
                if (agent.x == null || agent.x.Length == 0 || agent.t == null || agent.t.Length == 0)
                    continue;

                var x0 = agent.x[0] - dataOffset.x;
                var z0 = agent.y[0] - dataOffset.y;

                var initialVelocity = Vector3.zero;
                if (agent.x.Length > 1 && agent.t.Length > 1)
                {
                    var dt = agent.t[1] - agent.t[0];
                    if (dt > 0.0001f)
                    {
                        var vx = ((agent.x[1] - dataOffset.x) - x0) / dt;
                        var vz = ((agent.y[1] - dataOffset.y) - z0) / dt;
                        initialVelocity = new Vector3(vx, 0f, vz);
                    }
                }

                var matched = false;
                foreach (var stream in streams)
                {
                    if (x0 < stream.filterMinX || x0 > stream.filterMaxX) continue;
                    if (z0 < stream.filterMinZ || z0 > stream.filterMaxZ) continue;

                    _pending.Add(new PendingSpawn
                    {
                        AgentId = agent.id,
                        Time = agent.t[0],
                        Position = new Vector3(x0, 1f, z0),
                        InitialVelocity = initialVelocity,
                        Goal = stream.goal
                    });
                    matched = true;
                    break;
                }

                if (!matched) unassigned++;
            }

            _pending.Sort((a, b) => a.Time.CompareTo(b.Time));

            metricsLogger.totalAgents = _pending.Count;

            if (unassigned > 0)
                Debug.LogWarning($"[SFM] {unassigned} empirical agents unmatched to any spawn stream");
        }

        private void FixedUpdate()
        {
            var elapsed = Time.time - _simStart;
            while (_pending.Count > 0 && _pending[0].Time <= elapsed)
            {
                SpawnAgent(_pending[0]);
                _pending.RemoveAt(0);
            }
        }

        private void SpawnAgent(PendingSpawn spawn)
        {
            var go = Instantiate(sfmAgentPrefab, spawn.Position, Quaternion.identity, transform);
            go.name = $"SFM_Agent_{spawn.AgentId}";

            var agent = go.GetComponent<SocialForceAgent>();
            agent.goal = spawn.Goal;
            agent.DesiredSpeed = AgentConfig.SampleDesiredSpeed();
            agent.Radius = AgentConfig.SampleRadius();
            agent.SetInitialVelocity(spawn.InitialVelocity);

            var d = agent.Radius * 2f;
            go.transform.localScale = new Vector3(d, go.transform.localScale.y, d);
            go.GetComponent<CapsuleCollider>().isTrigger = true;

            var ma = go.GetComponent<MetricsAgent>();
            ma.agentId = spawn.AgentId;
            ma.RegisterLogger(metricsLogger);
            metricsLogger.RegisterAgent(ma);

            _agents.Add(agent);
        }
    }
}