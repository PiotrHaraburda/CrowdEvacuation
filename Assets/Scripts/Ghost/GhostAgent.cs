using Metrics;
using UnityEngine;

namespace Ghost
{
    public class GhostAgent : MonoBehaviour
    {
        private float[] _posX;
        private float[] _posZ;
        private float[] _times;

        private int _currentFrame;
        private MetricsAgent _ma;

        public bool IsFinished { get; private set; }

        public void Init(float[] x, float[] z, float[] t)
        {
            _posX = x;
            _posZ = z;
            _times = t;
            _currentFrame = 0;
            IsFinished = false;
            _ma = GetComponent<MetricsAgent>();

            if (_posX.Length > 0)
                transform.position = new Vector3(_posX[0], 1f, _posZ[0]);

            gameObject.SetActive(false);
        }

        public void Tick(float playbackTime)
        {
            if (IsFinished || _times == null || _times.Length == 0) return;

            if (!gameObject.activeSelf)
            {
                if (playbackTime >= _times[0])
                    gameObject.SetActive(true);
                else
                    return;
            }

            while (_currentFrame < _times.Length - 1 && _times[_currentFrame + 1] <= playbackTime)
                _currentFrame++;

            if (_currentFrame >= _times.Length - 1)
            {
                _ma.CheckExit(new Vector3(_posX[_posX.Length - 1], 1f, _posZ[_posZ.Length - 1]));
                IsFinished = true;
                gameObject.SetActive(false);
                return;
            }

            var f0 = _currentFrame;
            var f1 = _currentFrame + 1;
            var t0 = _times[f0];
            var t1 = _times[f1];
            var alpha = (t1 > t0) ? (playbackTime - t0) / (t1 - t0) : 0f;
            alpha = Mathf.Clamp01(alpha);

            var x = Mathf.Lerp(_posX[f0], _posX[f1], alpha);
            var z = Mathf.Lerp(_posZ[f0], _posZ[f1], alpha);

            var newPos = new Vector3(x, 1f, z);

            var delta = newPos - transform.position;
            if (delta.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(delta);

            transform.position = newPos;

            _ma.OverrideSpeed(delta.magnitude / Time.fixedDeltaTime);
            _ma.CheckExit(newPos);

            if (_ma.IsEvacuated)
            {
                IsFinished = true;
                gameObject.SetActive(false);
            }
        }
    }
}