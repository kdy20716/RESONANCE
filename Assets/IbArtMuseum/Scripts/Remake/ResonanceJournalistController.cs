using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IbArtMuseum.Remake
{
    /// <summary>
    /// 주인공 기자(Journalist) 1인칭 컨트롤러 + 손전등([F]) + 취재용 카메라 뷰파인더([RMB]/[C]) & 플래시 촬영([LMB]) + 상호작용([E])
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ResonanceJournalistController : MonoBehaviour
    {
        [Header("Movement")]
        public float walkSpeed = 4.2f;
        public float sprintSpeed = 6.8f;
        public float gravity = -16f;
        public float mouseSensitivity = 0.12f;

        [Header("Camera & Equipment")]
        public Transform playerCamera;
        public GameObject flashlightModel;
        public Light flashlightSpot;
        public GameObject cameraModel;
        public Light cameraFlashLight;

        [Header("Interaction")]
        public float interactDistance = 4.0f;
        public float photoFlashRange = 15.0f;

        private CharacterController _cc;
        private float _pitch = 0f;
        private Vector3 _velocity;
        private bool _controlEnabled = false;
        private bool _isAimingCamera = false;
        private float _flashCooldown = 0f;
        private float _bobTimer = 0f;
        private Vector3 _savedPreHidePosition;
        private Quaternion _savedPreHideRotation;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (playerCamera == null && Camera.main != null)
            {
                playerCamera = Camera.main.transform;
            }
        }

        public void SetControlEnabled(bool enabled)
        {
            _controlEnabled = enabled;
            if (enabled)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SetCameraAiming(false);
            }
        }

        public void TeleportTo(Vector3 pos, Quaternion rot)
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();
            if (_cc != null) _cc.enabled = false;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            _pitch = 0f;
            if (playerCamera != null)
            {
                playerCamera.localPosition = new Vector3(0f, 1.65f, 0f);
                playerCamera.localRotation = Quaternion.identity;
            }
            _velocity = Vector3.zero;
            if (_cc != null) _cc.enabled = true;
        }

        private void Update()
        {
            if (_flashCooldown > 0f) _flashCooldown -= Time.deltaTime;

            if (!_controlEnabled)
            {
                return;
            }

            // 숨어있는 상태일 때 [E]키로 밖으로 나오기
            if (ResonanceGameManager.Instance != null && ResonanceGameManager.Instance.isPlayerHiding)
            {
                HandleLookOnly();
                bool isKo = ResonanceGameManager.Instance.IsKorean;
                ResonanceGameManager.Instance.ui?.SetHoverPrompt(true, isKo ? "[E] 캐비닛 밖으로 나오기" : "[E] Exit Locker");
                if (ResonanceGameManager.WasKeyPressed(KeyCode.E))
                {
                    ExitHidingLocker();
                }
                return;
            }

            HandleLookOnly();
            HandleMovement();
            HandleEquipmentInput();
            HandleInteractionRaycast();
        }

        private void HandleLookOnly()
        {
            float mx = 0f;
            float my = 0f;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                mx = delta.x * mouseSensitivity;
                my = delta.y * mouseSensitivity;
            }
#else
            mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity * 12f;
            my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity * 12f;
#endif
            transform.Rotate(Vector3.up * mx);
            _pitch = Mathf.Clamp(_pitch - my, -80f, 80f);
            if (playerCamera != null)
            {
                playerCamera.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }

        private void HandleMovement()
        {
            float h = 0f;
            float v = 0f;
            bool sprint = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v -= 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h += 1f;
                if (Keyboard.current.leftShiftKey.isPressed) sprint = true;
            }
#else
            h = Input.GetAxisRaw("Horizontal");
            v = Input.GetAxisRaw("Vertical");
            sprint = Input.GetKey(KeyCode.LeftShift);
#endif
            Vector3 dir = (transform.right * h + transform.forward * v).normalized;
            float spd = sprint ? sprintSpeed : walkSpeed;
            if (_isAimingCamera) spd *= 0.65f;

            if (_cc.isGrounded && _velocity.y < 0f)
                _velocity.y = -2f;

            _cc.Move(dir * spd * Time.deltaTime);
            _velocity.y += gravity * Time.deltaTime;
            _cc.Move(_velocity * Time.deltaTime);

            if (playerCamera != null)
            {
                if (dir.sqrMagnitude > 0.05f && _cc.isGrounded)
                {
                    _bobTimer += Time.deltaTime * (sprint ? 11f : 7.5f);
                    playerCamera.localPosition = new Vector3(
                        Mathf.Cos(_bobTimer * 0.5f) * 0.025f,
                        1.65f + Mathf.Sin(_bobTimer) * 0.035f,
                        0f
                    );
                }
                else
                {
                    playerCamera.localPosition = Vector3.Lerp(playerCamera.localPosition, new Vector3(0f, 1.65f, 0f), Time.deltaTime * 6f);
                }
            }
        }

        private void HandleEquipmentInput()
        {
            if (ResonanceGameManager.WasKeyPressed(KeyCode.F))
            {
                if (flashlightSpot != null)
                {
                    flashlightSpot.enabled = !flashlightSpot.enabled;
                    bool isKo = ResonanceGameManager.Instance == null || ResonanceGameManager.Instance.IsKorean;
                    ResonanceGameManager.Instance?.ui?.ShowToastMessage(
                        flashlightSpot.enabled
                            ? (isKo ? "🔦 손전등 ON" : "🔦 Flashlight ON")
                            : (isKo ? "🔦 손전등 OFF" : "🔦 Flashlight OFF"),
                        1.5f
                    );
                }
            }

            bool hasCam = ResonanceGameManager.Instance != null && ResonanceGameManager.Instance.hasCamera;
            if (!hasCam)
            {
                if (_isAimingCamera) SetCameraAiming(false);
                return;
            }

            bool rmbHeld = false;
            bool lmbPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                rmbHeld = Mouse.current.rightButton.isPressed;
                lmbPressed = Mouse.current.leftButton.wasPressedThisFrame;
            }
#else
            rmbHeld = Input.GetMouseButton(1);
            lmbPressed = Input.GetMouseButtonDown(0);
#endif
            if (ResonanceGameManager.WasKeyPressed(KeyCode.C))
            {
                SetCameraAiming(!_isAimingCamera);
            }
            else if (rmbHeld && !_isAimingCamera)
            {
                SetCameraAiming(true);
            }

            if (_isAimingCamera && lmbPressed && _flashCooldown <= 0f)
            {
                FireCameraFlash();
            }
        }

        public void SetCameraAiming(bool aiming)
        {
            _isAimingCamera = aiming;
            if (cameraModel != null) cameraModel.SetActive(!aiming);
            if (ResonanceGameManager.Instance != null && ResonanceGameManager.Instance.ui != null)
            {
                ResonanceGameManager.Instance.ui.SetCameraViewfinderActive(aiming);
            }
        }

        private void FireCameraFlash()
        {
            _flashCooldown = 1.1f;
            StartCoroutine(CoCameraFlashLightPulse());

            if (ResonanceGameManager.Instance != null && ResonanceGameManager.Instance.ui != null)
            {
                ResonanceGameManager.Instance.ui.TriggerScreenFlash(Color.white, 0.35f);
            }

            if (playerCamera == null) return;

            ResonanceAnomalyTarget[] allTargets = FindObjectsByType<ResonanceAnomalyTarget>(FindObjectsSortMode.None);
            int purifiedNow = 0;

            foreach (var target in allTargets)
            {
                if (target == null || target.isPurified) continue;
                Vector3 toTarget = target.transform.position - playerCamera.position;
                float dist = toTarget.magnitude;
                if (dist <= photoFlashRange)
                {
                    float angle = Vector3.Angle(playerCamera.forward, toTarget.normalized);
                    if (angle <= 42f)
                    {
                        target.PurifyByCameraFlash();
                        purifiedNow++;
                    }
                }
            }

            if (purifiedNow == 0)
            {
                bool isKo = ResonanceGameManager.Instance == null || ResonanceGameManager.Instance.IsKorean;
                ResonanceGameManager.Instance?.ui?.ShowToastMessage(
                    isKo
                        ? "📸 찰칵! (시야 내에 정화할 안구/원혼 왜곡이 없습니다)"
                        : "📸 SNAP! (No eyeball distortion targets in range)",
                    2.0f
                );
            }
        }

        private IEnumerator CoCameraFlashLightPulse()
        {
            if (cameraFlashLight == null) yield break;
            cameraFlashLight.enabled = true;
            var hd = cameraFlashLight.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            cameraFlashLight.intensity = 8500f;
            if (hd != null) hd.intensity = 8500f;
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float val = Mathf.Lerp(8500f, 0f, t / 0.35f);
                cameraFlashLight.intensity = val;
                if (hd != null) hd.intensity = val;
                yield return null;
            }
            cameraFlashLight.enabled = false;
        }

        public void EnterHidingLocker(Transform hideInsidePoint)
        {
            if (ResonanceGameManager.Instance == null) return;
            _savedPreHidePosition = transform.position;
            _savedPreHideRotation = transform.rotation;
            ResonanceGameManager.Instance.isPlayerHiding = true;
            TeleportTo(hideInsidePoint.position, hideInsidePoint.rotation);
            bool isKo = ResonanceGameManager.Instance.IsKorean;
            ResonanceGameManager.Instance.ui?.ShowToastMessage(
                isKo
                    ? "캐비닛 내부에 숨었습니다. J.A.N.I.T.O.R.가 당신을 감지하지 못합니다. ([E]키로 나오기)"
                    : "Hidden inside locker. J.A.N.I.T.O.R. cannot detect you. (Press [E] to exit)",
                3.5f
            );
        }

        public void ExitHidingLocker()
        {
            if (ResonanceGameManager.Instance == null) return;
            ResonanceGameManager.Instance.isPlayerHiding = false;
            TeleportTo(_savedPreHidePosition, _savedPreHideRotation);
            bool isKo = ResonanceGameManager.Instance.IsKorean;
            ResonanceGameManager.Instance.ui?.ShowToastMessage(
                isKo ? "캐비닛 밖으로 나왔습니다." : "Stepped out of the locker.",
                2.0f
            );
        }

        private void HandleInteractionRaycast()
        {
            if (playerCamera == null || ResonanceGameManager.Instance == null || ResonanceGameManager.Instance.ui == null)
                return;

            Ray ray = new Ray(playerCamera.position, playerCamera.forward);
            RaycastHit[] hits = Physics.RaycastAll(ray, interactDistance, ~0, QueryTriggerInteraction.Collide);

            ResonanceInteractable bestTarget = null;
            float bestDist = float.MaxValue;

            if (hits != null && hits.Length > 0)
            {
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(transform)) continue;
                    var inter = h.collider.GetComponentInParent<ResonanceInteractable>() ?? h.collider.GetComponent<ResonanceInteractable>();
                    if (inter != null && inter.isInteractable && h.distance < bestDist)
                    {
                        bestDist = h.distance;
                        bestTarget = inter;
                    }
                }
            }

            if (bestTarget != null)
            {
                ResonanceGameManager.Instance.ui.SetHoverPrompt(true, bestTarget.GetPromptText());
                if (ResonanceGameManager.WasKeyPressed(KeyCode.E))
                {
                    bestTarget.TriggerInteraction(this);
                }
            }
            else
            {
                ResonanceGameManager.Instance.ui.SetHoverPrompt(false, "");
            }
        }
    }
}
