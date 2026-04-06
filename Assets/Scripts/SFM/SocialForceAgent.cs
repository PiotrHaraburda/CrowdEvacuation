using System;
using System.Collections.Generic;
using System.Linq;
using Metrics;
using UnityEngine;
using Utility;

namespace SFM
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(MetricsAgent))]
    public class SocialForceAgent : MonoBehaviour
    {
        [Header("Goal")]
        public Transform goal;

        [NonSerialized] public float DesiredSpeed;
        [NonSerialized] public float Radius;

        [Header("SFM parameters")]
        public float A = 2000f; // default from (Helbing, Farkas & Vicsek 2000), then calibrated
        public float B = 0.08f; // default from (Helbing, Farkas & Vicsek 2000), then calibrated
        public float k = 120000f; // default from (Helbing, Farkas & Vicsek 2000), then calibrated
        public float kappa = 240000f; // default from (Helbing, Farkas & Vicsek 2000), then calibrated
        public float tau = 0.5f; // default from (Helbing & Molnar 1995), then calibrated

        [Header("State")]
        [SerializeField] private Vector3 velocity;

        private MetricsAgent _ma;
        private static readonly List<SocialForceAgent> AllAgents = new();
        private static Collider[] _wallColliders;
        private static bool _wallsCached;

        private void Awake()
        {
            var rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            _ma = GetComponent<MetricsAgent>();
        }

        private void OnEnable() => AllAgents.Add(this);

        private void OnDisable()
        {
            AllAgents.Remove(this);
            if (AllAgents.Count == 0)
                _wallsCached = false;
        }

        private void FixedUpdate()
        {
            if (!goal) return;

            var drivingAccel = ComputeDrivingForce();
            var interactionForce = ComputeAgentRepulsion() + ComputeWallRepulsion();

            velocity += (drivingAccel + interactionForce / AgentConfig.Mass) * Time.fixedDeltaTime;

            if (velocity.magnitude > DesiredSpeed * AgentConfig.VelocityClamp)
                velocity = velocity.normalized * (DesiredSpeed * AgentConfig.VelocityClamp);
            
            transform.position += velocity * Time.fixedDeltaTime;

            if (velocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(velocity);
            
            _ma.CheckExit(transform.position);
        }

        private Vector3 ComputeDrivingForce()
        {
            var dir = (goal.position - transform.position).normalized;
            dir.y = 0f;
            var desiredVelocity = dir * DesiredSpeed;
            return (desiredVelocity - velocity) / tau;
        }

        private Vector3 ComputeAgentRepulsion()
        {
            var force = Vector3.zero;

            foreach (var other in AllAgents)
            {
                if (other == this) continue;

                var diff = transform.position - other.transform.position;
                diff.y = 0f;
                var dist = diff.magnitude;
                if (dist < 0.001f) continue;

                var nij = diff / dist;
                var tij = new Vector3(-nij.z, 0f, nij.x);
                var rij = Radius + other.Radius;
                var overlap = rij - dist;
                
                var repulsive = A * Mathf.Exp(overlap / B) * nij;
                
                if (overlap > 0f)
                {
                    var pushing = k * overlap * nij;
                    var dvt = Vector3.Dot(other.velocity - velocity, tij);
                    var friction = kappa * overlap * dvt * tij;
                    repulsive += pushing + friction;
                    
                    _ma.ReportCollision("Agent", other._ma.agentId);
                }

                force += repulsive;
            }

            return force;
        }

        private Vector3 ComputeWallRepulsion()
        {
            var force = Vector3.zero;
            var pos = transform.position;
            pos.y = 0f;

            if (!_wallsCached)
            {
                var walls = GameObject.FindGameObjectsWithTag("Wall");
                _wallColliders = walls
                    .Select(w => w.GetComponent<Collider>())
                    .Where(c => c != null)
                    .ToArray();
                _wallsCached = true;
            }

            foreach (var col in _wallColliders)
            {
                var closest = col.ClosestPoint(pos);
                closest.y = 0f;

                var diff = pos - closest;
                var dist = diff.magnitude;
                if (dist < 0.001f || dist > 3f) continue;

                var nio = diff.normalized;
                var tio = new Vector3(-nio.z, 0f, nio.x);
                var overlap = Radius - dist;

                var repulsive = A * Mathf.Exp(overlap / B) * nio;

                if (overlap > 0f)
                {
                    var pushing = k * overlap * nio;
                    var vt = Vector3.Dot(velocity, tio);
                    var friction = kappa * overlap * vt * tio;
                    repulsive += pushing - friction;
                    
                    _ma.ReportCollision("Wall");
                }

                force += repulsive;
            }

            return force;
        }
    }
}