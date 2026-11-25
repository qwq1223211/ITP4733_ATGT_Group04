using System.Collections.Generic;
using UnityEngine;

namespace Local.UnityAgent.Runtime
{
    /// <summary>碰到带 Health 的物体就造成伤害。同一目标有冷却。</summary>
    public class DamageDealer : MonoBehaviour
    {
        public float damage = 10f;
        public float cooldown = 1f;

        readonly Dictionary<int, float> _next = new Dictionary<int, float>();

        void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            Hit(other.GetComponentInParent<Health>());
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null) return;
            Hit(collision.collider.GetComponentInParent<Health>());
        }

        void Hit(Health hp)
        {
            if (hp == null || damage <= 0f) return;
            var id = hp.GetInstanceID();
            var now = Time.time;
            if (_next.TryGetValue(id, out var readyAt) && now < readyAt) return;
            _next[id] = now + Mathf.Max(0f, cooldown);
            hp.TakeDamage(damage);
        }
    }
}
