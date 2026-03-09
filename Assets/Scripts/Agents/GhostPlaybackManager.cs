using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Metrics;
using UnityEngine;

namespace Agents
{
    public class GhostPlaybackManager : MonoBehaviour
    {
        [Header("Full data folder path")]
        public string dataFolderPath = "";

        [Tooltip("Episode name")]
        public string specificFileName = "";

        [Header("Ghost prefab")]
        public GameObject ghostPrefab;

        [Header("Speed")]
        [Range(0.1f, 5f)]
        public float playbackSpeed = 1f;
        
        [Header("Data offset (coordinate correction)")]
        public Vector2 dataOffset = Vector2.zero;

        [Header("Info")]
        [SerializeField] private int totalAgents;
        [SerializeField] private int activeAgents;
        [SerializeField] private float playbackTime;
        [SerializeField] private float totalDuration;
        [SerializeField] private string loadedFile = "";

        private readonly List<GhostAgent> _ghosts = new();
        private bool _isPlaying;
        private float _startTime;

        private void Start()
        {
            StartCoroutine(LoadAndPlay());
        }

        private void FixedUpdate()
        {
            if (!_isPlaying) return;

            playbackTime = (Time.time - _startTime) * playbackSpeed;

            var active = 0;
            foreach (var ghost in _ghosts.Where(ghost => !ghost.IsFinished))
            {
                ghost.Tick(playbackTime);
                active++;
            }
            activeAgents = active;

            if (active != 0 || _ghosts.Count <= 0) return;
            _isPlaying = false;
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

            loadedFile = Path.GetFileName(filePath);
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
            foreach (var g in _ghosts.Where(g => g)) Destroy(g.gameObject);
            _ghosts.Clear();

            if (!ghostPrefab)
            {
                Debug.LogError("[GhostManager] GhostPrefab is null");
                return;
            }

            var maxTime = 0f;

            foreach (var agentData in episode.agents)
            {
                if (agentData.x == null || agentData.x.Length == 0)
                {
                    continue;
                }

                var go = Instantiate(ghostPrefab, transform);
                go.name = $"Ghost_{agentData.id}";

                var ghost = go.GetComponent<GhostAgent>();
                ghost.metricsLogger = FindObjectOfType<EvacuationMetricsLogger>();
                if (!ghost)
                {
                    Debug.LogError("[GhostManager] GhostPrefab doesnt have GhostAgent component");
                    Destroy(go);
                    continue;
                }
                var ma = go.GetComponent<MetricsAgent>();
                if (ma)
                {
                    ma.agentId = agentData.id;
                }
                var correctedX = agentData.x.Select(v => v - dataOffset.x).ToArray();
                var correctedZ = agentData.y.Select(v => v - dataOffset.y).ToArray();
                ghost.Init(correctedX, correctedZ, agentData.t);
                _ghosts.Add(ghost);

                if (agentData.t != null && agentData.t.Length > 0)
                    maxTime = Mathf.Max(maxTime, agentData.t[agentData.t.Length - 1]);
            }

            totalAgents = _ghosts.Count;
            totalDuration = maxTime;
            _startTime = Time.time;
            _isPlaying = true;

            Debug.Log($"[GhostManager] Loaded {totalAgents} agents, time: {totalDuration:F1}s");
            FindObjectOfType<EvacuationMetricsLogger>()?.RegisterAgents();
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

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _isPlaying = !_isPlaying;
                if (_isPlaying)
                {
                    _startTime = Time.time - playbackTime / playbackSpeed;
                }
                Debug.Log(_isPlaying ? "[GhostManager] Resumed." : "[GhostManager] Paused.");
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                StopAllCoroutines();
                StartCoroutine(LoadAndPlay());
                Debug.Log("[GhostManager] Reset.");
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                var newSpeed = Mathf.Min(playbackSpeed + 0.1f, 5f);
                _startTime = Time.time - playbackTime / newSpeed;
                playbackSpeed = newSpeed;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                var newSpeed = Mathf.Max(playbackSpeed - 0.1f, 0.1f);
                _startTime = Time.time - playbackTime / newSpeed;
                playbackSpeed = newSpeed;
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                var logger = FindObjectOfType<EvacuationMetricsLogger>();
                Debug.Log($"Logger found: {logger}");
                Debug.Log($"Evacuated: {logger?.evacuatedAgents} / {logger?.totalAgents}");
                logger?.ForceExport();
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 120));
            GUILayout.Box($"Ghost Playback\n" +
                          $"File: {loadedFile}\n" +
                          $"Agents: {activeAgents}/{totalAgents}\n" +
                          $"Time: {playbackTime:F1}s / {totalDuration:F1}s\n" +
                          $"Speed: {playbackSpeed:F1}x\n" +
                          $"[Space]=Pause  [R]=Reset  [↑↓]=Speed");
            GUILayout.EndArea();
        }
    }
}
