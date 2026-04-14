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

        [Header("Arena")]
        public float minArenaSize = 6f;
        public float arenaScalePerAgent = 3f;
        public float wallHeight = 2f;
        public float wallThickness = 0.2f;
        public float exitDepthBehind = 3f;

        [Header("Goals")]
        public Transform goalPrimary;
        public Transform goalSecondary;

        [Header("Defaults (overridden by curriculum)")]
        public int defaultNumAgents = 1;
        public float defaultCorridorWidth = 4f;
        public bool defaultThreatActive;

        private readonly List<GameObject> _spawnedAgents = new();
        private readonly List<GameObject> _spawnedObstacles = new();
        private readonly List<GameObject> _walls = new();
        private GameObject _spawnedHazard;
        private GameObject _exitPrimary;
        private GameObject _exitSecondary;

        private float _arenaWidth;
        private float _arenaDepth;
        private int _numAgents;
        private int _numObstacles;
        private float _corridorWidth;
        private bool _threatActive;
        private bool _counterflowActive;

        private int _nextAgentId = 1;
        private int _agentsDone;
        private bool _pendingFullReset;
        private float _nextCurriculumCheck;
        private float _hazardDespawnTime;
        private float _hazardSpawnTime;

        private void Start()
        {
            _exitPrimary = CreateExitZone("ExitPrimary");
            _exitSecondary = CreateExitZone("ExitSecondary");
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
                    _pendingFullReset = true;
            }

            if (_threatActive && _spawnedHazard && Time.time > _hazardDespawnTime)
            {
                Destroy(_spawnedHazard);
                _spawnedHazard = null;
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
            _agentsDone++;
            if (_agentsDone >= _numAgents)
                _pendingFullReset = true;
        }

        public void OnAgentTimedOut(GameObject agentGo)
        {
            _agentsDone++;
            if (_agentsDone >= _numAgents)
                _pendingFullReset = true;
        }

        private void PerformFullReset()
        {
            foreach (var go in _spawnedAgents)
            {
                if (!go) continue;
                go.SetActive(true);
                go.GetComponent<RLAgent>()?.EndEpisode();
            }

            ResetEnvironment();
        }

        private void ResetEnvironment()
        {
            ReadCurriculumParameters();
            _counterflowActive = _numAgents >= 10 && Random.value > 0.5f;
            ComputeArenaSize();
            _numObstacles = _numAgents <= 1 ? 0 : Mathf.RoundToInt(_arenaWidth * _arenaDepth / 20f);

            ClearSpawned();
            ClearWalls();
            BuildWalls();
            BuildExits();
            MetricsAgent.ResetExitCache();
            SpawnObstacles();
            if (_threatActive) 
                SpawnHazard();
            SpawnAgents();

            _agentsDone = 0;
            _pendingFullReset = false;
        }

        private void ReadCurriculumParameters()
        {
            var envParams = Academy.Instance.EnvironmentParameters;
            _numAgents = Mathf.RoundToInt(envParams.GetWithDefault("num_agents", defaultNumAgents));
            _corridorWidth = envParams.GetWithDefault("corridor_width", defaultCorridorWidth);
            _threatActive = envParams.GetWithDefault("threat_active", defaultThreatActive ? 1f : 0f) > 0.5f;
        }

        private void ComputeArenaSize()
        {
            var baseSize = Mathf.Max(minArenaSize, Mathf.Sqrt(_numAgents) * arenaScalePerAgent);

            if (_counterflowActive)
            {
                _arenaWidth = baseSize;
                _arenaDepth = 4f;
            }
            else
            {
                _arenaWidth = baseSize;
                _arenaDepth = baseSize;
            }
        }

        private void BuildWalls()
        {
            var halfW = _arenaWidth / 2f;
            var halfD = _arenaDepth / 2f;
            var cy = wallHeight / 2f;

            CreateWall(new Vector3(0, cy, halfD), new Vector3(_arenaWidth, wallHeight, wallThickness), "Wall_N");

            if (_counterflowActive)
            {
                CreateWall(new Vector3(0, cy, -halfD), new Vector3(_arenaWidth, wallHeight, wallThickness), "Wall_S");
                BuildExitWall(new Vector3(-halfW, cy, 0), wallThickness, _arenaDepth, _corridorWidth, "Wall_W");
                BuildExitWall(new Vector3(halfW, cy, 0), wallThickness, _arenaDepth, _corridorWidth, "Wall_E");
            }
            else
            {
                CreateWall(new Vector3(halfW, cy, 0), new Vector3(wallThickness, wallHeight, _arenaDepth), "Wall_E");
                CreateWall(new Vector3(-halfW, cy, 0), new Vector3(wallThickness, wallHeight, _arenaDepth), "Wall_W");
                BuildExitWall(new Vector3(0, cy, -halfD), _arenaWidth, wallThickness, _corridorWidth, "Wall_S");
            }
        }

        private void BuildExitWall(Vector3 center, float totalWidth, float totalDepth, float doorWidth, string namePrefix)
        {
            var isVertical = totalDepth > totalWidth;
            var totalLength = isVertical ? totalDepth : totalWidth;
            var halfDoor = doorWidth / 2f;
            var sideLength = (totalLength - doorWidth) / 2f;

            if (sideLength <= 0.01f) 
                return;

            if (isVertical)
            {
                var offset = sideLength / 2f + halfDoor;
                CreateWall(center + new Vector3(0, 0, offset),
                    new Vector3(totalWidth, wallHeight, sideLength), namePrefix + "_Top");
                CreateWall(center + new Vector3(0, 0, -offset),
                    new Vector3(totalWidth, wallHeight, sideLength), namePrefix + "_Bot");
            }
            else
            {
                var offset = sideLength / 2f + halfDoor;
                CreateWall(center + new Vector3(-offset, 0, 0),
                    new Vector3(sideLength, wallHeight, totalDepth), namePrefix + "_Left");
                CreateWall(center + new Vector3(offset, 0, 0),
                    new Vector3(sideLength, wallHeight, totalDepth), namePrefix + "_Right");
            }
        }

        private void BuildExits()
        {
            var halfW = _arenaWidth / 2f;
            var halfD = _arenaDepth / 2f;

            if (_counterflowActive)
            {
                PositionExit(_exitPrimary, new Vector3(-halfW - 0.5f, 1f, 0), new Vector3(1f, 2f, _corridorWidth));
                PositionExit(_exitSecondary, new Vector3(halfW + 0.5f, 1f, 0), new Vector3(1f, 2f, _corridorWidth));
                goalPrimary.localPosition = new Vector3(-halfW - exitDepthBehind, 0, 0);
                goalSecondary.localPosition = new Vector3(halfW + exitDepthBehind, 0, 0);
            }
            else
            {
                PositionExit(_exitPrimary, new Vector3(0, 1f, -halfD - 0.5f), new Vector3(_corridorWidth, 2f, 1f));
                _exitSecondary.SetActive(false);
                goalPrimary.localPosition = new Vector3(0, 0, -halfD - exitDepthBehind);
            }
        }

        private void PositionExit(GameObject exitGo, Vector3 localPos, Vector3 scale)
        {
            exitGo.SetActive(true);
            exitGo.transform.localPosition = localPos;
            exitGo.transform.localScale = scale;
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
            wall.GetComponent<Renderer>().material.color = new Color(0.4f, 0.4f, 0.4f);
            _walls.Add(wall);
        }

        private GameObject CreateExitZone(string exitName)
        {
            var go = new GameObject(exitName);
            go.transform.SetParent(transform);
            go.tag = "Exit";
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = Vector3.one;
            return go;
        }

        private void ClearWalls()
        {
            foreach (var w in _walls.Where(w => w)) 
                Destroy(w);
            _walls.Clear();
        }

        private void SpawnAgents()
        {
            _nextAgentId = 1;
            var halfW = _arenaWidth / 2f - 0.5f;
            var halfD = _arenaDepth / 2f - 0.5f;

            for (var i = 0; i < _numAgents; i++)
            {
                var useSecondaryGoal = _counterflowActive && i % 2 == 1;
                var goalTransform = useSecondaryGoal ? goalSecondary : goalPrimary;

                var localPos = FindAgentSpawnPosition(halfW, halfD, useSecondaryGoal);
                if (!localPos.HasValue) 
                    continue;

                var worldPos = transform.TransformPoint(localPos.Value);
                var go = Instantiate(
                    agentPrefab, worldPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform
                );
                go.name = $"RL_Agent_{_nextAgentId}";
                go.layer = LayerMask.NameToLayer("Agent");

                var agent = go.GetComponent<RLAgent>();
                agent.goal = goalTransform;
                agent.trainingManager = this;
                agent.Radius = AgentConfig.SampleRadius();
                agent.MaxAgentSpeed = AgentConfig.SampleDesiredSpeed() * AgentConfig.VelocityClamp;

                var d = agent.Radius * 2f;
                go.transform.localScale = new Vector3(d, go.transform.localScale.y, d);
                go.GetComponent<CapsuleCollider>().isTrigger = true;
                go.GetComponent<MetricsAgent>().agentId = _nextAgentId;

                _spawnedAgents.Add(go);
                _nextAgentId++;
            }
        }

        private Vector3? FindAgentSpawnPosition(float halfW, float halfD, bool rightSide)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                Vector3 localPos;
                if (_counterflowActive)
                {
                    var x = rightSide
                        ? Random.Range(-halfW, -halfW * 0.5f)
                        : Random.Range(halfW * 0.5f, halfW);
                    localPos = new Vector3(x, 1f, Random.Range(-halfD, halfD));
                }
                else
                {
                    localPos = new Vector3(
                        Random.Range(-halfW, halfW), 1f,
                        Random.Range(0f, halfD));
                }

                if (IsSpawnClear(transform.TransformPoint(localPos), 0.5f))
                    return localPos;
            }

            return null;
        }

        private void ResetAgentPosition(GameObject agentGo)
        {
            var halfW = _arenaWidth / 2f - 0.5f;
            var halfD = _arenaDepth / 2f - 0.5f;
            var agent = agentGo.GetComponent<RLAgent>();
            var useSecondary = _counterflowActive && agent.goal == goalSecondary;

            var pos = FindAgentSpawnPosition(halfW, halfD, useSecondary);
            if (pos.HasValue)
                agentGo.transform.position = transform.TransformPoint(pos.Value);

            agentGo.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            agentGo.SetActive(true);
            agentGo.GetComponent<MetricsAgent>().ResetState();
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
            if (!obstaclePrefab || _counterflowActive)
                return;

            var halfW = _arenaWidth / 2f - 1f;
            var halfD = _arenaDepth / 2f - 1f;

            for (var i = 0; i < _numObstacles; i++)
            {
                Vector3? pos = null;
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    var candidate = new Vector3(
                        Random.Range(-halfW, halfW), 1f,
                        Random.Range(-halfD + 1f, halfD));
                    var worldCandidate = transform.TransformPoint(candidate);

                    if (IsObstacleClear(worldCandidate, 1.5f))
                    {
                        pos = candidate;
                        break;
                    }
                }

                if (!pos.HasValue) continue;

                var obs = Instantiate(
                    obstaclePrefab, transform.TransformPoint(pos.Value), Quaternion.identity, transform
                );
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
                if (!go) 
                    continue;
                if (Vector3.Distance(go.transform.position, worldPos) < minDist)
                    return false;
            }

            var localPos = transform.InverseTransformPoint(worldPos);
            var halfDoor = _corridorWidth / 2f;

            if (_counterflowActive)
            {
                var halfW = _arenaWidth / 2f;
                if (Mathf.Abs(localPos.z) < halfDoor + 0.5f && (localPos.x < -halfW + 2f || localPos.x > halfW - 2f))
                    return false;
            }
            else
            {
                var halfD = _arenaDepth / 2f;
                if (Mathf.Abs(localPos.x) < halfDoor + 0.5f && localPos.z < -halfD + 2f)
                    return false;
            }

            return true;
        }

        private void SpawnHazard()
        {
            if (!hazardPrefab)
                return;

            var maxSize = _counterflowActive ? _arenaDepth * 0.4f : 3f;
            var hazardSize = Mathf.Clamp(_arenaWidth * 0.15f, 1f, maxSize);
            var halfW = _arenaWidth / 2f - hazardSize;
            var halfD = _arenaDepth / 2f - hazardSize;

            for (var attempt = 0; attempt < 50; attempt++)
            {
                Vector3 localPos;
                if (_counterflowActive)
                {
                    localPos = new Vector3(
                        Random.Range(-halfW * 0.4f, halfW * 0.4f),
                        1f,
                        Random.Range(-halfD, halfD));
                }
                else
                {
                    localPos = new Vector3(
                        Random.Range(-halfW, halfW),
                        1f,
                        Random.Range(-halfD, 0f));
                }

                if (!IsSpawnClear(transform.TransformPoint(localPos), hazardSize)) continue;

                _spawnedHazard = Instantiate(
                    hazardPrefab, transform.TransformPoint(localPos), Quaternion.identity, transform
                );
                _spawnedHazard.name = "Hazard";
                _spawnedHazard.tag = "Hazard";
                _spawnedHazard.layer = LayerMask.NameToLayer("Hazard");
                _spawnedHazard.transform.localScale = new Vector3(hazardSize, 2f, hazardSize);
                _hazardDespawnTime = Time.time + Random.Range(5f, 15f);
                return;
            }
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawnedAgents.Where(go => go)) 
                Destroy(go);
            _spawnedAgents.Clear();

            foreach (var go in _spawnedObstacles.Where(go => go))
                Destroy(go);
            _spawnedObstacles.Clear();

            if (_spawnedHazard)
            {
                Destroy(_spawnedHazard);
                _spawnedHazard = null;
            }
        }
    }
}