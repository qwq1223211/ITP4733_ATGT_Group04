using UnityEngine;
using UnityEngine.Events;

namespace Local.UnityAgent.Runtime
{
    /// <summary>拾取物。交互后发事件并销毁。碰撞体保持 Trigger。</summary>
    public class Pickup : MonoBehaviour
    {
        public string itemId = "coin";
        public int amount = 1;
        public UnityEvent onPickup = new UnityEvent();

        bool _used;

        void Reset()
        {
            ForceTrigger();
        }

        void Awake()
        {
            ForceTrigger();
        }

        public void ForceTrigger()
        {
            var cols = GetComponents<Collider>();
            for (var i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].isTrigger = true;
            }
        }

        public bool Interact()
        {
            if (_used) return false;
            _used = true;
            if (onPickup != null) onPickup.Invoke();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
            return true;
        }
    }
}
