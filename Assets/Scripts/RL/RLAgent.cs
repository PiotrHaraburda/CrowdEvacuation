using System;
using Metrics;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using Utility;

namespace RL
{
    [RequireComponent(typeof(MetricsAgent))]
    public class RLAgent : Agent
    {
        [Header("Movement")]
        public float maxRotation = 180f;
        [NonSerialized] public float Radius;
        [NonSerialized] public float MaxAgentSpeed;

        [Header("Goal")]
        public Transform goal;

        [Header("Reward")]
        public float goalReward = 3.0f;
        public float timeoutPenalty = -1.0f;
        public float timePenalty = -0.001f;
        public float wallCollisionPenalty = -0.01f;
        public float agentCollisionPenalty = -0.001f;
        public float stagnationPenalty = -0.05f;
        public float distanceRewardScale = 0.5f;
        public float hazardProximityPenalty = -0.05f;
        public float hazardContactPenalty = -0.5f;
        public float hazardDetectionRadius = 3f;

        [Header("Stagnation")]
        public int stagnationCheckSteps = 50;
        public float stagnationThreshold = 0.1f;

        [Header("Training")]
        public TrainingEnvironmentManager trainingManager;

        private MetricsAgent _metrics;
        private Vector3 _velocity;
        private float _prevDistToGoal;
        private Vector3 _stagnationCheckPos;
        private int _stepsSinceStagnationCheck;

        private int _wallLayer;
        private int _agentLayer;
        private int _wallMask;
        private int _agentMask;
        private int _hazardMask;

        private readonly Collider[] _wallBuffer = new Collider[8];
        private readonly Collider[] _agentBuffer = new Collider[32];

        public override void Initialize()
        {
            _metrics = GetComponent<MetricsAgent>();
            _wallLayer = LayerMask.NameToLayer("Wall");
            _agentLayer = LayerMask.NameToLayer("Agent");
            _wallMask = LayerMask.GetMask("Wall");
            _agentMask = LayerMask.GetMask("Agent");
            _hazardMask = LayerMask.GetMask("Hazard");
        }

        public override void OnEpisodeBegin()
        {
            _velocity = Vector3.zero;

            if (trainingManager)
                trainingManager.OnAgentEpisodeBegin(gameObject);

            _prevDistToGoal = goal ? Vector3.Distance(transform.position, goal.position) : 0f;
            _stagnationCheckPos = transform.position;
            _stepsSinceStagnationCheck = 0;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (goal)
            {
                var toGoal = goal.position - transform.position;
                toGoal.y = 0f;
                var localDir = transform.InverseTransformDirection(toGoal.normalized);
                sensor.AddObservation(localDir.x);
                sensor.AddObservation(localDir.z);
                sensor.AddObservation(Mathf.Clamp01(toGoal.magnitude / 20f));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }

            sensor.AddObservation(_velocity.magnitude / MaxAgentSpeed);
            var localVel = transform.InverseTransformDirection(_velocity);
            sensor.AddObservation(localVel.x / MaxAgentSpeed);
            sensor.AddObservation(localVel.z / MaxAgentSpeed);
            sensor.AddObservation(Radius / AgentConfig.MaxRadius);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var forceAction = Mathf.Clamp(actions.ContinuousActions[0], -0.3f, 1f);
            var rotationAction = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

            transform.Rotate(0f, rotationAction * maxRotation * Time.fixedDeltaTime, 0f);

            _velocity += transform.forward * (forceAction * AgentConfig.MaxForce / AgentConfig.Mass * Time.fixedDeltaTime);
            if (_velocity.magnitude > MaxAgentSpeed)
                _velocity = _velocity.normalized * MaxAgentSpeed;
            _velocity *= 1f - 0.5f * Time.fixedDeltaTime;

            transform.position += _velocity * Time.fixedDeltaTime;

            ResolveWallCollisions();
            ResolveAgentCollisions();

            if (_metrics.CheckExit(transform.position, deactivate: !trainingManager))
            {
                AddReward(goalReward);
                if (trainingManager)
                    trainingManager.OnAgentEvacuated(gameObject);
                return;
            }

            RewardCollisions();
            RewardDistance();
            RewardHazard();
            AddReward(timePenalty);
            CheckStagnation();
            CheckTimeout();
        }

        private void RewardCollisions()
        {
            var wallHit = false;
            var agentHit = false;

            var count = Physics.OverlapSphereNonAlloc(
                transform.position + Vector3.up * 0.5f, Radius, _agentBuffer, _agentMask | _wallMask);

            for (var i = 0; i < count; i++)
            {
                var col = _agentBuffer[i];
                if (col.gameObject == gameObject) continue;

                if (col.gameObject.layer == _wallLayer)
                {
                    wallHit = true;
                    _metrics.ReportCollision("Wall");
                }
                else if (col.gameObject.layer == _agentLayer)
                {
                    agentHit = true;
                    var otherMetrics = col.GetComponent<MetricsAgent>();
                    _metrics.ReportCollision("Agent", otherMetrics ? otherMetrics.agentId : -1);
                }
            }

            if (wallHit) 
                AddReward(wallCollisionPenalty);
            if (agentHit) 
                AddReward(agentCollisionPenalty);
        }

        private void RewardDistance()
        {
            if (!goal) return;
            var dist = Vector3.Distance(transform.position, goal.position);
            AddReward((_prevDistToGoal - dist) * distanceRewardScale);
            _prevDistToGoal = dist;
        }

        private void RewardHazard()
        {
            if (Physics.CheckSphere(transform.position, Radius, _hazardMask))
                AddReward(hazardContactPenalty);
            else if (Physics.CheckSphere(transform.position, hazardDetectionRadius, _hazardMask))
                AddReward(hazardProximityPenalty);
        }

        private void CheckStagnation()
        {
            _stepsSinceStagnationCheck++;
            if (_stepsSinceStagnationCheck < stagnationCheckSteps) return;

            if (Vector3.Distance(transform.position, _stagnationCheckPos) < stagnationThreshold)
                AddReward(stagnationPenalty);

            _stagnationCheckPos = transform.position;
            _stepsSinceStagnationCheck = 0;
        }

        private void CheckTimeout()
        {
            if (StepCount < MaxStep - 1) return;

            AddReward(timeoutPenalty);
            if (trainingManager)
                trainingManager.OnAgentTimedOut(gameObject);
            else
                EndEpisode();
        }

        private void ResolveWallCollisions()
        {
            var pos = transform.position;
            var wallCount = Physics.OverlapSphereNonAlloc(
                pos + Vector3.up * 0.5f, Radius, _wallBuffer, _wallMask);

            for (var i = 0; i < wallCount; i++)
            {
                var col = _wallBuffer[i];
                var closest = col.ClosestPoint(pos);
                closest.y = pos.y;
                var diff = pos - closest;
                var dist = diff.magnitude;

                if (dist < Radius && dist > 0.001f)
                {
                    var normal = diff.normalized;
                    transform.position += normal * (Radius - dist);
                    var velIntoWall = Vector3.Dot(_velocity, -normal);
                    if (velIntoWall > 0) _velocity += normal * velIntoWall;
                }
                else if (dist < 0.001f)
                {
                    var away = (pos - col.bounds.center).normalized;
                    away.y = 0;
                    transform.position += away * Radius;
                    _velocity = Vector3.zero;
                }
            }
        }

        private void ResolveAgentCollisions()
        {
            var pos = transform.position;
            var count = Physics.OverlapSphereNonAlloc(
                pos + Vector3.up * 0.5f, Radius, _agentBuffer, _agentMask);

            for (var i = 0; i < count; i++)
            {
                var col = _agentBuffer[i];
                if (col.gameObject == gameObject) continue;

                var otherPos = col.transform.position;
                otherPos.y = pos.y;
                var diff = pos - otherPos;
                var dist = diff.magnitude;

                var otherRadius = Radius;
                var otherAgent = col.GetComponent<RLAgent>();
                if (otherAgent) otherRadius = otherAgent.Radius;

                var overlap = Radius + otherRadius - dist;
                if (overlap <= 0f) continue;

                var normal = dist > 0.001f ? diff / dist : Vector3.right;
                transform.position += normal * (overlap * 0.3f);
                var velInto = Vector3.Dot(_velocity, -normal);
                if (velInto > 0)
                    _velocity += normal * (velInto * 0.5f);
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var ca = actionsOut.ContinuousActions;
            ca[0] = Input.GetAxis("Vertical");
            ca[1] = Input.GetAxis("Horizontal");
        }
    }
}