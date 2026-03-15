using Metrics;
using UnityEngine;

namespace Agents
{
    public class GhostAgent : MonoBehaviour
    {
        [HideInInspector] public EvacuationMetricsLogger metricsLogger;
        [HideInInspector] public float[] posX;
        [HideInInspector] public float[] posZ;
        [HideInInspector] public float[] times;

        private int _currentFrame;

        public bool IsFinished { get; private set; }

        public void Init(float[] x, float[] z, float[] t)
        {
            posX = x;
            posZ = z;
            times = t;
            _currentFrame = 0;
            IsFinished = false;

            if (posX.Length > 0)
                transform.position = new Vector3(posX[0], 1f, posZ[0]);
            
            gameObject.SetActive(false);
        }

        public void Tick(float playbackTime)
        {
            if (IsFinished || times == null || times.Length == 0) return;
            
            if (!gameObject.activeSelf)
            {
                if (playbackTime >= times[0])
                    gameObject.SetActive(true);
                else
                    return;
            }

            while (_currentFrame < times.Length - 1 && times[_currentFrame + 1] <= playbackTime)
            {
                _currentFrame++;
            }
            
            var ma = GetComponent<MetricsAgent>();
            if (!metricsLogger)
            {
                return;
            }
            if (_currentFrame >= times.Length - 1)
            {
                if (ma && !ma.IsEvacuated)
                {
                    var lastPos = new Vector3(posX[posX.Length - 1], 1f, posZ[posZ.Length - 1]);
                    metricsLogger.CheckExitCrossing(ma, lastPos);
                }

                IsFinished = true;
                gameObject.SetActive(false);
                return;
            }

            var f0 = _currentFrame;
            var f1 = _currentFrame + 1;
            var t0 = times[f0];
            var t1 = times[f1];
            var alpha = (t1 > t0) ? (playbackTime - t0) / (t1 - t0) : 0f;
            alpha = Mathf.Clamp01(alpha);

            var x = Mathf.Lerp(posX[f0], posX[f1], alpha);
            var z = Mathf.Lerp(posZ[f0], posZ[f1], alpha);

            var newPos = new Vector3(x, 1f, z);

            var delta = newPos - transform.position;
            if (delta.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(delta);

            transform.position = newPos;

            if (!ma || ma.IsEvacuated) return;
            var speed = delta.magnitude / Time.deltaTime;
            ma.OverrideSpeed(speed);
            metricsLogger.CheckExitCrossing(ma, newPos);
        }
    }
}