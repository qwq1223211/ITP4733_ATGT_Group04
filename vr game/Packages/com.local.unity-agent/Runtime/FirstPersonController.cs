using UnityEngine;

namespace Local.UnityAgent.Runtime
{
    /// <summary>第一人称：WASD、鼠标视角、跳跃、Shift 冲刺。键鼠走 Legacy Input。</summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public Transform cameraTransform;
        public float moveSpeed = 5f;
        public float sprintMultiplier = 1.8f;
        public float jumpHeight = 1.2f;
        public float gravity = -19.62f;
        public float mouseSensitivity = 2f;
        public float maxPitch = 85f;

        CharacterController _cc;
        float _pitch;
        float _vy;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            Look();
            Move();
        }

        void Look()
        {
            if (cameraTransform == null) return;
            if (Cursor.lockState != CursorLockMode.Locked) return;

            var yaw = Input.GetAxis("Mouse X") * mouseSensitivity;
            var pitchDelta = Input.GetAxis("Mouse Y") * mouseSensitivity;
            transform.Rotate(0f, yaw, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - pitchDelta, -maxPitch, maxPitch);
            cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move()
        {
            if (_cc == null) return;

            if (_cc.isGrounded && _vy < 0f) _vy = -2f;

            var wish = transform.right * Input.GetAxis("Horizontal") + transform.forward * Input.GetAxis("Vertical");
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            var speed = moveSpeed;
            // 默认 Input Manager 里 Fire3 是左 Shift
            if (Input.GetButton("Fire3")) speed *= sprintMultiplier;
            wish *= speed;

            if (_cc.isGrounded && Input.GetButtonDown("Jump"))
                _vy = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _vy += gravity * Time.deltaTime;
            wish.y = _vy;
            _cc.Move(wish * Time.deltaTime);
        }
    }
}
