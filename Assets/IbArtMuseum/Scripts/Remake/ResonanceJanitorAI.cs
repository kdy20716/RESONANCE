using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IbArtMuseum.Remake
{
    /// <summary>
    /// J.A.N.I.T.O.R. (보육 로봇이자 관리인) — Part 1에서는 단어 입력 문지기, Part 3 B1F 미로에서는 플레이어 추격 AI로 동작
    /// </summary>
    public class ResonanceJanitorAI : MonoBehaviour
    {
        public bool isChaserInMaze = false;
        public bool isCleared = false;
        public bool isPoweredDown = false;

        [Header("Part 1 Gatekeeper Settings")]
        public int floorNumber = 1;
        public string requiredWord = "LUCIA";
        public string hintDescription = "1층 서쪽 벽의 그림 앞 [붉은 버튼]을 눌러 박사의 진짜 딸 이름을 확인하세요.";
        public Vector3 stepAsideOffset = new Vector3(2.2f, 0f, 0f);

        [Header("Part 3 Maze Chaser Settings")]
        public List<Transform> patrolWaypoints = new List<Transform>();
        public float patrolSpeed = 2.6f;
        public float chaseSpeed = 4.35f;
        public float detectRange = 11.5f;
        public float catchDistance = 1.45f;
        public Light eyeLight;

        private int _currentWaypointIndex = 0;
        private Vector3 _initialPosition;

        private void Start()
        {
            _initialPosition = transform.position;
        }

        private void Update()
        {
            if (isPoweredDown || !isChaserInMaze) return;

            var gm = ResonanceGameManager.Instance;
            if (gm == null || gm.currentStage != ResonanceStage.Part3_B1FMaze || gm.player == null)
                return;

            bool chasing = false;
            Vector3 toPlayer = gm.player.transform.position - transform.position;
            toPlayer.y = 0f;
            float distToPlayer = toPlayer.magnitude;

            if (!gm.isPlayerHiding && distToPlayer <= detectRange)
            {
                chasing = true;
            }

            if (chasing)
            {
                if (eyeLight != null) eyeLight.color = Color.red;
                Vector3 moveDir = toPlayer.normalized;
                transform.position += moveDir * chaseSpeed * Time.deltaTime;
                if (moveDir.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * 8f);
                }

                if (distToPlayer <= catchDistance)
                {
                    gm.ui?.TriggerScreenFlash(new Color(0.9f, 0f, 0f, 0.9f), 0.7f);
                    gm.ui?.ShowToastMessage(
                        gm.IsKorean
                            ? "⚠️ 폭주한 J.A.N.I.T.O.R.에게 붙잡혔습니다! 지하 1층 미로 입구로 되돌아갑니다."
                            : "⚠️ Caught by J.A.N.I.T.O.R.! Respawning at B1F Maze entrance.",
                        4f
                    );
                    if (gm.part3MazeStartPoint != null)
                    {
                        gm.player.TeleportTo(gm.part3MazeStartPoint.position, gm.part3MazeStartPoint.rotation);
                    }
                    transform.position = _initialPosition;
                }
            }
            else
            {
                if (eyeLight != null) eyeLight.color = new Color(1f, 0.55f, 0.1f);
                if (patrolWaypoints != null && patrolWaypoints.Count > 0)
                {
                    Transform wp = patrolWaypoints[_currentWaypointIndex];
                    if (wp != null)
                    {
                        Vector3 toWp = wp.position - transform.position;
                        toWp.y = 0f;
                        if (toWp.magnitude < 0.4f)
                        {
                            _currentWaypointIndex = (_currentWaypointIndex + 1) % patrolWaypoints.Count;
                        }
                        else
                        {
                            Vector3 dir = toWp.normalized;
                            transform.position += dir * patrolSpeed * Time.deltaTime;
                            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 6f);
                        }
                    }
                }
            }
        }

        public void UnlockAndStepAside()
        {
            if (isCleared) return;
            isCleared = true;

            var inter = GetComponent<ResonanceInteractable>();
            if (inter != null)
            {
                inter.promptText = $"[통행 허가됨] J.A.N.I.T.O.R. ({floorNumber}F)";
                inter.isInteractable = false;
            }

            StartCoroutine(CoStepAside());
        }

        private IEnumerator CoStepAside()
        {
            Vector3 start = transform.position;
            Vector3 end = start + transform.TransformDirection(stepAsideOffset);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 1.8f;
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            transform.position = end;
        }

        public void PowerDown()
        {
            isPoweredDown = true;
            if (eyeLight != null) eyeLight.enabled = false;
            transform.localRotation *= Quaternion.Euler(25f, 0f, 0f);
        }
    }
}
