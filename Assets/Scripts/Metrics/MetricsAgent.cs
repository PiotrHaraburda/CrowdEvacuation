using UnityEngine;

namespace Metrics
{
    public class MetricsAgent : MonoBehaviour
    {
        public int agentId;
        public bool IsEvacuated { get; private set; }

        private EvacuationMetricsLogger _logger;
        private Vector3 _prevPosition;
        private float _prevSpeed;
        private float _prevSpeedTime;
        private bool _useOverrideSpeed;
        
        private static Collider[] _exitColliders;

        public void RegisterLogger(EvacuationMetricsLogger logger)
        {
            _logger = logger;
            _prevPosition = transform.position;
            _prevSpeed = 0f;
            _prevSpeedTime = Time.time;
        }

        public void OverrideSpeed(float speed)
        {
            _prevSpeed = speed;
            _useOverrideSpeed = true;
        }

        public float GetInstantSpeed()
        {
            if (_useOverrideSpeed)
            {
                return _prevSpeed;
            }
    
            var dt = Time.time - _prevSpeedTime;
            if (dt < 0.001f)
            {
                return _prevSpeed;
            }
    
            var speed = Vector3.Distance(transform.position, _prevPosition) / dt;
            _prevPosition = transform.position;
            _prevSpeedTime = Time.time;
            _prevSpeed = speed;
            return speed;
        }

        public float GetLastSpeed() => _prevSpeed;
        
        public void MarkEvacuated()
        {
            IsEvacuated = true;
        }

        public void ReportCollision(string type, int otherId = -1)
        {
            if (IsEvacuated || !_logger)
            {
                return;
            }
            _logger.OnAgentCollision(agentId, type, otherId);
        }

        public void CheckExit(Vector3 pos)
        {
            if (IsEvacuated)
            {
                return;
            }
    
            if (_exitColliders == null)
            {
                var exits = GameObject.FindGameObjectsWithTag("Exit");
                _exitColliders = new Collider[exits.Length];
                for (var i = 0; i < exits.Length; i++)
                    _exitColliders[i] = exits[i].GetComponent<Collider>();
            }

            foreach (var col in _exitColliders)
            {
                if (!col || !col.bounds.Contains(pos))
                {
                    continue;
                }
                IsEvacuated = true;
                _logger?.OnAgentEvacuated(agentId, col.gameObject.name);
                gameObject.SetActive(false);
                return;
            }
        }
    }
}