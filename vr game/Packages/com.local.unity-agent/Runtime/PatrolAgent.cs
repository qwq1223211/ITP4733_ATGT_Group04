using UnityEngine;
using UnityEngine.AI;

namespace Local.UnityAgent.Runtime
{
    /// <summary>NavMesh 巡逻，靠近 Player 时改追击。不读输入。</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class PatrolAgent : MonoBehaviour
    {
        public Transform[] waypoints;
        public float waitTime = 1f;
        public float chaseRange = 8f;
        public string playerTag = "Player";
        public bool chase = true;

        NavMeshAgent _agent;
        int _index;
        float _waitLeft;
        bool _waiting;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }

        void Update()
        {
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            if (_agent == null || !_agent.isOnNavMesh) return;

            var player = FindPlayer();
            if (chase && player != null)
            {
                var dist = Vector3.Distance(transform.position, player.position);
                if (dist <= chaseRange)
                {
                    _waiting = false;
                    _agent.SetDestination(player.position);
                    return;
                }
            }

            Patrol();
        }

        void Patrol()
        {
            if (waypoints == null || waypoints.Length == 0) return;
            if (_index < 0 || _index >= waypoints.Length) _index = 0;

            if (_waiting)
            {
                _waitLeft -= Time.deltaTime;
                if (_waitLeft > 0f) return;
                _waiting = false;
                _index = (_index + 1) % waypoints.Length;
            }

            var point = waypoints[_index];
            if (point == null) return;

            _agent.SetDestination(point.position);
            if (_agent.pathPending) return;
            if (_agent.remainingDistance <= _agent.stoppingDistance + 0.1f)
            {
                _waiting = true;
                _waitLeft = waitTime;
            }
        }

        Transform FindPlayer()
        {
            if (string.IsNullOrEmpty(playerTag)) return null;
            try
            {
                var go = GameObject.FindWithTag(playerTag);
                return go != null ? go.transform : null;
            }
            catch (UnityException)
            {
                return null;
            }
        }
    }
}
