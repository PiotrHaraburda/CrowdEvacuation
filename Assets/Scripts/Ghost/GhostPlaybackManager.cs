using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Metrics;
using UnityEngine;
using Utility;

namespace Ghost
{
    public class GhostPlaybackManager : MonoBehaviour
    {
        [Header("Full data folder path")]
        public string dataFolderPath = "";

        [Tooltip("Episode name")]
        public string specificFileName = "";

        [Header("Ghost prefab")]
        public GameObject ghostPrefab;
        
        [Header("Data offset (coordinate correction)")]
        public Vector2 dataOffset = Vector2.zero;

        [Header("References")]
        public EvacuationMetricsLogger metricsLogger;
        
        private readonly List<GhostAgent> _ghosts = new();
        private bool _isPlaying;

        private void Start()
        {
            StartCoroutine(LoadAndPlay());
        }

        private void FixedUpdate()
        {
            if (!_isPlaying) return;

            var active = 0;
            foreach (var ghost in _ghosts)
            {
                if (ghost.IsFinished) 
                {
                    continue;
                }
                ghost.Tick(metricsLogger.elapsedTime);
                active++;
            }

            if (active == 0 && _ghosts.Count > 0)
            {
                _isPlaying = false;
                metricsLogger.ForceExport();
            }
        }

        private IEnumerator LoadAndPlay()
        {
            yield return null;

            var filePath = ResolveFilePath();
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError($"[GhostManager] JSON not found in: {dataFolderPath}");
                yield break;
            }

            Debug.Log($"[GhostManager] Loading: {filePath}");

            var json = File.ReadAllText(filePath);

            var wrappedJson = "{\"agents\":" + json + "}";
            var episode = JsonUtility.FromJson<EpisodeData>(wrappedJson);

            if (episode?.agents == null || episode.agents.Length == 0)
            {
                Debug.LogError("[GhostManager] JSON parse error or 0 agents.");
                yield break;
            }

            SpawnGhosts(episode);
        }

        private void SpawnGhosts(EpisodeData episode)
        {
            foreach (var g in _ghosts.Where(g => g))
            {
                Destroy(g.gameObject);
            }
            _ghosts.Clear();

            foreach (var agentData in episode.agents)
            {
                if (agentData.x == null || agentData.x.Length == 0)
                {
                    continue;
                }

                var go = Instantiate(ghostPrefab, transform);
                go.name = $"Ghost_{agentData.id}";

                var ghost = go.GetComponent<GhostAgent>();
                var r = AgentConfig.SampleRadius();
                var d = r * 2f;
                go.transform.localScale = new Vector3(d, go.transform.localScale.y, d);
                var ma = go.GetComponent<MetricsAgent>();
                ma.agentId = agentData.id;
                ma.RegisterLogger(metricsLogger);
                var correctedX = agentData.x.Select(v => v - dataOffset.x).ToArray();
                var correctedZ = agentData.y.Select(v => v - dataOffset.y).ToArray();
                ghost.Init(correctedX, correctedZ, agentData.t);
                _ghosts.Add(ghost);
            }

            _isPlaying = true;

            metricsLogger.totalAgents = _ghosts.Count;
            metricsLogger.RegisterAgents();
        }

        private string ResolveFilePath()
        {
            if (string.IsNullOrEmpty(specificFileName))
            {
                return null;
            }
            var specific = Path.Combine(dataFolderPath, specificFileName);
            if (File.Exists(specific))
            {
                return specific;
            }
            Debug.LogWarning($"[GhostManager] Not found: {specific}");
            return null;
        }

    }
}
