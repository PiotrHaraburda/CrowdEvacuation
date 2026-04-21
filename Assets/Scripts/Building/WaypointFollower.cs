using System;
using UnityEngine;

namespace Building
{
    public class WaypointFollower : MonoBehaviour
    {
        public Transform[] waypoints;
        public float reachRadius = 1.5f;

        public event Action<Transform> onWaypointChanged;

        private int _currentIndex;

        private void FixedUpdate()
        {
            if (waypoints == null || waypoints.Length == 0) 
                return;
            if (_currentIndex >= waypoints.Length - 1) 
                return;

            var currentTarget = waypoints[_currentIndex];
            if (!currentTarget) 
                return;

            var flatPos = transform.position;
            flatPos.y = 0f;
            var flatTarget = currentTarget.position;
            flatTarget.y = 0f;

            if (Vector3.Distance(flatPos, flatTarget) <= reachRadius)
            {
                _currentIndex++;
                var next = waypoints[_currentIndex];
                if (next) 
                    onWaypointChanged?.Invoke(next);
            }
        }
    }
}