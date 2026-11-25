using System;
using UnityEngine;
using UnityEngine.Events;

namespace Local.UnityAgent.Runtime
{
    [Serializable]
    public class HealthDamagedEvent : UnityEvent<float> { }

    /// <summary>生命值。死亡事件在 Revive 之前只触发一次。</summary>
    public class Health : MonoBehaviour
    {
        public float maxHealth = 100f;
        public float current = 100f;
        public bool destroyOnDeath;
        public HealthDamagedEvent onDamaged = new HealthDamagedEvent();
        public UnityEvent onDied = new UnityEvent();

        bool _died;

        public bool IsDead => _died;

        public void TakeDamage(float amount)
        {
            if (amount <= 0f || _died) return;
            current = Mathf.Max(0f, current - amount);
            if (onDamaged != null) onDamaged.Invoke(amount);
            if (current > 0f) return;
            current = 0f;
            _died = true;
            if (onDied != null) onDied.Invoke();
            if (!destroyOnDeath) return;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || _died) return;
            current = Mathf.Min(maxHealth, current + amount);
        }

        public void Revive()
        {
            _died = false;
            if (maxHealth < 1f) maxHealth = 1f;
            current = maxHealth;
        }
    }
}
