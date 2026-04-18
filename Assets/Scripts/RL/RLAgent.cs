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
        public float relaxationTime = 0.3f;
        [NonSerialized] public float Radius;
        [NonSerialized] public float MaxAgentSpeed;

        [Header("Goal")]
        public Transform goal;

        [Header("Reward")]
        public float goalReward = 3.0f;
        public float timeoutPenalty = -1.0f;
        public float timePenalty = -0.001f;
        public float wallCollisionPenalty = -0.01f;
        public float agentCollisionPenalty = -0.1f;
        public float stagnationPenalty = -0.2f;
        public float distanceRewardScale = 0.5f;
        public float hazardContactPenalty = -3.0f;

        [Header("Stagnation")]
        public int stagnationCheckSteps = 50;
        public float stagnationThreshold = 0.1f;

        [Header("Training")]
        public TrainingEnvironmentManager trainingManager;

        private MetricsAgent _metrics;
        private Vector3 _velocity;
        private bool _wasInHazard;
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

        public Vector3 Velocity => _velocity;

        public void SetInitialVelocity(Vector3 v)
        {
            _velocity = v;
        }

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
            _wasInHazard = false;

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

            ObserveNearestNeighbors(sensor, 3);
        }

        private static readonly (float sqr, RLAgent agent)[] _neighborBuffer = new (float, RLAgent)[32];

        private void ObserveNearestNeighbors(VectorSensor sensor, int k)
        {
            const float searchRadius = 3f;
            var count = Physics.OverlapSphereNonAlloc(
                transform.position + Vector3.up * 0.5f, searchRadius, _agentBuffer, _agentMask);

            var found = 0;
            for (var i = 0; i < count && found < _neighborBuffer.Length; i++)
            {
                var other = _agentBuffer[i].GetComponent<RLAgent>();
                if (!other || other == this) continue;
                var sqr = (other.transform.position - transform.position).sqrMagnitude;
                _neighborBuffer[found++] = (sqr, other);
            }

            for (var i = 1; i < found; i++)
            {
                var cur = _neighborBuffer[i];
                var j = i - 1;
                while (j >= 0 && _neighborBuffer[j].sqr > cur.sqr)
                {
                    _neighborBuffer[j + 1] = _neighborBuffer[j];
                    j--;
                }
                _neighborBuffer[j + 1] = cur;
            }

            for (var i = 0; i < k; i++)
            {
                if (i < found)
                {
                    var other = _neighborBuffer[i].agent;
                    var relPos = other.transform.position - transform.position;
                    relPos.y = 0f;
                    var localRelPos = transform.InverseTransformDirection(relPos);
                    var relVel = other.Velocity - _velocity;
                    var localRelVel = transform.InverseTransformDirection(relVel);
                    sensor.AddObservation(localRelPos.x / searchRadius);
                    sensor.AddObservation(localRelPos.z / searchRadius);
                    sensor.AddObservation(localRelVel.x / MaxAgentSpeed);
                    sensor.AddObservation(localRelVel.z / MaxAgentSpeed);
                }
                else
                {
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                }
            }
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var vxAction = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
            var vzAction = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

            var desiredVelocity = new Vector3(vxAction, 0f, vzAction) * MaxAgentSpeed;
            if (desiredVelocity.magnitude > MaxAgentSpeed)
                desiredVelocity = desiredVelocity.normalized * MaxAgentSpeed;

            _velocity = Vector3.Lerp(_velocity, desiredVelocity, Time.fixedDeltaTime / relaxationTime);

            transform.position += _velocity * Time.fixedDeltaTime;

            if (_velocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(_velocity);

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
            var maxVRelScale = 0f;

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
                    var otherAgent = col.GetComponent<RLAgent>();
                    var vRel = otherAgent ? (_velocity - otherAgent.Velocity).magnitude : _velocity.magnitude;
                    var vRelNorm = vRel / MaxAgentSpeed;
                    var scale = vRelNorm * vRelNorm;
                    if (scale > maxVRelScale)
                        maxVRelScale = scale;

                    var otherMetrics = col.GetComponent<MetricsAgent>();
                    _metrics.ReportCollision("Agent", otherMetrics ? otherMetrics.agentId : -1);
                }
            }

            if (wallHit)
                AddReward(wallCollisionPenalty);
            if (maxVRelScale > 0f)
                AddReward(agentCollisionPenalty * maxVRelScale);
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
            var inHazard = Physics.CheckSphere(transform.position, Radius, _hazardMask);

            if (inHazard && !_wasInHazard)
                AddReward(hazardContactPenalty);

            _wasInHazard = inHazard;
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
                transform.position += normal * (overlap * 0.5f);
                var velInto = Vector3.Dot(_velocity, -normal);
                if (velInto > 0)
                    _velocity += normal * (velInto * 0.5f);
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var ca = actionsOut.ContinuousActions;
            ca[0] = Input.GetAxis("Horizontal");
            ca[1] = Input.GetAxis("Vertical");
        }
    }
}