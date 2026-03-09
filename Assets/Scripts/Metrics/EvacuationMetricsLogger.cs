using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Metrics
{
    public class EvacuationMetricsLogger : MonoBehaviour
    {
        [Header("Run Identity")] 
        public string modelType = "SFM";
        public string scenarioId = "NarrowDoor";
        public string runId = "run_001";
        public string outputDirectory = "Metrics";

        [Header("Geometry")] 
        public float exitWidthMeters = 0.69f;
        public Transform exitMeasurementPoint;
        public Transform exitMeasurementPoint2;

        [Header("Live (read-only)")] 
        [SerializeField]
        public int totalAgents;
        
        [SerializeField] public int evacuatedAgents;
        [SerializeField] private float elapsedTime;
        [SerializeField] private float currentSpecificFlow;
        [SerializeField] private float currentExitDensity;

        private float _simStart;
        private bool _running;

        private readonly Dictionary<int, float> _evacuationTimes = new();
        private readonly Dictionary<int, int> _wallCollisions = new();
        private readonly Dictionary<int, int> _agentCollisions = new();

        private float _lastFrameSample;
        private float _lastCurveSample;
        private float _lastHeadwaySample;

        private MetricsAgent[] _agents;

        private void Start()
        {
            _simStart = Time.time;
            _running = true;
        }
        
        public void RegisterAgents()
        {
            _agents = FindObjectsOfType<MetricsAgent>(true);
            totalAgents = _agents.Length;
            Debug.Log($"[Metrics] Registered {totalAgents} agents");
        }

        private void FixedUpdate()
        {
            if (_agents == null || !_running) return;
            elapsedTime = Time.time - _simStart;

            var active = _agents.Where(a => !a.IsEvacuated && a.gameObject.activeSelf).ToArray();
        }
        public void OnAgentEvacuated(int agentId, string exitId)
        {
        }

        public void OnAgentCollision(int agentId, string type, int otherId = -1)
        {
            switch (type)
            {
                case "Wall":
                {
                    _wallCollisions.TryAdd(agentId, 0);
                    _wallCollisions[agentId]++;
                    break;
                }
                case "Agent" when otherId > agentId:
                {
                    _agentCollisions.TryAdd(agentId, 0);
                    _agentCollisions[agentId]++;
                    break;
                }
            }
        }

        public void ForceExport()
        {
            _running = false;
        }
        
        public void CheckExitCrossing(MetricsAgent ma, Vector3 pos)
        {
            if (ma.IsEvacuated) return;

            var exitId = scenarioId switch
            {
                "NarrowDoor" when pos.z < -1.1f => "Exit",
                "Crossing90" when pos.z < -5.5f => "Exit_South",
                "Crossing90" when pos.x < -6.0f => "Exit_West",
                _ => null
            };

            if (exitId == null) return;

            ma.MarkEvacuated();
            OnAgentEvacuated(ma.agentId, exitId);
        }
    }
}