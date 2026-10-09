using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class SheepAgent : MonoBehaviour
{
    public enum SheepState { Grazing, Flocking, Fleeing }
    public SheepState CurrentState { get; private set; } = SheepState.Grazing;

    // Arousal: 0 = tranquila, 1 = pánico total
    // Público para que otras ovejas puedan leerlo (liderazgo emergente)
    public float Arousal { get; private set; } = 0f;

    private FlockManager _flock;
    private NavMeshAgent _agent;
    private Animator _animator;

    private float _grazingTimer;
    private float _grazingDuration;
    private bool _initialized = false;

    // ??? HELPER ??????????????????????????????????????????????????????????
    private bool AgentReady => _agent != null
                            && _agent.isActiveAndEnabled
                            && _agent.isOnNavMesh;

    // ??? INIT ?????????????????????????????????????????????????????????????
    public void Init(FlockManager manager)
    {
        _flock = manager;
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();

        _agent.speed = Random.Range(_flock.minSpeed, _flock.maxSpeed);
        _agent.angularSpeed = 200f;
        _agent.stoppingDistance = 0.5f;
        _agent.autoBraking = false; // evita que se frene solo antes de llegar
    }

    // ??? UPDATE ???????????????????????????????????????????????????????????
    void Update()
    {
        if (!AgentReady) return;

        // Inicialización diferida: primer frame seguro
        if (!_initialized)
        {
            SetGrazing();
            _initialized = true;
        }

        float distToPlayer = Vector3.Distance(transform.position,
                                              _flock.playerTransform.position);

        UpdateArousal(distToPlayer);
        UpdateState(distToPlayer);
        UpdateAnimator();
    }

    // ??? AROUSAL ??????????????????????????????????????????????????????????
    void UpdateArousal(float distToPlayer)
    {
        if (distToPlayer < _flock.fleeRadius)
        {
            // Sube rápido cuando el coche está cerca
            Arousal = Mathf.MoveTowards(Arousal, 1f,
                          Time.deltaTime * _flock.arousalRiseSpeed);
        }
        else
        {
            // Baja lentamente cuando el coche se va
            Arousal = Mathf.MoveTowards(Arousal, 0f,
                          Time.deltaTime / _flock.arousalDecayTime);
        }

        // Contagio de pánico: si una vecina muy asustada está cerca, contagia
        if (Arousal < 0.5f)
        {
            foreach (var other in _flock.allSheep)
            {
                if (other == this) continue;
                float d = Vector3.Distance(transform.position, other.transform.position);
                if (d < _flock.separationRadius * 2f && other.Arousal > 0.7f)
                {
                    // Se contagia proporcionalmente al arousal del vecino
                    Arousal = Mathf.MoveTowards(Arousal, other.Arousal,
                                  Time.deltaTime * _flock.arousalRiseSpeed * 0.5f);
                    break;
                }
            }
        }
    }

    // ??? MÁQUINA DE ESTADOS ???????????????????????????????????????????????
    void UpdateState(float distToPlayer)
    {
        switch (CurrentState)
        {
            case SheepState.Grazing:
                // Entra en Fleeing si el coche está muy cerca
                if (distToPlayer < _flock.fleeRadius) { SetFleeing(); return; }
                // Entra en Flocking si el coche está cerca O sigue nerviosa
                if (distToPlayer < _flock.flockRadius || Arousal > 0.3f) { SetFlocking(); return; }
                GrazingUpdate();
                break;

            case SheepState.Flocking:
                if (distToPlayer < _flock.fleeRadius) { SetFleeing(); return; }
                // Vuelve a pastar solo si está lejos Y completamente calmada
                if (distToPlayer > _flock.flockRadius + 3f && Arousal < 0.1f) { SetGrazing(); return; }
                FlockingUpdate();
                break;

            case SheepState.Fleeing:
                // Sale de Fleeing solo si el coche se ha ido Y el arousal ha bajado bastante
                if (distToPlayer > _flock.fleeRadius + 2f && Arousal < 0.5f)
                {
                    _agent.speed = _flock.minSpeed + 1f;
                    SetFlocking();
                    return;
                }
                FleeingUpdate();
                break;
        }
    }

    // ??? GRAZING ??????????????????????????????????????????????????????????
    void SetGrazing()
    {
        CurrentState = SheepState.Grazing;
        _grazingDuration = Random.Range(3f, 8f);
        _grazingTimer = 0f;
        if (AgentReady) _agent.isStopped = true;
    }

    void GrazingUpdate()
    {
        _grazingTimer += Time.deltaTime;
        if (_grazingTimer >= _grazingDuration)
        {
            // Paseo aleatorio corto
            Vector3 wander = transform.position
                           + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized
                           * Random.Range(2f, 5f);

            if (NavMesh.SamplePosition(wander, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                _agent.isStopped = false;
                _agent.speed = _flock.minSpeed;
                _agent.SetDestination(hit.position);
            }
            SetGrazing();
        }
    }

    // ??? FLOCKING ?????????????????????????????????????????????????????????
    void SetFlocking()
    {
        CurrentState = SheepState.Flocking;
        if (AgentReady) _agent.isStopped = false;
    }

    void FlockingUpdate()
    {
        // La velocidad escala con el arousal (más nerviosa = más rápida)
        _agent.speed = Mathf.Lerp(_flock.minSpeed, _flock.maxSpeed, Arousal);

        Vector3 boidForce = CalculateBoids();
        Vector3 target = transform.position + boidForce * 2f;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    // ??? FLEEING ??????????????????????????????????????????????????????????
    void SetFleeing()
    {
        CurrentState = SheepState.Fleeing;
        if (AgentReady)
        {
            _agent.isStopped = false;
            _agent.speed = _flock.maxSpeed * 1.3f;
        }
    }

    void FleeingUpdate()
    {
        Vector3 fleeDir = (transform.position - _flock.playerTransform.position).normalized;
        Vector3 boidForce = CalculateBoids();

        // fleeWeight escala con el arousal — cuanto más asustada, más ignora al grupo
        float dynFleeWeight = _flock.fleeWeight * Arousal;
        Vector3 combined = (fleeDir * dynFleeWeight + boidForce).normalized;

        Vector3 target = transform.position + combined * 5f;
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    // ??? BOIDS CORE ???????????????????????????????????????????????????????
    Vector3 CalculateBoids()
    {
        Vector3 sep = Vector3.zero, ali = Vector3.zero, coh = Vector3.zero;
        float totalInfluence = 0f;

        // El radio de percepción aumenta con el arousal
        float perception = Mathf.Lerp(_flock.perceptionRadiusMin,
                                       _flock.perceptionRadiusMax, Arousal);

        foreach (var other in _flock.allSheep)
        {
            if (other == this) continue;
            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d > perception) continue;

            // Liderazgo emergente: las ovejas más asustadas tienen más influencia
            float influence = 1f + other.Arousal;
            totalInfluence += influence;

            coh += other.transform.position * influence;
            ali += (other.AgentVelocity) * influence;

            // Separación (independiente del arousal)
            if (d < _flock.separationRadius)
                sep += (transform.position - other.transform.position) / Mathf.Max(d, 0.01f);
        }

        if (totalInfluence == 0f) return transform.forward;

        coh = (coh / totalInfluence - transform.position).normalized * _flock.cohesionWeight;
        ali = (ali / totalInfluence).normalized * _flock.alignmentWeight;
        sep = sep.normalized * _flock.separationWeight;

        return (sep + ali + coh).normalized;
    }

    // Propiedad pública para que CalculateBoids de otras ovejas acceda a la velocidad
    public Vector3 AgentVelocity => AgentReady ? _agent.velocity : Vector3.zero;

    // ??? ANIMACIÓN ????????????????????????????????????????????????????????
    void UpdateAnimator()
    {
        if (_animator == null || !AgentReady) return;
        _animator.SetFloat("Speed", _agent.velocity.magnitude);
        _animator.SetFloat("Arousal", Arousal);
        _animator.SetBool("IsFleeing", CurrentState == SheepState.Fleeing);
        _animator.SetBool("IsGrazing", CurrentState == SheepState.Grazing);
    }
}