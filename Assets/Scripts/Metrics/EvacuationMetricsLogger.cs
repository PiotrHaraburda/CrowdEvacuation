using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Metrics
{
    public class EvacuationMetricsLogger : MonoBehaviour
    {
        [Header("Run identity")] 
        public string modelType = "SFM";
        public string scenarioId = "NarrowDoor";
        public string runId = "run_001";
        public string outputDirectory = "Metrics";

        [Header("Geometry")] 
        public float densityMeasurementRadius = 1.5f;
        public Transform exitMeasurementPoint;
        public Transform exitMeasurementPoint2;
        public float scenarioAreaM2 = 25f;
        
        [Header("Sampling")] 
        public float frameSampleInterval = 0.1f;
        public float curveSampleInterval = 0.5f;
        public float headwaySampleInterval = 0.25f;

        [Header("State")] 
        public int totalAgents;
        
        [SerializeField] public int evacuatedAgents;
        [SerializeField] private float elapsedTime;
        [SerializeField] private float currentSpecificFlow;
        [SerializeField] private float currentExitDensity;

        private float _simStart;
        private bool _running;

        private readonly List<AgentFrameRecord> _frameRecords = new();
        private readonly List<CollisionRecord> _collisionRecords = new();
        private readonly List<ThroughputRecord> _throughputRecords = new();
        private readonly List<HeadwayRecord> _headwayRecords = new();

        private readonly Dictionary<int, float> _evacuationTimes = new();
        private readonly Dictionary<int, int> _wallCollisions = new();
        private readonly Dictionary<int, int> _agentCollisions = new();

        private readonly List<float> _evacuationCurveTimes = new();
        private readonly List<float> _evacuationCurvePercents = new();
        private readonly List<float> _specificFlowTimeSeries = new();
        private readonly List<float> _specificFlowTimeStamps = new();
        private readonly List<float> _exitDensityTimeSeries = new();
        private readonly List<float> _exitDensityTimeStamps = new();

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

            if (elapsedTime - _lastFrameSample >= frameSampleInterval)
            {
                _lastFrameSample = elapsedTime;
                SampleFrame(active);
            }

            if (elapsedTime - _lastCurveSample >= curveSampleInterval)
            {
                _lastCurveSample = elapsedTime;
                SampleEvacuationCurve();
            }

            if (elapsedTime - _lastHeadwaySample >= headwaySampleInterval)
            {
                _lastHeadwaySample = elapsedTime;
                SampleHeadways(active);
            }
        }

        private void SampleFrame(MetricsAgent[] active)
        {
            var d1 = MeasureLocalDensityCircle(active, exitMeasurementPoint, densityMeasurementRadius);
            var d2 = exitMeasurementPoint2 != null 
                ? MeasureLocalDensityCircle(active, exitMeasurementPoint2, densityMeasurementRadius) 
                : 0f;
            var exitDensity = exitMeasurementPoint2 != null ? (d1 + d2) / 2f : d1;
            currentExitDensity = exitDensity;
            _exitDensityTimeSeries.Add(exitDensity);
            _exitDensityTimeStamps.Add(elapsedTime);

            var count = active.Length;

            foreach (var agent in active)
            {
                var speed = agent.GetInstantSpeed();

                _frameRecords.Add(new AgentFrameRecord
                {
                    agentId = agent.agentId,
                    time = elapsedTime,
                    posX = agent.transform.position.x,
                    posZ = agent.transform.position.z,
                    speed = speed,
                    localDensity = exitDensity
                });
            }

            if (count <= 0)
            {
                return;
            }

            var exitAgents = active.Where(a =>
                exitMeasurementPoint &&
                Vector3.Distance(a.transform.position, exitMeasurementPoint.position) <= densityMeasurementRadius
            ).ToArray();
            var exitSpeed = exitAgents.Length > 0 ? exitAgents.Average(a => a.GetLastSpeed()) : 0f;
            var specificFlow = exitDensity * exitSpeed;
            currentSpecificFlow = specificFlow;
            _specificFlowTimeSeries.Add(specificFlow);
            _specificFlowTimeStamps.Add(elapsedTime);
        }

        private static float MeasureLocalDensityCircle(MetricsAgent[] agents, Transform center, float radius)
        {
            if (!center)
            {
                return 0f;
            }
            var area = Mathf.PI * radius * radius;
            var count = agents.Count(a => Vector3.Distance(a.transform.position, center.position) <= radius);
            return count / area;
        }

        private void SampleEvacuationCurve()
        {
            var pct = totalAgents > 0 ? (float)evacuatedAgents / totalAgents * 100f : 0f;
            _evacuationCurveTimes.Add(elapsedTime);
            _evacuationCurvePercents.Add(pct);
        }

        private void SampleHeadways(MetricsAgent[] active)
        {
            for (var i = 0; i < active.Length; i++)
            {
                var minDist = float.MaxValue;
                var speedAtMin = 0f;
                for (var j = 0; j < active.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }
                    var d = Vector3.Distance(active[i].transform.position, active[j].transform.position);
                    
                    if (!(d < minDist))
                    {
                        continue;
                    }
                    minDist = d;
                    speedAtMin = active[j].GetLastSpeed();
                }

                if (minDist < float.MaxValue)
                {
                    _headwayRecords.Add(new HeadwayRecord
                    {
                        time = elapsedTime,
                        agentId = active[i].agentId,
                        headwayDistance = minDist,
                        headwayVelocity = speedAtMin
                    });
                }
            }
        }

        public void OnAgentEvacuated(int agentId, string exitId)
        {
            if (!_evacuationTimes.TryAdd(agentId, elapsedTime)) return;
            evacuatedAgents++;

            _throughputRecords.Add(new ThroughputRecord
            {
                time = elapsedTime,
                agentId = agentId,
                exitId = exitId
            });

            if (evacuatedAgents < totalAgents) return;
            
            _running = false;
            ExportAll();
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

            _collisionRecords.Add(new CollisionRecord
            {
                time = elapsedTime,
                agentIdA = agentId,
                agentIdB = otherId,
                type = type
            });
        }

        public void ForceExport()
        {
            _running = false;
            ExportAll();
        }

        private void ExportAll()
        {
            var dir = Path.Combine(Application.persistentDataPath, outputDirectory, scenarioId, runId);
            Directory.CreateDirectory(dir);

            WriteAgentsCsv(dir);
            WriteCollisionsCsv(dir);
            WriteThroughputCsv(dir);
            WriteHeadwayCsv(dir);
            WriteEvacuationCurveCsv(dir);
            WriteSpecificFlowCsv(dir);
            WriteSummary(dir);

            Debug.Log($"[Metrics] Export complete - {dir}");
        }

        private void WriteAgentsCsv(string dir)
        {
            var sb = new StringBuilder("agentId;time;posX;posZ;speed;localDensity\n");
            foreach (var r in _frameRecords)
                sb.AppendLine(
                    $"{r.agentId};{r.time:F4};{r.posX:F4};{r.posZ:F4};{r.speed:F4};{r.localDensity:F4}");
            File.WriteAllText(Path.Combine(dir, "agents_frame.csv"), sb.ToString());
        }

        private void WriteCollisionsCsv(string dir)
        {
            var sb = new StringBuilder("time;agentIdA;agentIdB;type\n");
            foreach (var r in _collisionRecords)
                sb.AppendLine($"{r.time:F4};{r.agentIdA};{r.agentIdB};{r.type}");
            File.WriteAllText(Path.Combine(dir, "collisions.csv"), sb.ToString());
        }

        private void WriteThroughputCsv(string dir)
        {
            var sb = new StringBuilder("time;agentId;exitId\n");
            foreach (var r in _throughputRecords)
                sb.AppendLine($"{r.time:F4};{r.agentId};{r.exitId}");
            File.WriteAllText(Path.Combine(dir, "throughput.csv"), sb.ToString());
        }

        private void WriteHeadwayCsv(string dir)
        {
            var sb = new StringBuilder("time;agentId;headwayDistance;headwayVelocity\n");
            foreach (var r in _headwayRecords)
                sb.AppendLine($"{r.time:F4};{r.agentId};{r.headwayDistance:F4};{r.headwayVelocity:F4}");
            File.WriteAllText(Path.Combine(dir, "headway.csv"), sb.ToString());
        }

        private void WriteEvacuationCurveCsv(string dir)
        {
            var sb = new StringBuilder("time;evacuated_percent\n");
            for (var i = 0; i < _evacuationCurveTimes.Count; i++)
                sb.AppendLine($"{_evacuationCurveTimes[i]:F3};{_evacuationCurvePercents[i]:F2}");
            File.WriteAllText(Path.Combine(dir, "evacuation_curve.csv"), sb.ToString());
        }

        private void WriteSpecificFlowCsv(string dir)
        {
            var sb = new StringBuilder("time;specificFlow;exitDensity\n");
            var n = Mathf.Min(_specificFlowTimeStamps.Count, _exitDensityTimeSeries.Count);
            for (var i = 0; i < n; i++)
                sb.AppendLine(
                    $"{_specificFlowTimeStamps[i]:F3};{_specificFlowTimeSeries[i]:F4};{_exitDensityTimeSeries[i]:F4}");
            File.WriteAllText(Path.Combine(dir, "specific_flow.csv"), sb.ToString());
        }

        private void WriteSummary(string dir)
        {
            var sortedTimes = _evacuationTimes.Values.OrderBy(t => t).ToList();
            var rset100 = sortedTimes.Count > 0 ? sortedTimes.Last() : -1f;
            var rset95 = Percentile(sortedTimes, 0.95f);
            var rset90 = Percentile(sortedTimes, 0.90f);
            var rset50 = Percentile(sortedTimes, 0.50f);

            var allSpeeds = _frameRecords
                .Select(r => r.speed)
                .Where(s => s < 4.0f)
                .ToList();
            var meanSpeed = allSpeeds.Count > 0 ? allSpeeds.Average() : 0f;
            var stdSpeed = allSpeeds.Count > 0
                ? Mathf.Sqrt(allSpeeds.Average(s => (s - meanSpeed) * (s - meanSpeed)))
                : 0f;
            var maxSpeed = allSpeeds.Count > 0 ? allSpeeds.Max() : 0f;

            var meanExitDensity = _exitDensityTimeSeries.Count > 0 ? _exitDensityTimeSeries.Average() : 0f;
            var peakExitDensity = _exitDensityTimeSeries.Count > 0 ? _exitDensityTimeSeries.Max() : 0f;
            var meanGlobalDensity = _frameRecords.Count > 0
                ? _frameRecords.GroupBy(r => r.time).Average(g => g.Count() / scenarioAreaM2)
                : 0f;

            var meanFlow = _specificFlowTimeSeries.Count > 0 ? _specificFlowTimeSeries.Average() : 0f;
            var peakFlow = _specificFlowTimeSeries.Count > 0 
                ? _specificFlowTimeSeries.Where(f => f < 10f).DefaultIfEmpty(0f).Max() 
                : 0f;
            var throughput = rset100 > 0 ? (float)totalAgents / rset100 : 0f;
            var throughputByExit = _throughputRecords
                .GroupBy(r => r.exitId)
                .ToDictionary(g => g.Key, g => g.Count() / (rset100 > 0 ? rset100 : 1f));

            var totalWall = _wallCollisions.Values.Sum();
            var totalAA = _agentCollisions.Values.Sum();

            var hwDists = _headwayRecords.Select(h => h.headwayDistance).ToList();
            var meanHW = hwDists.Count > 0 ? hwDists.Average() : 0f;
            var minHW = hwDists.Count > 0 ? hwDists.Min() : 0f;
            var stdHW = hwDists.Count > 0 ? Mathf.Sqrt(hwDists.Average(d => (d - meanHW) * (d - meanHW))) : 0f;

            var sb = new StringBuilder("metric;value\n");
            sb.AppendLine($"model_type;{modelType}");
            sb.AppendLine($"scenario_id;{scenarioId}");
            sb.AppendLine($"run_id;{runId}");
            sb.AppendLine($"total_agents;{totalAgents}");
            sb.AppendLine($"rset_100s;{rset100:F3}");
            sb.AppendLine($"rset_95s;{rset95:F3}");
            sb.AppendLine($"rset_90s;{rset90:F3}");
            sb.AppendLine($"rset_50s;{rset50:F3}");
            sb.AppendLine($"mean_speed_ms;{meanSpeed:F4}");
            sb.AppendLine($"std_speed_ms;{stdSpeed:F4}");
            sb.AppendLine($"max_speed_ms;{maxSpeed:F4}");
            sb.AppendLine($"mean_exit_density_pm2;{meanExitDensity:F4}");
            sb.AppendLine($"peak_exit_density_pm2;{peakExitDensity:F4}");
            sb.AppendLine($"mean_global_density_pm2;{meanGlobalDensity:F4}");
            sb.AppendLine($"specific_flow_mean_pms;{meanFlow:F4}");
            sb.AppendLine($"specific_flow_peak_pms;{peakFlow:F4}");
            sb.AppendLine($"throughput_ps;{throughput:F4}");
            foreach (var kvp in throughputByExit)
                sb.AppendLine($"throughput_{kvp.Key}_ps;{kvp.Value:F4}");
            sb.AppendLine($"collisions_agent_agent;{totalAA}");
            sb.AppendLine($"collisions_agent_wall;{totalWall}");
            sb.AppendLine($"mean_headway_m;{meanHW:F4}");
            sb.AppendLine($"min_headway_m;{minHW:F4}");
            sb.AppendLine($"std_headway_m;{stdHW:F4}");
            File.WriteAllText(Path.Combine(dir, "summary.csv"), sb.ToString());
        }

        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return -1f;
            var idx = Mathf.Clamp(Mathf.FloorToInt(sorted.Count * p) - 1, 0, sorted.Count - 1);
            return sorted[idx];
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