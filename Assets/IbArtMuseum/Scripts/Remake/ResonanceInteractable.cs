using UnityEngine;

namespace IbArtMuseum.Remake
{
    /// <summary>
    /// 씬 내 모든 상호작용 오브젝트(붉은 버튼 스탠드, 액자, 제니터, 엘리베이터, 비밀 일지, 캐비닛, 차단기, 탈출문 등)
    /// </summary>
    public class ResonanceInteractable : MonoBehaviour
    {
        public ResonanceInteractType interactType;
        public bool isInteractable = true;
        public string promptText = "[E] 조사하기";
        public string titleText = "";
        [TextArea(2, 6)]
        public string bodyText = "";
        public string clueWord = "";
        public int floorNumber = 1;

        public ResonanceJanitorAI linkedJanitor;
        public Transform hidingInsidePoint;

        public string GetPromptText()
        {
            return promptText;
        }

        public void TriggerInteraction(ResonanceJournalistController player)
        {
            var gm = ResonanceGameManager.Instance;
            if (gm == null || gm.ui == null) return;

            switch (interactType)
            {
                case ResonanceInteractType.PaintingPedestalButton:
                    if (!string.IsNullOrEmpty(clueWord))
                    {
                        gm.RecordClueWord(clueWord, floorNumber);
                    }
                    gm.ui.ShowDialogueModal(
                        titleText,
                        bodyText + (string.IsNullOrEmpty(clueWord)
                            ? ""
                            : (gm.IsKorean
                                ? $"\n\n▶ 발견한 숨겨진 단어 : 【 {clueWord} 】\n(이 층의 계단을 지키는 J.A.N.I.T.O.R.에게 입력하세요)"
                                : $"\n\n▶ Discovered Hidden Word : 【 {clueWord} 】\n(Input this word to J.A.N.I.T.O.R. guarding this floor's stairs)")),
                        gm.IsKorean ? "단서 확인 완료" : "Confirm Clue"
                    );
                    break;

                case ResonanceInteractType.ArtworkFrame:
                case ResonanceInteractType.FriendlyReplicaNPC:
                    gm.ui.ShowDialogueModal(titleText, bodyText, gm.IsKorean ? "닫기" : "Close");
                    break;

                case ResonanceInteractType.JanitorGatekeeper:
                    if (linkedJanitor != null && !linkedJanitor.isCleared)
                    {
                        gm.ui.OpenWordInputModal(linkedJanitor);
                    }
                    break;

                case ResonanceInteractType.ElevatorCallButton:
                    isInteractable = false;
                    gm.TriggerElevatorCrashSequence();
                    break;

                case ResonanceInteractType.TopSecretDocument:
                    gm.OnReadTopSecretDocument();
                    break;

                case ResonanceInteractType.LuciaSpiritPortrait:
                    gm.OnUnlockCameraFromLucia();
                    break;

                case ResonanceInteractType.LabDoorToCorridor:
                    if (!gm.hasCamera)
                    {
                        gm.ui.ShowToastMessage(
                            gm.IsKorean
                                ? "문이 잠겨 있습니다! 먼저 책상의 [기밀 실험 일지]와 벽의 [루시아의 초상화]를 조사하세요."
                                : "Locked! Inspect the [Top Secret Document] and [Lucia's Portrait] first.",
                            3.5f
                        );
                    }
                    break;

                case ResonanceInteractType.CorridorDoorToB1FMaze:
                    if (gm.purifiedDistortionCount >= gm.totalDistortionCount)
                    {
                        gm.OnEnterB1FMaze();
                    }
                    else
                    {
                        gm.ui.ShowToastMessage(
                            gm.IsKorean
                                ? $"공간 왜곡이 문을 가로막고 있습니다! 카메라 플래시로 안구 왜곡을 먼저 정화하세요 ({gm.purifiedDistortionCount}/{gm.totalDistortionCount})"
                                : $"Spatial distortion blocks the door! Purify all eyeball clusters first ({gm.purifiedDistortionCount}/{gm.totalDistortionCount})",
                            3.5f
                        );
                    }
                    break;

                case ResonanceInteractType.HidingLocker:
                    if (hidingInsidePoint != null)
                    {
                        player.EnterHidingLocker(hidingInsidePoint);
                    }
                    break;

                case ResonanceInteractType.MazeExitKey:
                    isInteractable = false;
                    gameObject.SetActive(false);
                    gm.OnPickupMazeExitKey();
                    break;

                case ResonanceInteractType.MazeDoorToEscapeLobby:
                    if (gm.hasMazeExitKey)
                    {
                        gm.OnEnterEscapeLobby();
                    }
                    else
                    {
                        gm.ui.ShowToastMessage(
                            gm.IsKorean
                                ? "🔒 잠겨 있습니다! 지하 1층 미로 안쪽에서 [출구 열쇠]를 먼저 찾으세요."
                                : "🔒 Locked! Find the [Exit Key] on the central desk inside the B1F Maze first.",
                            3.5f
                        );
                    }
                    break;

                case ResonanceInteractType.ElectricalRoomDoor:
                    if (!gm.isElectricalDoorUnlocked)
                    {
                        gm.ui.OpenElectricalKeypadModal();
                    }
                    break;

                case ResonanceInteractType.MainBreakerSwitch:
                    if (!gm.isMainBreakerOff)
                    {
                        promptText = gm.IsKorean ? "[차단 완료] 메인 차단기가 내려가 있습니다" : "[SHUT DOWN] Main Breaker is OFF";
                        gm.OnPullMainBreaker();
                    }
                    break;

                case ResonanceInteractType.FinalExitDoor:
                    gm.OnKickOpenFinalExitDoor();
                    break;
            }
        }
    }
}
