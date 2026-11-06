using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Oveja individual. Se mueve con steering propio (boids + modelo de pastoreo de Strömbom)
/// y usa NavMeshAgent.Move solo para quedar pegada al NavMesh, de modo que nunca atraviesa
/// bordes ni las zarzas (que abren huecos en el NavMesh con carving).
///
/// Estados:
///  Grazing     – pasta: pasea despacio, se para a comer hierba, no se aleja del rebaño.
///  Alert       – el jugador está cerca: levanta la cabeza, le mira y se arrima a las demás.
///  Fleeing     – el jugador (o un lobo) entra en su zona de fuga: huye hacia el centro de sus vecinas.
///  GoingToFood – va a comer una rosa (o sigue a una vecina que va a comer).
///  Eating      – comiendo una rosa.
///  Caught      – atrapada por el lobo.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class SheepAgent : MonoBehaviour
{
    public enum SheepState { Grazing, Alert, Fleeing, GoingToFood, Eating, Caught }

    public SheepState CurrentState { get; private set; } = SheepState.Grazing;
    /// <summary>Miedo actual 0..1.</summary>
    public float Fear { get; private set; }
    /// <summary>Alias de <see cref="Fear"/> (compatibilidad).</summary>
    public float Arousal => Fear;
    public Vector3 Velocity => _velocity;
    public FoodSource TargetFood => _food;
    /// <summary>True cuando la oveja ya ha entrado en el corral: a partir de ahí no puede salir.</summary>
    public bool IsPenned { get; private set; }

    private FlockManager _flock;
    private NavMeshAgent _agent;
    private Animator _animator;

    private Vector3 _velocity;
    private float _bodyRadius = 0.5f;

    // Personalidad (cada oveja es un poco distinta)
    private float _boldness = 1f;
    private float _speedMul = 1f;
    private float _fearRiseMul = 1f;

    // Amenazas
    private Vector3 _awayDir;
    private float _playerPressure;
    private bool _playerInAlert;
    private Transform _extThreat;
    private float _extThreatAmount;
    private float _extThreatTimer;

    // Pastar
    private Vector3 _wanderTarget;
    private bool _hasWanderTarget;
    private float _grazeTimer;

    // Comida
    private FoodSource _food;
    private int _foodSlot;
    private bool _eating;
    private float _foodSearchTimer;
    private float _foodCooldown; // tras renunciar a una rosa, espera antes de volver a intentarlo

    private bool _caught;
    private readonly List<SheepAgent> _neighbors = new List<SheepAgent>();
    private readonly List<float> _neighborDist = new List<float>();

    // Animator (solo se escriben los parámetros que existan)
    private int _pSpeed = -1, _pFleeing = -1, _pGrazing = -1, _pEating = -1;

    private bool AgentReady => _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh;

    // ───────────────────────── Inicialización ─────────────────────────

    public void Init(FlockManager manager)
    {
        _flock = manager;
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();

        _agent.updateRotation = false;
        _agent.autoBraking = false;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            _agent.Warp(hit.position);

        _bodyRadius = Mathf.Max(0.3f, _agent.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z));

        _boldness = Random.Range(0.85f, 1.2f);
        _speedMul = Random.Range(0.9f, 1.1f);
        _fearRiseMul = Random.Range(0.75f, 1.25f);
        _grazeTimer = Random.Range(0f, _flock.grazeTimeMax);

        CacheAnimatorParams();
    }

    void CacheAnimatorParams()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null) return;
        foreach (var p in _animator.parameters)
        {
            if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) _pSpeed = p.nameHash;
            if (p.name == "IsFleeing" && p.type == AnimatorControllerParameterType.Bool) _pFleeing = p.nameHash;
            if (p.name == "IsGrazing" && p.type == AnimatorControllerParameterType.Bool) _pGrazing = p.nameHash;
            if (p.name == "IsEating" && p.type == AnimatorControllerParameterType.Bool) _pEating = p.nameHash;
        }
    }

    // ───────────────────────── API pública (lobo) ─────────────────────────

    /// <summary>Asusta a la oveja con una amenaza externa (lobo).</summary>
    public void AddThreat(Transform threat, float fearAmount)
    {
        if (threat == null || _caught) return;
        _extThreat = threat;
        _extThreatAmount = Mathf.Max(_extThreatAmount, Mathf.Clamp01(fearAmount));
        _extThreatTimer = 1.5f;
    }

    public void CatchByWolf()
    {
        if (_caught) return;
        _caught = true;
        CurrentState = SheepState.Caught;
        Fear = 1f;
        _velocity = Vector3.zero;
        ReleaseFood();
        if (AgentReady) _agent.velocity = Vector3.zero;
        Animate();
    }

    public void DisableMovementCompletely()
    {
        CatchByWolf();
        if (_agent != null) _agent.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }

    // ───────────────────────── Bucle principal ─────────────────────────

    void Update()
    {
        if (_flock == null || _caught || !AgentReady) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        GatherNeighbors();

        // Una vez cruza la entrada y está bien dentro del corral, queda encerrada para siempre
        if (!IsPenned && HerdingGame.IsInsidePen(transform.position, _bodyRadius * 0.5f + 0.5f))
        {
            IsPenned = true;
            if (_food != null && !HerdingGame.IsInsidePen(_food.transform.position, 0f)) ReleaseFood();
        }

        float threatFear = UpdateFear(dt);
        UpdateState(threatFear, dt);

        Vector3 desired = CurrentState switch
        {
            SheepState.Fleeing => FleeSteering(),
            SheepState.Alert => AlertSteering(),
            SheepState.GoingToFood => FoodSteering(),
            SheepState.Eating => Separation() * _flock.walkSpeed,
            _ => GrazeSteering(dt),
        };

        desired = ApplyObstacleAvoidance(desired);
        if (IsPenned) desired = ApplyPenContainment(desired);

        Vector3 prev = transform.position;
        _velocity = Vector3.MoveTowards(_velocity, desired, _flock.acceleration * dt);
        _velocity.y = 0f;
        _agent.Move(_velocity * dt);
        if (IsPenned) ClampInsidePen();

        // Si el NavMesh nos ha frenado (borde/zarza) ajustamos la velocidad real
        Vector3 actual = (transform.position - prev) / dt; actual.y = 0f;
        _velocity = Vector3.Lerp(_velocity, actual, 0.5f);

        Rotate(dt);
        Animate();
    }

    // ───────────────────────── Percepción ─────────────────────────

    void GatherNeighbors()
    {
        _neighbors.Clear();
        _neighborDist.Clear();
        Vector3 p = transform.position;
        float r2 = _flock.neighborRadius * _flock.neighborRadius * 4f;
        foreach (var s in _flock.allSheep)
        {
            if (s == null || s == this || s._caught) continue;
            float d2 = (s.transform.position - p).sqrMagnitude;
            if (d2 > r2) continue;
            // inserción ordenada por distancia
            float d = Mathf.Sqrt(d2);
            int i = 0;
            while (i < _neighborDist.Count && _neighborDist[i] < d) i++;
            _neighbors.Insert(i, s);
            _neighborDist.Insert(i, d);
        }
    }

    /// <summary>Actualiza el miedo y la dirección de huida. Devuelve el miedo "directo" (no contagiado).</summary>
    float UpdateFear(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 away = Vector3.zero;
        float playerFear = 0f;
        _playerPressure = 0f;
        _playerInAlert = false;

        Transform player = _flock.playerTransform;
        if (player != null && player.gameObject.activeInHierarchy)
        {
            Vector3 toSheep = Flat(pos - player.position);
            float d = toSheep.magnitude;
            Vector3 dir = d > 0.01f ? toSheep / d : transform.forward;

            Vector3 pv = Flat(_flock.PlayerVelocity);
            float approach = Mathf.Max(0f, Vector3.Dot(pv, dir));
            float flight = _flock.flightZoneRadius * _boldness + approach * _flock.approachSpeedBonus;
            if (pv.magnitude > _flock.playerChargeSpeed) flight *= 1.15f;

            if (d < flight)
            {
                _playerPressure = 1f - d / flight;
                playerFear = Mathf.Lerp(0.45f, 1f, _playerPressure);
                away += dir * (0.5f + _playerPressure);
            }
            if (d < _flock.alertRadius * _boldness) _playerInAlert = true;
        }

        float extFear = 0f;
        if (_extThreatTimer > 0f && _extThreat != null)
        {
            _extThreatTimer -= dt;
            extFear = _extThreatAmount;
            Vector3 dir = Flat(pos - _extThreat.position);
            if (dir.sqrMagnitude > 0.001f) away += dir.normalized * (1f + extFear) * 1.5f;
        }
        else _extThreatAmount = 0f;

        // Contagio: el miedo de las vecinas cercanas se transmite
        float contagion = 0f, wsum = 0f;
        Vector3 fleeingFlow = Vector3.zero;
        for (int i = 0; i < _neighbors.Count; i++)
        {
            float d = _neighborDist[i];
            if (d > _flock.neighborRadius) break;
            float w = 1f - d / _flock.neighborRadius;
            contagion += _neighbors[i].Fear * w;
            wsum += w;
            if (_neighbors[i].CurrentState == SheepState.Fleeing) fleeingFlow += _neighbors[i].Velocity * w;
        }
        contagion = wsum > 0f ? contagion / wsum * _flock.fearContagion : 0f;

        float direct = Mathf.Max(playerFear, extFear);
        float target = Mathf.Max(direct, contagion);
        Fear = target > Fear
            ? Mathf.MoveTowards(Fear, target, _flock.fearRise * _fearRiseMul * dt)
            : Mathf.MoveTowards(Fear, target, _flock.fearDecay * dt);

        // Sin amenaza directa, la oveja asustada sigue el flujo del rebaño
        if (away.sqrMagnitude < 0.0001f && fleeingFlow.sqrMagnitude > 0.01f) away = fleeingFlow.normalized * 0.6f;
        if (away.sqrMagnitude > 0.0001f) _awayDir = Vector3.Lerp(_awayDir, away, 8f * dt);
        else _awayDir = Vector3.Lerp(_awayDir, Vector3.zero, 1.5f * dt);

        return direct;
    }

    // ───────────────────────── Estados ─────────────────────────

    void UpdateState(float directFear, float dt)
    {
        bool threatened = directFear > 0f;
        if (_food == null && _eating) _eating = false; // la rosa se ha acabado
        if (_foodCooldown > 0f) _foodCooldown -= dt;

        // Si el jugador se interpone entre la oveja y la rosa, renuncia (en vez de ir y volver en bucle)
        if (_food != null && !_eating && PlayerBlocksFood(_food, 1.1f)) AbandonFood();

        if (threatened || Fear > (CurrentState == SheepState.Fleeing ? _flock.calmThreshold * 0.7f : _flock.calmThreshold))
        {
            // Una rosa puede vencer a un miedo residual (contagiado), nunca a una amenaza directa
            if (!threatened && Fear < _flock.roseMaxFear && (_food != null || (_foodCooldown <= 0f && FindFood())))
            {
                CurrentState = _eating ? SheepState.Eating : SheepState.GoingToFood;
                return;
            }
            EnterFlee();
            return;
        }

        if (_food != null || FindFoodThrottled(dt))
        {
            CurrentState = _eating ? SheepState.Eating : SheepState.GoingToFood;
            return;
        }

        CurrentState = _playerInAlert ? SheepState.Alert : SheepState.Grazing;
    }

    void EnterFlee()
    {
        if (CurrentState != SheepState.Fleeing)
        {
            if (_food != null) AbandonFood();
            ReleaseFood();
                _hasWanderTarget = false;
            _grazeTimer = 0f;
        }
        CurrentState = SheepState.Fleeing;
    }

    // ───────────────────────── Steering por estado ─────────────────────────

    Vector3 FleeSteering()
    {
        Vector3 lcm = LocalCenterOfMass(out bool hasNeighbors);
        Vector3 toLcm = Flat(lcm - transform.position);
        float distLcm = toLcm.magnitude;

        // Rebaño egoísta: cuanto más miedo, más se aprietan
        float cohW = Mathf.Lerp(_flock.cohesionCalmWeight, _flock.cohesionAfraidWeight, Fear);
        Vector3 cohesion = distLcm > _bodyRadius * 2f ? toLcm / distLcm * cohW * Mathf.Clamp01(distLcm / (_bodyRadius * 6f)) : Vector3.zero;

        Vector3 steer = _awayDir * _flock.fleeWeight
                      + cohesion
                      + Alignment() * _flock.alignmentWeight
                      + Separation() * _flock.separationWeight;

        // Sin presión directa reducen velocidad poco a poco (se recolocan y vuelven a pastar)
        float speed = Mathf.Lerp(_flock.walkSpeed, Mathf.Lerp(_flock.trotSpeed, _flock.runSpeed, Fear), Mathf.Clamp01(Fear * 1.3f));
        speed *= _speedMul * Mathf.Lerp(0.85f, 1.1f, _playerPressure);

        if (steer.sqrMagnitude < 0.0001f) return Vector3.zero;
        return Vector3.ClampMagnitude(steer, 1f) * speed;
    }

    Vector3 AlertSteering()
    {
        // Quieta, mirando al jugador, arrimándose ligeramente al grupo
        Vector3 lcm = LocalCenterOfMass(out _);
        Vector3 toLcm = Flat(lcm - transform.position);
        Vector3 steer = Separation() * _flock.separationWeight;
        if (toLcm.magnitude > _bodyRadius * 4f) steer += toLcm.normalized * 0.6f;
        return Vector3.ClampMagnitude(steer, 1f) * _flock.walkSpeed;
    }

    Vector3 GrazeSteering(float dt)
    {
        Vector3 sep = Separation() * _flock.separationWeight;

        if (_grazeTimer > 0f)
        {
            _grazeTimer -= dt;
            if (_grazeTimer <= 0f) _hasWanderTarget = false;
            return Vector3.ClampMagnitude(sep, 1f) * _flock.walkSpeed * 0.6f;
        }

        if (!_hasWanderTarget) PickWanderTarget();

        Vector3 to = Flat(_wanderTarget - transform.position);
        float d = to.magnitude;
        if (d < Mathf.Max(0.6f, _bodyRadius))
        {
            _hasWanderTarget = false;
            _grazeTimer = Random.Range(_flock.grazeTimeMin, _flock.grazeTimeMax);
            return Vector3.zero;
        }

        Vector3 lcm = LocalCenterOfMass(out _);
        // Dentro del corral no se dejan arrastrar hacia las que siguen fuera
        bool inPen = HerdingGame.IsInsidePen(transform.position, 0f);
        Vector3 coh = inPen && !HerdingGame.IsInsidePen(lcm, 0f) ? Vector3.zero : Flat(lcm - transform.position).normalized * _flock.cohesionCalmWeight;
        Vector3 steer = to / d + sep + coh;
        float speed = _flock.walkSpeed * _speedMul * Mathf.Clamp01(d / 1.5f + 0.3f);
        return Vector3.ClampMagnitude(steer, 1f) * speed;
    }

    Vector3 FoodSteering()
    {
        if (_food == null || (!_eating && _food.Remaining01 <= 0f)) { ReleaseFood(); return Vector3.zero; }

        Vector3 eatPoint = _food.GetEatPoint(_foodSlot, _bodyRadius + 0.4f);
        Vector3 to = Flat(eatPoint - transform.position);
        float d = to.magnitude;

        if (d < 0.45f + _bodyRadius * 0.3f)
        {
            _eating = true;
            _food.StartEating();
            CurrentState = SheepState.Eating;
            return Vector3.zero;
        }

        // Las ovejas van a la comida al trote y frenan al llegar
        float speed = Mathf.Lerp(_flock.walkSpeed, _flock.trotSpeed * 0.85f, Mathf.Clamp01(d / 6f)) * _speedMul;
        Vector3 steer = to / d + Separation() * _flock.separationWeight * 0.7f;
        return Vector3.ClampMagnitude(steer, 1f) * speed;
    }

    // ───────────────────────── Fuerzas de grupo ─────────────────────────

    /// <summary>Centro de masas de las k vecinas más cercanas (o del rebaño si está aislada).</summary>
    Vector3 LocalCenterOfMass(out bool hasNeighbors)
    {
        int k = Mathf.Min(_flock.nearestNeighbors, _neighbors.Count);
        hasNeighbors = k > 0;
        if (k == 0) return _flock.FlockCentroid;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < k; i++) sum += _neighbors[i].transform.position;
        return sum / k;
    }

    Vector3 Separation()
    {
        Vector3 push = Vector3.zero;
        Vector3 p = transform.position;
        for (int i = 0; i < _neighbors.Count; i++)
        {
            var n = _neighbors[i];
            float ps = (_bodyRadius + n._bodyRadius) * 0.5f * _flock.personalSpaceFactor;
            float d = _neighborDist[i];
            if (d >= ps) break;
            Vector3 away = Flat(p - n.transform.position);
            if (away.sqrMagnitude < 0.0001f) away = new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f);
            float t = 1f - d / ps;
            push += away.normalized * t * t;
        }
        return push;
    }

    Vector3 Alignment()
    {
        Vector3 sum = Vector3.zero;
        int c = 0;
        for (int i = 0; i < _neighbors.Count && c < _flock.nearestNeighbors; i++, c++)
            sum += _neighbors[i].Velocity;
        if (c == 0 || sum.sqrMagnitude < 0.01f) return Vector3.zero;
        return sum.normalized;
    }

    // ───────────────────────── Obstáculos (bordes + zarzas) ─────────────────────────

    Vector3 ApplyObstacleAvoidance(Vector3 desired)
    {
        Vector3 pos = transform.position;
        float look = _flock.obstacleLookAhead + _bodyRadius;
        float speed = desired.magnitude;
        Vector3 push = Vector3.zero;

        // Zarzas: repulsión anticipada + deslizamiento a lo largo de la barrera
        foreach (var b in Bramble.All)
        {
            if (b == null) continue;
            Vector3 q = b.ClosestPoint(pos);
            Vector3 diff = Flat(pos - q);
            float dist = diff.magnitude - b.thickness * 0.5f - _bodyRadius;
            if (dist > look || diff.sqrMagnitude < 0.0001f) continue;
            Vector3 n = diff.normalized;
            float prox = 1f - Mathf.Clamp01(dist / look);
            push += n * prox * prox;
            float into = Vector3.Dot(desired, n);
            if (into < 0f) desired -= n * into * prox; // quita la componente que entra en la zarza
        }

        // Bordes del NavMesh (vallas, rocas, huecos de zarza)
        if (speed > 0.05f)
        {
            Vector3 dir = desired / speed;
            if (NavMesh.Raycast(pos, pos + dir * look, out NavMeshHit hit, NavMesh.AllAreas))
            {
                Vector3 n = Flat(hit.normal);
                if (n.sqrMagnitude > 0.0001f)
                {
                    n.Normalize();
                    float prox = 1f - Mathf.Clamp01(hit.distance / look);
                    float into = Vector3.Dot(desired, n);
                    if (into < 0f) desired -= n * into * prox;
                    push += n * prox;
                }
            }
        }

        if (push.sqrMagnitude > 0f)
            desired += push * _flock.obstacleWeight * Mathf.Max(speed, _flock.walkSpeed) * 0.5f;

        float maxSpeed = _flock.runSpeed * 1.2f;
        return Vector3.ClampMagnitude(Flat(desired), maxSpeed);
    }

    // ───────────────────────── Corral ─────────────────────────

    /// <summary>Radio máximo (desde el centro) que puede alcanzar una oveja encerrada.</summary>
    float PenLimit(float penRadius) => penRadius - _bodyRadius * 0.5f - 0.3f;

    /// <summary>Cerca del borde del corral quita la componente que sale hacia fuera y empuja hacia dentro.</summary>
    Vector3 ApplyPenContainment(Vector3 desired)
    {
        if (!HerdingGame.TryGetPen(out Vector3 c, out float r)) return desired;
        Vector3 fromC = Flat(transform.position - c);
        float d = fromC.magnitude;
        float limit = PenLimit(r);
        float soft = limit - 1.5f;
        if (d < soft || d < 0.001f) return desired;

        Vector3 outward = fromC / d;
        float prox = Mathf.Clamp01((d - soft) / (limit - soft));
        float outSpeed = Vector3.Dot(desired, outward);
        if (outSpeed > 0f) desired -= outward * outSpeed * prox;        // no sale
        desired -= outward * prox * _flock.walkSpeed;                    // y se recoloca hacia dentro
        return desired;
    }

    /// <summary>Límite duro: si algo la empuja fuera (p.ej. por la entrada), se la devuelve dentro.</summary>
    void ClampInsidePen()
    {
        if (!HerdingGame.TryGetPen(out Vector3 c, out float r)) return;
        Vector3 fromC = Flat(transform.position - c);
        float d = fromC.magnitude;
        float limit = PenLimit(r);
        if (d <= limit || d < 0.001f) return;

        Vector3 back = -fromC / d * (d - limit);
        _agent.Move(back);
        float outSpeed = Vector3.Dot(_velocity, fromC / d);
        if (outSpeed > 0f) _velocity -= fromC / d * outSpeed;
    }

    // ───────────────────────── Pastar ─────────────────────────

    void PickWanderTarget()
    {
        Vector3 pos = transform.position;
        Vector3 candidate;

        if (HerdingGame.TryGetPen(out Vector3 penCenter, out float penRadius) && HerdingGame.IsInsidePen(pos, 0f))
        {
            // Ya en el corral: se queda paseando dentro
            Vector2 r = Random.insideUnitCircle * penRadius * 0.6f;
            candidate = penCenter + new Vector3(r.x, 0f, r.y);
        }
        else if (Vector3.Distance(pos, _flock.FlockCentroid) > _flock.maxCalmSpread)
        {
            Vector2 r = Random.insideUnitCircle * 3f;
            candidate = _flock.FlockCentroid + new Vector3(r.x, 0f, r.y);
        }
        else
        {
            Vector2 r = Random.insideUnitCircle * _flock.wanderRadius;
            candidate = pos + new Vector3(r.x, 0f, r.y);
        }

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            _wanderTarget = hit.position;
            _hasWanderTarget = true;
        }
        else _grazeTimer = 1f;
    }

    // ───────────────────────── Comida (rosas) ─────────────────────────

    bool FindFoodThrottled(float dt)
    {
        _foodSearchTimer -= dt;
        if (_foodSearchTimer > 0f || _foodCooldown > 0f) return false;
        _foodSearchTimer = Random.Range(0.25f, 0.45f);
        return FindFood();
    }

    bool FindFood()
    {
        Vector3 pos = transform.position;
        FoodSource best = null;
        float bestScore = 0f;

        foreach (var f in FoodSource.All)
        {
            if (f == null || !f.IsAvailable || PlayerBlocksFood(f, 1.3f)) continue;
            if (IsPenned && !HerdingGame.IsInsidePen(f.transform.position, _bodyRadius)) continue;
            float d = Vector3.Distance(pos, f.transform.position);
            if (d > f.detectionRadius) continue;
            float score = f.attractionPriority / (d + 1f);
            if (score > bestScore) { bestScore = score; best = f; }
        }

        // Comportamiento social: seguir a una vecina que va a comer
        if (best == null)
        {
            for (int i = 0; i < _neighbors.Count; i++)
            {
                if (_neighborDist[i] > _flock.followEaterRadius) break;
                var f = _neighbors[i]._food;
                if (f != null && f.IsAvailable && !PlayerBlocksFood(f, 1.3f)
                    && (!IsPenned || HerdingGame.IsInsidePen(f.transform.position, _bodyRadius))) { best = f; break; }
            }
        }

        if (best == null || !best.TryOccupy()) return false;
        _food = best;
        _foodSlot = best.Occupants - 1;
        _eating = false;
        _hasWanderTarget = false;
        _grazeTimer = 0f;
        return true;
    }

    /// <summary>
    /// ¿Está el jugador entre la oveja y la rosa (o junto a la rosa)?
    /// Se usa un margen mayor para elegir rosa (1.3) que para abandonarla (1.1): histéresis anti-bucle.
    /// Si el jugador está DETRÁS de la oveja no bloquea: empujarla hacia la rosa es pastoreo válido.
    /// </summary>
    bool PlayerBlocksFood(FoodSource f, float margin)
    {
        Transform player = _flock.playerTransform;
        if (player == null || f == null || !player.gameObject.activeInHierarchy) return false;

        float r = _flock.flightZoneRadius * _boldness * margin;
        Vector3 p = Flat(player.position), a = Flat(transform.position), b = Flat(f.transform.position);
        if ((p - b).sqrMagnitude < r * r) return true;

        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) return false;
        float t = Vector3.Dot(p - a, ab) / len2;
        if (t <= 0f) return false; // jugador detrás de la oveja
        Vector3 closest = a + ab * Mathf.Min(t, 1f);
        return (p - closest).sqrMagnitude < r * r;
    }

    void AbandonFood()
    {
        ReleaseFood();
        _foodCooldown = Random.Range(2.5f, 4f);
    }

    void ReleaseFood()
    {
        if (_food != null)
        {
            if (_eating) _food.StopEating();
            _food.Release();
        }
        _food = null;
        _eating = false;
    }

    // ───────────────────────── Visual ─────────────────────────

    void Rotate(float dt)
    {
        Vector3 look = Vector3.zero;
        if (_velocity.sqrMagnitude > 0.04f) look = _velocity;
        else if (CurrentState == SheepState.Alert && _flock.playerTransform != null) look = _flock.playerTransform.position - transform.position;
        else if (CurrentState == SheepState.Eating && _food != null) look = _food.transform.position - transform.position;

        look = Flat(look);
        if (look.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(look.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, _flock.turnSpeed * dt);
    }

    void Animate()
    {
        if (_animator == null) return;
        if (_pSpeed != -1) _animator.SetFloat(_pSpeed, _velocity.magnitude);
        if (_pFleeing != -1) _animator.SetBool(_pFleeing, CurrentState == SheepState.Fleeing);
        if (_pGrazing != -1) _animator.SetBool(_pGrazing, CurrentState == SheepState.Grazing && _grazeTimer > 0f);
        if (_pEating != -1) _animator.SetBool(_pEating, CurrentState == SheepState.Eating);
    }

    void OnDisable()
    {
        ReleaseFood();
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + _velocity);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + _awayDir * 2f);
        if (_hasWanderTarget) { Gizmos.color = Color.green; Gizmos.DrawWireSphere(_wanderTarget, 0.3f); }
    }
}
