using UnityEngine;

namespace Utility
{
    public class FreeLookCamera : MonoBehaviour
    {
        public float moveSpeed = 30f;
 
        private void Update()
        {
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move.z += 1f;
            if (Input.GetKey(KeyCode.S)) move.z -= 1f;
            if (Input.GetKey(KeyCode.A)) move.x -= 1f;
            if (Input.GetKey(KeyCode.D)) move.x += 1f;
            if (Input.GetKey(KeyCode.E)) move.y += 1f;
            if (Input.GetKey(KeyCode.Q)) move.y -= 1f;
 
            transform.position += move.normalized * (moveSpeed * Time.unscaledDeltaTime);
        }
    }
}