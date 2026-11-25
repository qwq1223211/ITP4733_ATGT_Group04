using UnityEngine;
using UnityEngine.Events;

namespace Local.UnityAgent.Runtime
{
    /// <summary>可交互物。once 为真时 Interact 只成功一次。</summary>
    public class Interactable : MonoBehaviour
    {
        public string prompt = "按 E 交互";
        public bool once;
        public UnityEvent onInteract = new UnityEvent();

        bool _used;

        public bool Interact()
        {
            if (once && _used) return false;
            _used = true;
            if (onInteract != null) onInteract.Invoke();
            return true;
        }
    }
}
