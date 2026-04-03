using System.Collections.Generic;
using System.Linq;
using Metrics;
using Unity.MLAgents;
using UnityEngine;
using Utility;

namespace RL
{
    public class TrainingEnvironmentManager : MonoBehaviour
    {
        [Header("Prefabs")] 
        public GameObject agentPrefab;
        public GameObject obstaclePrefab;
        public GameObject hazardPrefab;

        [Header("Arena geometry")] 
        public float minArenaSize = 6f;
        public float arenaScalePerAgent = 3f;
        public float wallHeight = 2f;
        public float wallThickness = 0.2f;

        [Header("Exit")]
        public float exitDepthBehind = 3f;

        [Header("Goal")] 
        public Transform goal;

        [Header("Metrics")] 
        public EvacuationMetricsLogger metricsLogger;

        [Header("Defaults (overridden by curriculum)")]
        public int defaultNumAgents = 1;
        public int defaultNumObstacles;
        public float defaultCorridorWidth = 4f;
        public bool defaultThreatActive;

        private readonly List<GameObject> _spawnedAgents = new();
        private readonly List<GameObject> _spawnedObstacles = new();
        private GameObject _spawnedHazard;
        private readonly List<GameObject> _walls = new();
        private GameObject _exitZone;

        private float _arenaWidth;
        private float _arenaDepth;

        private int _numAgents;
        private int _numObstacles;
        private float _corridorWidth;
        private bool _threatActive;

        private bool _hazardSpawned;
        private float _hazardDespawnTime;
        private int _nextAgentId = 1;

        private int _agentsEvacuated;
        private int _agentsTimedOut;
        private bool _pendingFullReset;

        private float _nextCurriculumCheck;
        

        private void Start()
        {
            _arenaWidth = minArenaSize;
            _arenaDepth = minArenaSize;
            CreateExitZone();
            ResetEnvironment();
        }

        private void Update()
        {
            if (_pendingFullReset)
            {
                _pendingFullReset = false;
                PerformFullReset();
            }

            if (Time.time > _nextCurriculumCheck)
            {
                _nextCurriculumCheck = Time.time + 30f;
                var envParams = Academy.Instance.EnvironmentParameters;
                var newNumAgents = Mathf.RoundToInt(
                    envParams.GetWithDefault("num_agents", defaultNumAgents));
                var newThreat = envParams.GetWithDefault("threat_active", 0f) > 0.5f;

                if (Mathf.Abs(newNumAgents - _numAgents) > 3 || newThreat != _threatActive)
                {
                    _pendingFullReset = true;
                }
            }

            if (_threatActive && _hazardSpawned && _spawnedHazard && Time.time > _hazardDespawnTime)
            {
                Destroy(_spawnedHazard);
                _spawnedHazard = null;
                _hazardSpawned = false;
                SpawnHazard();
            }
        }

        public void OnAgentEpisodeBegin(GameObject agentGo)
        {
            ResetAgentPosition(agentGo);
        }

        public void OnAgentEvacuated(GameObject agentGo)
        {
            agentGo.SetActive(false);
            _agentsEvacuated++;
            CheckAllDone();
        }

        public void OnAgentTimedOut(GameObject agentGo)
        {
            _agentsTimedOut++;
            CheckAllDone();
        }

        private void CheckAllDone()
        {
            if (_agentsEvacuated + _agentsTimedOut >= _numAgents)
                _pendingFullReset = true;
        }

        private void PerformFullReset()
        {
            var agentsCopy = new List<GameObject>(_spawnedAgents);
            foreach (var go in agentsCopy)
            {
                if (!go)
                {
                    continue;
                }
                go.SetActive(true);
                var agent = go.GetComponent<RLAgent>();
                if (agent)
                {
                    agent.EndEpisode();
                }
            }

            ResetEnvironment();
        }

        private void ResetEnvironment()
        {
            ReadCurriculumParameters();
            ComputeArenaSize();
            ClearSpawned();
            RebuildWalls();
            RebuildExit();
            MetricsAgent.ResetExitCache();
            SpawnObstacles();
            if (_threatActive)
            {
                SpawnHazard();
            }
            SpawnAgents();
            SetupMetrics();

            _agentsEvacuated = 0;
            _agentsTimedOut = 0;
            _pendingFullReset = false;
        }

        private void ReadCurriculumParameters()
        {
            var envParams = Academy.Instance.EnvironmentParameters;
            _numAgents = Mathf.RoundToInt(envParams.GetWithDefault("num_agents", defaultNumAgents));
            _numObstacles = Mathf.RoundToInt(envParams.GetWithDefault("num_obstacles", defaultNumObstacles));
            _corridorWidth = envParams.GetWithDefault("corridor_width", defaultCorridorWidth);
            _threatActive = envParams.GetWithDefault("threat_active", defaultThreatActive ? 1f : 0f) > 0.5f;
        }

        private void ComputeArenaSize()
        {
            var computed = Mathf.Sqrt(_numAgents) * arenaScalePerAgent;
            _arenaWidth = Mathf.Max(minArenaSize, computed);
            _arenaDepth = _arenaWidth;
        }

        private void RebuildWalls()
        {
            ClearWalls();

            var halfW = _arenaWidth / 2f;
            var halfD = _arenaDepth / 2f;
            var cy = wallHeight / 2f;

            CreateWall(new Vector3(0, cy, halfD),
                new Vector3(_arenaWidth, wallHeight, wallThickness), "Wall_N");
            CreateWall(new Vector3(halfW, cy, 0),
                new Vector3(wallThickness, wallHeight, _arenaDepth), "Wall_E");
            CreateWall(new Vector3(-halfW, cy, 0),
                new Vector3(wallThickness, wallHeight, _arenaDepth), "Wall_W");
        }

        private void RebuildExit()
        {
            _walls.RemoveAll(w =>
            {
                if (!w || !w.name.StartsWith("Wall_S")) return false;
                Destroy(w);
                return true;
            });

            var halfW = _arenaWidth / 2f;
            var halfD = _arenaDepth / 2f;
            var cy = wallHeight / 2f;
            var halfDoor = _corridorWidth / 2f;

            var leftWidth = halfW - halfDoor;
            if (leftWidth > 0.01f)
            {
                var leftCenter = -halfW + leftWidth / 2f;
                CreateWall(new Vector3(leftCenter, cy, -halfD),
                    new Vector3(leftWidth, wallHeight, wallThickness), "Wall_S_Left");
            }

            var rightWidth = halfW - halfDoor;
            if (rightWidth > 0.01f)
            {
                var rightCenter = halfW - rightWidth / 2f;
                CreateWall(new Vector3(rightCenter, cy, -halfD),
                    new Vector3(rightWidth, wallHeight, wallThickness), "Wall_S_Right");
            }

            if (_exitZone)
            {
                _exitZone.transform.localPosition = new Vector3(0, 1f, -halfD - 0.5f);
                _exitZone.transform.localScale = new Vector3(_corridorWidth, 2f, 1f);
            }

            if (goal)
                goal.localPosition = new Vector3(0, 0, -halfD - exitDepthBehind);
        }

        private void CreateWall(Vector3 localPos, Vector3 scale, string wallName)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = wallName;
            wall.transform.SetParent(transform);
            wall.transform.localPosition = localPos;
            wall.transform.localScale = scale;
            wall.tag = "Wall";
            wall.layer = LayerMask.NameToLayer("Wall");

            var renderer = wall.GetComponent<Renderer>();
            if (renderer)
                renderer.material.color = new Color(0.4f, 0.4f, 0.4f);

            _walls.Add(wall);
        }

        private void CreateExitZone()
        {
            _exitZone = new GameObject("ExitZone");
            _exitZone.transform.SetParent(transform);
            _exitZone.tag = "Exit";

            var col = _exitZone.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = Vector3.one;
        }

        private void ClearWalls()
        {
            foreach (var w in _walls.Where(w => w))
            {
                Destroy(w);
            }

            _walls.Clear();
        }

        private void SpawnAgents()
        {
            var halfW = _arenaWidth / 2f - 0.5f;
            const float spawnMinZ = 0f;
            var spawnMaxZ = _arenaDepth / 2f - 0.5f;

            _nextAgentId = 1;

            for (var i = 0; i < _numAgents; i++)
            {
                Vector3 localPos;
                var attempts = 0;
                do
                {
                    localPos = new Vector3(
                        Random.Range(-halfW, halfW),
                        1f,
                        Random.Range(spawnMinZ, spawnMaxZ)
                    );
                    attempts++;
                } while (!IsSpawnClear(transform.TransformPoint(localPos), 0.5f)
                         && attempts < 200);

                if (attempts >= 200)
                {
                    Debug.LogWarning($"[Training] Could not find spawn for agent {i}");
                    continue;
                }

                var worldPos = transform.TransformPoint(localPos);
                var go = Instantiate(agentPrefab, worldPos,
                    Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
                go.name = $"RL_Agent_{_nextAgentId}";
                go.layer = LayerMask.NameToLayer("Agent");

                var agent = go.GetComponent<RLAgent>();
                if (agent)
                {
                    agent.goal = goal;
                    agent.trainingManager = this;
                    agent.radius = AgentConfig.SampleRadius();
                }

                var capsule = go.GetComponent<CapsuleCollider>();
                if (capsule)
                    capsule.radius = agent.radius;

                var ma = go.GetComponent<MetricsAgent>();
                if (ma)
                {
                    ma.agentId = _nextAgentId;
                    if (metricsLogger)
                        ma.RegisterLogger(metricsLogger);
                }

                _spawnedAgents.Add(go);
                _nextAgentId++;
            }
        }

        private bool IsSpawnClear(Vector3 worldPos, float minDist)
        {
            foreach (var go in _spawnedAgents)
            {
                if (!go || !go.activeSelf) continue;
                if (Vector3.Distance(go.transform.position, worldPos) < minDist)
                    return false;
            }

            foreach (var go in _spawnedObstacles)
            {
                if (!go) continue;
                if (Vector3.Distance(go.transform.position, worldPos) < minDist + 0.5f)
                    return false;
            }

            return true;
        }

        private void SpawnObstacles()
        {
            var halfW = _arenaWidth / 2f - 1f;
            var halfD = _arenaDepth / 2f - 1f;

            for (var i = 0; i < _numObstacles; i++)
            {
                Vector3 localPos;
                var attempts = 0;
                do
                {
                    localPos = new Vector3(
                        Random.Range(-halfW, halfW),
                        1f,
                        Random.Range(-halfD + 1f, halfD)
                    );
                    attempts++;
                } while (!IsObstacleClear(transform.TransformPoint(localPos), 1.5f)
                         && attempts < 100);

                if (attempts >= 100) continue;

                var worldPos = transform.TransformPoint(localPos);
                var obs = Instantiate(obstaclePrefab, worldPos, Quaternion.identity, transform);
                obs.name = $"Obstacle_{i}";
                obs.tag = "Wall";
                obs.layer = LayerMask.NameToLayer("Wall");
                _spawnedObstacles.Add(obs);
            }
        }

        private bool IsObstacleClear(Vector3 worldPos, float minDist)
        {
            foreach (var go in _spawnedObstacles)
            {
                if (!go) continue;
                if (Vector3.Distance(go.transform.position, worldPos) < minDist)
                    return false;
            }

            var localPos = transform.InverseTransformPoint(worldPos);
            var halfDoor = _corridorWidth / 2f;
            var halfD = _arenaDepth / 2f;
            if (Mathf.Abs(localPos.x) < halfDoor + 0.5f && localPos.z < -halfD + 2f)
                return false;

            return true;
        }

        private void SpawnHazard()
        {
            if (!hazardPrefab)
            {
                return;
            }

            var halfW = _arenaWidth / 2f - 2f;
            var halfD = _arenaDepth / 2f - 2f;
            var hazardSize = _arenaWidth * 0.2f;

            Vector3 localPos;
            var attempts = 0;
            do
            {
                localPos = new Vector3(
                    Random.Range(-halfW, halfW),
                    1f,
                    Random.Range(-halfD + 2f, 0f));
                attempts++;
            } while (!IsHazardClear(transform.TransformPoint(localPos), hazardSize)
                     && attempts < 50);

            if (attempts >= 50)
            {
                return;
            }

            _spawnedHazard = Instantiate(
                hazardPrefab, transform.TransformPoint(localPos), Quaternion.identity, transform
            );
            _spawnedHazard.name = "Hazard";
            _spawnedHazard.tag = "Hazard";
            _spawnedHazard.transform.localScale = new Vector3(hazardSize, 2f, hazardSize);
            _hazardSpawned = true;
            _hazardDespawnTime = Time.time + Random.Range(15f, 30f);
        }

        private bool IsHazardClear(Vector3 worldPos, float minDist)
        {
            foreach (var go in _spawnedAgents)
            {
                if (!go || !go.activeSelf) continue;
                if (Vector3.Distance(go.transform.position, worldPos) < minDist)
                    return false;
            }

            var localPos = transform.InverseTransformPoint(worldPos);
            var halfDoor = _corridorWidth / 2f;
            var halfD = _arenaDepth / 2f;
            if (Mathf.Abs(localPos.x) < halfDoor + 1f && localPos.z < -halfD + 3f)
                return false;

            return true;
        }

        private void SetupMetrics()
        {
            if (!metricsLogger)
            {
                return;
            }
            metricsLogger.totalAgents = _numAgents;
            metricsLogger.RegisterAgents();
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawnedAgents.Where(go => go))
            {
                Destroy(go);
            }
            _spawnedAgents.Clear();

            foreach (var go in _spawnedObstacles.Where(go => go))
            {
                Destroy(go);
            }
            _spawnedObstacles.Clear();

            if (_spawnedHazard)
            {
                Destroy(_spawnedHazard);
                _spawnedHazard = null;
            }
        }

        private void ResetAgentPosition(GameObject agentGo)
        {
            var halfW = _arenaWidth / 2f - 0.5f;
            var spawnMaxZ = _arenaDepth / 2f - 0.5f;

            Vector3 localPos;
            var attempts = 0;
            do
            {
                localPos = new Vector3(
                    Random.Range(-halfW, halfW),
                    1f,
                    Random.Range(0f, spawnMaxZ));
                attempts++;
            } while (!IsSpawnClear(transform.TransformPoint(localPos), 0.5f) && attempts < 200);

            agentGo.transform.position = transform.TransformPoint(localPos);
            agentGo.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            agentGo.SetActive(true);

            var ma = agentGo.GetComponent<MetricsAgent>();
            if (ma)
            {
                ma.ResetState();
                if (metricsLogger)
                    ma.RegisterLogger(metricsLogger);
            }
        }
    }
}