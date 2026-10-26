using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace IbArtMuseum.Remake
{
    /// <summary>
    /// RESONANCE (공명) 통합 UI 시스템 (나노바나나 메인 메뉴 [게임 시작 / 설정 / 언어 / 나가기], 설정 모달, 카메라 뷰파인더, 단어 입력창, 기밀 문서 뷰어, 멀티 엔딩 화면)
    /// </summary>
    public class ResonanceUIManager : MonoBehaviour
    {
        [Header("HUD Elements")]
        public GameObject hudRoot;
        public Text objectiveTitleText;
        public Text objectiveBodyText;
        public Text controlsGuideText;
        public GameObject hoverPromptBox;
        public Text hoverPromptText;
        public GameObject toastBox;
        public Text toastText;
        public Image screenFlashImage;

        [Header("Camera Viewfinder HUD")]
        public GameObject cameraViewfinderRoot;
        public Text viewfinderStatusText;

        [Header("Main Menu Screen (Nano Banana Art + 4 Buttons)")]
        public GameObject mainMenuPanel;
        public RawImage mainMenuBackgroundRawImage;
        public Text mainMenuSubHeaderText;
        public Text mainMenuTitleText;
        public Text mainMenuDescText;
        public Button btnStartGame;
        public Text txtStartGame;
        public Button btnOpenSettings;
        public Text txtOpenSettings;
        public Button btnToggleLanguage;
        public Text txtToggleLanguage;
        public Button btnExitGame;
        public Text txtExitGame;

        [Header("Settings Modal (설정 창)")]
        public GameObject settingsPanel;
        public Text settingsTitleText;
        public Text sensLabelText;
        public Text sensValueText;
        public Button btnSensMinus;
        public Button btnSensPlus;
        public Text volLabelText;
        public Text volValueText;
        public Button btnVolMinus;
        public Button btnVolPlus;
        public Text lightLabelText;
        public Text lightValueText;
        public Button btnLightMinus;
        public Button btnLightPlus;
        public Button btnCloseSettings;
        public Text txtCloseSettings;

        [Header("Tutorial / Other Modals")]
        public GameObject tutorialPanel;
        public GameObject dialoguePanel;
        public Text dialogueTitleText;
        public Text dialogueBodyText;
        public Text dialogueButtonText;
        public Button btnCloseDialogue;

        public GameObject documentPanel;
        public Text documentTitleText;
        public Text documentBodyText;
        public Button btnCloseDocument;

        public GameObject wordInputPanel;
        public Text wordInputTitleText;
        public Text wordInputHintText;
        public InputField wordInputField;
        public Text wordFeedbackText;
        public Button btnAutoFillWord;
        public Button btnSubmitWord;
        public Button btnCancelWord;

        public GameObject endingPanel;
        public Text endingBadgeText;
        public Text endingTitleText;
        public Text endingDescriptionText;
        public Button btnRetryLobby;
        public Button btnReturnToTitle;

        private ResonanceJanitorAI _activeJanitorForInput;
        private bool _isKeypadMode = false;
        private Coroutine _toastCoroutine;
        private Coroutine _flashCoroutine;
        private float _lastButtonClickFrameTime = -10f;
        private List<Button> _allManagedButtons = new List<Button>();

        private void Awake()
        {
            EnsureEventSystemInputModule();
            EnsureMainMenuBackgroundLoaded();
            BindAllButtonsRuntime();
        }

        private void EnsureMainMenuBackgroundLoaded()
        {
            if (mainMenuBackgroundRawImage != null && mainMenuBackgroundRawImage.texture == null)
            {
                string path = "Assets/IbArtMuseum/RemakeAssets/Tex_MainMenuBG.jpg";
                if (System.IO.File.Exists(path))
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (tex.LoadImage(bytes))
                    {
                        mainMenuBackgroundRawImage.texture = tex;
                        mainMenuBackgroundRawImage.color = Color.white;
                    }
                }
            }
        }

        private void Start()
        {
            RefreshLanguageTexts();
            RefreshSettingsTexts();
        }

        private void EnsureEventSystemInputModule()
        {
            EventSystem es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                es = esObj.AddComponent<EventSystem>();
            }
#if ENABLE_INPUT_SYSTEM
            if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                StandaloneInputModule oldMod = es.GetComponent<StandaloneInputModule>();
                if (oldMod != null) Destroy(oldMod);
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (es.GetComponent<StandaloneInputModule>() == null)
            {
                es.gameObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }

        private void BindAllButtonsRuntime()
        {
            _allManagedButtons.Clear();

            void Bind(Button btn, UnityEngine.Events.UnityAction action)
            {
                if (btn == null) return;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    _lastButtonClickFrameTime = Time.unscaledTime;
                    action?.Invoke();
                });
                if (!_allManagedButtons.Contains(btn))
                {
                    _allManagedButtons.Add(btn);
                }
            }

            // 메인 메뉴 4대 버튼 (게임 시작 / 설정 / 언어 / 나가기)
            Bind(btnStartGame, OnClickMainMenuStartGame);
            Bind(btnOpenSettings, OnClickOpenSettings);
            Bind(btnToggleLanguage, OnClickToggleLanguage);
            Bind(btnExitGame, OnClickExitGame);

            // 설정 모달 버튼
            Bind(btnSensMinus, () => AdjustSensitivity(-0.02f));
            Bind(btnSensPlus, () => AdjustSensitivity(0.02f));
            Bind(btnVolMinus, () => AdjustVolume(-0.1f));
            Bind(btnVolPlus, () => AdjustVolume(0.1f));
            Bind(btnLightMinus, () => AdjustFlashlight(-0.2f));
            Bind(btnLightPlus, () => AdjustFlashlight(0.2f));
            Bind(btnCloseSettings, OnClickCloseSettings);

            // 인게임 모달 버튼
            Bind(btnCloseDialogue, OnClickCloseDialogueModal);
            Bind(btnCloseDocument, OnClickCloseDocumentModal);
            Bind(btnAutoFillWord, OnClickAutoFillAnswer);
            Bind(btnSubmitWord, OnClickSubmitWordInput);
            Bind(btnCancelWord, OnClickCloseWordInput);
            Bind(btnRetryLobby, OnClickRetryFromEscapeLobby);
            Bind(btnReturnToTitle, OnClickReturnToTitle);
        }

        private void Update()
        {
            // 단어 입력창에서 Enter 키로 즉시 제출
            if (wordInputPanel != null && wordInputPanel.activeInHierarchy)
            {
                if (ResonanceGameManager.WasKeyPressed(KeyCode.Return))
                {
                    OnClickSubmitWordInput();
                    return;
                }
            }

            // 대화창이나 문서창이 열려있을 때 Space/Enter로 닫기
            if (dialoguePanel != null && dialoguePanel.activeInHierarchy)
            {
                if (ResonanceGameManager.WasKeyPressed(KeyCode.Space) || ResonanceGameManager.WasKeyPressed(KeyCode.Return))
                {
                    OnClickCloseDialogueModal();
                    return;
                }
            }
            if (documentPanel != null && documentPanel.activeInHierarchy)
            {
                if (ResonanceGameManager.WasKeyPressed(KeyCode.Space) || ResonanceGameManager.WasKeyPressed(KeyCode.Return))
                {
                    OnClickCloseDocumentModal();
                    return;
                }
            }

            // EventSystem이 어떠한 이유로든 클릭을 놓쳤을 때를 대비한 100% 보장 직접 좌표 클릭 폴백!
            if (WasLeftMouseButtonClickedThisFrame(out Vector2 mousePos))
            {
                // 같은 프레임에 이미 EventSystem을 통해 버튼이 실행되었다면 건너뜀
                if (Time.unscaledTime - _lastButtonClickFrameTime > 0.08f)
                {
                    for (int i = _allManagedButtons.Count - 1; i >= 0; i--)
                    {
                        Button b = _allManagedButtons[i];
                        if (b == null || !b.gameObject.activeInHierarchy || !b.interactable) continue;
                        RectTransform rt = b.GetComponent<RectTransform>();
                        if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
                        {
                            _lastButtonClickFrameTime = Time.unscaledTime;
                            b.onClick.Invoke();
                            break;
                        }
                    }
                }
            }
        }

        private bool WasLeftMouseButtonClickedThisFrame(out Vector2 mousePos)
        {
            mousePos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                mousePos = Mouse.current.position.ReadValue();
                return true;
            }
#endif
            try
            {
                if (Input.GetMouseButtonDown(0))
                {
                    mousePos = Input.mousePosition;
                    return true;
                }
            }
            catch { }
            return false;
        }

        // ==================== 메인 메뉴 4대 버튼 동작 ====================
        public void OnClickMainMenuStartGame()
        {
            if (settingsPanel != null && settingsPanel.activeInHierarchy)
                settingsPanel.SetActive(false);

            if (ResonanceGameManager.Instance != null)
            {
                ResonanceGameManager.Instance.OnClickStartGame();
            }
        }

        public void OnClickOpenSettings()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
                RefreshSettingsTexts();
            }
        }

        public void OnClickCloseSettings()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        public void OnClickToggleLanguage()
        {
            if (ResonanceGameManager.Instance != null)
            {
                ResonanceGameManager.Instance.ToggleLanguage();
            }
        }

        public void OnClickExitGame()
        {
            if (ResonanceGameManager.Instance != null)
            {
                ResonanceGameManager.Instance.QuitGame();
            }
        }

        // ==================== 설정값 조절 동작 ====================
        public void AdjustSensitivity(float delta)
        {
            var gm = ResonanceGameManager.Instance;
            if (gm == null) return;
            gm.settingMouseSensitivity = Mathf.Clamp(gm.settingMouseSensitivity + delta, 0.02f, 0.35f);
            gm.ApplySettingsToPlayer();
            RefreshSettingsTexts();
        }

        public void AdjustVolume(float delta)
        {
            var gm = ResonanceGameManager.Instance;
            if (gm == null) return;
            gm.settingMasterVolume = Mathf.Clamp01(gm.settingMasterVolume + delta);
            gm.ApplySettingsToPlayer();
            RefreshSettingsTexts();
        }

        public void AdjustFlashlight(float delta)
        {
            var gm = ResonanceGameManager.Instance;
            if (gm == null) return;
            gm.settingFlashlightScale = Mathf.Clamp(gm.settingFlashlightScale + delta, 0.4f, 2.0f);
            gm.ApplySettingsToPlayer();
            RefreshSettingsTexts();
        }

        public void RefreshSettingsTexts()
        {
            var gm = ResonanceGameManager.Instance;
            bool isKo = gm == null || gm.IsKorean;

            if (settingsTitleText != null)
                settingsTitleText.text = isKo ? "⚙️  환 경 설 정 (SETTINGS)" : "⚙️  G A M E   S E T T I N G S";
            if (sensLabelText != null)
                sensLabelText.text = isKo ? "마우스 회전 감도 (Mouse Sensitivity)" : "Mouse Sensitivity";
            if (volLabelText != null)
                volLabelText.text = isKo ? "전체 음량 및 배경음 (Master Volume)" : "Master & BGM Volume";
            if (lightLabelText != null)
                lightLabelText.text = isKo ? "손전등 밝기 배율 (Flashlight Power)" : "Flashlight Brightness";
            if (txtCloseSettings != null)
                txtCloseSettings.text = isKo ? "적용 및 닫기 (APPLY & CLOSE)" : "APPLY & CLOSE";

            if (gm != null)
            {
                if (sensValueText != null)
                    sensValueText.text = $"{Mathf.RoundToInt(gm.settingMouseSensitivity * 1000f)}";
                if (volValueText != null)
                    volValueText.text = $"{Mathf.RoundToInt(gm.settingMasterVolume * 100f)} %";
                if (lightValueText != null)
                    lightValueText.text = $"{Mathf.RoundToInt(gm.settingFlashlightScale * 100f)} %";
            }
        }

        public void RefreshLanguageTexts()
        {
            var gm = ResonanceGameManager.Instance;
            bool isKo = gm == null || gm.IsKorean;

            if (mainMenuSubHeaderText != null)
                mainMenuSubHeaderText.text = isKo
                    ? "1989 WEISSMANN ART MUSEUM — INVESTIGATIVE HORROR"
                    : "1989 WEISSMANN ART MUSEUM — INVESTIGATIVE HORROR";

            if (mainMenuTitleText != null)
                mainMenuTitleText.text = isKo
                    ? "R E S O N A N C E\n<size=34>공 명  :  폐 쇄 된  미 술 관 의  기 록</size>"
                    : "R E S O N A N C E\n<size=34>THE CLOSED MUSEUM ARCHIVES</size>";

            if (mainMenuDescText != null)
                mainMenuDescText.text = isKo
                    ? "폐관된 바이스만 미술관에서 벌어지는 기묘한 실종 사건과 이상현상.\n손전등과 취재 카메라를 들고 숨겨진 지하 연구소와 진실을 파헤치십시오."
                    : "Strange disappearances and anomalies haunt the closed Weissmann Art Museum.\nUncover the hidden underground lab with your flashlight and press camera.";

            if (txtStartGame != null)
                txtStartGame.text = isKo ? "▶   게 임 시 작   (START GAME)" : "▶   S T A R T   G A M E";
            if (txtOpenSettings != null)
                txtOpenSettings.text = isKo ? "⚙   설 정   (SETTINGS)" : "⚙   S E T T I N G S";
            if (txtToggleLanguage != null)
                txtToggleLanguage.text = isKo ? "🌐   언 어 : 한국어   (LANGUAGE : KR)" : "🌐   L A N G U A G E : ENGLISH";
            if (txtExitGame != null)
                txtExitGame.text = isKo ? "✖   나 가 기   (EXIT GAME)" : "✖   E X I T   G A M E";

            if (controlsGuideText != null)
                controlsGuideText.text = isKo
                    ? "[WASD] 이동  |  [Shift] 달리기  |  [E] 조사  |  [F] 손전등\n[우클릭 / C] 카메라 조준  |  [좌클릭] 플래시 촬영 📸\n(챕터 이동 — F1: 미술관 / F2: 지하연구소 / F3: 미로 / F4: 탈출로비)"
                    : "[WASD] Move  |  [Shift] Sprint  |  [E] Interact  |  [F] Flashlight\n[RMB / C] Aim Camera  |  [LMB] Flash Photo 📸\n(Quick Jump — F1: Museum / F2: Lab / F3: Maze / F4: Escape Lobby)";

            RefreshSettingsTexts();
        }

        // ==================== HUD 및 모달 제어 ====================
        public void CloseAllModals()
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (tutorialPanel != null) tutorialPanel.SetActive(false);
            if (dialoguePanel != null) dialoguePanel.SetActive(false);
            if (documentPanel != null) documentPanel.SetActive(false);
            if (wordInputPanel != null) wordInputPanel.SetActive(false);
            if (endingPanel != null) endingPanel.SetActive(false);
        }

        public void ShowMainMenu(bool show)
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(show);
            if (hudRoot != null) hudRoot.SetActive(!show);
            if (show)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void ShowTutorialModal(bool show)
        {
            if (tutorialPanel != null) tutorialPanel.SetActive(show);
            if (hudRoot != null) hudRoot.SetActive(!show);
        }

        public void UpdateObjective(string title, string body)
        {
            if (hudRoot != null) hudRoot.SetActive(true);
            if (objectiveTitleText != null) objectiveTitleText.text = title;
            if (objectiveBodyText != null) objectiveBodyText.text = body;
        }

        public void SetHoverPrompt(bool visible, string text)
        {
            if (hoverPromptBox != null) hoverPromptBox.SetActive(visible);
            if (hoverPromptText != null && visible) hoverPromptText.text = text;
        }

        public void SetCameraViewfinderActive(bool active)
        {
            if (cameraViewfinderRoot != null) cameraViewfinderRoot.SetActive(active);
            if (active && viewfinderStatusText != null && ResonanceGameManager.Instance != null)
            {
                var gm = ResonanceGameManager.Instance;
                viewfinderStatusText.text = gm.IsKorean
                    ? $"● REC  [1989 PRESS CAMERA]   |   공간 왜곡 정화 : {gm.purifiedDistortionCount} / {gm.totalDistortionCount}   |   [좌클릭] 플래시 촬영 📸"
                    : $"● REC  [1989 PRESS CAMERA]   |   Distortion Purified : {gm.purifiedDistortionCount} / {gm.totalDistortionCount}   |   [LMB] Flash Photo 📸";
            }
        }

        public void ShowToastMessage(string message, float duration = 3.0f)
        {
            if (toastBox == null || toastText == null) return;
            if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
            _toastCoroutine = StartCoroutine(CoToast(message, duration));
        }

        private IEnumerator CoToast(string msg, float dur)
        {
            toastBox.SetActive(true);
            toastText.text = msg;
            yield return new WaitForSeconds(dur);
            toastBox.SetActive(false);
        }

        public void TriggerScreenFlash(Color flashColor, float duration = 0.4f)
        {
            if (screenFlashImage == null) return;
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(CoScreenFlash(flashColor, duration));
        }

        private IEnumerator CoScreenFlash(Color col, float dur)
        {
            screenFlashImage.gameObject.SetActive(true);
            screenFlashImage.color = col;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                Color c = col;
                c.a = Mathf.Lerp(col.a, 0f, t / dur);
                screenFlashImage.color = c;
                yield return null;
            }
            screenFlashImage.gameObject.SetActive(false);
        }

        public void ShowDialogueModal(string title, string body, string btnLabel = "확인")
        {
            if (dialoguePanel == null) return;
            dialoguePanel.SetActive(true);
            if (dialogueTitleText != null) dialogueTitleText.text = title;
            if (dialogueBodyText != null) dialogueBodyText.text = body;
            if (dialogueButtonText != null) dialogueButtonText.text = btnLabel;
            ResonanceGameManager.Instance?.player?.SetControlEnabled(false);
        }

        public void OnClickCloseDialogueModal()
        {
            if (dialoguePanel != null) dialoguePanel.SetActive(false);
            ResonanceGameManager.Instance?.player?.SetControlEnabled(true);
        }

        public void ShowDocumentModal(string title, string body)
        {
            if (documentPanel == null) return;
            documentPanel.SetActive(true);
            if (documentTitleText != null) documentTitleText.text = title;
            if (documentBodyText != null) documentBodyText.text = body;
            ResonanceGameManager.Instance?.player?.SetControlEnabled(false);
        }

        public void OnClickCloseDocumentModal()
        {
            if (documentPanel != null) documentPanel.SetActive(false);
            ResonanceGameManager.Instance?.player?.SetControlEnabled(true);
        }

        public void OpenWordInputModal(ResonanceJanitorAI janitor)
        {
            _activeJanitorForInput = janitor;
            _isKeypadMode = false;
            if (wordInputPanel == null) return;

            bool isKo = ResonanceGameManager.Instance == null || ResonanceGameManager.Instance.IsKorean;
            wordInputPanel.SetActive(true);
            if (wordInputTitleText != null)
                wordInputTitleText.text = isKo
                    ? $"🤖 J.A.N.I.T.O.R. ({janitor.floorNumber}층 계단 보안 관리인) — 단어 입력"
                    : $"🤖 J.A.N.I.T.O.R. ({janitor.floorNumber}F Stairway Custodian) — Word Verification";
            if (wordInputHintText != null)
                wordInputHintText.text = isKo
                    ? $"\"삐빅— 위층 전시실로 올라가려면 이 층의 그림 속에 숨겨진 단어를 입력하십시오.\"\n💡 단서 힌트 : {janitor.hintDescription}"
                    : $"\"BEEP— Enter the hidden word from this floor's painting to proceed upstairs.\"\n💡 Hint : {janitor.hintDescription}";
            if (wordInputField != null)
            {
                wordInputField.text = "";
                wordInputField.ActivateInputField();
            }
            if (wordFeedbackText != null) wordFeedbackText.text = "";

            ResonanceGameManager.Instance?.player?.SetControlEnabled(false);
        }

        public void OpenElectricalKeypadModal()
        {
            _activeJanitorForInput = null;
            _isKeypadMode = true;
            if (wordInputPanel == null) return;

            bool isKo = ResonanceGameManager.Instance == null || ResonanceGameManager.Instance.IsKorean;
            wordInputPanel.SetActive(true);
            if (wordInputTitleText != null)
                wordInputTitleText.text = isKo
                    ? "⚡ 1층 전기 제어실 보안 키패드 — 암호 입력"
                    : "⚡ 1F Electrical Control Room Keypad — Enter Passcode";
            if (wordInputHintText != null)
                wordInputHintText.text = isKo
                    ? "지하 연구소 기밀 실험 일지(TOP SECRET)에 기록되어 있던 4자리 연도 암호를 입력하세요.\n💡 단서 : 바이스만 박사의 실험 연도 [ 1989 ]"
                    : "Enter the 4-digit year passcode recorded in the Underground Lab's Top Secret Document.\n💡 Hint : [ 1989 ]";
            if (wordInputField != null)
            {
                wordInputField.text = "";
                wordInputField.ActivateInputField();
            }
            if (wordFeedbackText != null) wordFeedbackText.text = "";

            ResonanceGameManager.Instance?.player?.SetControlEnabled(false);
        }

        public void OnClickAutoFillAnswer()
        {
            if (wordInputField == null) return;
            if (_isKeypadMode)
            {
                wordInputField.text = "1989";
            }
            else if (_activeJanitorForInput != null)
            {
                wordInputField.text = _activeJanitorForInput.requiredWord;
            }
        }

        public void OnClickSubmitWordInput()
        {
            if (wordInputField == null) return;
            string input = wordInputField.text.Trim().ToUpperInvariant();
            bool isKo = ResonanceGameManager.Instance == null || ResonanceGameManager.Instance.IsKorean;

            if (_isKeypadMode)
            {
                if (input == "1989" || input.Contains("1989"))
                {
                    wordInputPanel.SetActive(false);
                    ResonanceGameManager.Instance?.player?.SetControlEnabled(true);
                    ResonanceGameManager.Instance?.OnUnlockElectricalRoom();
                }
                else
                {
                    if (wordFeedbackText != null)
                        wordFeedbackText.text = isKo ? "❌ 암호가 일치하지 않습니다! (힌트: 1989)" : "❌ Incorrect passcode! (Hint: 1989)";
                }
                return;
            }

            if (_activeJanitorForInput != null)
            {
                string expected = _activeJanitorForInput.requiredWord.Trim().ToUpperInvariant();
                bool isMatch = (input == expected) ||
                               (expected == "LUCIA" && input == "루시아") ||
                               (expected == "MEMORY" && (input == "기억" || input == "메모리")) ||
                               (expected == "REPLICA" && (input == "레플리카" || input == "인공소녀"));

                if (isMatch)
                {
                    wordInputPanel.SetActive(false);
                    ResonanceGameManager.Instance?.player?.SetControlEnabled(true);
                    _activeJanitorForInput.UnlockAndStepAside();
                    ShowToastMessage(
                        isKo
                            ? $"✅ J.A.N.I.T.O.R. : \"단어 [{expected}] 인증 완료. 위층 통행을 허가합니다.\""
                            : $"✅ J.A.N.I.T.O.R. : \"Word [{expected}] verified. Passage granted.\"",
                        4f
                    );
                }
                else
                {
                    if (wordFeedbackText != null)
                        wordFeedbackText.text = isKo
                            ? "❌ 오답입니다! 그림 앞 붉은 버튼을 조사하거나 [단서 자동 입력] 버튼을 눌러보세요."
                            : "❌ Incorrect! Inspect the Red Button before the painting or click [Auto-Fill Clue].";
                }
            }
        }

        public void OnClickCloseWordInput()
        {
            if (wordInputPanel != null) wordInputPanel.SetActive(false);
            ResonanceGameManager.Instance?.player?.SetControlEnabled(true);
        }

        public void ShowEndingScreen(bool show, bool isHappy, string title, string description)
        {
            if (endingPanel == null) return;
            endingPanel.SetActive(show);
            if (!show) return;

            if (endingBadgeText != null)
            {
                endingBadgeText.text = isHappy ? "★ TRUE / HAPPY ENDING ★" : "💀 BAD ENDING 💀";
                endingBadgeText.color = isHappy ? new Color(0.4f, 0.95f, 0.55f) : new Color(1f, 0.35f, 0.35f);
            }
            if (endingTitleText != null) endingTitleText.text = title;
            if (endingDescriptionText != null) endingDescriptionText.text = description;

            ResonanceGameManager.Instance?.player?.SetControlEnabled(false);
        }

        public void OnClickRetryFromEscapeLobby()
        {
            if (endingPanel != null) endingPanel.SetActive(false);
            ResonanceGameManager.Instance?.JumpToStage(ResonanceStage.EscapeLobby);
        }

        public void OnClickReturnToTitle()
        {
            if (endingPanel != null) endingPanel.SetActive(false);
            ResonanceGameManager.Instance?.EnterMainMenu();
        }
    }
}
