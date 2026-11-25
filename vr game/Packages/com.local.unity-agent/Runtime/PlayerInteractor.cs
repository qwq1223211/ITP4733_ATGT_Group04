using UnityEngine;

namespace Local.UnityAgent.Runtime
{
    /// <summary>从相机向前射线，E 键交互。没有相机就跳过。</summary>
    public class PlayerInteractor : MonoBehaviour
    {
        public Transform camera;
        public float range = 3f;
        public LayerMask layerMask = ~0;
        public string CurrentPrompt;

        Interactable _hitInteract;
        Pickup _hitPickup;

        void Update()
        {
            CurrentPrompt = "";
            _hitInteract = null;
            _hitPickup = null;
            if (camera == null) return;

            if (Physics.Raycast(camera.position, camera.forward, out var hit, range, layerMask, QueryTriggerInteraction.Collide))
            {
                var col = hit.collider;
                if (col != null)
                {
                    _hitInteract = col.GetComponentInParent<Interactable>();
                    _hitPickup = col.GetComponentInParent<Pickup>();
                }
                if (_hitInteract != null) CurrentPrompt = _hitInteract.prompt ?? "";
                else if (_hitPickup != null)
                    CurrentPrompt = string.IsNullOrEmpty(_hitPickup.itemId) ? "拾取" : "拾取 " + _hitPickup.itemId;
            }

            if (!Input.GetKeyDown(KeyCode.E)) return;
            if (_hitInteract != null) _hitInteract.Interact();
            else if (_hitPickup != null) _hitPickup.Interact();
        }
    }
}
