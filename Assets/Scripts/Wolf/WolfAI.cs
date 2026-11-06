using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class WolfAI : MonoBehaviour
{
    public enum WolfState
    {
        Idle,
        Stalking,
        Chasing,
        Grabbing,
        CarryingSheep,
        Leaving
    }

    [Header("State")]
    public WolfState currentState = WolfState.Idle;

    [Header("References")]
    public FlockManager flockManager;
    public Transform exitPoint;

    [Header("Detection")]
    public float sheepDetectionRadius = 20f;
    public float fearRadius = 10f;
    public float attackDistance = 2.5f;

    [Header("Movement")]
    public float stalkingSpeed = 3.2f;
    public float chasingSpeed = 7f;
    public float carryingSpeed = 10f;

    [Header("Grab")]
    public float grabDelay = 0.5f;
    public Vector3 carriedSheepLocalOffset = new Vector3(0f, 0.2f, 1.1f);

    [Header("Debug")]
    public bool activateAutomaticallyForTest = false;
    public float automaticActivationDelay = 2f;
    public bool logDebug = true;

    private NavMeshAgent _agent;
    private SheepAgent _targetSheep;
    private SheepAgent _carriedSheep;
    private Transform _carriedSheepOriginalParent;

    private float _grabTimer;
    private bool _hasBeenActivated;

    private bool AgentReady => _agent != null
                            && _agent.isActiveAndEnabled
                            && _agent.isOnNavMesh;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        if (_agent != null)
        {
            _agent.angularSpeed = 360f;
            _agent.acceleration = 20f;
            _agent.stoppingDistance = 0.6f;
            _agent.autoBraking = true;
        }
    }

    private void Start()
    {
        if (!_hasBeenActivated)
        {
            currentState = WolfState.Idle;

            if (AgentReady)
            {
                _agent.isStopped = true;
            }
        }

        if (activateAutomaticallyForTest)
        {
            Invoke(nameof(ActivateWolf), automaticActivationDelay);
        }
    }

    private void Update()
    {
        if (!AgentReady)
        {
            return;
        }

        if (currentState != WolfState.Idle && currentState != WolfState.CarryingSheep && currentState != WolfState.Leaving)
        {
            ScareNearbySheep();
        }

        switch (currentState)
        {
            case WolfState.Idle:
                UpdateIdle();
                break;

            case WolfState.Stalking:
                UpdateStalking();
                break;

            case WolfState.Chasing:
                UpdateChasing();
                break;

            case WolfState.Grabbing:
                UpdateGrabbing();
                break;

            case WolfState.CarryingSheep:
                UpdateCarryingSheep();
                break;

            case WolfState.Leaving:
                UpdateLeaving();
                break;
        }
    }

    public void ActivateWolf()
    {
        if (flockManager == null)
        {
            Debug.LogWarning("WolfAI: falta asignar FlockManager.");
            return;
        }

        if (!AgentReady)
        {
            Debug.LogWarning("WolfAI: el NavMeshAgent no está sobre el NavMesh.");
            return;
        }

        _hasBeenActivated = true;
        _targetSheep = null;
        _carriedSheep = null;

        _agent.isStopped = false;
        _agent.speed = stalkingSpeed;

        currentState = WolfState.Stalking;

        if (logDebug)
        {
            Debug.Log("WolfAI: lobo activado. Estado = Stalking.");
        }
    }

    private void UpdateIdle()
    {
        _agent.isStopped = true;
    }

    private void UpdateStalking()
    {
        if (flockManager == null)
        {
            return;
        }

        _agent.isStopped = false;
        _agent.speed = stalkingSpeed;

        Vector3 destination = flockManager.FlockCentroid;

        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            _agent.SetDestination(hit.position);
        }

        _targetSheep = FindBestTargetSheep();

        if (_targetSheep != null)
        {
            currentState = WolfState.Chasing;

            if (logDebug)
            {
                Debug.Log("WolfAI: objetivo encontrado. Estado = Chasing.");
            }
        }
    }

    private void UpdateChasing()
    {
        if (_targetSheep == null)
        {
            _targetSheep = FindBestTargetSheep();

            if (_targetSheep == null)
            {
                StartLeavingWithoutSheep();
                return;
            }
        }

        _agent.isStopped = false;
        _agent.speed = chasingSpeed;

        Vector3 sheepPosition = _targetSheep.transform.position;

        if (NavMesh.SamplePosition(sheepPosition, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            _agent.SetDestination(hit.position);
        }

        float distance = Vector3.Distance(transform.position, sheepPosition);

        if (distance <= attackDistance)
        {
            _agent.isStopped = true;
            _agent.ResetPath();

            _targetSheep.CatchByWolf();

            _grabTimer = 0f;
            currentState = WolfState.Grabbing;

            if (logDebug)
            {
                Debug.Log("WolfAI: oveja mordida. Preparando arrastre.");
            }
        }
    }

    private void UpdateGrabbing()
    {
        _grabTimer += Time.deltaTime;

        if (_targetSheep == null)
        {
            StartLeavingWithoutSheep();
            return;
        }

        FaceTarget(_targetSheep.transform.position);

        if (_grabTimer >= grabDelay)
        {
            GrabSheep();
            StartCarryingSheep();
        }
    }

    private void GrabSheep()
    {
        if (_targetSheep == null)
        {
            return;
        }

        _carriedSheep = _targetSheep;
        _targetSheep = null;

        _carriedSheep.DisableMovementCompletely();

        if (flockManager != null)
        {
            flockManager.allSheep.Remove(_carriedSheep);
        }

        _carriedSheepOriginalParent = _carriedSheep.transform.parent;

        _carriedSheep.transform.SetParent(transform);
        _carriedSheep.transform.localPosition = carriedSheepLocalOffset;
        _carriedSheep.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        if (logDebug)
        {
            Debug.Log("WolfAI: oveja agarrada por el lobo.");
        }
    }

    private void StartCarryingSheep()
    {
        if (exitPoint == null)
        {
            Debug.LogWarning("WolfAI: no tiene Exit Point. Desaparecen lobo y oveja.");
            DestroyCarriedSheepAndWolf();
            return;
        }

        _agent.isStopped = false;
        _agent.ResetPath();
        _agent.speed = carryingSpeed;
        _agent.acceleration = 25f;
        _agent.stoppingDistance = 0.5f;

        if (NavMesh.SamplePosition(exitPoint.position, out NavMeshHit hit, 15f, NavMesh.AllAreas))
        {
            _agent.SetDestination(hit.position);
            currentState = WolfState.CarryingSheep;

            if (logDebug)
            {
                Debug.Log("WolfAI: llevando oveja hacia Exit Point: " + exitPoint.name);
            }
        }
        else
        {
            Debug.LogWarning("WolfAI: Exit Point fuera del NavMesh. Desaparecen lobo y oveja.");
            DestroyCarriedSheepAndWolf();
        }
    }

    private void UpdateCarryingSheep()
    {
        if (_carriedSheep == null)
        {
            StartLeavingWithoutSheep();
            return;
        }

        if (exitPoint == null)
        {
            DestroyCarriedSheepAndWolf();
            return;
        }

        if (!_agent.pathPending && _agent.remainingDistance <= 1.2f)
        {
            Debug.Log("WolfAI: llegó al punto de salida con la oveja.");
            DestroyCarriedSheepAndWolf();
            return;
        }

        float directDistance = Vector3.Distance(transform.position, exitPoint.position);

        if (directDistance <= 2f)
        {
            Debug.Log("WolfAI: llegó cerca del Exit Point con la oveja.");
            DestroyCarriedSheepAndWolf();
        }
    }

    private void StartLeavingWithoutSheep()
    {
        currentState = WolfState.Leaving;

        if (!AgentReady)
        {
            Destroy(gameObject);
            return;
        }

        if (exitPoint == null)
        {
            Destroy(gameObject);
            return;
        }

        _agent.isStopped = false;
        _agent.ResetPath();
        _agent.speed = carryingSpeed;
        _agent.acceleration = 20f;
        _agent.stoppingDistance = 0.5f;

        if (NavMesh.SamplePosition(exitPoint.position, out NavMeshHit hit, 15f, NavMesh.AllAreas))
        {
            _agent.SetDestination(hit.position);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void UpdateLeaving()
    {
        if (exitPoint == null)
        {
            Destroy(gameObject);
            return;
        }

        if (!_agent.pathPending && _agent.remainingDistance <= 1.2f)
        {
            Destroy(gameObject);
            return;
        }

        float directDistance = Vector3.Distance(transform.position, exitPoint.position);

        if (directDistance <= 2f)
        {
            Destroy(gameObject);
        }
    }

    private void DestroyCarriedSheepAndWolf()
    {
        if (_carriedSheep != null)
        {
            _carriedSheep.transform.SetParent(_carriedSheepOriginalParent);
            Destroy(_carriedSheep.gameObject);
        }

        Destroy(gameObject);
    }

    private SheepAgent FindBestTargetSheep()
    {
        if (flockManager == null || flockManager.allSheep == null)
        {
            return null;
        }

        SheepAgent bestSheep = null;
        float bestScore = Mathf.Infinity;

        foreach (SheepAgent sheep in flockManager.allSheep)
        {
            if (sheep == null)
            {
                continue;
            }

            float distanceToWolf = Vector3.Distance(transform.position, sheep.transform.position);

            if (distanceToWolf > sheepDetectionRadius)
            {
                continue;
            }

            float distanceToCentroid = Vector3.Distance(sheep.transform.position, flockManager.FlockCentroid);

            float score = distanceToWolf - distanceToCentroid * 0.7f;

            if (score < bestScore)
            {
                bestScore = score;
                bestSheep = sheep;
            }
        }

        return bestSheep;
    }

    private void ScareNearbySheep()
    {
        if (flockManager == null || flockManager.allSheep == null)
        {
            return;
        }

        foreach (SheepAgent sheep in flockManager.allSheep)
        {
            if (sheep == null)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, sheep.transform.position);

            if (distance <= fearRadius)
            {
                float fear = Mathf.InverseLerp(fearRadius, 0f, distance);
                float fearAmount = Mathf.Lerp(0.55f, 1f, fear);

                sheep.AddThreat(transform, fearAmount);
            }
        }
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            720f * Time.deltaTime
        );
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, sheepDetectionRadius);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, fearRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackDistance);
    }
}