using System;
using System.Collections.Generic;
using Metrics;
using RL;
using SFM;
using UnityEngine;
using Utility;
using Random = UnityEngine.Random;

namespace Building
{
    public enum AgentModelType { SFM, RL }

    public class BuildingEvacuationManager : MonoBehaviour
    {
        [Header("Agent setup")]
        public AgentModelType modelType = AgentModelType.RL;
        public GameObject sfmPrefab;
        public GameObject rlPrefab;

        [Header("References")]
        public EvacuationMetricsLogger metricsLogger;
        public Transform hazardPointsRoot;
        public Transform exitWaypoint;

        [Header("Hazard")]
        public GameObject hazardPrefab;
        public float hazardStartTime = 5f;
        public float hazardRelocateInterval = 8f;

        [Header("Timeout")]
        public float simulationTimeout = 200f;

        [Serializable]
        public class RoomConfig
        {
            public string roomId;
            public Transform spawnZone;
            public Transform[] waypointChain;
            public int agentCount = 4;
        }

        [Header("Rooms")]
        public RoomConfig[] rooms;

        private readonly List<GameObject> _spawnedAgents = new();
        private GameObject _spawnedHazard;
        private Transform _chosenHazardPoint;
        private float _simStart;
        private bool _hazardActivated;
        private bool _timedOut;
        private float _nextRelocateTime;
        private int _nextAgentId = 1;

        [Header("Replication")]
        public int runSeed;

        private void Start()
        {
            _simStart = Time.time;
            if (runSeed != 0) 
                Random.InitState(runSeed);
            SpawnAllAgents();
        }

        private void ChooseHazardPoint()
        {
            if (!hazardPointsRoot || hazardPointsRoot.childCount == 0)
                return;

            var idx = Random.Range(0, hazardPointsRoot.childCount);
            _chosenHazardPoint = hazardPointsRoot.GetChild(idx);
        }

        private void SpawnAllAgents()
        {
            foreach (var room in rooms)
            {
                for (var i = 0; i < room.agentCount; i++)
                    SpawnAgentInRoom(room);
            }

            metricsLogger.totalAgents = _spawnedAgents.Count;
        }

        private void SpawnAgentInRoom(RoomConfig room)
        {
            var pos = FindSpawnPositionInZone(room.spawnZone, 0.5f, 100);
            if (!pos.HasValue) 
                return;

            var firstWaypoint = room.waypointChain.Length > 0 ? room.waypointChain[0] : exitWaypoint;
            var dirToWp = (firstWaypoint.position - pos.Value).normalized;
            dirToWp.y = 0f;
            var rotation = dirToWp.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(dirToWp)
                : Quaternion.identity;

            var prefab = modelType == AgentModelType.SFM ? sfmPrefab : rlPrefab;
            var go = Instantiate(prefab, pos.Value, rotation, transform);
            go.name = $"{modelType}_Agent_{_nextAgentId}";
            go.layer = LayerMask.NameToLayer("Agent");

            ConfigureAgent(go, room, firstWaypoint);

            var ma = go.GetComponent<MetricsAgent>();
            ma.agentId = _nextAgentId;
            ma.RegisterLogger(metricsLogger);
            metricsLogger.RegisterAgent(ma);

            _spawnedAgents.Add(go);
            _nextAgentId++;
        }

        private void ConfigureAgent(GameObject go, RoomConfig room, Transform firstWaypoint)
        {
            var waypointFollower = go.GetComponent<WaypointFollower>() ?? go.AddComponent<WaypointFollower>();
            waypointFollower.waypoints = new List<Transform>(room.waypointChain) { exitWaypoint }.ToArray();
            waypointFollower.reachRadius = 0.8f;

            var radius = AgentConfig.SampleRadius();
            var desiredSpeed = AgentConfig.SampleDesiredSpeed();
            var d = radius * 2f;
            go.transform.localScale = new Vector3(d, go.transform.localScale.y, d);
            go.GetComponent<CapsuleCollider>().isTrigger = true;

            if (modelType == AgentModelType.SFM)
            {
                var sfm = go.GetComponent<SocialForceAgent>();
                sfm.DesiredSpeed = desiredSpeed;
                sfm.Radius = radius;
                sfm.goal = firstWaypoint;
                waypointFollower.onWaypointChanged += (wp) => sfm.goal = wp;
            }
            else
            {
                var rl = go.GetComponent<RLAgent>();
                rl.MaxAgentSpeed = desiredSpeed * AgentConfig.VelocityClamp;
                rl.Radius = radius;
                rl.goal = firstWaypoint;
                waypointFollower.onWaypointChanged += (wp) => rl.goal = wp;
            }
        }

        private Vector3? FindSpawnPositionInZone(Transform zone, float minDist, int maxAttempts)
        {
            var halfX = zone.localScale.x / 2f - 0.3f;
            var halfZ = zone.localScale.z / 2f - 0.3f;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var local = new Vector3(
                    Random.Range(-halfX, halfX), 0f,
                    Random.Range(-halfZ, halfZ));
                var world = zone.position + local;

                if (IsSpawnClear(world, minDist))
                    return world;
            }
            return null;
        }

        private bool IsSpawnClear(Vector3 pos, float minDist)
        {
            foreach (var go in _spawnedAgents)
            {
                if (!go) 
                    continue;
                if (Vector3.Distance(go.transform.position, pos) < minDist)
                    return false;
            }
            return true;
        }

        private void Update()
        {
            var elapsed = Time.time - _simStart;

            if (!_timedOut && elapsed >= simulationTimeout)
            {
                _timedOut = true;
                Debug.LogWarning($"[Building] Timeout after {simulationTimeout}s — evacuated {metricsLogger.evacuatedAgents}/{metricsLogger.totalAgents}");
                metricsLogger.ForceExport();
            }

            if (!_hazardActivated && elapsed >= hazardStartTime && hazardPrefab)
            {
                SpawnHazard();
                _hazardActivated = true;
                _nextRelocateTime = Time.time + hazardRelocateInterval;
            }

            if (_hazardActivated && Time.time >= _nextRelocateTime)
            {
                RelocateHazard();
                _nextRelocateTime = Time.time + hazardRelocateInterval;
            }
        }

        private void SpawnHazard()
        {
            ChooseHazardPoint();
            if (!_chosenHazardPoint) 
                return;

            _spawnedHazard = Instantiate(hazardPrefab, RandomHazardPosition(), Quaternion.identity, transform);
            _spawnedHazard.name = "Hazard_Active";
            _spawnedHazard.tag = "Hazard";
            _spawnedHazard.layer = LayerMask.NameToLayer("Hazard");
            _spawnedHazard.transform.localScale = RandomHazardScale();

            SocialForceAgent.InvalidateWallCache();
        }

        private void RelocateHazard()
        {
            if (!_spawnedHazard) 
                return;
            var previous = _chosenHazardPoint;
            for (var i = 0; i < 5 && _chosenHazardPoint == previous; i++)
                ChooseHazardPoint();
            if (!_chosenHazardPoint) 
                return;
            _spawnedHazard.transform.position = RandomHazardPosition();
            _spawnedHazard.transform.localScale = RandomHazardScale();
        }

        private Vector3 RandomHazardPosition()
        {
            var p = _chosenHazardPoint.position;
            p.x += Random.Range(-3f, 3f);
            p.z += Random.Range(-2f, 0f);
            return p;
        }

        private Vector3 RandomHazardScale()
        {
            return new Vector3(Random.Range(1f, 2f), 2f, Random.Range(1f, 2f));
        }
    }
}