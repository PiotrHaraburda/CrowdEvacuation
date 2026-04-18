using System.Collections.Generic;
using System.Linq;
using Metrics;
using Unity.MLAgents;
using UnityEngine;
using Utility;

namespace RL
{
    public enum ArenaMode { Normal, Counterflow, Crossing }

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
        public float corridorDepth = 4f;

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
        private ArenaMode _mode;

        private int _nextAgentId = 1;
        private int _agentsDone;
        private bool _pendingFullReset;
        private float _nextCurriculumCheck;
        private float _hazardDespawnTime;

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
            PickArenaMode();
            ComputeArenaSize();
            _numObstacles = _numAgents <= 1 || _mode != ArenaMode.Normal
                ? 0
                : Mathf.RoundToInt(_arenaWidth * _arenaDepth / 20f);

            ClearSpawned();
            ClearWalls();
            BuildWalls();
            BuildExits();
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

        private void PickArenaMode()
        {
            if (_numAgents < 10)
            {
                _mode = ArenaMode.Normal;
                return;
            }

            var roll = Random.value;
            if (roll < 0.2f)
                _mode = ArenaMode.Normal;
            else if (roll < 0.4f)
                _mode = ArenaMode.Counterflow;
            else
                _mode = ArenaMode.Crossing;
        }

        private void ComputeArenaSize()
        {
            var baseSize = Mathf.Max(minArenaSize, Mathf.Sqrt(_numAgents) * arenaScalePerAgent);

            switch (_mode)
            {
                case ArenaMode.Counterflow:
                    _arenaWidth = baseSize;
                    _arenaDepth = corridorDepth;
                    break;
                case ArenaMode.Crossing:
                    _arenaWidth = baseSize;
                    _arenaDepth = baseSize;
                    break;
                default:
                    _arenaWidth = baseSize;
                    _arenaDepth = baseSize;
                    break;
            }
        }

        private void BuildWalls()
        {
            var halfW = _arenaWidth / 2f;
            var halfD = _arenaDepth / 2f;
            var cy = wallHeight / 2f;
            var halfCor = corridorDepth / 2f;

            switch (_mode)
            {
                case ArenaMode.Normal:
                    CreateWall(new Vector3(0, cy, halfD), new Vector3(_arenaWidth, wallHeight, wallThickness), "Wall_N");
                    CreateWall(new Vector3(halfW, cy, 0), new Vector3(wallThickness, wallHeight, _arenaDepth), "Wall_E");
                    CreateWall(new Vector3(-halfW, cy, 0), new Vector3(wallThickness, wallHeight, _arenaDepth), "Wall_W");
                    BuildExitWall(new Vector3(0, cy, -halfD), _arenaWidth, wallThickness, _corridorWidth, "Wall_S");
                    break;

                case ArenaMode.Counterflow:
                    CreateWall(new Vector3(0, cy, halfCor), new Vector3(_arenaWidth, wallHeight, wallThickness), "Wall_N");
                    CreateWall(new Vector3(0, cy, -halfCor), new Vector3(_arenaWidth, wallHeight, wallThickness), "Wall_S");
                    BuildExitWall(new Vector3(-halfW, cy, 0), wallThickness, corridorDepth, _corridorWidth, "Wall_W");
                    BuildExitWall(new Vector3(halfW, cy, 0), wallThickness, corridorDepth, _corridorWidth, "Wall_E");
                    break;

                case ArenaMode.Crossing:
                    BuildCrossingWalls(halfW, halfD, halfCor, cy);
                    break;
            }
        }

        private void BuildCrossingWalls(float halfW, float halfD, float halfCor, float cy)
        {
            var blockW = halfW - halfCor;
            var blockD = halfD - halfCor;

            if (blockW > 0.1f && blockD > 0.1f)
            {
                CreateWall(new Vector3(-halfCor - blockW / 2f, cy, halfCor + blockD / 2f),
                    new Vector3(blockW, wallHeight, blockD), "Block_NW");
                CreateWall(new Vector3(halfCor + blockW / 2f, cy, halfCor + blockD / 2f),
                    new Vector3(blockW, wallHeight, blockD), "Block_NE");
                CreateWall(new Vector3(-halfCor - blockW / 2f, cy, -halfCor - blockD / 2f),
                    new Vector3(blockW, wallHeight, blockD), "Block_SW");
                CreateWall(new Vector3(halfCor + blockW / 2f, cy, -halfCor - blockD / 2f),
                    new Vector3(blockW, wallHeight, blockD), "Block_SE");
            }

            CreateWall(new Vector3(0, cy, halfD), new Vector3(_arenaWidth, wallHeight, wallThickness), "Wall_N");
            CreateWall(new Vector3(halfW, cy, 0), new Vector3(wallThickness, wallHeight, _arenaDepth), "Wall_E");

            BuildExitWall(new Vector3(0, cy, -halfD), corridorDepth, wallThickness, _corridorWidth, "Wall_S");

            BuildExitWall(new Vector3(-halfW, cy, 0), wallThickness, corridorDepth, _corridorWidth, "Wall_W");
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

            switch (_mode)
            {
                case ArenaMode.Normal:
                    PositionExit(_exitPrimary, new Vector3(0, 1f, -halfD - 0.5f), new Vector3(_corridorWidth, 2f, 1f));
                    _exitSecondary.SetActive(false);
                    goalPrimary.localPosition = new Vector3(0, 0, -halfD - exitDepthBehind);
                    break;

                case ArenaMode.Counterflow:
                    PositionExit(_exitPrimary, new Vector3(-halfW - 0.5f, 1f, 0), new Vector3(1f, 2f, _corridorWidth));
                    PositionExit(_exitSecondary, new Vector3(halfW + 0.5f, 1f, 0), new Vector3(1f, 2f, _corridorWidth));
                    goalPrimary.localPosition = new Vector3(-halfW - exitDepthBehind, 0, 0);
                    goalSecondary.localPosition = new Vector3(halfW + exitDepthBehind, 0, 0);
                    break;

                case ArenaMode.Crossing:
                    PositionExit(_exitPrimary, new Vector3(0, 1f, -halfD - 0.5f), new Vector3(_corridorWidth, 2f, 1f));
                    PositionExit(_exitSecondary, new Vector3(-halfW - 0.5f, 1f, 0), new Vector3(1f, 2f, _corridorWidth));
                    goalPrimary.localPosition = new Vector3(0, 0, -halfD - exitDepthBehind);
                    goalSecondary.localPosition = new Vector3(-halfW - exitDepthBehind, 0, 0);
                    break;
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
            var halfCor = corridorDepth / 2f - 0.5f;

            for (var i = 0; i < _numAgents; i++)
            {
                var useSecondary = _mode != ArenaMode.Normal && i % 2 == 1;
                var goalTransform = useSecondary ? goalSecondary : goalPrimary;

                var localPos = FindAgentSpawnPosition(halfW, halfD, halfCor, useSecondary);
                if (!localPos.HasValue) 
                    continue;

                var worldPos = transform.TransformPoint(localPos.Value);
                var dirToGoal = (goalTransform.position - worldPos).normalized;
                dirToGoal.y = 0;
                var rotation = dirToGoal.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(dirToGoal)
                    : Quaternion.identity;

                var go = Instantiate(agentPrefab, worldPos, rotation, transform);
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

        private Vector3? FindAgentSpawnPosition(float halfW, float halfD, float halfCor, bool useSecondary)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                Vector3 localPos;
                switch (_mode)
                {
                    case ArenaMode.Counterflow:
                        var cx = useSecondary
                            ? Random.Range(-halfW, -halfW * 0.5f)
                            : Random.Range(halfW * 0.5f, halfW);
                        localPos = new Vector3(cx, 1f, Random.Range(-halfCor, halfCor));
                        break;

                    case ArenaMode.Crossing:
                        if (useSecondary)
                            localPos = new Vector3(
                                Random.Range(halfW * 0.5f, halfW), 1f,
                                Random.Range(-halfCor, halfCor));
                        else
                            localPos = new Vector3(
                                Random.Range(-halfCor, halfCor), 1f,
                                Random.Range(halfD * 0.5f, halfD));
                        break;

                    default:
                        localPos = new Vector3(
                            Random.Range(-halfW, halfW), 1f,
                            Random.Range(0f, halfD));
                        break;
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
            var halfCor = corridorDepth / 2f - 0.5f;
            var agent = agentGo.GetComponent<RLAgent>();
            var useSecondary = _mode != ArenaMode.Normal && agent.goal == goalSecondary;

            var pos = FindAgentSpawnPosition(halfW, halfD, halfCor, useSecondary);
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
            if (!obstaclePrefab || _numObstacles == 0) 
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
            var halfD = _arenaDepth / 2f;
            if (Mathf.Abs(localPos.x) < halfDoor + 0.5f && localPos.z < -halfD + 2f)
                return false;

            return true;
        }

        private void SpawnHazard()
        {
            if (!hazardPrefab)
                return;

            var corHalf = corridorDepth / 2f;
            var maxSize = _mode != ArenaMode.Normal ? corHalf * 0.4f : 3f;
            var hazardSize = Mathf.Clamp(_arenaWidth * 0.15f, 1f, maxSize);
            var halfW = _arenaWidth / 2f - hazardSize;
            var halfD = _arenaDepth / 2f - hazardSize;

            for (var attempt = 0; attempt < 50; attempt++)
            {
                Vector3 localPos;
                switch (_mode)
                {
                    case ArenaMode.Counterflow:
                        localPos = new Vector3(
                            Random.Range(-halfW * 0.4f, halfW * 0.4f), 1f,
                            Random.Range(-corHalf + 1f, corHalf - 1f));
                        break;
                    case ArenaMode.Crossing:
                        if (Random.value > 0.5f)
                            localPos = new Vector3(
                                Random.Range(-corHalf + 0.5f, corHalf - 0.5f), 1f,
                                Random.Range(-halfD + 1f, halfD - 1f));
                        else
                            localPos = new Vector3(
                                Random.Range(-halfW + 1f, halfW - 1f), 1f,
                                Random.Range(-corHalf + 0.5f, corHalf - 0.5f));
                        break;
                    default:
                        localPos = new Vector3(
                            Random.Range(-halfW, halfW), 1f,
                            Random.Range(-halfD + 1f, halfD - 1f));
                        break;
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