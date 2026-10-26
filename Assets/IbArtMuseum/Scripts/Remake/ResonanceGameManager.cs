using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IbArtMuseum.Remake
{
    public enum ResonanceStage
    {
        MainMenu,
        Tutorial,
        Part1_Museum,
        ElevatorCrash,
        Part2_UndergroundLab,
        Part3_B1FMaze,
        EscapeLobby,
        EndingCutscene
    }

    public enum ResonanceInteractType
    {
        PaintingPedestalButton,
        ArtworkFrame,
        JanitorGatekeeper,
        FriendlyReplicaNPC,
        ElevatorCallButton,
        TopSecretDocument,
        LuciaSpiritPortrait,
        LabDoorToCorridor,
        CorridorDoorToB1FMaze,
        HidingLocker,
        MazeExitKey,
        MazeDoorToEscapeLobby,
        ElectricalRoomDoor,
        MainBreakerSwitch,
        FinalExitDoor
    }

    public enum ResonanceLanguage
    {
        Korean,
        English
    }

    /// <summary>
    /// RESONANCE (공명) 리메이크 전체 게임 상태, 퀘스트 목표, 언어/설정, 엘리베이터 추락, 멀티 엔딩 연출 총괄 매니저
    /// </summary>
    public class ResonanceGameManager : MonoBehaviour
    {
        public static ResonanceGameManager Instance { get; private set; }

        [Header("Game State")]
        public ResonanceStage currentStage = ResonanceStage.MainMenu;
        public ResonanceLanguage currentLanguage = ResonanceLanguage.Korean;
        public bool hasFlashlight = true;
        public bool hasCamera = false;
        public bool hasReadSecretDoc = false;
        public bool hasMazeExitKey = false;
        public bool isElectricalDoorUnlocked = false;
        public bool isMainBreakerOff = false;
        public bool isPlayerHiding = false;

        [Header("Settings")]
        public float settingMouseSensitivity = 0.12f;
        public float settingMasterVolume = 0.5f;
        public float settingFlashlightScale = 1.0f;

        [Header("Part 1 Clues")]
        public List<string> discoveredWords = new List<string>();

        [Header("Part 2 Distortion Purification")]
        public int purifiedDistortionCount = 0;
        public int totalDistortionCount = 4;

        [Header("Scene References")]
        public ResonanceJournalistController player;
        public ResonanceUIManager ui;
        public Transform mainMenuCamPoint;
        public Transform part1StartPoint;
        public Transform elevatorInsidePoint;
        public Transform part2LabWakePoint;
        public Transform part3MazeStartPoint;
        public Transform escapeLobbySpawnPoint;
        public Transform badEndingCamPoint;
        public Transform happyEndingCamPoint;

        [Header("Dynamic Doors & Objects")]
        public Transform elevatorLeftDoor;
        public Transform elevatorRightDoor;
        public Light elevatorInteriorLight;
        public Transform labExitDoor;
        public Transform distortionExitDoorToB1F;
        public Transform b1fMazeExitDoor;
        public Transform electricalRoomDoor;
        public Transform mainBreakerLever;
        public Transform finalExitLeftPlank;
        public Transform finalExitRightPlank;
        public GameObject hostileReplicaLabFigure;
        public GameObject badEndingReplicaMonster;
        public List<ResonanceJanitorAI> allJanitors = new List<ResonanceJanitorAI>();
        public List<Light> facilityLights = new List<Light>();

        private bool _elevatorSequenceRunning = false;
        private bool _endingSequenceRunning = false;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (player == null) player = FindFirstObjectByType<ResonanceJournalistController>();
            if (ui == null) ui = FindFirstObjectByType<ResonanceUIManager>();

            EnsureRuntimeHDRPIndoorExposureAndFill();
            ApplySettingsToPlayer();
            EnterMainMenu();
        }

        /// <summary>
        /// HDRP 야외 노출(EV100=12~14)로 인해 실내 미술관이 암흑으로 렌더링되는 현상을 방지하고
        /// 참고 컨셉아트와 동일한 은은한 청회색 천창/월광 앰비언트 + 따뜻한 갤러리 스포트라이트 노출(EV100=6.2)을 보장합니다.
        /// </summary>
        private void EnsureRuntimeHDRPIndoorExposureAndFill()
        {
            // 기존 야외 템플릿 Volume 무력화 및 실내 전용 Global Volume 보장
            Volume[] volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (var v in volumes)
            {
                if (v != null && v.gameObject.name != "RESONANCE_Atmosphere_GlobalVolume")
                {
                    v.weight = 0f;
                    v.enabled = false;
                }
            }

            GameObject volGo = GameObject.Find("RESONANCE_Atmosphere_GlobalVolume");
            if (volGo == null)
            {
                volGo = new GameObject("RESONANCE_Atmosphere_GlobalVolume");
                volGo.transform.SetParent(transform);
            }
            Volume vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            vol.weight = 1f;
            vol.enabled = true;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Resonance_RuntimeIndoor_Profile";

            VisualEnvironment visualEnv = profile.Add<VisualEnvironment>(true);
            visualEnv.skyType.Override(0);

            Exposure exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(6.2f);

            IndirectLightingController indirect = profile.Add<IndirectLightingController>(true);
            indirect.indirectDiffuseLightingMultiplier.Override(2.2f);
            indirect.reflectionLightingMultiplier.Override(1.4f);

            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.22f);

            vol.sharedProfile = profile;

            // 다각도 그림자 없는 은은한 건축 앰비언트 필 라이트 보장 (참고 이미지의 천창 달빛 & 실내 간접광 재현)
            if (GameObject.Find("Resonance_AmbientFill_Rig") == null)
            {
                GameObject rig = new GameObject("Resonance_AmbientFill_Rig");
                rig.transform.SetParent(transform);

                CreateFillDirectional(rig.transform, "FillDir_SkylightCool", new Vector3(58f, -35f, 0f), new Color(0.78f, 0.86f, 0.98f), 95f);
                CreateFillDirectional(rig.transform, "FillDir_WarmBounce", new Vector3(42f, 145f, 0f), new Color(0.96f, 0.89f, 0.78f), 75f);
                CreateFillDirectional(rig.transform, "FillDir_UpwardFloorBounce", new Vector3(-45f, 60f, 0f), new Color(0.80f, 0.84f, 0.90f), 55f);
                CreateFillDirectional(rig.transform, "FillDir_SideCrossFill", new Vector3(25f, -120f, 0f), new Color(0.84f, 0.88f, 0.95f), 55f);
            }
        }

        private static void CreateFillDirectional(Transform parent, string name, Vector3 euler, Color col, float luxIntensity)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.rotation = Quaternion.Euler(euler);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = col;
            l.shadows = LightShadows.None;
            HDAdditionalLightData hd = go.AddComponent<HDAdditionalLightData>();
            l.intensity = luxIntensity;
            hd.intensity = luxIntensity;
        }

        private void Update()
        {
            // 메인 메뉴에서 ESC로 설정창 닫기
            if (currentStage == ResonanceStage.MainMenu)
            {
                if (WasKeyPressed(KeyCode.Escape) && ui != null)
                {
                    ui.OnClickCloseSettings();
                }
                return;
            }

            // 시연 및 디버그 편의를 위한 챕터 즉시 이동 단축키 (F1~F4)
            if (WasKeyPressed(KeyCode.F1))
            {
                JumpToStage(ResonanceStage.Part1_Museum);
            }
            else if (WasKeyPressed(KeyCode.F2))
            {
                JumpToStage(ResonanceStage.Part2_UndergroundLab);
            }
            else if (WasKeyPressed(KeyCode.F3))
            {
                JumpToStage(ResonanceStage.Part3_B1FMaze);
            }
            else if (WasKeyPressed(KeyCode.F4))
            {
                JumpToStage(ResonanceStage.EscapeLobby);
            }
        }

        public bool IsKorean => currentLanguage == ResonanceLanguage.Korean;

        public void ApplySettingsToPlayer()
        {
            AudioListener.volume = Mathf.Clamp01(settingMasterVolume);
            if (player != null)
            {
                player.mouseSensitivity = settingMouseSensitivity;
                if (player.flashlightSpot != null)
                {
                    float flashPower = 1800f * settingFlashlightScale;
                    player.flashlightSpot.intensity = flashPower;
                    var hd = player.flashlightSpot.GetComponent<HDAdditionalLightData>();
                    if (hd != null) hd.intensity = flashPower;
                }
                AudioSource bgm = player.GetComponent<AudioSource>();
                if (bgm != null)
                {
                    bgm.volume = 0.16f * settingMasterVolume;
                }
            }
        }

        public void EnterMainMenu()
        {
            currentStage = ResonanceStage.MainMenu;
            if (player != null)
            {
                player.SetControlEnabled(false);
                Transform camPt = mainMenuCamPoint != null ? mainMenuCamPoint : part1StartPoint;
                if (camPt != null)
                {
                    player.TeleportTo(camPt.position, camPt.rotation);
                }
            }
            if (ui != null)
            {
                ui.CloseAllModals();
                ui.ShowMainMenu(true);
                ui.RefreshLanguageTexts();
            }
        }

        /// <summary>
        /// 메인 메뉴에서 [게임 시작 (START GAME)] 클릭 시 즉시 인게임 진입!
        /// </summary>
        public void OnClickStartGame()
        {
            currentStage = ResonanceStage.Part1_Museum;
            isPlayerHiding = false;

            if (ui != null)
            {
                ui.CloseAllModals();
                ui.ShowMainMenu(false);
                ui.UpdateObjective(
                    IsKorean
                        ? "PART 1 : 폐쇄된 바이스만 미술관 (1F ~ 4F)"
                        : "PART 1 : Abandoned Weissmann Art Museum (1F ~ 4F)",
                    IsKorean
                        ? "• 각 층의 그림 앞 [붉은 버튼 스탠드]를 [E]키로 눌러 숨겨진 단어를 찾으세요.\n" +
                          "• 계단을 막고 있는 관리 로봇 [J.A.N.I.T.O.R.]에게 단어를 입력해 위층으로 이동하세요.\n" +
                          "• [WASD] 이동 | [Shift] 달리기 | [F] 손전등 | [E] 상호작용"
                        : "• Press [E] on the [Red Button Pedestals] in front of paintings to find hidden words.\n" +
                          "• Enter the hidden word to [J.A.N.I.T.O.R.] blocking the stairs to ascend.\n" +
                          "• [WASD] Move | [Shift] Sprint | [F] Flashlight | [E] Interact"
                );
                ui.ShowToastMessage(
                    IsKorean
                        ? "1989년 10월 — 폐쇄된 바이스만 미술관 내부. 취재를 시작합니다. ([WASD] 이동 / [E] 조사 / [F] 손전등)"
                        : "October 1989 — Inside the closed Weissmann Art Museum. Investigation begins.",
                    5.0f
                );
            }

            if (player != null)
            {
                if (part1StartPoint != null)
                {
                    player.TeleportTo(part1StartPoint.position, part1StartPoint.rotation);
                }
                player.SetControlEnabled(true);
            }
        }

        public void OnClickConfirmTutorial()
        {
            OnClickStartGame();
        }

        public void ToggleLanguage()
        {
            currentLanguage = (currentLanguage == ResonanceLanguage.Korean)
                ? ResonanceLanguage.English
                : ResonanceLanguage.Korean;

            if (ui != null)
            {
                ui.RefreshLanguageTexts();
            }
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void JumpToStage(ResonanceStage targetStage)
        {
            if (ui != null)
            {
                ui.CloseAllModals();
            }
            isPlayerHiding = false;

            switch (targetStage)
            {
                case ResonanceStage.Part1_Museum:
                    currentStage = ResonanceStage.Part1_Museum;
                    if (player != null && part1StartPoint != null)
                        player.TeleportTo(part1StartPoint.position, part1StartPoint.rotation);
                    player?.SetControlEnabled(true);
                    ui?.UpdateObjective(
                        IsKorean ? "PART 1 : 폐쇄된 바이스만 미술관" : "PART 1 : Abandoned Art Museum",
                        IsKorean
                            ? "• 그림 앞 [붉은 버튼]을 조사하여 숨겨진 단어를 획득하세요.\n• [J.A.N.I.T.O.R.]에게 암호를 입력해 최상층 엘리베이터로 향하세요."
                            : "• Inspect [Red Buttons] before paintings to find hidden words.\n• Input words to [J.A.N.I.T.O.R.] to reach the top floor elevator."
                    );
                    ui?.ShowToastMessage(IsKorean ? "[챕터 이동] PART 1 : 폐쇄된 미술관 1F 로비" : "[Chapter Jump] PART 1 : 1F Museum Lobby", 3f);
                    break;

                case ResonanceStage.Part2_UndergroundLab:
                    currentStage = ResonanceStage.Part2_UndergroundLab;
                    if (player != null && part2LabWakePoint != null)
                        player.TeleportTo(part2LabWakePoint.position, part2LabWakePoint.rotation);
                    player?.SetControlEnabled(true);
                    ui?.UpdateObjective(
                        IsKorean ? "PART 2 : 지하 비밀 연구소 (B4F)" : "PART 2 : Underground Lab (B4F)",
                        IsKorean
                            ? "• 연구실 책상 위의 [바이스만 박사의 기밀 실험 일지]를 조사하세요.\n• 진짜 딸 [루시아의 초상화]와 공명하여 취재 카메라 플래시를 해금하세요."
                            : "• Read [Dr. Weissmann's Top Secret Document] on the desk.\n• Inspect [Lucia's Portrait] to unlock the Camera Flash."
                    );
                    ui?.ShowToastMessage(IsKorean ? "[챕터 이동] PART 2 : 지하 4층 바이스만 비밀 연구소" : "[Chapter Jump] PART 2 : B4F Secret Lab", 3f);
                    break;

                case ResonanceStage.Part3_B1FMaze:
                    currentStage = ResonanceStage.Part3_B1FMaze;
                    hasCamera = true;
                    hasReadSecretDoc = true;
                    if (player != null && part3MazeStartPoint != null)
                        player.TeleportTo(part3MazeStartPoint.position, part3MazeStartPoint.rotation);
                    player?.SetControlEnabled(true);
                    ui?.UpdateObjective(
                        IsKorean ? "PART 3 : 지하 1층 타일 미로 & 추격전" : "PART 3 : B1F Tile Maze & Chase",
                        IsKorean
                            ? "• 배회하는 [J.A.N.I.T.O.R.]를 피해 미로 안쪽에서 [출구 열쇠]를 찾으세요.\n• 위급할 때는 [캐비닛(사물함)]에 [E]키로 숨을 수 있습니다."
                            : "• Evade the patrolling [J.A.N.I.T.O.R.] and find the [Exit Key] in the maze.\n• Press [E] on lockers to hide."
                    );
                    ui?.ShowToastMessage(IsKorean ? "[챕터 이동] PART 3 : 지하 1층 백색 타일 미로" : "[Chapter Jump] PART 3 : B1F White Tile Maze", 3f);
                    break;

                case ResonanceStage.EscapeLobby:
                    currentStage = ResonanceStage.EscapeLobby;
                    hasCamera = true;
                    hasReadSecretDoc = true;
                    hasMazeExitKey = true;
                    if (player != null && escapeLobbySpawnPoint != null)
                        player.TeleportTo(escapeLobbySpawnPoint.position, escapeLobbySpawnPoint.rotation);
                    player?.SetControlEnabled(true);
                    UpdateEscapeLobbyObjective();
                    ui?.ShowToastMessage(IsKorean ? "[챕터 이동] 최종 구역 : 1층 탈출 로비 & 전기 제어실" : "[Chapter Jump] Final Zone : 1F Escape Lobby", 3f);
                    break;
            }
        }

        public void RecordClueWord(string word, int floor)
        {
            if (!discoveredWords.Contains(word))
            {
                discoveredWords.Add(word);
            }
            if (ui != null)
            {
                ui.ShowToastMessage(
                    IsKorean
                        ? $"[단서 획득] {floor}층 그림의 기억에서 숨겨진 단어 「{word}」를 발견했습니다!"
                        : $"[Clue Found] Discovered hidden word \"{word}\" from the {floor}F painting!",
                    4f
                );
            }
        }

        public void TriggerElevatorCrashSequence()
        {
            if (_elevatorSequenceRunning) return;
            StartCoroutine(CoElevatorCrashSequence());
        }

        private IEnumerator CoElevatorCrashSequence()
        {
            _elevatorSequenceRunning = true;
            currentStage = ResonanceStage.ElevatorCrash;

            if (ui != null)
            {
                ui.ShowToastMessage(
                    IsKorean ? "엘리베이터가 호출되었습니다. 내부로 탑승합니다..." : "Elevator called. Boarding...",
                    2.5f
                );
            }

            float t = 0f;
            Vector3 leftStart = elevatorLeftDoor != null ? elevatorLeftDoor.localPosition : Vector3.zero;
            Vector3 rightStart = elevatorRightDoor != null ? elevatorRightDoor.localPosition : Vector3.zero;
            while (t < 1f)
            {
                t += Time.deltaTime * 1.5f;
                if (elevatorLeftDoor != null)
                    elevatorLeftDoor.localPosition = Vector3.Lerp(leftStart, leftStart + new Vector3(-0.9f, 0, 0), t);
                if (elevatorRightDoor != null)
                    elevatorRightDoor.localPosition = Vector3.Lerp(rightStart, rightStart + new Vector3(0.9f, 0, 0), t);
                yield return null;
            }

            yield return new WaitForSeconds(0.4f);

            if (player != null && elevatorInsidePoint != null)
            {
                player.TeleportTo(elevatorInsidePoint.position, elevatorInsidePoint.rotation);
                player.SetControlEnabled(false);
            }

            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 2.2f;
                if (elevatorLeftDoor != null)
                    elevatorLeftDoor.localPosition = Vector3.Lerp(leftStart + new Vector3(-0.9f, 0, 0), leftStart, t);
                if (elevatorRightDoor != null)
                    elevatorRightDoor.localPosition = Vector3.Lerp(rightStart + new Vector3(0.9f, 0, 0), rightStart, t);
                yield return null;
            }

            if (ui != null)
            {
                ui.UpdateObjective(
                    IsKorean ? "비상 상황 : 엘리베이터 고장!" : "EMERGENCY : ELEVATOR FAILURE!",
                    IsKorean ? "• 케이블 장력이 급격히 손실되고 있습니다...!" : "• Cable tension failing! Plummeting to sub-levels...!"
                );
                ui.ShowToastMessage(
                    IsKorean
                        ? "덜컥—! \"경고: 엘리베이터 와이어 결함 감지! 지하 구역으로 추락합니다!!\""
                        : "CLANG—! \"WARNING: Elevator cable snapped! Plummeting to underground lab!!\"",
                    3.2f
                );
            }

            float shakeTime = 0f;
            while (shakeTime < 3.0f)
            {
                shakeTime += Time.deltaTime;
                if (elevatorInteriorLight != null)
                {
                    elevatorInteriorLight.color = Color.red;
                    elevatorInteriorLight.intensity = (Mathf.Sin(shakeTime * 28f) > 0f) ? 600f : 40f;
                }
                if (player != null && player.playerCamera != null)
                {
                    float intensity = Mathf.Lerp(0.03f, 0.16f, shakeTime / 3.0f);
                    player.playerCamera.localPosition = new Vector3(
                        UnityEngine.Random.Range(-intensity, intensity),
                        1.65f + UnityEngine.Random.Range(-intensity, intensity),
                        0f
                    );
                }
                yield return null;
            }

            if (ui != null) ui.TriggerScreenFlash(Color.black, 1.8f);
            yield return new WaitForSeconds(0.8f);

            currentStage = ResonanceStage.Part2_UndergroundLab;
            if (player != null && part2LabWakePoint != null)
            {
                player.TeleportTo(part2LabWakePoint.position, part2LabWakePoint.rotation);
                player.SetControlEnabled(true);
            }

            if (ui != null)
            {
                ui.UpdateObjective(
                    IsKorean ? "PART 2 : 지하 비밀 연구소 (B4F)" : "PART 2 : Underground Secret Lab (B4F)",
                    IsKorean
                        ? "• 책상 위의 [바이스만 박사의 기밀 실험 일지(TOP SECRET)]를 조사하세요.\n• 진짜 딸 [루시아의 초상화]를 조사하여 카메라 플래시 능력을 얻으세요."
                        : "• Read [Dr. Weissmann's Top Secret Document] on the desk.\n• Inspect [Lucia's Portrait] to unlock the Camera Flash."
                );
                ui.ShowToastMessage(
                    IsKorean
                        ? "B4F 지하 연구소에 추락했습니다... 공기 중에 피비린내와 포르말린 냄새가 진동합니다."
                        : "Crashed into B4F Underground Lab... The air reeks of blood and formalin.",
                    4.5f
                );
            }

            _elevatorSequenceRunning = false;
        }

        public void OnReadTopSecretDocument()
        {
            hasReadSecretDoc = true;
            if (hostileReplicaLabFigure != null)
            {
                hostileReplicaLabFigure.SetActive(true);
            }

            if (ui != null)
            {
                ui.ShowDocumentModal(
                    IsKorean ? "기밀 실험 기록 : PROJECT RESONANCE (1989)" : "TOP SECRET : PROJECT RESONANCE (1989)",
                    IsKorean
                        ? "【 바이스만 박사의 비밀 실험 일지 — TOP SECRET 】\n\n" +
                          "1. 실험 목적 : 죽은 내 딸 '루시아(Lucia)'의 뇌파와 기억을 완벽히 복원하기 위해,\n" +
                          "   미술관 관람객들의 신경 에너지를 추출하여 인공소녀 [레플리카(Replica)]에게 이식했다.\n\n" +
                          "2. 부작용 보고 : 희생된 피실험자들의 원혼이 미술관의 액자와 지하 복도 벽면에\n" +
                          "   [기괴한 안구(Eyeballs)]와 [잔향체] 형태로 뒤엉켜 공간을 왜곡시키고 있다.\n" +
                          "   ※ 강한 빛(카메라 플래시)을 터뜨리면 잔향체의 파동이 약화되어 왜곡이 걷힌다.\n\n" +
                          "3. 레플리카의 폭주 : 레플리카가 자신이 가짜라는 사실을 눈치채고 폭주하기 시작했다!\n" +
                          "   만약 레플리카와 관리 로봇(J.A.N.I.T.O.R.)을 완전히 멈추려면,\n" +
                          "   1층 로비 옆 [전기 제어실(암호: 1989)]에 들어가 반드시 [메인 차단기(MAIN BREAKER)]를 내려야 한다!\n" +
                          "   차단기를 내리지 않고 탈출하면 레플리카는 끝까지 추격할 것이다."
                        : "【 DR. WEISSMANN'S RESEARCH LOG — TOP SECRET 】\n\n" +
                          "1. Objective: To revive my deceased daughter 'Lucia', I extracted neural energy from\n" +
                          "   museum visitors and implanted Lucia's memories into the artificial girl [Replica].\n\n" +
                          "2. Side Effects: The victims' spirits have manifested as [Creepy Wall Eyeballs] and\n" +
                          "   [Resonants], distorting the underground corridors.\n" +
                          "   * Intense light (Camera Flash) weakens their wave and purifies the distortion.\n\n" +
                          "3. Emergency Protocol: Replica has gone berserk after realizing she is a fake!\n" +
                          "   To permanently stop Replica and J.A.N.I.T.O.R., you MUST enter the 1F [Electrical Room (Code: 1989)]\n" +
                          "   and pull down the [MAIN BREAKER] before escaping!"
                );

                ui.UpdateObjective(
                    IsKorean ? "PART 2 : 지하 연구소 — 루시아와의 공명" : "PART 2 : Resonate with Lucia's Portrait",
                    IsKorean
                        ? "• 레플리카가 본색을 드러냈습니다! 연구실 벽의 [루시아의 초상화]를 조사하세요.\n• (기억할 암호 : 1층 전기 제어실 보안 코드 [ 1989 ])"
                        : "• Replica turned hostile! Inspect [Lucia's Portrait] on the north wall.\n• (Remember Passcode: Electrical Room [ 1989 ])"
                );
            }
        }

        public void OnUnlockCameraFromLucia()
        {
            hasCamera = true;

            if (labExitDoor != null)
            {
                StartCoroutine(CoSlideDoorOpen(labExitDoor, new Vector3(0, 3.2f, 0)));
            }

            if (ui != null)
            {
                ui.ShowDialogueModal(
                    IsKorean ? "루시아 (진짜 딸의 영혼)" : "Lucia (Spirit of the Real Daughter)",
                    IsKorean
                        ? "\"기자님... 제발 저를 대신해 만들어진 가짜 아이, 레플리카를 멈춰주세요.\n" +
                          "아버지의 실험으로 희생된 사람들의 눈동자가 지하 복도를 가로막고 있어요.\n" +
                          "기자님의 [카메라 플래시(우클릭 조준 + 좌클릭 촬영)]로 벽면의 눈알과 원혼들을 찍어 왜곡을 정화해주세요!\""
                        : "\"Please stop Replica, the artificial girl created in my place...\n" +
                          "Use your [Camera Flash (Hold RMB or press C to aim + LMB to shoot)] to purify the eyeballs and spirits in the corridor!\"",
                    IsKorean ? "[ 카메라 플래시 해금 (RMB 조준 / LMB 촬영) ]" : "[ Camera Flash Unlocked (RMB Aim / LMB Shoot) ]"
                );

                ui.UpdateObjective(
                    IsKorean
                        ? $"PART 2 : 왜곡된 지하 복도 정화 ({purifiedDistortionCount} / {totalDistortionCount})"
                        : $"PART 2 : Purify Corridor Distortions ({purifiedDistortionCount} / {totalDistortionCount})",
                    IsKorean
                        ? "• [마우스 우클릭(또는 C키)]으로 카메라 뷰파인더를 켜고,\n" +
                          "• 복도 벽면의 [기괴한 눈알 군집 & 원혼]을 향해 [좌클릭]으로 플래시를 터뜨리세요!\n" +
                          $"• 정화 진행도 : {purifiedDistortionCount} / {totalDistortionCount}"
                        : "• Aim with [Right Click (or C)] and press [Left Click] to flash-photograph the Wall Eyeballs!\n" +
                          $"• Purified : {purifiedDistortionCount} / {totalDistortionCount}"
                );
            }
        }

        public void OnDistortionClusterPurified()
        {
            purifiedDistortionCount++;
            if (ui != null)
            {
                ui.TriggerScreenFlash(new Color(0.85f, 0.95f, 1f, 0.85f), 0.45f);
                ui.ShowToastMessage(
                    IsKorean
                        ? $"📸 [왜곡 약화 성공!] 잔향체의 공간 왜곡이 정화되었습니다 ({purifiedDistortionCount} / {totalDistortionCount})"
                        : $"📸 [Distortion Weakened!] Anomaly cluster purified ({purifiedDistortionCount} / {totalDistortionCount})",
                    3.2f
                );
            }

            if (purifiedDistortionCount >= totalDistortionCount)
            {
                if (distortionExitDoorToB1F != null)
                {
                    StartCoroutine(CoSlideDoorOpen(distortionExitDoorToB1F, new Vector3(0, 3.2f, 0)));
                }
                if (ui != null)
                {
                    ui.UpdateObjective(
                        IsKorean ? "PART 2 완료 : 지하 1층 미로 진입" : "PART 2 Clear : Enter B1F Tile Maze",
                        IsKorean
                            ? "• 모든 공간 왜곡이 약화되어 B1F 보안문이 열렸습니다!\n• 복도 끝의 계단 통로를 통해 [지하 1층 타일 미로]로 올라가세요."
                            : "• All distortions purified! Proceed through the north door to the [B1F Tile Maze]."
                    );
                    ui.ShowToastMessage(
                        IsKorean
                            ? "모든 왜곡이 정화되어 [B1F 지하 1층 미로]로 향하는 철문이 열렸습니다!"
                            : "All distortions purified! The security door to B1F Maze is now open!",
                        4.5f
                    );
                }
            }
            else
            {
                if (ui != null)
                {
                    ui.UpdateObjective(
                        IsKorean
                            ? $"PART 2 : 왜곡된 지하 복도 정화 ({purifiedDistortionCount} / {totalDistortionCount})"
                            : $"PART 2 : Purify Corridor Distortions ({purifiedDistortionCount} / {totalDistortionCount})",
                        IsKorean
                            ? "• [우클릭(또는 C키)] 카메라 조준 후 [좌클릭] 플래시로 남은 눈알 벽면과 원혼을 촬영하세요.\n" +
                              $"• 정화 진행도 : {purifiedDistortionCount} / {totalDistortionCount}"
                            : $"• Aim Camera [RMB/C] and Flash [LMB] at remaining eyeball clusters ({purifiedDistortionCount}/{totalDistortionCount})."
                    );
                }
            }
        }

        public void OnEnterB1FMaze()
        {
            currentStage = ResonanceStage.Part3_B1FMaze;
            if (player != null && part3MazeStartPoint != null)
            {
                player.TeleportTo(part3MazeStartPoint.position, part3MazeStartPoint.rotation);
            }
            if (ui != null)
            {
                ui.UpdateObjective(
                    IsKorean ? "PART 3 : 지하 1층 백색 타일 미로 (B1F)" : "PART 3 : B1F White Tile Maze",
                    IsKorean
                        ? "• 미로를 순찰하는 폭주 관리 로봇 [J.A.N.I.T.O.R.]에게 잡히지 마세요!\n" +
                          "• 위급할 땐 미로 곳곳의 [철제 캐비닛]을 [E]키로 조사해 숨을 수 있습니다.\n" +
                          "• 미로 중앙의 보안 책상에서 [1층 로비 출구 열쇠]를 획득하세요."
                        : "• Evade the berserk [J.A.N.I.T.O.R.] patrolling the maze!\n" +
                          "• Press [E] on Steel Lockers to hide.\n" +
                          "• Find the [Exit Key] on the central desk."
                );
                ui.ShowToastMessage(
                    IsKorean
                        ? "경고: 지하 1층 미로에서 폭주한 J.A.N.I.T.O.R.의 발소리가 들립니다!"
                        : "WARNING: A berserk J.A.N.I.T.O.R. is patrolling the B1F Maze!",
                    4f
                );
            }
        }

        public void OnPickupMazeExitKey()
        {
            hasMazeExitKey = true;
            if (b1fMazeExitDoor != null)
            {
                StartCoroutine(CoSlideDoorOpen(b1fMazeExitDoor, new Vector3(0, 3.2f, 0)));
            }
            if (ui != null)
            {
                ui.ShowToastMessage(
                    IsKorean
                        ? "🔑 [출구 열쇠 획득!] 지하 1층 미로 북쪽의 [1F 탈출 로비 계단문]이 열렸습니다!"
                        : "🔑 [Exit Key Acquired!] The north door to the 1F Escape Lobby is now open!",
                    4.5f
                );
                ui.UpdateObjective(
                    IsKorean ? "PART 3 : 1층 탈출 로비로 이동" : "PART 3 : Proceed to 1F Escape Lobby",
                    IsKorean
                        ? "• [출구 열쇠]를 획득했습니다! J.A.N.I.T.O.R.를 피해 열린 북쪽 계단문으로 탈출하세요."
                        : "• Escape through the unlocked north door to reach the 1F Escape Lobby."
                );
            }
        }

        public void OnEnterEscapeLobby()
        {
            currentStage = ResonanceStage.EscapeLobby;
            isPlayerHiding = false;
            if (player != null && escapeLobbySpawnPoint != null)
            {
                player.TeleportTo(escapeLobbySpawnPoint.position, escapeLobbySpawnPoint.rotation);
            }
            UpdateEscapeLobbyObjective();
            if (ui != null)
            {
                ui.ShowToastMessage(
                    IsKorean
                        ? "1층 탈출 로비에 도착했습니다! 정문(EXIT)으로 바로 나갈지, 전기 제어실의 차단기를 내릴지 선택하세요."
                        : "Reached 1F Escape Lobby! Choose between kicking open the EXIT door or shutting down the Main Breaker first.",
                    5f
                );
            }
        }

        public void UpdateEscapeLobbyObjective()
        {
            if (ui == null) return;
            if (!isMainBreakerOff)
            {
                ui.UpdateObjective(
                    IsKorean ? "최종 선택 : 로비 탈출 vs 전기 제어실 차단" : "Final Choice : Immediate Escape vs Main Breaker",
                    IsKorean
                        ? "• [선택 A - 즉시 탈출] 정문(EXIT)의 판자를 발로 차고 즉시 탈출합니다.\n" +
                          "• [선택 B - 진실의 결말] 우측 [전기 제어실(암호: 1989)] 문을 열고 [메인 차단기]를 내린 뒤 탈출합니다."
                        : "• [Branch A] Kick open the boarded [EXIT] door immediately.\n" +
                          "• [Branch B] Unlock the [Electrical Room (Code: 1989)] and pull the [Main Breaker] before escaping."
                );
            }
            else
            {
                ui.UpdateObjective(
                    IsKorean ? "최종 탈출 : 메인 차단기 가동 중지 완료!" : "Final Escape : Main Breaker Shut Down!",
                    IsKorean
                        ? "• 레플리카와 J.A.N.I.T.O.R.의 동력이 완전히 차단되었습니다!\n" +
                          "• 이제 정문 [EXIT] 문을 [E]키로 발로 차고 미술관을 탈출하세요!"
                        : "• Replica and J.A.N.I.T.O.R. are permanently powered down!\n" +
                          "• Press [E] at the [EXIT] door to kick it open and escape!"
                );
            }
        }

        public void OnUnlockElectricalRoom()
        {
            if (isElectricalDoorUnlocked) return;
            isElectricalDoorUnlocked = true;
            if (electricalRoomDoor != null)
            {
                StartCoroutine(CoSlideDoorOpen(electricalRoomDoor, new Vector3(0, 3.2f, 0)));
            }
            if (ui != null)
            {
                ui.ShowToastMessage(
                    IsKorean
                        ? "🔓 암호 [1989] 인증 완료! [전기 제어실] 보안문이 열렸습니다."
                        : "🔓 Passcode [1989] verified! Electrical Control Room unlocked.",
                    3.5f
                );
            }
        }

        public void OnPullMainBreaker()
        {
            if (isMainBreakerOff) return;
            isMainBreakerOff = true;

            if (mainBreakerLever != null)
            {
                mainBreakerLever.localRotation = Quaternion.Euler(55f, 0f, 0f);
            }

            foreach (var janitor in allJanitors)
            {
                if (janitor != null) janitor.PowerDown();
            }

            foreach (var l in facilityLights)
            {
                if (l != null)
                {
                    l.color = new Color(1f, 0.25f, 0.2f);
                    l.intensity *= 0.45f;
                }
            }

            if (ui != null)
            {
                ui.TriggerScreenFlash(new Color(1f, 0.4f, 0.2f, 0.7f), 0.6f);
                ui.ShowToastMessage(
                    IsKorean
                        ? "⚡ [메인 차단기 OFF!] 미술관과 지하 연구소의 모든 인공 신경망 및 제니터 가동이 정지되었습니다!"
                        : "⚡ [MAIN BREAKER OFF!] All neural networks, Replica, and J.A.N.I.T.O.R. units have been shut down!",
                    5f
                );
            }

            UpdateEscapeLobbyObjective();
        }

        public void OnKickOpenFinalExitDoor()
        {
            if (_endingSequenceRunning) return;
            StartCoroutine(CoTriggerEndingSequence(isMainBreakerOff));
        }

        private IEnumerator CoTriggerEndingSequence(bool isHappyEnding)
        {
            _endingSequenceRunning = true;
            currentStage = ResonanceStage.EndingCutscene;

            if (player != null)
            {
                player.SetControlEnabled(false);
            }

            if (finalExitLeftPlank != null)
            {
                finalExitLeftPlank.localPosition += new Vector3(-0.8f, -0.5f, 0.6f);
                finalExitLeftPlank.localRotation = Quaternion.Euler(25f, 40f, -65f);
            }
            if (finalExitRightPlank != null)
            {
                finalExitRightPlank.localPosition += new Vector3(0.8f, -0.5f, 0.6f);
                finalExitRightPlank.localRotation = Quaternion.Euler(-20f, -35f, 60f);
            }

            if (ui != null)
            {
                ui.ShowToastMessage(
                    IsKorean
                        ? "쾅—!! 굳게 막혀 있던 미술관 정문을 발로 차고 바깥으로 탈출합니다!"
                        : "CRASH—!! You kick open the boarded museum doors and escape outside!",
                    2.5f
                );
                ui.TriggerScreenFlash(Color.white, 1.2f);
            }

            yield return new WaitForSeconds(1.2f);

            if (!isHappyEnding)
            {
                if (player != null && badEndingCamPoint != null)
                {
                    player.TeleportTo(badEndingCamPoint.position, badEndingCamPoint.rotation);
                }

                if (ui != null)
                {
                    ui.UpdateObjective(
                        IsKorean ? "EPILOGUE : 그날 밤, 기자의 자택" : "EPILOGUE : That Night, Your Apartment",
                        IsKorean ? "• 미술관에서 간신히 도망쳐 집으로 돌아왔습니다..." : "• You barely escaped the museum and returned home..."
                    );
                    ui.ShowToastMessage(
                        IsKorean
                            ? "그날 밤 자택... 딩동—! 현관문 밖에서 누군가 초인종을 누릅니다."
                            : "That night at home... Ding-dong! Someone rings your front doorbell.",
                        3.2f
                    );
                }

                yield return new WaitForSeconds(2.2f);

                if (badEndingReplicaMonster != null)
                {
                    badEndingReplicaMonster.SetActive(true);
                    Vector3 startPos = badEndingReplicaMonster.transform.position;
                    Vector3 endPos = badEndingCamPoint != null
                        ? badEndingCamPoint.position + badEndingCamPoint.forward * 1.15f - new Vector3(0, 0.9f, 0)
                        : startPos;

                    float rushT = 0f;
                    while (rushT < 1f)
                    {
                        rushT += Time.deltaTime * 3.8f;
                        badEndingReplicaMonster.transform.position = Vector3.Lerp(startPos, endPos, rushT);
                        yield return null;
                    }
                }

                if (ui != null)
                {
                    ui.TriggerScreenFlash(new Color(0.8f, 0f, 0f, 0.95f), 0.7f);
                }

                yield return new WaitForSeconds(0.9f);

                if (ui != null)
                {
                    ui.ShowEndingScreen(
                        true,
                        false,
                        IsKorean ? "BAD ENDING : 끝나지 않은 공포" : "BAD ENDING : Unending Nightmare",
                        IsKorean
                            ? "당신은 메인 차단기를 내리지 않은 채 미술관 정문을 박차고 도망쳤습니다.\n\n" +
                              "하지만 동력이 살아있던 인공소녀 '레플리카'는 자신의 비밀을 알고 있는 당신을\n" +
                              "결코 살려두지 않았습니다. 그날 밤, 현관문을 두드린 것은 다름 아닌 레플리카였습니다.\n\n" +
                              "💡 [힌트] 1층 탈출 로비 우측의 '전기 제어실(암호: 1989)'에서 메인 차단기를 내린 후 탈출하면 해피 엔딩을 볼 수 있습니다!"
                            : "You escaped the museum without shutting down the Main Breaker.\n\n" +
                              "Still powered, the monstrous 'Replica' tracked you down to silence the truth forever.\n\n" +
                              "💡 [Hint] Enter the Electrical Room (Code: 1989) in the 1F Escape Lobby and pull the Main Breaker to unlock the Happy Ending!"
                    );
                }
            }
            else
            {
                if (player != null && happyEndingCamPoint != null)
                {
                    player.TeleportTo(happyEndingCamPoint.position, happyEndingCamPoint.rotation);
                }

                if (ui != null)
                {
                    ui.UpdateObjective(
                        IsKorean ? "EPILOGUE : 다음 날 아침, 특종 보도" : "EPILOGUE : Next Morning, Breaking News",
                        IsKorean ? "• 당신이 촬영한 사진과 비밀 일지가 세상에 공개되었습니다." : "• Your photographs and Dr. Weissmann's secret log have been broadcast worldwide."
                    );
                    ui.ShowToastMessage(
                        IsKorean
                            ? "다음 날 아침 — TV 뉴스 특보를 통해 바이스만 미술관의 진실이 세상에 보도됩니다!"
                            : "Next Morning — TV Breaking News exposes the truth behind the Weissmann Art Museum!",
                        3.8f
                    );
                }

                yield return new WaitForSeconds(3.2f);

                if (ui != null)
                {
                    ui.ShowEndingScreen(
                        true,
                        true,
                        IsKorean ? "HAPPY ENDING : 진실의 기록 (TRUE ENDING)" : "HAPPY ENDING : Record of Truth (TRUE ENDING)",
                        IsKorean
                            ? "메인 차단기를 내려 폭주하던 레플리카와 관리 로봇 J.A.N.I.T.O.R.를 영구 정지시킨 당신은\n" +
                              "무사히 미술관을 탈출하여 바이스만 박사의 기밀 실험 일지와 현장 사진들을 공개했습니다.\n\n" +
                              "다음 날 아침, 모든 방송국이 폐쇄된 미술관 지하 연구소의 참상을 특종으로 보도했고,\n" +
                              "그림 속에 갇혀 있던 진짜 딸 '루시아'와 희생자들의 영혼은 마침내 안식을 찾았습니다."
                            : "By pulling the Main Breaker, you permanently shut down Replica and J.A.N.I.T.O.R.\n\n" +
                              "Your investigative report and photographs exposed Dr. Weissmann's underground lab on national television,\n" +
                              "finally granting peace to Lucia and the trapped souls."
                    );
                }
            }

            _endingSequenceRunning = false;
        }

        private IEnumerator CoSlideDoorOpen(Transform door, Vector3 localOffset)
        {
            if (door == null) yield break;
            Vector3 start = door.localPosition;
            Vector3 target = start + localOffset;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 1.6f;
                door.localPosition = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            door.localPosition = target;
        }

        public static bool WasKeyPressed(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                switch (key)
                {
                    case KeyCode.E: if (Keyboard.current.eKey.wasPressedThisFrame) return true; break;
                    case KeyCode.F: if (Keyboard.current.fKey.wasPressedThisFrame) return true; break;
                    case KeyCode.C: if (Keyboard.current.cKey.wasPressedThisFrame) return true; break;
                    case KeyCode.Escape: if (Keyboard.current.escapeKey.wasPressedThisFrame) return true; break;
                    case KeyCode.Return: if (Keyboard.current.enterKey.wasPressedThisFrame) return true; break;
                    case KeyCode.Space: if (Keyboard.current.spaceKey.wasPressedThisFrame) return true; break;
                    case KeyCode.F1: if (Keyboard.current.f1Key.wasPressedThisFrame) return true; break;
                    case KeyCode.F2: if (Keyboard.current.f2Key.wasPressedThisFrame) return true; break;
                    case KeyCode.F3: if (Keyboard.current.f3Key.wasPressedThisFrame) return true; break;
                    case KeyCode.F4: if (Keyboard.current.f4Key.wasPressedThisFrame) return true; break;
                }
            }
#endif
            try
            {
                return Input.GetKeyDown(key);
            }
            catch
            {
                return false;
            }
        }
    }
}
