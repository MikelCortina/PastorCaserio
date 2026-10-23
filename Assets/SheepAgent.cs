using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class SheepAgent : MonoBehaviour
{
    public enum SheepState { Grazing, Flocking, Fleeing }
    public SheepState CurrentState { get; private set; } = SheepState.Grazing;
    public float Arousal { get; private set; } = 0f;

    private FlockManager _flock;
    private NavMeshAgent _agent;
    private Animator _animator;

    private float _grazingTimer;
    private float _grazingDuration;
    private bool _initialized = false;

    // ?? Idle Wander ???????????????????????????????????????????????
    private float _stillTimer = 0f;
    private bool _isIdleWandering = false;
    private float _idleWanderCooldown = 0f;

    // ?? Dirección personal de dispersión ?????????????????????????
    private Vector3 _personalDriftDir = Vector3.zero;
    private float _personalDriftTimer = 0f;

    // ?? Food seeking ??????????????????????????????????????????????
    private FoodSource _targetFood = null;
    private bool _isEating = false;
    private float _eatTimer = 0f;
    private float _eatDuration = 0f;

    private bool AgentReady => _agent != null
                            && _agent.isActiveAndEnabled
                            && _agent.isOnNavMesh;

    // ?????????????????????????????????????????????????????????????
    public void Init(FlockManager manager)
    {
        _flock = manager;
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();

        _agent.speed = Random.Range(_flock.minSpeed, _flock.maxSpeed);
        _agent.angularSpeed = 200f;
        _agent.stoppingDistance = 0.5f;
        _agent.autoBraking = false;
        _agent.acceleration = 4f;

        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        _personalDriftDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        _personalDriftTimer = Random.Range(0f, 5f);
    }

    // ?????????????????????????????????????????????????????????????
    void Update()
    {
        if (!AgentReady) return;

        if (!_initialized)
        {
            SetGrazing();
            _initialized = true;
        }

        float distToPlayer = Vector3.Distance(transform.position, _flock.playerTransform.position);

        UpdateArousal(distToPlayer);
        UpdateState(distToPlayer);
        UpdateAnimator();
    }

    // ?????????????????????????????????????????????????????????????
    // AROUSAL
    // ?????????????????????????????????????????????????????????????
    void UpdateArousal(float distToPlayer)
    {
        if (distToPlayer < _flock.fleeRadius)
            Arousal = Mathf.MoveTowards(Arousal, 1f, Time.deltaTime * _flock.arousalRiseSpeed);
        else
            Arousal = Mathf.MoveTowards(Arousal, 0f, Time.deltaTime / _flock.arousalDecayTime);

        if (Arousal < 0.5f)
        {
            foreach (var other in _flock.allSheep)
            {
                if (other == this) continue;
                float d = Vector3.Distance(transform.position, other.transform.position);
                if (d < _flock.separationRadius * 2f && other.Arousal > 0.7f)
                {
                    Arousal = Mathf.MoveTowards(Arousal, other.Arousal,
                                  Time.deltaTime * _flock.arousalRiseSpeed * 0.5f);
                    break;
                }
            }
        }
    }

    // ?????????????????????????????????????????????????????????????
    // ESTADOS
    // ?????????????????????????????????????????????????????????????
    void UpdateState(float distToPlayer)
    {
        switch (CurrentState)
        {
            case SheepState.Grazing:
                if (distToPlayer < _flock.fleeRadius) { SetFleeing(); return; }
                if (distToPlayer < _flock.flockRadius || Arousal > 0.3f) { SetFlocking(); return; }
                GrazingUpdate();
                break;

            case SheepState.Flocking:
                if (distToPlayer < _flock.fleeRadius) { SetFleeing(); return; }
                if (distToPlayer > _flock.flockRadius + 3f && Arousal < 0.1f) { SetGrazing(); return; }
                FlockingUpdate();
                break;

            case SheepState.Fleeing:
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

    // ?????????????????????????????????????????????????????????????
    // GRAZING
    // ?????????????????????????????????????????????????????????????
    void SetGrazing()
    {
        CurrentState = SheepState.Grazing;
        _grazingDuration = Random.Range(3f, 8f);
        _grazingTimer = 0f;
        _stillTimer = 0f;

        // No activar IdleWander inmediatamente — esperar idleRelaxTime en GrazingUpdate
        _isIdleWandering = false;
        _idleWanderCooldown = Random.Range(0f, _flock.idleWanderInterval);

        if (AgentReady) _agent.isStopped = true;
    }

    void GrazingUpdate()
    {
        bool playerFar = Vector3.Distance(transform.position, _flock.playerTransform.position)
                          > _flock.flockRadius;
        bool calmEnough = Arousal < 0.15f;

        // Acumular tiempo tranquilo hasta idleRelaxTime
        if (!_isIdleWandering)
        {
            if (playerFar && calmEnough)
            {
                _stillTimer += Time.deltaTime;
                if (_stillTimer >= _flock.idleRelaxTime)
                {
                    _isIdleWandering = true;
                    _idleWanderCooldown = 0f;
                }
            }
            else
            {
                _stillTimer = 0f;
            }
        }

        // Desactivar campeo si aparece amenaza
        if (_isIdleWandering && (!playerFar || !calmEnough))
        {
            _isIdleWandering = false;
            _stillTimer = 0f;
            _agent.isStopped = true;
        }

        // Detectar llegada a fuente de comida
        if (_targetFood != null && !_isEating)
        {
            float distToFood = Vector3.Distance(transform.position, _targetFood.transform.position);
            bool arrived = distToFood < 1.5f
                        || (!_agent.pathPending && _agent.remainingDistance < 1.5f);
            if (arrived)
            {
                _isEating = true;
                _eatTimer = 0f;
                _eatDuration = Random.Range(3f, 7f);
                _agent.isStopped = true;
            }
        }

        if (_isIdleWandering)
        {
            IdleWanderUpdate();
            return;
        }

        // Paseo pasivo mientras espera — NO llama SetGrazing() para no resetear _stillTimer
        _grazingTimer += Time.deltaTime;
        if (_grazingTimer >= _grazingDuration)
        {
            _grazingTimer = 0f;
            _grazingDuration = Random.Range(3f, 8f);

            Vector3 wander = transform.position
                           + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized
                           * Random.Range(2f, 5f);

            if (NavMesh.SamplePosition(wander, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                _agent.isStopped = false;
                _agent.speed = _flock.minSpeed;
                _agent.SetDestination(hit.position);
            }
        }
    }

    // ?????????????????????????????????????????????????????????????
    // IDLE WANDER (campeo con dirección personal + radio límite)
    // ?????????????????????????????????????????????????????????????
    void IdleWanderUpdate()
    {
        if (_isEating) { EatingUpdate(); return; }

        _idleWanderCooldown -= Time.deltaTime;
        if (_idleWanderCooldown > 0f) return;

        _idleWanderCooldown = _flock.idleWanderInterval + Random.Range(-0.3f, 0.8f);

        // Buscar flor/comida prioritaria cercana
        FoodSource nearestFood = FindBestFood();
        if (nearestFood != null) { MoveToFood(nearestFood); return; }

        if (Random.value < 0.25f) { _agent.isStopped = true; return; }

        float distFromCentroid = Vector3.Distance(transform.position, _flock.FlockCentroid);
        bool overLimit = distFromCentroid > _flock.idleMaxSpreadRadius;

        Vector3 driftDir;

        if (overLimit)
        {
            // Fuera del radio límite: volver hacia el centroide
            Vector3 backDir = (_flock.FlockCentroid - transform.position).normalized;
            Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            driftDir = Vector3.Lerp(backDir, randomDir, 0.2f).normalized;
        }
        else
        {
            // Renovar dirección personal cada 4–7 segundos
            _personalDriftTimer -= Time.deltaTime;
            if (_personalDriftTimer <= 0f || _personalDriftDir == Vector3.zero)
            {
                _personalDriftTimer = Random.Range(4f, 7f);

                Vector3 awayFromCenter = transform.position - _flock.FlockCentroid;

                if (awayFromCenter.magnitude > 1.5f)
                {
                    // Oveja fuera del centro exacto: alejarse con variación perpendicular
                    Vector3 awayDir = awayFromCenter.normalized;
                    Vector3 randPerp = Vector3.Cross(awayDir, Vector3.up).normalized
                                     * Random.Range(-0.6f, 0.6f);
                    _personalDriftDir = (awayDir + randPerp).normalized;
                }
                else
                {
                    // Oveja EN el centroide: dirección aleatoria fija garantizada
                    float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    _personalDriftDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }
            }

            // Bias se reduce cerca del límite (barrera blanda)
            float proximityFactor = Mathf.InverseLerp(_flock.idleMaxSpreadRadius,
                                                       _flock.idleMaxSpreadRadius * 0.6f,
                                                       distFromCentroid);
            float effectiveBias = _flock.idleSpreadBias * proximityFactor;

            Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            driftDir = Vector3.Lerp(randomDir, _personalDriftDir, effectiveBias).normalized;
        }

        float stepDist = Random.Range(_flock.idleWanderRadius * 0.6f, _flock.idleWanderRadius);
        Vector3 candidate = transform.position + driftDir * stepDist;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, stepDist * 0.5f + 1f, NavMesh.AllAreas))
        {
            _agent.isStopped = false;
            _agent.speed = _flock.idleWanderSpeed * Random.Range(0.8f, 1.2f);
            _agent.SetDestination(hit.position);
        }
        else
        {
            Vector3 fallback = transform.position
                             + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * 2f;
            if (NavMesh.SamplePosition(fallback, out NavMeshHit fallbackHit, 3f, NavMesh.AllAreas))
            {
                _agent.isStopped = false;
                _agent.speed = _flock.idleWanderSpeed;
                _agent.SetDestination(fallbackHit.position);
            }
            else _agent.isStopped = true;
        }
    }

    // ?????????????????????????????????????????????????????????????
    // FOOD: búsqueda, movimiento y comer
    // ?????????????????????????????????????????????????????????????
    FoodSource FindBestFood()
    {
        FoodSource best = null;
        float bestScore = -1f;

        foreach (var food in _flock.foodSources)
        {
            if (food == null || !food.IsAvailable) continue;
            float d = Vector3.Distance(transform.position, food.transform.position);
            if (d > food.detectionRadius) continue;

            float score = food.attractionPriority / Mathf.Max(d, 0.1f);
            if (score > bestScore) { bestScore = score; best = food; }
        }
        return best;
    }

    // Fuerza de atracción suave hacia la mejor flor cercana
    Vector3 CalculateFlowerAttraction(float flowerBiasFactor)
    {
        FoodSource best = null;
        float bestScore = -1f;

        foreach (var food in _flock.foodSources)
        {
            if (food == null || !food.IsAvailable) continue;
            if (food.attractionPriority < 2f) continue;
            float d = Vector3.Distance(transform.position, food.transform.position);
            if (d > food.detectionRadius) continue;

            float score = food.attractionPriority / Mathf.Max(d, 0.1f);
            if (score > bestScore) { bestScore = score; best = food; }
        }

        if (best == null) return Vector3.zero;

        float dist = Vector3.Distance(transform.position, best.transform.position);
        Vector3 toFood = (best.transform.position - transform.position).normalized;
        float proximity = Mathf.InverseLerp(best.detectionRadius, 0f, dist);
        return toFood * proximity * flowerBiasFactor * best.attractionPriority;
    }

    void MoveToFood(FoodSource food)
    {
        if (!food.TryOccupy()) return;
        _targetFood = food;

        if (NavMesh.SamplePosition(food.transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            _agent.isStopped = false;
            _agent.speed = _flock.idleWanderSpeed * 1.5f;
            _agent.SetDestination(hit.position);
        }
    }

    void EatingUpdate()
    {
        _eatTimer += Time.deltaTime;
        if (_eatTimer >= _eatDuration)
        {
            if (_targetFood != null) { _targetFood.Release(); _targetFood = null; }
            _isEating = false;
            _eatTimer = 0f;
            _idleWanderCooldown = _flock.idleWanderInterval;
        }
    }

    // ?????????????????????????????????????????????????????????????
    // FLOCKING
    // ?????????????????????????????????????????????????????????????
    void SetFlocking()
    {
        CurrentState = SheepState.Flocking;
        if (AgentReady) _agent.isStopped = false;
    }

    void FlockingUpdate()
    {
        if (_isEating) { EatingUpdate(); return; }

        if (_targetFood != null)
        {
            float distToFood = Vector3.Distance(transform.position, _targetFood.transform.position);
            bool arrived = distToFood < 1.5f
                        || (!_agent.pathPending && _agent.remainingDistance < 1.5f);
            if (arrived)
            {
                _isEating = true;
                _eatTimer = 0f;
                _eatDuration = Random.Range(2f, 5f);
                _agent.isStopped = true;
                return;
            }
        }

        _agent.speed = Mathf.Lerp(_flock.minSpeed, _flock.maxSpeed, Arousal);

        Vector3 boidForce = CalculateBoids();
        Vector3 gravityForce = CalculateFlockGravity(panicMode: false);
        Vector3 antiSplit = CalculateAntiSplit();

        float flowerBias = Mathf.Lerp(0.4f, 0f, Arousal);
        Vector3 flowerForce = CalculateFlowerAttraction(flowerBias);

        Vector3 combined = (boidForce + gravityForce + antiSplit + flowerForce).normalized;
        Vector3 target = transform.position + combined * 2f;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);

        FoodSource urgentFood = FindBestFood();
        if (urgentFood != null && Arousal < 0.3f && _targetFood == null)
            MoveToFood(urgentFood);
    }

    // ?????????????????????????????????????????????????????????????
    // FLEEING
    // ?????????????????????????????????????????????????????????????
    void SetFleeing()
    {
        CurrentState = SheepState.Fleeing;

        if (_targetFood != null) { _targetFood.Release(); _targetFood = null; }
        _isEating = false;

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
        Vector3 gravityForce = CalculateFlockGravity(panicMode: true);
        Vector3 antiSplit = CalculateAntiSplit();

        float flowerBias = Mathf.Lerp(0.15f, 0f, Arousal);
        Vector3 flowerForce = CalculateFlowerAttraction(flowerBias);

        float dynFleeWeight = _flock.fleeWeight * Arousal;
        Vector3 combined = (fleeDir * dynFleeWeight
                             + boidForce
                             + gravityForce
                             + antiSplit
                             + flowerForce).normalized;

        Vector3 target = transform.position + combined * 5f;
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    // ?????????????????????????????????????????????????????????????
    // ANTI-SPLIT
    // ?????????????????????????????????????????????????????????????
    Vector3 CalculateAntiSplit()
    {
        if (Arousal < _flock.antiSplitArousalThreshold) return Vector3.zero;

        float dist = Vector3.Distance(transform.position, _flock.FlockCentroid);
        if (dist < _flock.antiSplitRadius) return Vector3.zero;

        Vector3 toCenter = (_flock.FlockCentroid - transform.position).normalized;
        float distanceFactor = Mathf.InverseLerp(_flock.antiSplitRadius,
                                                   _flock.antiSplitRadius * 2f, dist);
        float arousalFactor = Mathf.InverseLerp(_flock.antiSplitArousalThreshold, 1f, Arousal);

        return toCenter * _flock.antiSplitWeight * distanceFactor * arousalFactor;
    }

    // ?????????????????????????????????????????????????????????????
    // FLOCK GRAVITY
    // ?????????????????????????????????????????????????????????????
    Vector3 CalculateFlockGravity(bool panicMode)
    {
        if (_isIdleWandering) return Vector3.zero;

        Vector3 centroid = _flock.FlockCentroid;
        float dist = Vector3.Distance(transform.position, centroid);
        if (dist < _flock.flockGravityRadius) return Vector3.zero;

        Vector3 toCenter = (centroid - transform.position).normalized;
        float distanceFactor = Mathf.InverseLerp(_flock.flockGravityRadius,
                                                   _flock.flockGravityRadius * 3f, dist);
        distanceFactor = Mathf.Max(distanceFactor, _flock.flockGravityFalloff);

        float panicFactor = panicMode ? Mathf.Lerp(1f, _flock.flockGravityPanicMult, Arousal) : 1f;
        float spreadFactor = Mathf.Clamp(
            _flock.FlockSpreadRadius / (_flock.flockGravityRadius * 2f), 1f, 2f);

        return toCenter * _flock.flockGravityWeight * distanceFactor * panicFactor * spreadFactor;
    }

    // ?????????????????????????????????????????????????????????????
    // BOIDS CORE
    // Cohesión desactivada durante IdleWander para permitir dispersión libre
    // ?????????????????????????????????????????????????????????????
    Vector3 CalculateBoids()
    {
        float cohesionMult = _isIdleWandering ? 0f : 1f;
        float alignmentMult = _isIdleWandering ? 0.2f : 1f;

        Vector3 sep = Vector3.zero, ali = Vector3.zero, coh = Vector3.zero;
        float totalInfluence = 0f;

        float perception = Mathf.Lerp(_flock.perceptionRadiusMin,
                                       _flock.perceptionRadiusMax, Arousal);

        int densityCount = 0;
        float expectedDensity = _flock.flockSize * 0.3f;

        foreach (var other in _flock.allSheep)
        {
            if (other == this) continue;
            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d > perception) continue;

            float influence = 1f + other.Arousal;
            totalInfluence += influence;

            coh += other.transform.position * influence;
            ali += other.AgentVelocity * influence;

            if (d < _flock.separationRadius)
                sep += (transform.position - other.transform.position) / Mathf.Max(d, 0.01f);

            if (d < _flock.densityStressRadius) densityCount++;
        }

        if (totalInfluence == 0f) return transform.forward;

        coh = (coh / totalInfluence - transform.position).normalized * _flock.cohesionWeight * cohesionMult;
        ali = (ali / totalInfluence).normalized * _flock.alignmentWeight * alignmentMult;
        sep = sep.normalized * _flock.separationWeight;

        Vector3 steering = sep + ali + coh;

        float localDensity = densityCount / Mathf.Max(1f, expectedDensity);
        float panicMult = Mathf.Lerp(1f, _flock.densityStressPanicMult, Arousal);
        float effectiveThresh = _flock.densityStressThreshold * panicMult;

        if (localDensity > effectiveThresh)
        {
            Vector3 crowdCenter = GetLocalCrowdCenter(_flock.densityStressRadius);
            Vector3 disperseDir = (transform.position - crowdCenter).normalized;
            float intensity = Mathf.InverseLerp(effectiveThresh, 1f, localDensity);
            steering += disperseDir * _flock.dispersionWeight * intensity;
        }

        return steering.normalized;
    }

    Vector3 GetLocalCrowdCenter(float radius)
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        foreach (var sheep in _flock.allSheep)
        {
            if (sheep == this) continue;
            if (Vector3.Distance(transform.position, sheep.transform.position) < radius)
            { center += sheep.transform.position; count++; }
        }
        return count > 0 ? center / count : transform.position;
    }

    public Vector3 AgentVelocity => AgentReady ? _agent.velocity : Vector3.zero;

    // ?????????????????????????????????????????????????????????????
    // ANIMACIÓN
    // ?????????????????????????????????????????????????????????????
    void UpdateAnimator()
    {
        if (_animator == null || !AgentReady) return;
        _animator.SetFloat("Speed", _agent.velocity.magnitude);
        _animator.SetFloat("Arousal", Arousal);
        _animator.SetBool("IsFleeing", CurrentState == SheepState.Fleeing);
        _animator.SetBool("IsGrazing", CurrentState == SheepState.Grazing);
        _animator.SetBool("IsIdleWandering", _isIdleWandering);
        _animator.SetBool("IsEating", _isEating);
    }
}

// ?????????????????????????????????????????????????????????????????
public static class Vector2Ext
{
    public static Vector3 ToVector3XZ(this Vector2 v) => new Vector3(v.x, 0f, v.y);
}