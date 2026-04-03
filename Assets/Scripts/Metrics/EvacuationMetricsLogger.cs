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
        public float headwaySampleInterval = 0.25f;
        public float fdSampleInterval = 0.5f;
        public float heatmapSampleInterval = 2.0f;

        [Header("Measurement zone")]
        public float measureZoneMinX = -2.8f;
        public float measureZoneMaxX = 2.4f;
        public float measureZoneMinZ = -1.2f;
        public float measureZoneMaxZ = 1.7f;
        
        [Header("State")]
        public int totalAgents;
        public int evacuatedAgents;
        public float elapsedTime;

        private float _simStart;
        private bool _running;

        private readonly List<AgentFrameRecord> _frameRecords = new();
        private readonly List<CollisionRecord> _collisionRecords = new();
        private readonly List<ThroughputRecord> _throughputRecords = new();
        private readonly List<HeadwayRecord> _headwayRecords = new();

        private readonly Dictionary<int, float> _evacuationTimes = new();
        private readonly Dictionary<int, int> _wallCollisions = new();
        private readonly Dictionary<int, int> _agentCollisions = new();

        private readonly List<float> _evacuationCurve = new();
        private readonly List<(float time, float density, float speed)> _fundamentalDiagramRecords = new();
        private readonly List<(float time, float density, float flow)> _specificFlowTimeSeriesDetailed = new();
        private readonly List<(float time, int x, int z, float density)> _densityHeatmapRecords = new();

        private float _lastFrameSample;
        private float _lastHeadwaySample;
        private float _lastFDSample;
        private float _lastHeatmapSample;

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

        public void RegisterAgent(MetricsAgent agent)
        {
            var list = _agents != null ? new List<MetricsAgent>(_agents) : new List<MetricsAgent>();
            if (!list.Contains(agent))
                list.Add(agent);
            _agents = list.ToArray();
            totalAgents = _agents.Length;
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

            if (elapsedTime - _lastHeadwaySample >= headwaySampleInterval)
            {
                _lastHeadwaySample = elapsedTime;
                SampleHeadways(active);
            }
            
            if (elapsedTime - _lastFDSample >= fdSampleInterval)
            {
                _lastFDSample = elapsedTime;
                SampleFundamentalDiagram(active);
            }

            if (elapsedTime - _lastHeatmapSample >= heatmapSampleInterval)
            {
                _lastHeatmapSample = elapsedTime;
                SampleDensityHeatmap(active);
            }
        }

        private void SampleFrame(MetricsAgent[] active)
        {
            var d1 = MeasureLocalDensityCircle(active, exitMeasurementPoint, densityMeasurementRadius);
            var d2 = exitMeasurementPoint2 != null 
                ? MeasureLocalDensityCircle(active, exitMeasurementPoint2, densityMeasurementRadius) 
                : 0f;
            var exitDensity = exitMeasurementPoint2 != null ? (d1 + d2) / 2f : d1;

            var count = active.Length;

            foreach (var agent in active)
            {
                if (!IsInMeasurementZone(agent.transform.position))
                {
                    continue;
                }
                var speed = agent.GetInstantSpeed();

                _frameRecords.Add(new AgentFrameRecord
                {
                    agentId = agent.agentId,
                    time = elapsedTime,
                    posX = agent.transform.position.x,
                    posZ = agent.transform.position.z,
                    speed = speed
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
            _specificFlowTimeSeriesDetailed.Add((elapsedTime, exitDensity, specificFlow));
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

        private void SampleHeadways(MetricsAgent[] active)
        {
            for (var i = 0; i < active.Length; i++)
            {
                if (!IsInMeasurementZone(active[i].transform.position))
                {
                    continue;
                }
                
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
        
        private void SampleFundamentalDiagram(MetricsAgent[] active)
        {
            if (active.Length < 2) return;

            foreach (var agent in active)
            {
                if (!IsInMeasurementZone(agent.transform.position))
                {
                    continue;
                }
                
                var pos = agent.transform.position;
                pos.y = 0f;

                var nearbyCount = 0;
                var speedSum = 0f;
                var measureRadius = 2f;

                foreach (var other in active)
                {
                    if (other == agent)
                    {
                        continue;
                    }
                    var otherPos = other.transform.position;
                    otherPos.y = 0f;
                    if ((pos - otherPos).sqrMagnitude <= measureRadius * measureRadius)
                    {
                        nearbyCount++;
                        speedSum += other.GetLastSpeed();
                    }
                }

                var localDensity = (nearbyCount + 1) / (Mathf.PI * measureRadius * measureRadius);
                var localSpeed = (agent.GetLastSpeed() + speedSum) / (nearbyCount + 1);

                if (localDensity > 0.1f)
                    _fundamentalDiagramRecords.Add((elapsedTime, localDensity, localSpeed));
            }
        }

        private void SampleDensityHeatmap(MetricsAgent[] active)
        {
            if (active.Length == 0)
            {
                return;
            }

            var cellSize = 1f;
            var counts = new Dictionary<(int, int), int>();

            foreach (var agent in active)
            {
                var gx = Mathf.FloorToInt(agent.transform.position.x / cellSize);
                var gz = Mathf.FloorToInt(agent.transform.position.z / cellSize);
                var key = (gx, gz);
                counts[key] = counts.GetValueOrDefault(key, 0) + 1;
            }

            foreach (var kvp in counts)
            {
                var density = kvp.Value / (cellSize * cellSize);
                _densityHeatmapRecords.Add((elapsedTime, kvp.Key.Item1, kvp.Key.Item2, density));
            }
        }

        public void OnAgentEvacuated(int agentId, string exitId)
        {
            if (!_evacuationTimes.TryAdd(agentId, elapsedTime))
            {
                return;
            }
            evacuatedAgents++;

            _throughputRecords.Add(new ThroughputRecord
            {
                time = elapsedTime,
                agentId = agentId,
                exitId = exitId
            });
            
            _evacuationCurve.Add(elapsedTime);
            
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
                    _collisionRecords.Add(new CollisionRecord
                    {
                        time = elapsedTime, agentIdA = agentId, agentIdB = -1, type = "Wall"
                    });
                    break;
                }
                case "Agent" when otherId > agentId:
                {
                    _agentCollisions.TryAdd(agentId, 0);
                    _agentCollisions[agentId]++;
                    _collisionRecords.Add(new CollisionRecord
                    {
                        time = elapsedTime, agentIdA = agentId, agentIdB = otherId, type = "Agent"
                    });
                    break;
                }
            }
        }

        public void ForceExport()
        {
            _running = false;
            ExportAll();
        }

        private void ExportAll()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (projectRoot == null)
            {
                return;
            }
            
            var dir = Path.Combine(projectRoot, outputDirectory, scenarioId, runId);
            Directory.CreateDirectory(dir);

            WriteAgentsCsv(dir);
            WriteCollisionsCsv(dir);
            WriteThroughputCsv(dir);
            WriteHeadwayCsv(dir);
            WriteEvacuationCurve(dir);
            WriteFundamentalDiagram(dir);
            WriteSpecificFlowTimeSeries(dir);
            WriteDensityHeatmap(dir);
            WriteSummary(dir);

            Debug.Log($"[Metrics] Export complete - {dir}");
        }

        private void WriteAgentsCsv(string dir)
        {
            var sb = new StringBuilder("agentId;time;posX;posZ;speed\n");
            foreach (var r in _frameRecords)
                sb.AppendLine(
                    $"{r.agentId};{r.time:F4};{r.posX:F4};{r.posZ:F4};{r.speed:F4}");
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
        
        private void WriteEvacuationCurve(string dir)
        {
            var sb = new StringBuilder("time;evacuated_count\n");
            _evacuationCurve.Sort();
            for (var i = 0; i < _evacuationCurve.Count; i++)
                sb.AppendLine($"{_evacuationCurve[i]:F3};{i + 1}");
            File.WriteAllText(Path.Combine(dir, "evacuation_curve.csv"), sb.ToString());
        }

        private void WriteFundamentalDiagram(string dir)
        {
            var sb = new StringBuilder("time;density;speed\n");
            foreach (var r in _fundamentalDiagramRecords)
                sb.AppendLine($"{r.time:F3};{r.density:F4};{r.speed:F4}");
            File.WriteAllText(Path.Combine(dir, "fundamental_diagram.csv"), sb.ToString());
        }

        private void WriteSpecificFlowTimeSeries(string dir)
        {
            var sb = new StringBuilder("time;density;specific_flow\n");
            foreach (var r in _specificFlowTimeSeriesDetailed)
                sb.AppendLine($"{r.time:F3};{r.density:F4};{r.flow:F4}");
            File.WriteAllText(Path.Combine(dir, "specific_flow_series.csv"), sb.ToString());
        }

        private void WriteDensityHeatmap(string dir)
        {
            var sb = new StringBuilder("time;grid_x;grid_z;density\n");
            foreach (var r in _densityHeatmapRecords)
                sb.AppendLine($"{r.time:F3};{r.x};{r.z};{r.density:F4}");
            File.WriteAllText(Path.Combine(dir, "density_heatmap.csv"), sb.ToString());
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

            var exitDensities = _specificFlowTimeSeriesDetailed.Select(r => r.density).ToList();
            var meanExitDensity = exitDensities.Count > 0 ? exitDensities.Average() : 0f;
            var peakExitDensity = exitDensities.Count > 0 ? exitDensities.Max() : 0f;
            var meanGlobalDensity = _frameRecords.Count > 0
                ? _frameRecords.GroupBy(r => r.time).Average(g => g.Count() / scenarioAreaM2)
                : 0f;

            var flows = _specificFlowTimeSeriesDetailed.Select(r => r.flow).ToList();
            var meanFlow = flows.Count > 0 ? flows.Average() : 0f;
            var peakFlow = flows.Count > 0
                ? flows.Where(f => f < 10f).DefaultIfEmpty(0f).Max()
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
        
        public bool CheckExitCrossing(MetricsAgent ma, Vector3 pos)
        {
            if (ma.IsEvacuated) return false;

            var exitId = scenarioId switch
            {
                "NarrowDoor" when pos.z < -1.1f => "ExitZone",
                "Crossing90" when pos.z < -4.25f => "Exit_South",
                "Crossing90" when pos.x < -4.75f => "Exit_West",
                _ => null
            };

            if (exitId == null) return false;

            ma.MarkEvacuated();
            OnAgentEvacuated(ma.agentId, exitId);
            return true;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                Debug.Log($"Evacuated: {evacuatedAgents} / {totalAgents}");
                ForceExport();
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 120));
            GUILayout.Box($"{modelType} Playback\n" +
                          $"Agents: {totalAgents - evacuatedAgents}/{totalAgents}\n" +
                          $"Time: {elapsedTime:F1}s");
            GUILayout.EndArea();
        }
        
        private bool IsInMeasurementZone(Vector3 pos)
        {
            return pos.x >= measureZoneMinX && pos.x <= measureZoneMaxX &&
                   pos.z >= measureZoneMinZ && pos.z <= measureZoneMaxZ;
        }
    }
}