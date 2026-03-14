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
        private bool _useOverrideSpeed;

        public void RegisterLogger(EvacuationMetricsLogger logger)
        {
            _logger = logger;
            _prevPosition = transform.position;
            _prevSpeed = 0f;
        }

        public void OverrideSpeed(float speed)
        {
            _prevSpeed = speed;
            _useOverrideSpeed = true;
        }

        public float GetInstantSpeed()
        {
            if (_useOverrideSpeed) return _prevSpeed;
            var speed = Vector3.Distance(transform.position, _prevPosition) / Time.fixedDeltaTime;
            _prevPosition = transform.position;
            _prevSpeed = speed;
            return speed;
        }

        public float GetLastSpeed() => _prevSpeed;
        
        public void MarkEvacuated()
        {
            IsEvacuated = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsEvacuated) return;
            if (!other.CompareTag("Exit")) return;
            
            IsEvacuated = true;
            _logger?.OnAgentEvacuated(agentId, other.gameObject.name);
            gameObject.SetActive(false);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsEvacuated || _logger == null) return;
            if (collision.gameObject.CompareTag("Wall"))
            {
                _logger.OnAgentCollision(agentId, "Wall");
            }
            else if (collision.gameObject.TryGetComponent<MetricsAgent>(out var other))
            {
                _logger.OnAgentCollision(agentId, "Agent", other.agentId);
            }
        }
    }
}