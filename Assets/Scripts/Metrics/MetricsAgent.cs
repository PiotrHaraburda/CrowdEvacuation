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
            if (IsEvacuated)
            {
                return;
            }
            _logger.OnAgentCollision(agentId, type, otherId);
        }

        public bool CheckExit(Vector3 pos, bool deactivate = true)
        {
            if (IsEvacuated) return false;

            if (_logger.CheckExitCrossing(this, pos))
            {
                if (deactivate) 
                    gameObject.SetActive(false);
                return true;
            }

            var exitCol = FindOverlappingExit(pos);
            if (!exitCol) 
                return false;

            MarkEvacuated();
            _logger.OnAgentEvacuated(agentId, exitCol.gameObject.name);
            if (deactivate) 
                gameObject.SetActive(false);
            return true;
        }

        private static Collider FindOverlappingExit(Vector3 pos)
        {
            if (_exitColliders == null)
            {
                var exits = GameObject.FindGameObjectsWithTag("Exit");
                _exitColliders = new Collider[exits.Length];
                for (var i = 0; i < exits.Length; i++)
                    _exitColliders[i] = exits[i].GetComponent<Collider>();
            }

            foreach (var col in _exitColliders)
            {
                if (col && col.bounds.Contains(pos))
                    return col;
            }

            return null;
        }
        
        public void ResetState()
        {
            IsEvacuated = false;
            _prevPosition = transform.position;
            _prevSpeedTime = Time.time;
            _prevSpeed = 0f;
            _useOverrideSpeed = false;
        }

        public static void ResetExitCache()
        {
            _exitColliders = null;
        }
    }
}