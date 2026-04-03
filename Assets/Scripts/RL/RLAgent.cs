using Metrics;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace RL
{
    [RequireComponent(typeof(MetricsAgent))]
    public class RLAgent : Agent
    {
        [Header("Movement")] 
        public float maxSpeed = 1.74f; // 1.74 = 1.34 * 1.3 from Weldmann 1993
        public float maxForce = 214f; // 80kg * 1.34 m/s / 0.5s - same as SFM Helbing 2000
        public float maxRotation = 180f; // per second
        public float mass = 80f;
        public float radius = 0.23f;

        [Header("Goal")] 
        public Transform goal;

        [Header("Reward tuning")] 
        public float goalReward = 1.0f;
        public float wallCollisionPenalty = -0.005f;
        public float agentCollisionPenalty = -0.1f;
        public float timePenalty = -0.001f;
        public float stagnationPenalty = -0.01f;
        public float distanceRewardScale = 0.5f;
        public float hazardPenalty = -0.05f;
        public float hazardDetectionRadius = 3f;
        public float hazardDirectPenalty = -0.5f;
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
        private int _overlapMask;
        private int _hazardMask;

        private readonly Collider[] _wallBuffer = new Collider[8];
        private readonly Collider[] _overlapBuffer = new Collider[16];

        public override void Initialize()
        {
            _metrics = GetComponent<MetricsAgent>();
            _wallLayer = LayerMask.NameToLayer("Wall");
            _agentLayer = LayerMask.NameToLayer("Agent");
            _wallMask = LayerMask.GetMask("Wall");
            _overlapMask = LayerMask.GetMask("Wall", "Agent");
            _hazardMask = LayerMask.GetMask("Hazard");
        }

        public override void OnEpisodeBegin()
        {
            _velocity = Vector3.zero;

            if (trainingManager)
                trainingManager.OnAgentEpisodeBegin(gameObject);

            _prevDistToGoal = goal
                ? Vector3.Distance(transform.position, goal.position)
                : 0f;
            _stagnationCheckPos = transform.position;
            _stepsSinceStagnationCheck = 0;
        }

        // Observations (6 floats) + Ray Perception (automatic)
        public override void CollectObservations(VectorSensor sensor)
        {
            if (goal)
            {
                var toGoal = goal.position - transform.position;
                toGoal.y = 0f;
                var dist = toGoal.magnitude;
                var localDir = transform.InverseTransformDirection(toGoal.normalized);
                sensor.AddObservation(localDir.x);
                sensor.AddObservation(localDir.z);
                sensor.AddObservation(Mathf.Clamp01(dist / 20f));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }

            sensor.AddObservation(_velocity.magnitude / maxSpeed);

            var localVel = transform.InverseTransformDirection(_velocity);
            sensor.AddObservation(localVel.x / maxSpeed);
            sensor.AddObservation(localVel.z / maxSpeed);
        }

        // Actions: continuous [0]=force forward/back, [1]=rotation
        public override void OnActionReceived(ActionBuffers actions)
        {
            var forceAction = Mathf.Clamp(actions.ContinuousActions[0], -0.3f, 1f);
            var rotationAction = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

            var rotation = rotationAction * maxRotation * Time.fixedDeltaTime;
            transform.Rotate(0f, rotation, 0f);

            var force = transform.forward * (forceAction * maxForce);
            var acceleration = force / mass;
            _velocity += acceleration * Time.fixedDeltaTime;

            if (_velocity.magnitude > maxSpeed)
                _velocity = _velocity.normalized * maxSpeed;

            _velocity *= 1f - 0.5f * Time.fixedDeltaTime;

            transform.position += _velocity * Time.fixedDeltaTime;

            ResolveWallCollisions();

            if (_metrics.CheckExit(transform.position, deactivate: false))
            {
                AddReward(goalReward);
                if (trainingManager)
                    trainingManager.OnAgentEvacuated(gameObject);
                else
                    EndEpisode();
                return;
            }

            var wallHit = false;
            var agentHit = false;
            var overlapCount = Physics.OverlapSphereNonAlloc(
                transform.position + Vector3.up * 0.5f, radius * 2f, _overlapBuffer, _overlapMask);

            for (var i = 0; i < overlapCount; i++)
            {
                var col = _overlapBuffer[i];
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
                    var otherId = otherMetrics ? otherMetrics.agentId : -1;
                    _metrics.ReportCollision("Agent", otherId);
                }
            }

            if (wallHit) AddReward(wallCollisionPenalty);
            if (agentHit) AddReward(agentCollisionPenalty);

            if (goal)
            {
                var distToGoal = Vector3.Distance(transform.position, goal.position);
                var distDelta = _prevDistToGoal - distToGoal;
                AddReward(distDelta * distanceRewardScale);
                _prevDistToGoal = distToGoal;
            }

            AddReward(timePenalty);

            if (Physics.CheckSphere(transform.position, radius, _hazardMask))
                AddReward(hazardDirectPenalty);
            else if (Physics.CheckSphere(transform.position, hazardDetectionRadius, _hazardMask))
                AddReward(hazardPenalty);

            _stepsSinceStagnationCheck++;
            if (_stepsSinceStagnationCheck >= stagnationCheckSteps)
            {
                var moved = Vector3.Distance(transform.position, _stagnationCheckPos);
                if (moved < stagnationThreshold)
                    AddReward(stagnationPenalty);
                _stagnationCheckPos = transform.position;
                _stepsSinceStagnationCheck = 0;
            }

            if (StepCount >= MaxStep - 1)
            {
                if (trainingManager)
                    trainingManager.OnAgentTimedOut(gameObject);
            }
        }

        private void ResolveWallCollisions()
        {
            var pos = transform.position;
            var wallCount = Physics.OverlapSphereNonAlloc(
                pos + Vector3.up * 0.5f, radius, _wallBuffer, _wallMask);

            for (var i = 0; i < wallCount; i++)
            {
                var col = _wallBuffer[i];
                var closest = col.ClosestPoint(pos);
                closest.y = pos.y;
                var diff = pos - closest;
                var dist = diff.magnitude;

                if (dist < radius && dist > 0.001f)
                {
                    var normal = diff.normalized;
                    var pushback = normal * (radius - dist);
                    transform.position += pushback;

                    var velIntoWall = Vector3.Dot(_velocity, -normal);
                    if (velIntoWall > 0)
                        _velocity += normal * velIntoWall;
                }
                else if (dist < 0.001f)
                {
                    var wallCenter = col.bounds.center;
                    wallCenter.y = pos.y;
                    var away = (pos - wallCenter).normalized;
                    transform.position += away * radius;
                    _velocity = Vector3.zero;
                }
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