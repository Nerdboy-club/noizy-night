using UnityEngine;
using UnityEngine.AI;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Thin wrapper around a NavMeshAgent with null-safe checks and an optional straight-line fallback
    /// when there is no baked NavMesh (greybox testing). Plain C# object, owned by Grandpa / FatCat.
    /// </summary>
    internal sealed class NavMover
    {
        private readonly Transform _t;
        private readonly NavMeshAgent _agent;

        public bool AllowManualFallback;
        public float TurnSpeed = 240f;      // deg/s
        public float ArriveDistance = 0.6f;

        private Vector3 _target;
        private float _speed;
        private bool _moving;
        private bool _pathFailed;
        private float _moveTime;
        private float _manualSpeed;

        public NavMover(Transform t, NavMeshAgent agent)
        {
            _t = t;
            _agent = agent;
        }

        public bool AgentReady => _agent != null && _agent.enabled && _agent.isOnNavMesh;
        public bool IsMoving => _moving;
        public Vector3 Target => _target;

        public float CurrentSpeed
        {
            get
            {
                if (AgentReady) return _agent.velocity.magnitude;
                return _manualSpeed;
            }
        }

        public static bool Sample(Vector3 p, float radius, out Vector3 result)
        {
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
            result = p;
            return false;
        }

        /// <summary>Teleports to pos and enables the agent there. Returns true if the agent is on the NavMesh.</summary>
        public bool EnableAt(Vector3 pos)
        {
            _t.position = pos;
            if (_agent == null) return false;
            if (!_agent.enabled) _agent.enabled = true;
            _agent.Warp(pos); // Warp (re)binds the agent to the NavMesh at pos; isOnNavMesh stays false if there is none.
            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
                return true;
            }
            // No NavMesh here: keep the agent off so Unity does not spam warnings.
            _agent.enabled = false;
            return false;
        }

        public void Disable()
        {
            Stop();
            if (_agent != null && _agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.ResetPath();
                _agent.enabled = false;
            }
        }

        public void SetTarget(Vector3 target, float speed)
        {
            _target = target;
            _speed = speed;
            _moving = true;
            _moveTime = 0f;
            _pathFailed = false;
            if (AgentReady)
            {
                _agent.speed = speed;
                _agent.updateRotation = true;
                _agent.isStopped = false;
                _pathFailed = !_agent.SetDestination(target);
            }
        }

        /// <summary>Change destination without resetting arrival timers (for chasing).</summary>
        public void UpdateTarget(Vector3 target, float speed)
        {
            if (!_moving) { SetTarget(target, speed); return; }
            _target = target;
            _speed = speed;
            if (AgentReady)
            {
                _agent.speed = speed;
                _pathFailed = !_agent.SetDestination(target);
            }
        }

        /// <summary>Advance movement. Returns true when arrived (or when the target is unreachable).</summary>
        public bool Tick(float dt)
        {
            if (!_moving) { _manualSpeed = 0f; return true; }
            _moveTime += dt;

            if (AgentReady)
            {
                if (_agent.isStopped) _agent.isStopped = false;
                _agent.speed = _speed;
                if (_pathFailed) return true;
                if (_moveTime > 0.3f && !_agent.pathPending)
                {
                    float stop = Mathf.Max(ArriveDistance, _agent.stoppingDistance + 0.05f);
                    if (_agent.hasPath && _agent.remainingDistance <= stop) return true;
                    if (!_agent.hasPath && _agent.velocity.sqrMagnitude < 0.01f) return true;
                }
                return false;
            }

            if (!AllowManualFallback) { _manualSpeed = 0f; return _moveTime > 0.5f; }

            Vector3 pos = _t.position;
            Vector3 to = _target - pos;
            to.y = 0f;
            float d = to.magnitude;
            if (d <= ArriveDistance) { _manualSpeed = 0f; return true; }
            float step = Mathf.Min(d, _speed * dt);
            _t.position = pos + to * (step / d);
            FaceDirection(to, dt, 1f);
            _manualSpeed = _speed;
            return false;
        }

        public void Stop()
        {
            _moving = false;
            _manualSpeed = 0f;
            if (AgentReady)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
                _agent.updateRotation = false;
            }
        }

        /// <summary>Freeze (pause / inactive) without forgetting the target.</summary>
        public void Halt()
        {
            _manualSpeed = 0f;
            if (AgentReady && !_agent.isStopped) _agent.isStopped = true;
        }

        public void FaceTowards(Vector3 point, float dt, float speedMul)
        {
            Vector3 dir = point - _t.position;
            dir.y = 0f;
            FaceDirection(dir, dt, speedMul);
        }

        public void FaceDirection(Vector3 dir, float dt, float speedMul)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _t.rotation = Quaternion.RotateTowards(_t.rotation, target, TurnSpeed * speedMul * dt);
        }
    }
}
