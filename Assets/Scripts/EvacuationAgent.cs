using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class EvacuationAgent : Agent
{
    public Transform targetTransform;
    public float moveSpeed = 5f;
    public float turnSpeed = 200f;

    private Rigidbody _rb;

    public override void Initialize()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        transform.localPosition = new Vector3(Random.Range(-4f, 4f), 0.5f, Random.Range(-4f, 4f));
        targetTransform.localPosition = new Vector3(Random.Range(-4f, 4f), 0.5f, Random.Range(-4f, 4f));
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.InverseTransformDirection(_rb.linearVelocity));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var moveForward = actions.ContinuousActions[0];
        var turn = actions.ContinuousActions[1];

        _rb.MovePosition(transform.position + transform.forward * moveForward * moveSpeed * Time.fixedDeltaTime);
        transform.Rotate(0f, turn * turnSpeed * Time.fixedDeltaTime, 0f);

        AddReward(-1f / MaxStep); 
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        continuousActionsOut[0] = Input.GetAxis("Vertical");
        continuousActionsOut[1] = Input.GetAxis("Horizontal");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Target")) return;
        SetReward(1.0f);
        EndEpisode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.collider.CompareTag("Wall")) return;
        SetReward(-0.1f);
        EndEpisode();
    }
}