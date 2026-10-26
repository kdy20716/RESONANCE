using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using IbArtMuseum.Remake;

namespace IbArtMuseum.Remake
{
    [InitializeOnLoad]
    public static class ResonanceRemakeSceneBuilder
    {
        private const string WorldRootName = "_RESONANCE_REMAKE_WORLD";
        private const string MainMenuVersionMarker = "RESONANCE_MainMenu_NanoBanana_V3";
        private const string RemakeScenePath = "Assets/Scenes/remake.unity";

        static ResonanceRemakeSceneBuilder()
        {
            EditorPrefs.SetInt("kAutoRefresh", 1);
            EditorPrefs.SetInt("kAutoRefreshMode", 1);
            EditorApplication.delayCall += AutoBuildIfRemakeSceneEmpty;
            EditorSceneManager.sceneOpened += (scene, mode) => AutoBuildIfRemakeSceneEmpty();
        }

        private static void AutoBuildIfRemakeSceneEmpty()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoBuildIfRemakeSceneEmpty;
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name == "remake")
            {
                if (GameObject.Find(WorldRootName) == null || GameObject.Find(MainMenuVersionMarker) == null || GameObject.Find("Geometry") != null || GameObject.Find("Ground") != null)
                {
                    Debug.Log("[RESONANCE Remake] 'remake' 씬의 라이팅/노출/난간/천창 분위기 업그레이드(V3)를 자동 구축합니다...");
                    BuildCompleteRemakeScene();
                }
            }
        }

        [MenuItem("Tools/Ib Museum/🎬 Build RESONANCE Remake in 'remake' Scene (신규 기획 전체 원클릭 구축)", false, 0)]
        public static void BuildCompleteRemakeScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.name != "remake")
            {
                if (File.Exists(RemakeScenePath))
                {
                    activeScene = EditorSceneManager.OpenScene(RemakeScenePath, OpenSceneMode.Single);
                }
            }

            // 기존 루트 정리
            GameObject oldRoot = GameObject.Find(WorldRootName);
            if (oldRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRoot);
            }

            // 씬 템플릿의 핑크색 깨진 기본 바닥(Geometry, Ground) 및 야외 노출(EV100=12)로 실내를 암흑으로 만들던 Volume Profile / Lighting 완전 제거
            string[] templateNamesToDestroy = new string[]
            {
                "Geometry", "Ground", "UnityMaterialBall_Gold", "Stand_Logo", "Ball", "Rings", "Back_Part_1",
                "Volume Profile", "Lighting", "Sky and Fog Volume"
            };
            foreach (string name in templateNamesToDestroy)
            {
                GameObject obj = GameObject.Find(name);
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            if (activeScene.IsValid())
            {
                foreach (GameObject rootGo in activeScene.GetRootGameObjects())
                {
                    if (rootGo != null && (rootGo.name == "Geometry" || rootGo.name == "Ground" || rootGo.name.Contains("MaterialBall") || rootGo.name == "Volume Profile" || rootGo.name == "Lighting" || rootGo.name == "Sky and Fog Volume"))
                    {
                        UnityEngine.Object.DestroyImmediate(rootGo);
                    }
                }
            }

            // 기본 씬의 불필요한 기본 카메라/라이트 정리
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.gameObject.name == "Main Camera" || cam.gameObject.name == "Camera")
                {
                    UnityEngine.Object.DestroyImmediate(cam.gameObject);
                }
            }

            EnsureTexturesImported();
            Dictionary<string, Material> mats = CreateOrLoadRemakeMaterials();

            GameObject worldRoot = new GameObject(WorldRootName);
            ResonanceGameManager gm = worldRoot.AddComponent<ResonanceGameManager>();

            // 0. HDRP 실내 미술관/연구소 분위기 라이팅 & 노출(EV100=6.2) 볼륨 구축 (참고 이미지 분위기 완벽 재현!)
            SetupAtmosphericLightingAndVolume(worldRoot.transform);

            // 1. 플레이어 (기자 - 손전등 + 카메라 장착) 생성
            ResonanceJournalistController player = BuildJournalistPlayer(worldRoot.transform, mats);
            gm.player = player;

            // 2. ZONE 1 : PART 1 폐쇄된 바이스만 미술관 (1F 체크무늬 로비 ~ 4F 추락 엘리베이터)
            BuildZone1_AbandonedMuseum(worldRoot.transform, gm, mats);

            // 3. ZONE 2 : PART 2 지하 비밀 연구소 (B4F 연구실 ~ 기괴한 눈알 왜곡 복도)
            BuildZone2_UndergroundLabAndDistortion(worldRoot.transform, gm, mats);

            // 4. ZONE 3 : PART 3 지하 1층 타일 미로 & 1층 탈출 로비 / 전기 제어실
            BuildZone3_B1FMazeAndEscapeLobby(worldRoot.transform, gm, mats);

            // 5. ZONE 4 : 멀티 엔딩 3D 에필로그 세트 (배드 엔딩 현관문 / 해피 엔딩 거실 TV)
            BuildZone4_EndingEpilogueSets(worldRoot.transform, gm, mats);

            // 6. 전체 UI Canvas (메인 메뉴, 튜토리얼, 카메라 뷰파인더 HUD, 단어 입력창, 기밀 일지 뷰어, 멀티 엔딩 화면)
            ResonanceUIManager ui = BuildCompleteRemakeUI(worldRoot.transform, gm);
            gm.ui = ui;

            // 플레이어 초기 위치를 1층 로비 시작 지점으로 설정
            if (gm.part1StartPoint != null)
            {
                player.transform.position = gm.part1StartPoint.position;
                player.transform.rotation = gm.part1StartPoint.rotation;
            }

            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            AssetDatabase.SaveAssets();

            Debug.Log("✅ [RESONANCE Remake] 'remake.unity' 씬에 참고 컨셉아트 분위기 라이팅(천창 달빛 + 갤러리 스포트라이트 + 발코니 기둥 난간) 및 전체 월드 구축이 완료되었습니다!");
        }

        private static void SetupAtmosphericLightingAndVolume(Transform worldRoot)
        {
            // 1. 실내 전용 HDRP Global Volume 생성 및 에셋 저장 (Fixed Exposure = 6.2f)
            GameObject volGo = new GameObject("RESONANCE_Atmosphere_GlobalVolume");
            volGo.transform.SetParent(worldRoot);
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            vol.weight = 1f;

            string profilePath = "Assets/IbArtMuseum/RemakeAssets/ResonanceIndoorVolumeProfile.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile != null)
            {
                AssetDatabase.DeleteAsset(profilePath);
            }
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "ResonanceIndoorVolumeProfile";

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

            AssetDatabase.CreateAsset(profile, profilePath);
            EditorUtility.SetDirty(profile);
            vol.sharedProfile = profile;

            // 2. 다각도 그림자 없는 건축 간접광(Ambient Fill Rig) 구성
            // 외벽/천장에 막히지 않고 미술관 전 층과 지하 연구소에 은은한 청회색 달빛/천창 앰비언트와 바닥 반사광을 부여합니다.
            GameObject rig = new GameObject("Resonance_AmbientFill_Rig");
            rig.transform.SetParent(worldRoot);

            CreateDirectionalFillLight(rig.transform, "FillDir_SkylightCool", new Vector3(58f, -35f, 0f), new Color(0.78f, 0.86f, 0.98f), 95f);
            CreateDirectionalFillLight(rig.transform, "FillDir_WarmBounce", new Vector3(42f, 145f, 0f), new Color(0.96f, 0.89f, 0.78f), 75f);
            CreateDirectionalFillLight(rig.transform, "FillDir_UpwardFloorBounce", new Vector3(-45f, 60f, 0f), new Color(0.80f, 0.84f, 0.90f), 55f);
            CreateDirectionalFillLight(rig.transform, "FillDir_SideCrossFill", new Vector3(25f, -120f, 0f), new Color(0.84f, 0.88f, 0.95f), 55f);
        }

        private static void CreateDirectionalFillLight(Transform parent, string name, Vector3 euler, Color color, float luxIntensity)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.rotation = Quaternion.Euler(euler);

            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.shadows = LightShadows.None;

            HDAdditionalLightData hd = go.AddComponent<HDAdditionalLightData>();
            l.intensity = luxIntensity;
            hd.intensity = luxIntensity;
        }

        private static void EnsureTexturesImported()
        {
            string[] texDirs = new[]
            {
                "Assets/IbArtMuseum/RemakeAssets",
                "Assets/IbArtMuseum/Artworks_1F"
            };

            foreach (string dir in texDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir))
                {
                    if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        string assetPath = file.Replace("\\", "/");
                        TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                        if (ti != null && ti.wrapMode != TextureWrapMode.Repeat)
                        {
                            ti.wrapMode = TextureWrapMode.Repeat;
                            ti.SaveAndReimport();
                        }
                    }
                }
            }
        }

        private static Dictionary<string, Material> CreateOrLoadRemakeMaterials()
        {
            string matFolder = "Assets/IbArtMuseum/RemakeAssets/Materials";
            if (!Directory.Exists(matFolder))
            {
                Directory.CreateDirectory(matFolder);
            }

            Shader litShader = Shader.Find("HDRP/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            Dictionary<string, Material> dict = new Dictionary<string, Material>();

            Material MakeMat(string name, Color col, float smoothness = 0.35f, float metallic = 0f, string texPath = null, Vector2? tiling = null, Color? emission = null)
            {
                string path = $"{matFolder}/{name}.mat";
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(litShader);
                    AssetDatabase.CreateAsset(m, path);
                }
                m.shader = litShader;
                m.color = col;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);

                if (!string.IsNullOrEmpty(texPath))
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    if (tex != null)
                    {
                        m.mainTexture = tex;
                        if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", tex);
                        if (tiling.HasValue)
                        {
                            m.mainTextureScale = tiling.Value;
                            if (m.HasProperty("_BaseColorMap")) m.SetTextureScale("_BaseColorMap", tiling.Value);
                        }
                    }
                }

                if (emission.HasValue)
                {
                    if (m.HasProperty("_EmissiveColor"))
                    {
                        m.SetColor("_EmissiveColor", emission.Value);
                        m.EnableKeyword("_EMISSION");
                    }
                }
                else
                {
                    if (m.HasProperty("_EmissiveColor"))
                    {
                        m.SetColor("_EmissiveColor", Color.black);
                        m.DisableKeyword("_EMISSION");
                    }
                }

                EditorUtility.SetDirty(m);
                dict[name] = m;
                return m;
            }

            // 참고 컨셉아트(#6, #7)의 세련된 청회색 미술관 벽면 + 따뜻한 원목 몰딩/난간 + 밝은 대리석 톤 재현
            MakeMat("Mat_CheckeredMarble", Color.white, 0.82f, 0.05f, "Assets/IbArtMuseum/RemakeAssets/Tex_CheckeredMarble.png", new Vector2(4.5f, 4.5f));
            MakeMat("Mat_GalleryWallDark", new Color(0.46f, 0.50f, 0.56f), 0.32f, 0f);
            MakeMat("Mat_GalleryUpperWall", new Color(0.54f, 0.58f, 0.64f), 0.30f, 0f);
            MakeMat("Mat_WainscotPanel", new Color(0.28f, 0.26f, 0.25f), 0.45f, 0.05f);
            MakeMat("Mat_WoodTrim", new Color(0.42f, 0.28f, 0.18f), 0.68f, 0.05f);
            MakeMat("Mat_RedCarpet", new Color(0.62f, 0.12f, 0.14f), 0.20f, 0f);
            MakeMat("Mat_GoldFrame", new Color(0.85f, 0.68f, 0.28f), 0.82f, 0.85f);
            MakeMat("Mat_RedButton", new Color(0.98f, 0.15f, 0.15f), 0.85f, 0.1f, null, null, new Color(55f, 6f, 6f));
            MakeMat("Mat_PorcelainJanitor", new Color(0.94f, 0.92f, 0.88f), 0.72f, 0.15f);
            MakeMat("Mat_DarkJoint", new Color(0.22f, 0.23f, 0.26f), 0.5f, 0.6f);
            MakeMat("Mat_ReplicaDress", new Color(0.26f, 0.34f, 0.52f), 0.35f, 0f);
            MakeMat("Mat_MonsterBlack", new Color(0.06f, 0.05f, 0.08f), 0.85f, 0.2f, null, null, new Color(8f, 1f, 2f));
            MakeMat("Mat_WeatheredElevator", new Color(0.24f, 0.42f, 0.32f), 0.55f, 0.40f);
            MakeMat("Mat_DirtyLabTile", Color.white, 0.60f, 0.05f, "Assets/IbArtMuseum/RemakeAssets/Tex_DirtyLabTile.png", new Vector2(4f, 4f));
            MakeMat("Mat_DirtyMazeTile", new Color(0.95f, 0.97f, 0.95f), 0.65f, 0.05f, "Assets/IbArtMuseum/RemakeAssets/Tex_DirtyLabTile.png", new Vector2(6f, 2.5f));
            MakeMat("Mat_BloodStain", new Color(0.48f, 0.05f, 0.05f), 0.85f, 0f);
            MakeMat("Mat_BioVatGlass", new Color(0.30f, 0.92f, 0.62f), 0.9f, 0.1f, null, null, new Color(18f, 75f, 38f));
            MakeMat("Mat_SteelLocker", new Color(0.46f, 0.50f, 0.54f), 0.65f, 0.70f);
            MakeMat("Mat_ExitSignRed", new Color(1f, 0.2f, 0.2f), 0.5f, 0f, null, null, new Color(95f, 10f, 10f));
            MakeMat("Mat_GreenLED", new Color(0.2f, 1f, 0.35f), 0.5f, 0f, null, null, new Color(10f, 85f, 20f));
            MakeMat("Mat_SkylightGlass", new Color(0.82f, 0.92f, 1.0f), 0.9f, 0f, null, null, new Color(58f, 74f, 98f));
            MakeMat("Mat_WarmCanLight", new Color(1.0f, 0.96f, 0.86f), 0.8f, 0f, null, null, new Color(95f, 82f, 58f));

            // 커스텀 텍스처 머티리얼 (은은한 자체 발광을 더해 어두운 구석에서도 선명하게 시인성 확보)
            MakeMat("Mat_CreepyEyeball", Color.white, 0.88f, 0f, "Assets/IbArtMuseum/RemakeAssets/Tex_CreepyEyeball.png", Vector2.one, new Color(18f, 6f, 6f));
            MakeMat("Mat_TopSecretDoc", Color.white, 0.25f, 0f, "Assets/IbArtMuseum/RemakeAssets/Tex_TopSecretDoc.png", Vector2.one, new Color(14f, 13f, 11f));
            MakeMat("Mat_ClosedSign", Color.white, 0.3f, 0f, "Assets/IbArtMuseum/RemakeAssets/Tex_ClosedSign.png", Vector2.one, new Color(12f, 11f, 10f));
            MakeMat("Mat_WeissmannPortrait", Color.white, 0.35f, 0f, "Assets/IbArtMuseum/RemakeAssets/Tex_WeissmannPortrait.png", Vector2.one, new Color(12f, 11f, 10f));
            MakeMat("Mat_LuciaPortrait", Color.white, 0.4f, 0f, "Assets/IbArtMuseum/RemakeAssets/Tex_LuciaPortrait.png", Vector2.one, new Color(16f, 28f, 45f));
            MakeMat("Mat_TVNewsScreen", Color.white, 0.8f, 0f, "Assets/IbArtMuseum/RemakeAssets/Tex_TVNewsScreen.png", Vector2.one, new Color(36f, 44f, 55f));

            // 기존 1층 명화 텍스처 머티리얼 (미술관 스포트라이트 아래 선명하게 빛나도록 설정)
            string[] artFiles = new[]
            {
                "WomanOfAbyss_1F",
                "TheWatcher_1F",
                "SmilingMary_1F",
                "Hallway_1_1F",
                "Hallway_2_1F",
                "Hallway_3_1F",
                "Hallway_4_1F"
            };
            foreach (string art in artFiles)
            {
                MakeMat($"Mat_Art_{art}", Color.white, 0.35f, 0f, $"Assets/IbArtMuseum/Artworks_1F/{art}.jpg", Vector2.one, new Color(11f, 10.5f, 10f));
            }

            return dict;
        }

        // ========================================================================================
        // 1. 주인공 기자 (Journalist Player) + 손전등 + 취재 카메라 생성
        // ========================================================================================
        private static ResonanceJournalistController BuildJournalistPlayer(Transform parent, Dictionary<string, Material> mats)
        {
            GameObject playerObj = new GameObject("Player_Journalist");
            playerObj.transform.SetParent(parent);
            playerObj.transform.position = new Vector3(0f, 0.1f, -6.5f);
            playerObj.tag = "Player";

            CharacterController cc = playerObj.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.45f;
            cc.slopeLimit = 50f;

            ResonanceJournalistController ctrl = playerObj.AddComponent<ResonanceJournalistController>();

            // 메인 카메라
            GameObject camObj = new GameObject("JournalistCamera");
            camObj.tag = "MainCamera";
            camObj.transform.SetParent(playerObj.transform);
            camObj.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            camObj.transform.localRotation = Quaternion.identity;

            Camera cam = camObj.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;
            cam.fieldOfView = 65f;
            camObj.AddComponent<AudioListener>();
            if (camObj.GetComponent<HDAdditionalCameraData>() == null)
            {
                camObj.AddComponent<HDAdditionalCameraData>();
            }
            ctrl.playerCamera = camObj.transform;

            // BGM 오디오 소스 (기존 RESONANCE.mp3 연결)
            AudioSource bgm = playerObj.AddComponent<AudioSource>();
            AudioClip bgmClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/musics/RESONANCE.mp3");
            if (bgmClip != null)
            {
                bgm.clip = bgmClip;
                bgm.loop = true;
                bgm.playOnAwake = true;
                bgm.volume = 0.08f;
                bgm.spatialBlend = 0f;
            }

            // 좌하단 손전등 (Flashlight [F])
            GameObject flashHolder = CreatePrimitive(PrimitiveType.Cylinder, "Handheld_Flashlight", camObj.transform,
                new Vector3(-0.28f, -0.24f, 0.42f), new Vector3(0.045f, 0.14f, 0.045f), mats["Mat_DarkJoint"], false);
            flashHolder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ctrl.flashlightModel = flashHolder;

            GameObject flashLightObj = new GameObject("Flashlight_SpotLight");
            flashLightObj.transform.SetParent(camObj.transform);
            flashLightObj.transform.localPosition = new Vector3(-0.24f, -0.18f, 0.52f);
            flashLightObj.transform.localRotation = Quaternion.identity;
            Light spot = CreateConfiguredLight(flashLightObj, LightType.Spot, new Color(1f, 0.96f, 0.86f), 1800f, 28f, 66f);
            ctrl.flashlightSpot = spot;

            // 우하단 1980년대 취재 카메라 모델 + 플래시 라이트
            GameObject camBody = CreatePrimitive(PrimitiveType.Cube, "Handheld_PressCamera", camObj.transform,
                new Vector3(0.28f, -0.23f, 0.40f), new Vector3(0.14f, 0.10f, 0.09f), mats["Mat_DarkJoint"], false);
            CreatePrimitive(PrimitiveType.Cylinder, "CameraLens", camBody.transform,
                new Vector3(0f, 0f, 0.6f), new Vector3(0.45f, 0.4f, 0.45f), mats["Mat_SteelLocker"], false)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ctrl.cameraModel = camBody;

            GameObject camFlashObj = new GameObject("CameraFlash_BurstLight");
            camFlashObj.transform.SetParent(camObj.transform);
            camFlashObj.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            Light camFlash = CreateConfiguredLight(camFlashObj, LightType.Spot, new Color(0.9f, 0.97f, 1f), 8500f, 24f, 95f);
            camFlash.enabled = false;
            ctrl.cameraFlashLight = camFlash;

            return ctrl;
        }

        // ========================================================================================
        // 2. ZONE 1 : PART 1 폐쇄된 바이스만 미술관 (1F 체크무늬 로비 ~ 4F 엘리베이터 홀)
        // ========================================================================================
        private static void BuildZone1_AbandonedMuseum(Transform worldRoot, ResonanceGameManager gm, Dictionary<string, Material> mats)
        {
            GameObject zone1 = new GameObject("Zone1_Part1_AbandonedMuseum");
            zone1.transform.SetParent(worldRoot);
            zone1.transform.position = Vector3.zero;

            // 시작 스폰 포인트
            GameObject startPt = new GameObject("Part1_StartPoint");
            startPt.transform.SetParent(zone1.transform);
            startPt.transform.position = new Vector3(0f, 0.15f, -6.8f);
            startPt.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            gm.part1StartPoint = startPt.transform;

            float width = 18f;
            float depth = 18f;
            float floorHeight = 4.5f;
            float totalHeight = floorHeight * 4f; // 1F, 2F, 3F, 4F (총 18m 높이)

            // 1층 체크무늬 대리석 바닥 (Concept Art #6 완벽 재현!)
            CreatePrimitive(PrimitiveType.Cube, "1F_CheckeredMarbleFloor", zone1.transform,
                new Vector3(0f, -0.1f, 0f), new Vector3(width, 0.2f, depth), mats["Mat_CheckeredMarble"], true);

            // 외벽 4면 (전체 4개 층 높이 - 세련된 청회색 슬레이트 벽면)
            CreatePrimitive(PrimitiveType.Cube, "Wall_North", zone1.transform,
                new Vector3(0f, totalHeight * 0.5f, depth * 0.5f), new Vector3(width, totalHeight, 0.4f), mats["Mat_GalleryWallDark"], true);
            CreatePrimitive(PrimitiveType.Cube, "Wall_South", zone1.transform,
                new Vector3(0f, totalHeight * 0.5f, -depth * 0.5f), new Vector3(width, totalHeight, 0.4f), mats["Mat_GalleryWallDark"], true);
            CreatePrimitive(PrimitiveType.Cube, "Wall_West", zone1.transform,
                new Vector3(-width * 0.5f, totalHeight * 0.5f, 0f), new Vector3(0.4f, totalHeight, depth), mats["Mat_GalleryWallDark"], true);
            CreatePrimitive(PrimitiveType.Cube, "Wall_East", zone1.transform,
                new Vector3(width * 0.5f, totalHeight * 0.5f, 0f), new Vector3(0.4f, totalHeight, depth), mats["Mat_GalleryWallDark"], true);

            // 각 층(1F~4F) 벽면 하단 클래식 웨인스코팅(Wainscot) 패널 및 골드/우드 몰딩 장식 (Concept Art #7 완벽 재현!)
            for (int f = 0; f < 4; f++)
            {
                float fy = f * floorHeight;
                CreatePrimitive(PrimitiveType.Cube, $"Wainscot_North_{f + 1}F", zone1.transform,
                    new Vector3(0f, fy + 0.55f, depth * 0.5f - 0.22f), new Vector3(width - 0.4f, 1.1f, 0.06f), mats["Mat_WainscotPanel"], false);
                CreatePrimitive(PrimitiveType.Cube, $"Wainscot_South_{f + 1}F", zone1.transform,
                    new Vector3(0f, fy + 0.55f, -depth * 0.5f + 0.22f), new Vector3(width - 0.4f, 1.1f, 0.06f), mats["Mat_WainscotPanel"], false);
                CreatePrimitive(PrimitiveType.Cube, $"Wainscot_West_{f + 1}F", zone1.transform,
                    new Vector3(-width * 0.5f + 0.22f, fy + 0.55f, 0f), new Vector3(0.06f, 1.1f, depth - 0.4f), mats["Mat_WainscotPanel"], false);
                CreatePrimitive(PrimitiveType.Cube, $"Wainscot_East_{f + 1}F", zone1.transform,
                    new Vector3(width * 0.5f - 0.22f, fy + 0.55f, 0f), new Vector3(0.06f, 1.1f, depth - 0.4f), mats["Mat_WainscotPanel"], false);
            }

            // 최상층 천장 및 대형 격자형 유리 채광창 (Recessed Gridded Skylight - Concept Art #7 상단 채광창 완벽 재현!)
            BuildRecessedGriddedSkylightRoof(zone1.transform, gm, width, depth, totalHeight, mats);

            // 1층 로비 4방향 천장 갤러리 조명 & 중앙 아트리움 조명 배치
            Vector3[] lobbyLightPositions = new[]
            {
                new Vector3(0f, 4.1f, 0f),
                new Vector3(-5.8f, 4.1f, -5.8f),
                new Vector3(5.8f, 4.1f, -5.8f),
                new Vector3(-5.8f, 4.1f, 5.8f),
                new Vector3(5.8f, 4.1f, 5.8f)
            };
            foreach (var lp in lobbyLightPositions)
            {
                CreatePrimitive(PrimitiveType.Cylinder, "1F_CeilingCanFixture", zone1.transform,
                    lp + new Vector3(0f, 0.22f, 0f), new Vector3(0.55f, 0.05f, 0.55f), mats["Mat_WarmCanLight"], false);
                GameObject lObj = new GameObject("1F_LobbyGalleryLight");
                lObj.transform.SetParent(zone1.transform);
                lObj.transform.localPosition = lp;
                gm.facilityLights.Add(CreateConfiguredLight(lObj, LightType.Point, new Color(1f, 0.93f, 0.80f), 950f, 18f));
            }

            // 1층 남쪽 폐쇄된 정문 (판자로 막힌 문 + EXHIBITION CLOSED 표지판)
            BuildBoardedEntrance1F(zone1.transform, new Vector3(0f, 0f, -depth * 0.5f + 0.25f), mats);

            // 1층 북쪽 중앙 대계단 + 바이스만 박사 초상화 (Concept Art #6 재현)
            CreateFramedArtwork(zone1.transform, "Portrait_DrWeissmann",
                new Vector3(0f, 2.85f, depth * 0.5f - 0.28f), Quaternion.Euler(0f, 180f, 0f),
                new Vector2(2.4f, 3.0f), mats["Mat_WeissmannPortrait"], mats["Mat_GoldFrame"],
                "설립자 칼 바이스만 박사 (Dr. Carl Weissmann, 1928~1989)",
                "『예술은 영혼의 파동을 담는 그릇이며, 기억은 결코 죽지 않는다.』\n— 바이스만 미술관 설립자이자 신경과학자 칼 바이스만 박사의 초상화다. 눈빛이 소름 끼치도록 집요하다.",
                mats["Mat_WarmCanLight"]);

            // 1층 계단 옆 친절한 가이드 상태의 인공소녀 '레플리카(Replica)' 배치
            BuildFriendlyReplicaNPC(zone1.transform, new Vector3(-2.6f, 0f, 3.8f), Quaternion.Euler(0f, 155f, 0f), mats);

            // 2층, 3층, 4층 중정 발코니 바닥 (중앙이 뻥 뚫린 중정 보이드 구조 + 기둥형 발코니 난간 - Concept Art #7 재현!)
            BuildUpperGalleryFloorRing(zone1.transform, 2, floorHeight * 1f, width, depth, mats, gm);
            BuildUpperGalleryFloorRing(zone1.transform, 3, floorHeight * 2f, width, depth, mats, gm);
            BuildUpperGalleryFloorRing(zone1.transform, 4, floorHeight * 3f, width, depth, mats, gm);

            // 층간 경사 계단 (1F->2F, 2F->3F, 3F->4F)
            BuildStaircaseRamp(zone1.transform, "Stairs_1F_to_2F", new Vector3(5.8f, 0f, -3.5f), new Vector3(5.8f, floorHeight, 3.5f), 2.2f, mats);
            BuildStaircaseRamp(zone1.transform, "Stairs_2F_to_3F", new Vector3(-5.8f, floorHeight, 3.5f), new Vector3(-5.8f, floorHeight * 2f, -3.5f), 2.2f, mats);
            BuildStaircaseRamp(zone1.transform, "Stairs_3F_to_4F", new Vector3(5.8f, floorHeight * 2f, -3.5f), new Vector3(5.8f, floorHeight * 3f, 3.5f), 2.2f, mats);

            // ================= 1층, 2층, 3층 전시 그림 & 붉은 버튼 스탠드 & J.A.N.I.T.O.R. 문지기 =================
            // [1층] 단서 단어: LUCIA
            CreateGalleryPaintingWithRedPedestal(
                zone1.transform, 1,
                new Vector3(-width * 0.5f + 0.28f, 2.2f, -1.5f), Quaternion.Euler(0f, 90f, 0f),
                mats["Mat_Art_WomanOfAbyss_1F"], mats,
                "1층 특별 전시작 : 『심연 속의 소녀와 잃어버린 이름』",
                "붉은 버튼을 누르자 액자 뒤편의 오르골 장치가 돌아가며 바이스만 박사의 음성 기록이 흘러나온다.\n\n" +
                "\"내 사랑하는 진짜 딸의 이름은... 【 LUCIA (루시아) 】... 그 아이의 미소를 되찾기 위해서라면 나는 신의 영역이라도 침범할 것이다.\"",
                "LUCIA"
            );
            CreateFramedArtwork(zone1.transform, "1F_SubArt_Watcher",
                new Vector3(-width * 0.5f + 0.28f, 2.2f, 3.2f), Quaternion.Euler(0f, 90f, 0f),
                new Vector2(2.0f, 1.5f), mats["Mat_Art_TheWatcher_1F"], mats["Mat_GoldFrame"],
                "1층 전시작 : 『응시하는 눈』",
                "캔버스 한가운데의 거대한 눈동자가 마치 살아있는 것처럼 관람객의 움직임을 따라온다.",
                mats["Mat_WarmCanLight"]);

            // 1층 계단 문지기 J.A.N.I.T.O.R.
            BuildJanitorRobot(zone1.transform, gm, mats,
                new Vector3(5.8f, 0f, -4.2f), Quaternion.Euler(0f, 180f, 0f),
                false, 1, "LUCIA",
                "1층 서쪽 벽의 그림 앞 [붉은 버튼 스탠드]를 조사해 바이스만 박사의 진짜 딸 이름(영문 5글자: LUCIA)을 확인하세요.",
                new Vector3(-2.3f, 0f, 0f));

            // [2층] 단서 단어: MEMORY
            CreateGalleryPaintingWithRedPedestal(
                zone1.transform, 2,
                new Vector3(0f, floorHeight + 2.2f, -depth * 0.5f + 0.28f), Quaternion.Euler(0f, 0f, 0f),
                mats["Mat_Art_SmilingMary_1F"], mats,
                "2층 특별 전시작 : 『이식된 기억의 초상』",
                "붉은 버튼을 누르자 그림 속 소녀의 눈가에 붉은 눈물이 맺히며 숨겨진 문장이 떠오른다.\n\n" +
                "\"인간을 인간답게 만드는 것은 육체가 아닌 【 MEMORY (기억) 】이다. 나는 관람객들의 기억 파동을 추출해 캔버스에 가두었다.\"",
                "MEMORY"
            );
            CreateFramedArtwork(zone1.transform, "2F_SubArt_Hallway1",
                new Vector3(width * 0.5f - 0.28f, floorHeight + 2.2f, 0f), Quaternion.Euler(0f, -90f, 0f),
                new Vector2(2.1f, 1.5f), mats["Mat_Art_Hallway_1_1F"], mats["Mat_GoldFrame"],
                "2층 전시작 : 『푸른 복도의 잔향』",
                "미술관 지하 어딘가에 존재하는 비밀 연구 통로를 그린 듯한 음산한 풍경화다.",
                mats["Mat_WarmCanLight"]);

            // 2층 계단 문지기 J.A.N.I.T.O.R.
            BuildJanitorRobot(zone1.transform, gm, mats,
                new Vector3(-5.8f, floorHeight, 4.2f), Quaternion.Euler(0f, 0f, 0f),
                false, 2, "MEMORY",
                "2층 남쪽 발코니 벽의 그림 앞 [붉은 버튼 스탠드]를 조사해 숨겨진 단어(영문 6글자: MEMORY)를 확인하세요.",
                new Vector3(2.3f, 0f, 0f));

            // [3층] 단서 단어: REPLICA
            CreateGalleryPaintingWithRedPedestal(
                zone1.transform, 3,
                new Vector3(-width * 0.5f + 0.28f, floorHeight * 2f + 2.2f, 0f), Quaternion.Euler(0f, 90f, 0f),
                mats["Mat_Art_Hallway_2_1F"], mats,
                "3층 특별 전시작 : 『만들어진 인형, 레플리카』",
                "붉은 버튼을 누르자 액자 프레임에서 차가운 기계음이 울려 퍼진다.\n\n" +
                "\"루시아의 기억을 주입받아 탄생한 완전무결한 인공소녀... 프로젝트 코드명 【 REPLICA (레플리카) 】. 하지만 그 인형은 자신이 진짜 인간이라 믿고 있다.\"",
                "REPLICA"
            );
            CreateFramedArtwork(zone1.transform, "3F_SubArt_Hallway3",
                new Vector3(0f, floorHeight * 2f + 2.2f, depth * 0.5f - 0.28f), Quaternion.Euler(0f, 180f, 0f),
                new Vector2(2.1f, 1.5f), mats["Mat_Art_Hallway_3_1F"], mats["Mat_GoldFrame"],
                "3층 전시작 : 『공명하는 잔향』",
                "기억을 잃은 관람객들의 그림자가 캔버스 너머에서 일렁이고 있다.",
                mats["Mat_WarmCanLight"]);

            // 3층 계단 문지기 J.A.N.I.T.O.R.
            BuildJanitorRobot(zone1.transform, gm, mats,
                new Vector3(5.8f, floorHeight * 2f, -4.2f), Quaternion.Euler(0f, 180f, 0f),
                false, 3, "REPLICA",
                "3층 서쪽 발코니 벽의 그림 앞 [붉은 버튼 스탠드]를 조사해 인공소녀의 코드명(영문 7글자: REPLICA)을 확인하세요.",
                new Vector3(-2.3f, 0f, 0f));

            // [4층 최상층] 녹슨 녹색 엘리베이터 (Concept Art #7 우측 이미지 완벽 재현!)
            BuildTopFloorElevator(zone1.transform, gm, new Vector3(0f, floorHeight * 3f, depth * 0.5f - 1.2f), mats);
        }

        private static void BuildRecessedGriddedSkylightRoof(Transform parent, ResonanceGameManager gm, float width, float depth, float totalHeight, Dictionary<string, Material> mats)
        {
            GameObject roofRoot = new GameObject("Roof_And_GriddedSkylight");
            roofRoot.transform.SetParent(parent);

            float skySize = 9.0f; // 중앙 9m x 9m 대형 채광창 웰
            float border = (width - skySize) * 0.5f; // 4.5m

            // 외곽 천장 슬래브 4면
            CreatePrimitive(PrimitiveType.Cube, "Ceiling_North", roofRoot.transform,
                new Vector3(0f, totalHeight + 0.1f, depth * 0.5f - border * 0.5f), new Vector3(width, 0.25f, border), mats["Mat_GalleryUpperWall"], true);
            CreatePrimitive(PrimitiveType.Cube, "Ceiling_South", roofRoot.transform,
                new Vector3(0f, totalHeight + 0.1f, -depth * 0.5f + border * 0.5f), new Vector3(width, 0.25f, border), mats["Mat_GalleryUpperWall"], true);
            CreatePrimitive(PrimitiveType.Cube, "Ceiling_West", roofRoot.transform,
                new Vector3(-width * 0.5f + border * 0.5f, totalHeight + 0.1f, 0f), new Vector3(border, 0.25f, skySize), mats["Mat_GalleryUpperWall"], true);
            CreatePrimitive(PrimitiveType.Cube, "Ceiling_East", roofRoot.transform,
                new Vector3(width * 0.5f - border * 0.5f, totalHeight + 0.1f, 0f), new Vector3(border, 0.25f, skySize), mats["Mat_GalleryUpperWall"], true);

            // 채광창 상단 발광 유리 패널 (Concept Art #7 상단의 푸른 밤하늘 채광창 완벽 재현!)
            CreatePrimitive(PrimitiveType.Cube, "Skylight_GlowingGlassPane", roofRoot.transform,
                new Vector3(0f, totalHeight + 0.55f, 0f), new Vector3(skySize + 0.4f, 0.1f, skySize + 0.4f), mats["Mat_SkylightGlass"], true);

            // 채광창 우물 벽(Well Frame) 및 격자 프레임 바(Muntin Grid Bars)
            CreatePrimitive(PrimitiveType.Cube, "SkylightWell_N", roofRoot.transform, new Vector3(0f, totalHeight + 0.3f, skySize * 0.5f), new Vector3(skySize, 0.5f, 0.2f), mats["Mat_WainscotPanel"], false);
            CreatePrimitive(PrimitiveType.Cube, "SkylightWell_S", roofRoot.transform, new Vector3(0f, totalHeight + 0.3f, -skySize * 0.5f), new Vector3(skySize, 0.5f, 0.2f), mats["Mat_WainscotPanel"], false);
            CreatePrimitive(PrimitiveType.Cube, "SkylightWell_W", roofRoot.transform, new Vector3(-skySize * 0.5f, totalHeight + 0.3f, 0f), new Vector3(0.2f, 0.5f, skySize), mats["Mat_WainscotPanel"], false);
            CreatePrimitive(PrimitiveType.Cube, "SkylightWell_E", roofRoot.transform, new Vector3(skySize * 0.5f, totalHeight + 0.3f, 0f), new Vector3(0.2f, 0.5f, skySize), mats["Mat_WainscotPanel"], false);

            for (int i = -2; i <= 2; i++)
            {
                float offset = i * (skySize / 5f);
                CreatePrimitive(PrimitiveType.Cube, $"SkylightGridBar_X_{i}", roofRoot.transform,
                    new Vector3(offset, totalHeight + 0.42f, 0f), new Vector3(0.14f, 0.12f, skySize), mats["Mat_DarkJoint"], false);
                CreatePrimitive(PrimitiveType.Cube, $"SkylightGridBar_Z_{i}", roofRoot.transform,
                    new Vector3(0f, totalHeight + 0.42f, offset), new Vector3(skySize, 0.12f, 0.14f), mats["Mat_DarkJoint"], false);
            }

            // 천창에서 중정(Atrium) 전체로 쏟아져 내리는 푸른 달빛 스포트라이트 & 포인트라이트
            GameObject centerSkySpot = new GameObject("Skylight_AtriumMoonBeam_Center");
            centerSkySpot.transform.SetParent(roofRoot.transform);
            centerSkySpot.transform.position = new Vector3(0f, totalHeight - 0.3f, 0f);
            centerSkySpot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            gm.facilityLights.Add(CreateConfiguredLight(centerSkySpot, LightType.Spot, new Color(0.76f, 0.88f, 1.0f), 2200f, 28f, 105f));

            GameObject skyPointTop = new GameObject("Skylight_AtriumAmbientTop");
            skyPointTop.transform.SetParent(roofRoot.transform);
            skyPointTop.transform.position = new Vector3(0f, totalHeight - 1.2f, 0f);
            gm.facilityLights.Add(CreateConfiguredLight(skyPointTop, LightType.Point, new Color(0.80f, 0.90f, 1.0f), 1400f, 24f));
        }

        private static void BuildBoardedEntrance1F(Transform parent, Vector3 pos, Dictionary<string, Material> mats)
        {
            GameObject root = new GameObject("BoardedEntrance_1F");
            root.transform.SetParent(parent);
            root.transform.position = pos;

            // 정문 문틀
            CreatePrimitive(PrimitiveType.Cube, "DoorLeft", root.transform, new Vector3(-0.85f, 1.6f, 0f), new Vector3(1.5f, 3.2f, 0.15f), mats["Mat_WoodTrim"], true);
            CreatePrimitive(PrimitiveType.Cube, "DoorRight", root.transform, new Vector3(0.85f, 1.6f, 0f), new Vector3(1.5f, 3.2f, 0.15f), mats["Mat_WoodTrim"], true);

            // 가로막은 나무 판자들
            CreatePrimitive(PrimitiveType.Cube, "Plank1", root.transform, new Vector3(0f, 1.2f, 0.12f), new Vector3(3.2f, 0.28f, 0.08f), mats["Mat_WoodTrim"], false)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            CreatePrimitive(PrimitiveType.Cube, "Plank2", root.transform, new Vector3(0f, 2.0f, 0.12f), new Vector3(3.2f, 0.28f, 0.08f), mats["Mat_WoodTrim"], false)
                .transform.localRotation = Quaternion.Euler(0f, 0f, -6f);

            // EXHIBITION CLOSED 표지판
            GameObject sign = CreatePrimitive(PrimitiveType.Cube, "ClosedSign", root.transform, new Vector3(0f, 2.65f, 0.15f), new Vector3(1.5f, 0.9f, 0.04f), mats["Mat_ClosedSign"], true);
            ResonanceInteractable inter = sign.AddComponent<ResonanceInteractable>();
            inter.interactType = ResonanceInteractType.ArtworkFrame;
            inter.promptText = "[E] 폐관 안내문 읽기 (EXHIBITION CLOSED)";
            inter.titleText = "바이스만 미술관 무기한 폐관 공고";
            inter.bodyText = "『EXHIBITION CLOSED — KEEP OUT』\n관람객 연쇄 실종 사건으로 인해 본 미술관은 무기한 폐쇄되었습니다.\n정문은 단단한 판자로 못 박혀 있어 지금은 나갈 수 없습니다. 먼저 위층 전시실을 취재하세요.";
        }

        private static void BuildFriendlyReplicaNPC(Transform parent, Vector3 pos, Quaternion rot, Dictionary<string, Material> mats)
        {
            GameObject npc = new GameObject("NPC_Replica_FriendlyGuide");
            npc.transform.SetParent(parent);
            npc.transform.position = pos;
            npc.transform.rotation = rot;

            // 소녀 실루엣 모델링 (드레스 + 구체 머리 + 창백한 피부)
            CreatePrimitive(PrimitiveType.Cylinder, "DressSkirt", npc.transform, new Vector3(0f, 0.65f, 0f), new Vector3(0.55f, 0.65f, 0.55f), mats["Mat_ReplicaDress"], true);
            CreatePrimitive(PrimitiveType.Capsule, "Torso", npc.transform, new Vector3(0f, 1.25f, 0f), new Vector3(0.36f, 0.35f, 0.28f), mats["Mat_ReplicaDress"], true);
            CreatePrimitive(PrimitiveType.Sphere, "Head", npc.transform, new Vector3(0f, 1.68f, 0f), new Vector3(0.28f, 0.32f, 0.28f), mats["Mat_PorcelainJanitor"], true);
            CreatePrimitive(PrimitiveType.Sphere, "HairBack", npc.transform, new Vector3(0f, 1.62f, -0.06f), new Vector3(0.31f, 0.38f, 0.30f), mats["Mat_WoodTrim"], false);

            BoxCollider col = npc.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.0f, 0f);
            col.size = new Vector3(0.8f, 2.0f, 0.8f);

            ResonanceInteractable inter = npc.AddComponent<ResonanceInteractable>();
            inter.interactType = ResonanceInteractType.FriendlyReplicaNPC;
            inter.promptText = "[E] 소녀(레플리카)와 대화하기";
            inter.titleText = "미술관의 소녀 (레플리카)";
            inter.bodyText = "\"안녕하세요, 기자님... 이 폐쇄된 미술관에 찾아오신 분은 정말 오랜만이에요.\n" +
                             "저는 이 미술관의 설립자이신 바이스만 박사님의 딸이랍니다.\n\n" +
                             "계단을 지키고 있는 관리 로봇 [J.A.N.I.T.O.R.]들은 각 층의 그림 앞 [붉은 버튼 스탠드]를 눌러\n" +
                             "숨겨진 단어를 알려주면 길을 비켜줄 거예요. 꼭대기 층까지 올라가 아버지의 흔적을 찾아주세요!\"";
        }

        private static void BuildUpperGalleryFloorRing(Transform parent, int floorNum, float y, float width, float depth, Dictionary<string, Material> mats, ResonanceGameManager gm)
        {
            GameObject floorRoot = new GameObject($"Floor_{floorNum}F_GalleryRing");
            floorRoot.transform.SetParent(parent);
            floorRoot.transform.localPosition = new Vector3(0f, y, 0f);

            // 중앙 8.8m x 8.8m 중정 보이드(Void)를 남겨두고 사방 4.6m 폭의 발코니 복도 형성
            float walkwaysWidth = 4.6f;
            // 북쪽/남쪽 복도
            CreatePrimitive(PrimitiveType.Cube, $"{floorNum}F_Floor_North", floorRoot.transform,
                new Vector3(0f, -0.1f, depth * 0.5f - walkwaysWidth * 0.5f), new Vector3(width, 0.2f, walkwaysWidth), mats["Mat_WoodTrim"], true);
            CreatePrimitive(PrimitiveType.Cube, $"{floorNum}F_Floor_South", floorRoot.transform,
                new Vector3(0f, -0.1f, -depth * 0.5f + walkwaysWidth * 0.5f), new Vector3(width, 0.2f, walkwaysWidth), mats["Mat_WoodTrim"], true);
            // 서쪽/동쪽 복도
            CreatePrimitive(PrimitiveType.Cube, $"{floorNum}F_Floor_West", floorRoot.transform,
                new Vector3(-width * 0.5f + walkwaysWidth * 0.5f, -0.1f, 0f), new Vector3(walkwaysWidth, 0.2f, depth - walkwaysWidth * 2f), mats["Mat_WoodTrim"], true);
            CreatePrimitive(PrimitiveType.Cube, $"{floorNum}F_Floor_East", floorRoot.transform,
                new Vector3(width * 0.5f - walkwaysWidth * 0.5f, -0.1f, 0f), new Vector3(walkwaysWidth, 0.2f, depth - walkwaysWidth * 2f), mats["Mat_WoodTrim"], true);

            // ★ 중정 오픈형 기둥 발코니 난간 (Open Balustrade Railings with Spindles - Concept Art #7 완벽 재현!)
            float voidHalf = (width - walkwaysWidth * 2f) * 0.5f; // 4.4m
            BuildBalustradeSide(floorRoot.transform, $"{floorNum}F_Balustrade_North", new Vector3(-voidHalf, 0f, voidHalf), new Vector3(voidHalf, 0f, voidHalf), mats["Mat_WoodTrim"]);
            BuildBalustradeSide(floorRoot.transform, $"{floorNum}F_Balustrade_South", new Vector3(-voidHalf, 0f, -voidHalf), new Vector3(voidHalf, 0f, -voidHalf), mats["Mat_WoodTrim"]);
            BuildBalustradeSide(floorRoot.transform, $"{floorNum}F_Balustrade_West", new Vector3(-voidHalf, 0f, -voidHalf), new Vector3(-voidHalf, 0f, voidHalf), mats["Mat_WoodTrim"]);
            BuildBalustradeSide(floorRoot.transform, $"{floorNum}F_Balustrade_East", new Vector3(voidHalf, 0f, -voidHalf), new Vector3(voidHalf, 0f, voidHalf), mats["Mat_WoodTrim"]);

            // 각 층 중앙 아트리움 라이트 + 4면 갤러리 천장 스포트/포인트 조명 배치 (어두운 사각지대 제로!)
            GameObject centerLightObj = new GameObject($"{floorNum}F_AtriumCenterLight");
            centerLightObj.transform.SetParent(floorRoot.transform);
            centerLightObj.transform.localPosition = new Vector3(0f, 3.8f, 0f);
            gm.facilityLights.Add(CreateConfiguredLight(centerLightObj, LightType.Point, new Color(0.92f, 0.94f, 1.0f), 900f, 18f));

            Vector3[] walkwayLightOffsets = new[]
            {
                new Vector3(0f, 4.1f, 6.7f),
                new Vector3(0f, 4.1f, -6.7f),
                new Vector3(-6.7f, 4.1f, 0f),
                new Vector3(6.7f, 4.1f, 0f)
            };
            foreach (var wPos in walkwayLightOffsets)
            {
                CreatePrimitive(PrimitiveType.Cylinder, $"{floorNum}F_CanLightFixture", floorRoot.transform,
                    wPos + new Vector3(0f, 0.22f, 0f), new Vector3(0.48f, 0.05f, 0.48f), mats["Mat_WarmCanLight"], false);
                GameObject wl = new GameObject($"{floorNum}F_WalkwayLight");
                wl.transform.SetParent(floorRoot.transform);
                wl.transform.localPosition = wPos;
                gm.facilityLights.Add(CreateConfiguredLight(wl, LightType.Point, new Color(1f, 0.92f, 0.78f), 850f, 15f));
            }
        }

        private static void BuildBalustradeSide(Transform parent, string name, Vector3 startLocal, Vector3 endLocal, Material woodMat)
        {
            GameObject railRoot = new GameObject(name);
            railRoot.transform.SetParent(parent);
            Vector3 mid = (startLocal + endLocal) * 0.5f;
            float len = Vector3.Distance(startLocal, endLocal);
            railRoot.transform.localPosition = mid;
            railRoot.transform.localRotation = Quaternion.LookRotation((endLocal - startLocal).normalized, Vector3.up);

            // 상단 핸드레일 & 하단 받침대
            CreatePrimitive(PrimitiveType.Cube, "TopHandrail", railRoot.transform,
                new Vector3(0f, 1.05f, 0f), new Vector3(0.16f, 0.10f, len + 0.2f), woodMat, false);
            CreatePrimitive(PrimitiveType.Cube, "BottomPlinth", railRoot.transform,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.18f, 0.12f, len + 0.2f), woodMat, false);

            // 양끝 굵은 코너 기둥 (Newel Posts)
            CreatePrimitive(PrimitiveType.Cube, "NewelPost_Start", railRoot.transform,
                new Vector3(0f, 0.58f, -len * 0.5f), new Vector3(0.24f, 1.16f, 0.24f), woodMat, false);
            CreatePrimitive(PrimitiveType.Cube, "NewelPost_End", railRoot.transform,
                new Vector3(0f, 0.58f, len * 0.5f), new Vector3(0.24f, 1.16f, 0.24f), woodMat, false);

            // 세로 난간 동자석 기둥들 (Vertical Balusters - 사이사이로 중정 아래층이 훤히 내려다보임!)
            int count = Mathf.Max(2, Mathf.FloorToInt(len / 0.52f));
            for (int i = 1; i < count; i++)
            {
                float z = -len * 0.5f + (len * i / count);
                CreatePrimitive(PrimitiveType.Cube, $"Baluster_{i}", railRoot.transform,
                    new Vector3(0f, 0.56f, z), new Vector3(0.07f, 0.92f, 0.07f), woodMat, false);
            }

            // 플레이어 추락 방지용 투명 콜라이더
            BoxCollider safetyCol = railRoot.AddComponent<BoxCollider>();
            safetyCol.center = new Vector3(0f, 0.58f, 0f);
            safetyCol.size = new Vector3(0.18f, 1.16f, len);
        }

        private static void BuildStaircaseRamp(Transform parent, string name, Vector3 bottomPos, Vector3 topPos, float width, Dictionary<string, Material> mats)
        {
            GameObject stairRoot = new GameObject(name);
            stairRoot.transform.SetParent(parent);

            Vector3 mid = (bottomPos + topPos) * 0.5f;
            float length = Vector3.Distance(bottomPos, topPos);
            stairRoot.transform.position = mid;
            stairRoot.transform.LookAt(topPos);

            // 부드럽게 걸어 올라갈 수 있는 경사 콜라이더 + 붉은 카펫 러너
            CreatePrimitive(PrimitiveType.Cube, "RampCollider", stairRoot.transform,
                new Vector3(0f, -0.08f, 0f), new Vector3(width, 0.2f, length + 0.4f), mats["Mat_WoodTrim"], true);
            CreatePrimitive(PrimitiveType.Cube, "RedCarpetRunner", stairRoot.transform,
                new Vector3(0f, 0.03f, 0f), new Vector3(width * 0.65f, 0.04f, length + 0.2f), mats["Mat_RedCarpet"], false);
        }

        private static void CreateFramedArtwork(Transform parent, string name, Vector3 pos, Quaternion rot, Vector2 size, Material artMat, Material frameMat, string title, string desc, Material canLightMat = null)
        {
            GameObject artRoot = new GameObject(name);
            artRoot.transform.SetParent(parent);
            artRoot.transform.position = pos;
            artRoot.transform.rotation = rot;

            CreatePrimitive(PrimitiveType.Cube, "Frame", artRoot.transform,
                Vector3.zero, new Vector3(size.x + 0.18f, size.y + 0.18f, 0.08f), frameMat, true);
            GameObject canvas = CreatePrimitive(PrimitiveType.Cube, "Canvas", artRoot.transform,
                new Vector3(0f, 0f, 0.03f), new Vector3(size.x, size.y, 0.05f), artMat, true);

            // 액자 상단 갤러리 천장 스포트라이트 기구 & 따뜻한 원추형 조명 (Concept Art #7 완벽 재현!)
            if (canLightMat != null)
            {
                CreatePrimitive(PrimitiveType.Cylinder, "ArtCanFixture", artRoot.transform,
                    new Vector3(0f, size.y * 0.5f + 1.15f, 1.35f), new Vector3(0.36f, 0.05f, 0.36f), canLightMat, false);
            }
            GameObject spotObj = new GameObject($"{name}_GallerySpotLight");
            spotObj.transform.SetParent(artRoot.transform);
            spotObj.transform.localPosition = new Vector3(0f, size.y * 0.5f + 1.05f, 1.45f);
            spotObj.transform.LookAt(artRoot.transform.position - new Vector3(0f, 0.25f, 0f));
            CreateConfiguredLight(spotObj, LightType.Spot, new Color(1f, 0.93f, 0.78f), 1350f, 12f, 72f);

            ResonanceInteractable inter = canvas.AddComponent<ResonanceInteractable>();
            inter.interactType = ResonanceInteractType.ArtworkFrame;
            inter.promptText = $"[E] 작품 감상 : {title}";
            inter.titleText = title;
            inter.bodyText = desc;
        }

        private static void CreateGalleryPaintingWithRedPedestal(
            Transform parent, int floor, Vector3 artPos, Quaternion artRot,
            Material artMat, Dictionary<string, Material> mats,
            string title, string clueStory, string hiddenWord)
        {
            // 액자 생성 (상단 스포트라이트 및 천장 조명 기구 포함)
            CreateFramedArtwork(parent, $"Painting_{floor}F_MainClue", artPos, artRot,
                new Vector2(2.3f, 1.65f), artMat, mats["Mat_GoldFrame"], title,
                "작품 아래의 [붉은 버튼 스탠드]를 누르면 작품 속에 숨겨진 기억과 단어를 조사할 수 있습니다.",
                mats["Mat_WarmCanLight"]);

            // 그림 앞 붉은 버튼 스탠드 (Concept Art #7 좌측의 붉은 버튼 스탠드 완벽 재현!)
            Vector3 pedPos = new Vector3(artPos.x, (floor - 1) * 4.5f, artPos.z) + (artRot * Vector3.forward) * 1.35f;
            GameObject pedRoot = new GameObject($"RedButtonPedestal_{floor}F");
            pedRoot.transform.SetParent(parent);
            pedRoot.transform.position = pedPos;
            pedRoot.transform.rotation = artRot;

            CreatePrimitive(PrimitiveType.Cube, "PedestalBase", pedRoot.transform,
                new Vector3(0f, 0.5f, 0f), new Vector3(0.34f, 1.0f, 0.34f), mats["Mat_PorcelainJanitor"], true);
            CreatePrimitive(PrimitiveType.Cube, "PedestalTopPlate", pedRoot.transform,
                new Vector3(0f, 1.01f, 0f), new Vector3(0.44f, 0.06f, 0.44f), mats["Mat_PorcelainJanitor"], false);
            GameObject redBtn = CreatePrimitive(PrimitiveType.Cylinder, "RedButton", pedRoot.transform,
                new Vector3(0f, 1.07f, 0f), new Vector3(0.22f, 0.08f, 0.22f), mats["Mat_RedButton"], true);

            // 붉은 버튼 자체 발광 포인트 라이트
            GameObject pedGlow = new GameObject($"PedestalRedGlow_{floor}F");
            pedGlow.transform.SetParent(pedRoot.transform);
            pedGlow.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            CreateConfiguredLight(pedGlow, LightType.Point, new Color(1f, 0.3f, 0.25f), 180f, 3.8f);

            BoxCollider pedCol = pedRoot.AddComponent<BoxCollider>();
            pedCol.center = new Vector3(0f, 0.65f, 0f);
            pedCol.size = new Vector3(0.65f, 1.35f, 0.65f);

            ResonanceInteractable inter = pedRoot.AddComponent<ResonanceInteractable>();
            inter.interactType = ResonanceInteractType.PaintingPedestalButton;
            inter.floorNumber = floor;
            inter.clueWord = hiddenWord;
            inter.promptText = $"🔴 [E] {floor}층 전시작 붉은 버튼 누르기 (숨겨진 단어 조사)";
            inter.titleText = title;
            inter.bodyText = clueStory;
        }

        private static ResonanceJanitorAI BuildJanitorRobot(
            Transform parent, ResonanceGameManager gm, Dictionary<string, Material> mats,
            Vector3 pos, Quaternion rot, bool isChaser, int floorNum, string word, string hint, Vector3 stepOffset)
        {
            GameObject janitorObj = new GameObject(isChaser ? "JANITOR_MazeChaser_B1F" : $"JANITOR_Gatekeeper_{floorNum}F");
            janitorObj.transform.SetParent(parent);
            janitorObj.transform.position = pos;
            janitorObj.transform.rotation = rot;

            // 기존 Armature.fbx 메시가 있으면 로드하고, 그 위에 구체 관절 마네킹 디테일 추가
            GameObject armaturePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/SampleSceneAssets/Controllers/ThirdPersonController/Character/Models/Armature.fbx");
            if (armaturePrefab != null)
            {
                GameObject modelInst = (GameObject)PrefabUtility.InstantiatePrefab(armaturePrefab, janitorObj.transform);
                modelInst.transform.localPosition = Vector3.zero;
                modelInst.transform.localRotation = Quaternion.identity;
                foreach (var r in modelInst.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = mats["Mat_PorcelainJanitor"];
                }
            }
            else
            {
                // 폴백 마네킹 바디
                CreatePrimitive(PrimitiveType.Capsule, "Body", janitorObj.transform, new Vector3(0f, 1.0f, 0f), new Vector3(0.45f, 0.8f, 0.35f), mats["Mat_PorcelainJanitor"], false);
                CreatePrimitive(PrimitiveType.Sphere, "Head", janitorObj.transform, new Vector3(0f, 1.7f, 0f), new Vector3(0.3f, 0.35f, 0.3f), mats["Mat_PorcelainJanitor"], false);
            }

            // 제니터 안구 발광 라이트
            GameObject eyeLightObj = new GameObject("Janitor_EyeLight");
            eyeLightObj.transform.SetParent(janitorObj.transform);
            eyeLightObj.transform.localPosition = new Vector3(0f, 1.68f, 0.25f);
            Light eyeLight = CreateConfiguredLight(eyeLightObj, LightType.Point, isChaser ? Color.red : new Color(1f, 0.6f, 0.15f), 85f, 3.5f);

            BoxCollider col = janitorObj.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.0f, 0f);
            col.size = new Vector3(0.85f, 2.0f, 0.85f);

            ResonanceJanitorAI ai = janitorObj.AddComponent<ResonanceJanitorAI>();
            ai.isChaserInMaze = isChaser;
            ai.floorNumber = floorNum;
            ai.requiredWord = word;
            ai.hintDescription = hint;
            ai.stepAsideOffset = stepOffset;
            ai.eyeLight = eyeLight;
            gm.allJanitors.Add(ai);

            if (!isChaser)
            {
                ResonanceInteractable inter = janitorObj.AddComponent<ResonanceInteractable>();
                inter.interactType = ResonanceInteractType.JanitorGatekeeper;
                inter.linkedJanitor = ai;
                inter.floorNumber = floorNum;
                inter.promptText = $"🤖 [E] {floorNum}층 관리 로봇 J.A.N.I.T.O.R.에게 암호 단어 입력";
            }

            return ai;
        }

        private static void BuildTopFloorElevator(Transform parent, ResonanceGameManager gm, Vector3 pos, Dictionary<string, Material> mats)
        {
            GameObject elevRoot = new GameObject("TopFloor_Elevator_4F");
            elevRoot.transform.SetParent(parent);
            elevRoot.transform.position = pos;

            // 엘리베이터 외벽 프레임 (Concept Art #7 우측의 낡은 녹색 엘리베이터)
            CreatePrimitive(PrimitiveType.Cube, "ElevWallLeft", elevRoot.transform, new Vector3(-1.4f, 1.8f, 0f), new Vector3(1.0f, 3.6f, 2.2f), mats["Mat_WeatheredElevator"], true);
            CreatePrimitive(PrimitiveType.Cube, "ElevWallRight", elevRoot.transform, new Vector3(1.4f, 1.8f, 0f), new Vector3(1.0f, 3.6f, 2.2f), mats["Mat_WeatheredElevator"], true);
            CreatePrimitive(PrimitiveType.Cube, "ElevWallBack", elevRoot.transform, new Vector3(0f, 1.8f, 1.1f), new Vector3(3.8f, 3.6f, 0.2f), mats["Mat_WeatheredElevator"], true);
            CreatePrimitive(PrimitiveType.Cube, "ElevCeiling", elevRoot.transform, new Vector3(0f, 3.5f, 0f), new Vector3(3.8f, 0.2f, 2.2f), mats["Mat_WeatheredElevator"], true);

            // 슬라이딩 도어 좌/우
            GameObject doorL = CreatePrimitive(PrimitiveType.Cube, "ElevDoorLeft", elevRoot.transform, new Vector3(-0.45f, 1.6f, -0.95f), new Vector3(0.9f, 3.2f, 0.12f), mats["Mat_SteelLocker"], true);
            GameObject doorR = CreatePrimitive(PrimitiveType.Cube, "ElevDoorRight", elevRoot.transform, new Vector3(0.45f, 1.6f, -0.95f), new Vector3(0.9f, 3.2f, 0.12f), mats["Mat_SteelLocker"], true);
            gm.elevatorLeftDoor = doorL.transform;
            gm.elevatorRightDoor = doorR.transform;

            // OUT OF SERVICE 표지판
            CreatePrimitive(PrimitiveType.Cube, "OutOfServiceSign", elevRoot.transform, new Vector3(-1.35f, 2.1f, -1.12f), new Vector3(0.65f, 0.45f, 0.04f), mats["Mat_ClosedSign"], false);

            // 엘리베이터 내부 라이트 및 탑승 포인트
            GameObject inLightObj = new GameObject("ElevatorInteriorLight");
            inLightObj.transform.SetParent(elevRoot.transform);
            inLightObj.transform.localPosition = new Vector3(0f, 3.1f, 0f);
            gm.elevatorInteriorLight = CreateConfiguredLight(inLightObj, LightType.Point, new Color(1f, 0.92f, 0.7f), 650f, 8f);

            // 엘리베이터 정면 홀 조명 (4층 엘리베이터 외관과 OUT OF SERVICE 표지판이 선명하게 보이도록!)
            GameObject hallLightObj = new GameObject("ElevatorHallFrontLight");
            hallLightObj.transform.SetParent(elevRoot.transform);
            hallLightObj.transform.localPosition = new Vector3(0f, 3.3f, -2.2f);
            gm.facilityLights.Add(CreateConfiguredLight(hallLightObj, LightType.Point, new Color(1f, 0.93f, 0.80f), 850f, 12f));

            GameObject insidePt = new GameObject("ElevatorInsidePoint");
            insidePt.transform.SetParent(elevRoot.transform);
            insidePt.transform.localPosition = new Vector3(0f, 0.1f, 0.1f);
            insidePt.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            gm.elevatorInsidePoint = insidePt.transform;

            // 엘리베이터 호출 버튼 (PRESS [E] TO CALL ELEVATOR)
            GameObject callBtn = CreatePrimitive(PrimitiveType.Cube, "ElevatorCallButton", elevRoot.transform,
                new Vector3(1.15f, 1.45f, -1.12f), new Vector3(0.32f, 0.48f, 0.12f), mats["Mat_GreenLED"], true);
            BoxCollider btnCol = callBtn.GetComponent<BoxCollider>();
            btnCol.size = new Vector3(3.5f, 3.5f, 2.5f); // 문 근처 어디를 봐도 편리하게 상호작용 가능하도록 넉넉한 콜라이더

            ResonanceInteractable inter = callBtn.AddComponent<ResonanceInteractable>();
            inter.interactType = ResonanceInteractType.ElevatorCallButton;
            inter.promptText = "🟢 PRESS [E] TO CALL ELEVATOR (엘리베이터 탑승)";
        }

        // ========================================================================================
        // 3. ZONE 2 : PART 2 지하 비밀 연구소 (B4F 연구실 & 기괴한 눈알 왜곡 복도)
        // ========================================================================================
        private static void BuildZone2_UndergroundLabAndDistortion(Transform worldRoot, ResonanceGameManager gm, Dictionary<string, Material> mats)
        {
            Vector3 zoneOrigin = new Vector3(80f, -20f, 0f);
            GameObject zone2 = new GameObject("Zone2_Part2_UndergroundLab");
            zone2.transform.SetParent(worldRoot);
            zone2.transform.position = zoneOrigin;

            // 추락 후 깨어나는 스폰 포인트
            GameObject wakePt = new GameObject("Part2_LabWakePoint");
            wakePt.transform.SetParent(zone2.transform);
            wakePt.transform.localPosition = new Vector3(0f, 0.15f, -5.2f);
            wakePt.transform.localRotation = Quaternion.identity;
            gm.part2LabWakePoint = wakePt.transform;

            // B4F 비밀 연구실 (12m x 14m) — Concept Art #8 좌측 이미지 완벽 재현!
            BuildRoomBox(zone2.transform, "B4F_LabRoom", new Vector3(0f, 0f, 0f), new Vector3(12f, 4.2f, 14f), mats["Mat_DirtyLabTile"], mats["Mat_DirtyLabTile"]);

            // 연구실 천장 산업용 형광등 조명 (책상 위 CRT/해골/일지, 배양 캡슐, 루시아 초상화를 선명하게 비춤!)
            Vector3[] labLightCoords = new[]
            {
                new Vector3(-1.5f, 3.85f, 1.2f),
                new Vector3(2.8f, 3.85f, 2.2f),
                new Vector3(0f, 3.85f, -3.5f),
                new Vector3(0f, 3.85f, 5.2f)
            };
            foreach (var llc in labLightCoords)
            {
                CreatePrimitive(PrimitiveType.Cube, "LabFluorescentTube", zone2.transform, llc + new Vector3(0f, 0.2f, 0f), new Vector3(1.8f, 0.08f, 0.3f), mats["Mat_WarmCanLight"], false);
                GameObject labL = new GameObject("LabOverheadLight");
                labL.transform.SetParent(zone2.transform);
                labL.transform.localPosition = llc;
                gm.facilityLights.Add(CreateConfiguredLight(labL, LightType.Point, new Color(0.92f, 0.96f, 0.90f), 900f, 16f));
            }

            // 연구실 바닥 핏자국 데칼
            CreatePrimitive(PrimitiveType.Cylinder, "BloodStain_Floor1", zone2.transform, new Vector3(-1.2f, 0.02f, -1.5f), new Vector3(2.2f, 0.01f, 1.8f), mats["Mat_BloodStain"], false);
            CreatePrimitive(PrimitiveType.Cylinder, "BloodStain_Floor2", zone2.transform, new Vector3(1.8f, 0.02f, 1.2f), new Vector3(1.6f, 0.01f, 2.4f), mats["Mat_BloodStain"], false);

            // 연구실 중앙 책상 + 1980년대 CRT 모니터 + 해골 모형 + 뇌 표본 병 + 기밀 실험 일지 (TOP SECRET)
            GameObject desk = CreatePrimitive(PrimitiveType.Cube, "ResearchDesk", zone2.transform, new Vector3(-1.5f, 0.45f, 1.5f), new Vector3(3.2f, 0.9f, 1.4f), mats["Mat_WoodTrim"], true);
            // 1980s CRT 모니터
            CreatePrimitive(PrimitiveType.Cube, "CRT_Monitor", zone2.transform, new Vector3(-2.3f, 1.25f, 1.6f), new Vector3(0.65f, 0.58f, 0.55f), mats["Mat_PorcelainJanitor"], true);
            CreatePrimitive(PrimitiveType.Cube, "CRT_Screen", zone2.transform, new Vector3(-2.3f, 1.25f, 1.31f), new Vector3(0.52f, 0.42f, 0.04f), mats["Mat_GreenLED"], false);
            // 해골 모형 & 뇌 표본 병
            CreatePrimitive(PrimitiveType.Sphere, "HumanSkullProp", zone2.transform, new Vector3(-0.4f, 1.08f, 1.5f), new Vector3(0.26f, 0.30f, 0.28f), mats["Mat_PorcelainJanitor"], false);
            CreatePrimitive(PrimitiveType.Cylinder, "BrainSpecimenJar", zone2.transform, new Vector3(-0.85f, 1.12f, 1.7f), new Vector3(0.24f, 0.22f, 0.24f), mats["Mat_BioVatGlass"], false);

            // ★ 바이스만 박사의 기밀 실험 일지 (Tex_TopSecretDoc.png)
            GameObject docObj = CreatePrimitive(PrimitiveType.Cube, "TopSecretDocument_Interactable", zone2.transform,
                new Vector3(-1.4f, 0.93f, 1.25f), new Vector3(0.65f, 0.04f, 0.82f), mats["Mat_TopSecretDoc"], true);
            BoxCollider docCol = docObj.GetComponent<BoxCollider>();
            docCol.size = new Vector3(1.8f, 8.0f, 1.8f);
            ResonanceInteractable docInter = docObj.AddComponent<ResonanceInteractable>();
            docInter.interactType = ResonanceInteractType.TopSecretDocument;
            docInter.promptText = "📄 [E] 바이스만 박사의 기밀 실험 일지 읽기 (TOP SECRET)";

            // 우측 녹색 원통형 배양 캡슐 (Cylindrical Bio-Vat — Concept Art #8 좌측 배경!)
            GameObject vat = CreatePrimitive(PrimitiveType.Cylinder, "BioCultivationVat", zone2.transform,
                new Vector3(3.6f, 1.8f, 2.5f), new Vector3(1.6f, 1.8f, 1.6f), mats["Mat_BioVatGlass"], true);
            CreatePrimitive(PrimitiveType.Cylinder, "VatBase", zone2.transform, new Vector3(3.6f, 0.2f, 2.5f), new Vector3(1.8f, 0.2f, 1.8f), mats["Mat_SteelLocker"], true);
            CreatePrimitive(PrimitiveType.Cylinder, "VatCap", zone2.transform, new Vector3(3.6f, 3.6f, 2.5f), new Vector3(1.8f, 0.2f, 1.8f), mats["Mat_SteelLocker"], true);
            GameObject vatLightObj = new GameObject("BioVat_GreenLight");
            vatLightObj.transform.SetParent(zone2.transform);
            vatLightObj.transform.localPosition = new Vector3(3.6f, 2.0f, 2.5f);
            CreateConfiguredLight(vatLightObj, LightType.Point, new Color(0.2f, 1f, 0.55f), 950f, 12f);

            // 기밀 일지를 읽었을 때 나타나는 적대적 괴물 레플리카 실루엣
            GameObject monsterReplica = BuildMonsterReplicaFigure(zone2.transform, new Vector3(3.6f, 0f, 0.2f), Quaternion.Euler(0f, 200f, 0f), mats);
            monsterReplica.SetActive(false);
            gm.hostileReplicaLabFigure = monsterReplica;

            // ★ 진짜 딸 '루시아(Lucia)'의 영혼 초상화 (카메라 플래시 해금!)
            GameObject luciaPortrait = CreatePrimitive(PrimitiveType.Cube, "LuciaSpiritPortrait_Interactable", zone2.transform,
                new Vector3(1.5f, 2.1f, 6.75f), new Vector3(1.8f, 2.3f, 0.12f), mats["Mat_LuciaPortrait"], true);
            CreatePrimitive(PrimitiveType.Cube, "LuciaFrame", luciaPortrait.transform,
                new Vector3(0f, 0f, 0.2f), new Vector3(1.08f, 1.08f, 0.5f), mats["Mat_GoldFrame"], false);
            BoxCollider luciaCol = luciaPortrait.GetComponent<BoxCollider>();
            luciaCol.size = new Vector3(1.5f, 1.5f, 6.0f);
            ResonanceInteractable luciaInter = luciaPortrait.AddComponent<ResonanceInteractable>();
            luciaInter.interactType = ResonanceInteractType.LuciaSpiritPortrait;
            luciaInter.promptText = "📸 [E] 진짜 딸 '루시아'의 초상화와 공명하기 (카메라 플래시 해금)";

            // 연구실 북쪽 출구 철문 (루시아 초상화 조사 시 개방되어 눈알 복도로 연결!)
            GameObject labDoor = CreatePrimitive(PrimitiveType.Cube, "LabDoor_ToEyeCorridor", zone2.transform,
                new Vector3(-2.5f, 1.6f, 6.9f), new Vector3(2.8f, 3.2f, 0.25f), mats["Mat_SteelLocker"], true);
            gm.labExitDoor = labDoor.transform;
            ResonanceInteractable labDoorInter = labDoor.AddComponent<ResonanceInteractable>();
            labDoorInter.interactType = ResonanceInteractType.LabDoorToCorridor;
            labDoorInter.promptText = "🔒 [잠김] 먼저 책상의 실험 일지와 루시아의 초상화를 조사하세요";

            // ================= B4F ~ B2F 기괴한 눈알 & 원혼 왜곡 복도 (Concept Art #8 우측 이미지 재현!) =================
            // 북쪽으로 36m 뻗은 지하 왜곡 복도
            BuildRoomBox(zone2.transform, "EyeDistortionCorridor", new Vector3(-2.5f, 0f, 25f), new Vector3(6.5f, 4.0f, 36f), mats["Mat_DirtyLabTile"], mats["Mat_GalleryWallDark"]);
            // 연구실과 복도 사이 통로 개방(북쪽 벽 구멍 확보를 위해 연구실 북벽은 문으로 대체)
            Transform northWallLab = zone2.transform.Find("B4F_LabRoom/Wall_North");
            if (northWallLab != null) UnityEngine.Object.DestroyImmediate(northWallLab.gameObject);
            Transform southWallCorr = zone2.transform.Find("EyeDistortionCorridor/Wall_South");
            if (southWallCorr != null) UnityEngine.Object.DestroyImmediate(southWallCorr.gameObject);

            // 연구실 북쪽 벽 빈 공간 메우기 (문 주변)
            CreatePrimitive(PrimitiveType.Cube, "LabNorthWall_Right", zone2.transform, new Vector3(2.4f, 2.1f, 7.0f), new Vector3(7.2f, 4.2f, 0.3f), mats["Mat_DirtyLabTile"], true);

            // 눈알 왜곡 복도 천장 조명 배치 (Concept Art #8 우측 이미지처럼 복도 타일과 벽면 눈알들이 선명하게 보이도록!)
            for (int cz = 10; cz <= 40; cz += 6)
            {
                CreatePrimitive(PrimitiveType.Cube, $"CorridorLightTube_{cz}", zone2.transform,
                    new Vector3(-2.5f, 3.9f, cz), new Vector3(1.2f, 0.06f, 0.25f), mats["Mat_WarmCanLight"], false);
                GameObject corrL = new GameObject($"CorridorCeilingLight_{cz}");
                corrL.transform.SetParent(zone2.transform);
                corrL.transform.localPosition = new Vector3(-2.5f, 3.65f, cz);
                gm.facilityLights.Add(CreateConfiguredLight(corrL, LightType.Point, new Color(0.95f, 0.88f, 0.82f), 650f, 14f));
            }

            // 4개의 기괴한 안구(Eyeball) + 원혼(Resonant Spirit) 왜곡 군집 배치 (카메라 플래시로 촬영해 정화!)
            gm.totalDistortionCount = 4;
            BuildEyeballDistortionCluster(zone2.transform, 1, new Vector3(-2.5f, 0f, 13f), mats);
            BuildEyeballDistortionCluster(zone2.transform, 2, new Vector3(-2.5f, 0f, 20f), mats);
            BuildEyeballDistortionCluster(zone2.transform, 3, new Vector3(-2.5f, 0f, 27f), mats);
            BuildEyeballDistortionCluster(zone2.transform, 4, new Vector3(-2.5f, 0f, 34f), mats);

            // 복도 끝 B1F 미로 진입 보안문
            GameObject corrExitDoor = CreatePrimitive(PrimitiveType.Cube, "CorridorDoor_ToB1FMaze", zone2.transform,
                new Vector3(-2.5f, 1.6f, 42.2f), new Vector3(3.2f, 3.2f, 0.3f), mats["Mat_WeatheredElevator"], true);
            gm.distortionExitDoorToB1F = corrExitDoor.transform;

            // 문 바로 뒤 B1F 미로 이동 포탈 버튼
            GameObject toMazePortal = CreatePrimitive(PrimitiveType.Cube, "StairsPortal_ToB1FMaze", zone2.transform,
                new Vector3(-2.5f, 1.6f, 42.8f), new Vector3(3.6f, 3.2f, 0.5f), mats["Mat_GreenLED"], true);
            ResonanceInteractable portalInter = toMazePortal.AddComponent<ResonanceInteractable>();
            portalInter.interactType = ResonanceInteractType.CorridorDoorToB1FMaze;
            portalInter.promptText = "🚪 [E] 지하 1층 타일 미로(B1F) 계단으로 올라가기";
        }

        private static void BuildEyeballDistortionCluster(Transform parent, int index, Vector3 centerPos, Dictionary<string, Material> mats)
        {
            GameObject cluster = new GameObject($"DistortionCluster_{index}_EyeballsAndSpirit");
            cluster.transform.SetParent(parent);
            cluster.transform.localPosition = centerPos;

            ResonanceAnomalyTarget target = cluster.AddComponent<ResonanceAnomalyTarget>();

            // 좌/우 벽면과 천장에 기괴한 충혈된 눈알 구체 14개 생성 (Concept Art #8 우측 벽면 눈알 완벽 재현!)
            System.Random rng = new System.Random(1989 + index * 77);
            for (int i = 0; i < 14; i++)
            {
                bool leftWall = (i % 2 == 0);
                float x = leftWall ? -2.85f : 2.85f;
                float y = 0.7f + (float)rng.NextDouble() * 2.6f;
                float z = -2.2f + (float)rng.NextDouble() * 4.4f;
                float scale = 0.38f + (float)rng.NextDouble() * 0.48f;

                GameObject eye = CreatePrimitive(PrimitiveType.Sphere, $"CreepyEyeball_{i}", cluster.transform,
                    new Vector3(x, y, z), new Vector3(scale, scale, scale), mats["Mat_CreepyEyeball"], false);
                eye.transform.localRotation = Quaternion.Euler(0f, leftWall ? 90f : -90f, 0f);
                target.eyeballObjects.Add(eye.transform);
            }

            // 중앙에 떠다니는 검은 원혼(잔향체) 실루엣
            GameObject spirit = new GameObject("ResonantSpirit_Silhouette");
            spirit.transform.SetParent(cluster.transform);
            spirit.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            CreatePrimitive(PrimitiveType.Capsule, "SpiritBody", spirit.transform, Vector3.zero, new Vector3(0.65f, 0.95f, 0.4f), mats["Mat_MonsterBlack"], false);
            CreatePrimitive(PrimitiveType.Sphere, "SpiritEyeL", spirit.transform, new Vector3(-0.12f, 0.55f, 0.22f), new Vector3(0.14f, 0.14f, 0.14f), mats["Mat_CreepyEyeball"], false);
            CreatePrimitive(PrimitiveType.Sphere, "SpiritEyeR", spirit.transform, new Vector3(0.12f, 0.55f, 0.22f), new Vector3(0.14f, 0.14f, 0.14f), mats["Mat_CreepyEyeball"], false);
            target.spiritSilhouette = spirit.transform;

            // 기괴한 붉은 왜곡 조명
            GameObject lightObj = new GameObject("DistortionRedGlow");
            lightObj.transform.SetParent(cluster.transform);
            lightObj.transform.localPosition = new Vector3(0f, 2.4f, 0f);
            target.anomalyGlowLight = CreateConfiguredLight(lightObj, LightType.Point, new Color(1f, 0.22f, 0.25f), 750f, 10f);
        }

        private static GameObject BuildMonsterReplicaFigure(Transform parent, Vector3 pos, Quaternion rot, Dictionary<string, Material> mats)
        {
            GameObject monster = new GameObject("MonsterReplica_Figure");
            monster.transform.SetParent(parent);
            monster.transform.localPosition = pos;
            monster.transform.localRotation = rot;

            // 기괴하게 키가 크고 팔이 긴 검은 괴물 형태 (2.4m 장신)
            CreatePrimitive(PrimitiveType.Capsule, "TallTorso", monster.transform, new Vector3(0f, 1.35f, 0f), new Vector3(0.45f, 1.25f, 0.35f), mats["Mat_MonsterBlack"], false);
            CreatePrimitive(PrimitiveType.Sphere, "MonsterHead", monster.transform, new Vector3(0f, 2.35f, 0.08f), new Vector3(0.36f, 0.44f, 0.36f), mats["Mat_MonsterBlack"], false);
            CreatePrimitive(PrimitiveType.Sphere, "GlowingEyeL", monster.transform, new Vector3(-0.08f, 2.38f, 0.25f), new Vector3(0.06f, 0.06f, 0.06f), mats["Mat_RedButton"], false);
            CreatePrimitive(PrimitiveType.Sphere, "GlowingEyeR", monster.transform, new Vector3(0.08f, 2.38f, 0.25f), new Vector3(0.06f, 0.06f, 0.06f), mats["Mat_RedButton"], false);
            CreatePrimitive(PrimitiveType.Cylinder, "LongArmL", monster.transform, new Vector3(-0.38f, 1.15f, 0.15f), new Vector3(0.10f, 0.85f, 0.10f), mats["Mat_MonsterBlack"], false);
            CreatePrimitive(PrimitiveType.Cylinder, "LongArmR", monster.transform, new Vector3(0.38f, 1.15f, 0.15f), new Vector3(0.10f, 0.85f, 0.10f), mats["Mat_MonsterBlack"], false);

            return monster;
        }

        // ========================================================================================
        // 4. ZONE 3 : PART 3 지하 1층 타일 미로 & 1층 탈출 로비 / 전기 제어실 (Concept Art #9 재현!)
        // ========================================================================================
        private static void BuildZone3_B1FMazeAndEscapeLobby(Transform worldRoot, ResonanceGameManager gm, Dictionary<string, Material> mats)
        {
            // ---------------- A. 지하 1층 백색 타일 미로 (B1F Maze, Origin: (160, -10, 0)) ----------------
            Vector3 mazeOrigin = new Vector3(160f, -10f, 0f);
            GameObject mazeRoot = new GameObject("Zone3A_Part3_B1F_TileMaze");
            mazeRoot.transform.SetParent(worldRoot);
            mazeRoot.transform.position = mazeOrigin;

            GameObject mazeStart = new GameObject("Part3_MazeStartPoint");
            mazeStart.transform.SetParent(mazeRoot.transform);
            mazeStart.transform.localPosition = new Vector3(0f, 0.15f, -9.5f);
            mazeStart.transform.localRotation = Quaternion.identity;
            gm.part3MazeStartPoint = mazeStart.transform;

            // 미로 외곽 박스 (22m x 22m 백색 타일 방)
            BuildRoomBox(mazeRoot.transform, "B1F_MazeOuterBox", Vector3.zero, new Vector3(22f, 4.0f, 22f), mats["Mat_DirtyMazeTile"], mats["Mat_DirtyMazeTile"]);

            // 미로 내부 격벽 (순환형 미로 구조로 제니터를 피해 도망다니고 우회할 수 있도록 설계!)
            CreatePrimitive(PrimitiveType.Cube, "MazeWall_CenterSouth", mazeRoot.transform, new Vector3(0f, 2.0f, -4.5f), new Vector3(8.5f, 4.0f, 0.6f), mats["Mat_DirtyMazeTile"], true);
            CreatePrimitive(PrimitiveType.Cube, "MazeWall_CenterNorth", mazeRoot.transform, new Vector3(0f, 2.0f, 4.5f), new Vector3(8.5f, 4.0f, 0.6f), mats["Mat_DirtyMazeTile"], true);
            CreatePrimitive(PrimitiveType.Cube, "MazeWall_WestDivider", mazeRoot.transform, new Vector3(-5.5f, 2.0f, 0f), new Vector3(0.6f, 4.0f, 9.6f), mats["Mat_DirtyMazeTile"], true);
            CreatePrimitive(PrimitiveType.Cube, "MazeWall_EastDivider", mazeRoot.transform, new Vector3(5.5f, 2.0f, 0f), new Vector3(0.6f, 4.0f, 9.6f), mats["Mat_DirtyMazeTile"], true);

            // 천장 형광등 조명 & CCTV 카메라 배치
            Vector3[] lightCoords = new[]
            {
                new Vector3(-7.5f, 3.7f, -7.5f),
                new Vector3(7.5f, 3.7f, -7.5f),
                new Vector3(0f, 3.7f, 0f),
                new Vector3(-7.5f, 3.7f, 7.5f),
                new Vector3(7.5f, 3.7f, 7.5f)
            };
            foreach (var lc in lightCoords)
            {
                CreatePrimitive(PrimitiveType.Cube, "FluorescentTube", mazeRoot.transform, lc, new Vector3(1.6f, 0.08f, 0.25f), mats["Mat_WarmCanLight"], false);
                GameObject lObj = new GameObject("MazeFluorescentLight");
                lObj.transform.SetParent(mazeRoot.transform);
                lObj.transform.localPosition = lc - new Vector3(0f, 0.25f, 0f);
                gm.facilityLights.Add(CreateConfiguredLight(lObj, LightType.Point, new Color(0.90f, 0.96f, 0.94f), 850f, 16f));
            }

            // 은신 가능한 철제 캐비닛(사물함) 4곳 배치 ([E] 캐비닛에 숨기)
            BuildHidingLocker(mazeRoot.transform, "HidingLocker_SW", new Vector3(-9.8f, 0f, -5.0f), Quaternion.Euler(0f, 90f, 0f), mats);
            BuildHidingLocker(mazeRoot.transform, "HidingLocker_SE", new Vector3(9.8f, 0f, -5.0f), Quaternion.Euler(0f, -90f, 0f), mats);
            BuildHidingLocker(mazeRoot.transform, "HidingLocker_NW", new Vector3(-9.8f, 0f, 5.0f), Quaternion.Euler(0f, 90f, 0f), mats);
            BuildHidingLocker(mazeRoot.transform, "HidingLocker_NE", new Vector3(9.8f, 0f, 5.0f), Quaternion.Euler(0f, -90f, 0f), mats);

            // 미로 중앙 보안 책상 + ★ [1층 로비 출구 열쇠 (Exit Key)]
            CreatePrimitive(PrimitiveType.Cube, "MazeCenterDesk", mazeRoot.transform, new Vector3(0f, 0.45f, 0f), new Vector3(2.2f, 0.9f, 1.2f), mats["Mat_WoodTrim"], true);
            GameObject keyObj = CreatePrimitive(PrimitiveType.Cylinder, "MazeExitKey_Interactable", mazeRoot.transform,
                new Vector3(0f, 1.05f, 0f), new Vector3(0.42f, 0.08f, 0.42f), mats["Mat_GoldFrame"], true);
            GameObject keyGlow = new GameObject("ExitKeyGlow");
            keyGlow.transform.SetParent(keyObj.transform);
            keyGlow.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            CreateConfiguredLight(keyGlow, LightType.Point, new Color(1f, 0.85f, 0.2f), 450f, 6.5f);
            BoxCollider keyCol = keyObj.GetComponent<BoxCollider>();
            if (keyCol == null) keyCol = keyObj.AddComponent<BoxCollider>();
            keyCol.size = new Vector3(2.8f, 10f, 2.8f);

            ResonanceInteractable keyInter = keyObj.AddComponent<ResonanceInteractable>();
            keyInter.interactType = ResonanceInteractType.MazeExitKey;
            keyInter.promptText = "🔑 [E] 1층 탈출 로비 출구 열쇠 획득 (EXIT KEY)";

            // 미로 배회 및 추격 J.A.N.I.T.O.R. AI 배치!
            ResonanceJanitorAI chaser = BuildJanitorRobot(mazeRoot.transform, gm, mats,
                new Vector3(-7.5f, 0f, 7.5f), Quaternion.identity,
                true, -1, "", "", Vector3.zero);

            Vector3[] wpCoords = new[]
            {
                new Vector3(-7.8f, 0f, 7.8f),
                new Vector3(7.8f, 0f, 7.8f),
                new Vector3(7.8f, 0f, -7.8f),
                new Vector3(-7.8f, 0f, -7.8f)
            };
            for (int i = 0; i < wpCoords.Length; i++)
            {
                GameObject wp = new GameObject($"Janitor_PatrolWP_{i}");
                wp.transform.SetParent(mazeRoot.transform);
                wp.transform.localPosition = wpCoords[i];
                chaser.patrolWaypoints.Add(wp.transform);
            }

            // 미로 북쪽 끝 1층 탈출 로비 계단문
            GameObject mazeDoor = CreatePrimitive(PrimitiveType.Cube, "MazeDoor_SlideBarrier", mazeRoot.transform,
                new Vector3(0f, 1.6f, 10.6f), new Vector3(3.0f, 3.2f, 0.25f), mats["Mat_WeatheredElevator"], true);
            gm.b1fMazeExitDoor = mazeDoor.transform;

            GameObject mazeToLobbyTrigger = CreatePrimitive(PrimitiveType.Cube, "MazeDoor_ToEscapeLobby", mazeRoot.transform,
                new Vector3(0f, 1.6f, 10.9f), new Vector3(3.4f, 3.2f, 0.4f), mats["Mat_GreenLED"], true);
            ResonanceInteractable m2lInter = mazeToLobbyTrigger.AddComponent<ResonanceInteractable>();
            m2lInter.interactType = ResonanceInteractType.MazeDoorToEscapeLobby;
            m2lInter.promptText = "🚪 [E] 출구 열쇠를 사용해 1층 탈출 로비로 올라가기";

            // ---------------- B. 1층 최종 탈출 로비 & 전기 제어실 (Escape Lobby, Origin: (240, 0, 0)) ----------------
            // Concept Art #9 우측 이미지 (붉은 EXIT 간판 + 판자로 막힌 정문 + 우측 전기 제어실 차단기) 완벽 재현!
            Vector3 lobbyOrigin = new Vector3(240f, 0f, 0f);
            GameObject lobbyRoot = new GameObject("Zone3B_1F_EscapeLobby_And_ElectricalRoom");
            lobbyRoot.transform.SetParent(worldRoot);
            lobbyRoot.transform.position = lobbyOrigin;

            GameObject lobbySpawn = new GameObject("EscapeLobby_SpawnPoint");
            lobbySpawn.transform.SetParent(lobbyRoot.transform);
            lobbySpawn.transform.localPosition = new Vector3(0f, 0.15f, -5.0f);
            lobbySpawn.transform.localRotation = Quaternion.identity;
            gm.escapeLobbySpawnPoint = lobbySpawn.transform;

            BuildRoomBox(lobbyRoot.transform, "EscapeLobby_Room", Vector3.zero, new Vector3(14f, 4.5f, 14f), mats["Mat_CheckeredMarble"], mats["Mat_GalleryWallDark"]);

            GameObject lobbyLightObj = new GameObject("EscapeLobby_Light");
            lobbyLightObj.transform.SetParent(lobbyRoot.transform);
            lobbyLightObj.transform.localPosition = new Vector3(0f, 3.9f, 0f);
            gm.facilityLights.Add(CreateConfiguredLight(lobbyLightObj, LightType.Point, new Color(1f, 0.90f, 0.78f), 1100f, 20f));

            // 북쪽 정문 탈출구 (EXIT 네온 사인 + 나무 판자 + PRESS [E] TO KICK OPEN)
            CreatePrimitive(PrimitiveType.Cube, "ExitDoor_Left", lobbyRoot.transform, new Vector3(-0.95f, 1.6f, 6.8f), new Vector3(1.7f, 3.2f, 0.2f), mats["Mat_WoodTrim"], true);
            CreatePrimitive(PrimitiveType.Cube, "ExitDoor_Right", lobbyRoot.transform, new Vector3(0.95f, 1.6f, 6.8f), new Vector3(1.7f, 3.2f, 0.2f), mats["Mat_WoodTrim"], true);
            CreatePrimitive(PrimitiveType.Cube, "EXIT_NeonSign", lobbyRoot.transform, new Vector3(0f, 3.55f, 6.75f), new Vector3(1.3f, 0.48f, 0.12f), mats["Mat_ExitSignRed"], false);
            GameObject exitSignLight = new GameObject("ExitSign_RedLight");
            exitSignLight.transform.SetParent(lobbyRoot.transform);
            exitSignLight.transform.localPosition = new Vector3(0f, 3.45f, 6.2f);
            CreateConfiguredLight(exitSignLight, LightType.Point, Color.red, 550f, 8.0f);

            GameObject plankL = CreatePrimitive(PrimitiveType.Cube, "ExitPlank_Upper", lobbyRoot.transform,
                new Vector3(0f, 1.95f, 6.65f), new Vector3(3.6f, 0.3f, 0.1f), mats["Mat_WoodTrim"], false);
            plankL.transform.localRotation = Quaternion.Euler(0f, 0f, 6f);
            GameObject plankR = CreatePrimitive(PrimitiveType.Cube, "ExitPlank_Lower", lobbyRoot.transform,
                new Vector3(0f, 1.15f, 6.65f), new Vector3(3.6f, 0.3f, 0.1f), mats["Mat_WoodTrim"], false);
            plankR.transform.localRotation = Quaternion.Euler(0f, 0f, -5f);
            gm.finalExitLeftPlank = plankL.transform;
            gm.finalExitRightPlank = plankR.transform;

            GameObject kickExitTrigger = CreatePrimitive(PrimitiveType.Cube, "FinalExitDoor_Interactable", lobbyRoot.transform,
                new Vector3(0f, 1.6f, 6.5f), new Vector3(3.6f, 3.2f, 0.6f), mats["Mat_WoodTrim"], true);
            kickExitTrigger.GetComponent<Renderer>().enabled = false;
            ResonanceInteractable exitInter = kickExitTrigger.AddComponent<ResonanceInteractable>();
            exitInter.interactType = ResonanceInteractType.FinalExitDoor;
            exitInter.promptText = "🚪 ESCAPE! PRESS [E] TO KICK OPEN (정문 판자를 발로 차고 탈출!)";

            // 우측(동쪽) 전기 제어실 (Electrical Control Room — 암호 1989 입력 후 메인 차단기 내리기!)
            Transform eastWallLobby = lobbyRoot.transform.Find("EscapeLobby_Room/Wall_East");
            if (eastWallLobby != null) UnityEngine.Object.DestroyImmediate(eastWallLobby.gameObject);
            CreatePrimitive(PrimitiveType.Cube, "EastWall_SouthPart", lobbyRoot.transform, new Vector3(7.0f, 2.25f, -4.25f), new Vector3(0.3f, 4.5f, 5.5f), mats["Mat_GalleryWallDark"], true);
            CreatePrimitive(PrimitiveType.Cube, "EastWall_NorthPart", lobbyRoot.transform, new Vector3(7.0f, 2.25f, 4.25f), new Vector3(0.3f, 4.5f, 5.5f), mats["Mat_GalleryWallDark"], true);

            // 전기 제어실 내부 방 (7m x 6m)
            BuildRoomBox(lobbyRoot.transform, "ElectricalControlRoom", new Vector3(10.5f, 0f, 0f), new Vector3(7.0f, 4.5f, 6.0f), mats["Mat_DirtyLabTile"], mats["Mat_SteelLocker"]);
            Transform westWallElec = lobbyRoot.transform.Find("ElectricalControlRoom/Wall_West");
            if (westWallElec != null) UnityEngine.Object.DestroyImmediate(westWallElec.gameObject);

            // 전기 제어실 내부 전용 천장 조명 (메인 차단기 배전반을 선명하게 비춤!)
            GameObject elecLightObj = new GameObject("ElectricalRoom_CeilingLight");
            elecLightObj.transform.SetParent(lobbyRoot.transform);
            elecLightObj.transform.localPosition = new Vector3(11.0f, 3.8f, 0f);
            gm.facilityLights.Add(CreateConfiguredLight(elecLightObj, LightType.Point, new Color(0.92f, 0.96f, 1.0f), 850f, 12f));

            // 전기 제어실 보안 슬라이딩 도어 (암호: 1989)
            GameObject elecDoor = CreatePrimitive(PrimitiveType.Cube, "ElectricalRoom_SecurityDoor", lobbyRoot.transform,
                new Vector3(7.0f, 1.6f, 0f), new Vector3(0.28f, 3.2f, 3.0f), mats["Mat_WeatheredElevator"], true);
            gm.electricalRoomDoor = elecDoor.transform;
            ResonanceInteractable elecDoorInter = elecDoor.AddComponent<ResonanceInteractable>();
            elecDoorInter.interactType = ResonanceInteractType.ElectricalRoomDoor;
            elecDoorInter.promptText = "🔐 [E] 전기 제어실 보안 키패드 암호 입력 (힌트: 1989)";

            // ★ 전기 제어실 내부 [메인 차단기 (MAIN BREAKER PANEL)] (Concept Art #9 우측 하단 배전반 완벽 재현!)
            GameObject breakerBox = CreatePrimitive(PrimitiveType.Cube, "MainBreakerBox_Panel", lobbyRoot.transform,
                new Vector3(13.6f, 1.65f, 0f), new Vector3(0.35f, 1.6f, 1.2f), mats["Mat_SteelLocker"], true);
            CreatePrimitive(PrimitiveType.Sphere, "BreakerLamp_Red", breakerBox.transform, new Vector3(-0.55f, 0.32f, -0.25f), new Vector3(0.15f, 0.08f, 0.12f), mats["Mat_ExitSignRed"], false);
            CreatePrimitive(PrimitiveType.Sphere, "BreakerLamp_Green", breakerBox.transform, new Vector3(-0.55f, 0.32f, 0.25f), new Vector3(0.15f, 0.08f, 0.12f), mats["Mat_GreenLED"], false);

            GameObject leverPivot = new GameObject("BreakerLever_Pivot");
            leverPivot.transform.SetParent(lobbyRoot.transform);
            leverPivot.transform.localPosition = new Vector3(13.35f, 1.55f, 0f);
            leverPivot.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            CreatePrimitive(PrimitiveType.Cube, "LeverHandle", leverPivot.transform, new Vector3(-0.22f, 0f, 0f), new Vector3(0.45f, 0.12f, 0.35f), mats["Mat_RedButton"], false);
            gm.mainBreakerLever = leverPivot.transform;

            BoxCollider brkCol = breakerBox.GetComponent<BoxCollider>();
            brkCol.size = new Vector3(5.0f, 2.2f, 2.5f);
            ResonanceInteractable brkInter = breakerBox.AddComponent<ResonanceInteractable>();
            brkInter.interactType = ResonanceInteractType.MainBreakerSwitch;
            brkInter.promptText = "⚡ [E] 메인 차단기 내리기 (MAIN BREAKER OFF — 레플리카 & 제니터 영구 정지)";
        }

        private static void BuildHidingLocker(Transform parent, string name, Vector3 pos, Quaternion rot, Dictionary<string, Material> mats)
        {
            GameObject locker = new GameObject(name);
            locker.transform.SetParent(parent);
            locker.transform.localPosition = pos;
            locker.transform.localRotation = rot;

            CreatePrimitive(PrimitiveType.Cube, "LockerBody", locker.transform, new Vector3(0f, 1.15f, 0f), new Vector3(1.1f, 2.3f, 0.9f), mats["Mat_SteelLocker"], true);
            CreatePrimitive(PrimitiveType.Cube, "LockerDoorSlit", locker.transform, new Vector3(0f, 1.65f, 0.46f), new Vector3(0.75f, 0.18f, 0.04f), mats["Mat_DarkJoint"], false);

            GameObject hidePt = new GameObject("HideInsidePoint");
            hidePt.transform.SetParent(locker.transform);
            hidePt.transform.localPosition = new Vector3(0f, 0.1f, 0.85f);
            hidePt.transform.localRotation = Quaternion.identity;

            BoxCollider col = locker.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.15f, 0f);
            col.size = new Vector3(1.5f, 2.4f, 1.5f);

            ResonanceInteractable inter = locker.AddComponent<ResonanceInteractable>();
            inter.interactType = ResonanceInteractType.HidingLocker;
            inter.hidingInsidePoint = hidePt.transform;
            inter.promptText = "🚪 [E] 철제 캐비닛 안에 숨기 (J.A.N.I.T.O.R. 시야 회피)";
        }

        // ========================================================================================
        // 5. ZONE 4 : 멀티 엔딩 3D 에필로그 세트 (배드 엔딩 현관문 기습 & 해피 엔딩 거실 TV 뉴스)
        // ========================================================================================
        private static void BuildZone4_EndingEpilogueSets(Transform worldRoot, ResonanceGameManager gm, Dictionary<string, Material> mats)
        {
            Vector3 epilogueOrigin = new Vector3(320f, 0f, 0f);
            GameObject zone4 = new GameObject("Zone4_EndingEpilogueSets");
            zone4.transform.SetParent(worldRoot);
            zone4.transform.position = epilogueOrigin;

            // 1. 배드 엔딩 세트 : 밤의 기자 자택 현관 복도 (괴물 레플리카 기습!)
            GameObject badSet = new GameObject("BadEnding_HomePorchSet");
            badSet.transform.SetParent(zone4.transform);
            badSet.transform.localPosition = new Vector3(0f, 0f, 0f);
            BuildRoomBox(badSet.transform, "HomeHallway", Vector3.zero, new Vector3(5.0f, 3.6f, 8.0f), mats["Mat_WoodTrim"], mats["Mat_GalleryWallDark"]);

            GameObject badCamPt = new GameObject("BadEnding_CamPoint");
            badCamPt.transform.SetParent(badSet.transform);
            badCamPt.transform.localPosition = new Vector3(0f, 0.15f, -2.2f);
            badCamPt.transform.localRotation = Quaternion.identity;
            gm.badEndingCamPoint = badCamPt.transform;

            GameObject badLight = new GameObject("PorchNightLight");
            badLight.transform.SetParent(badSet.transform);
            badLight.transform.localPosition = new Vector3(0f, 2.8f, 1.5f);
            CreateConfiguredLight(badLight, LightType.Point, new Color(1f, 0.35f, 0.3f), 650f, 10f);

            GameObject badMonster = BuildMonsterReplicaFigure(badSet.transform, new Vector3(0f, 0f, 2.6f), Quaternion.Euler(0f, 180f, 0f), mats);
            badMonster.SetActive(false);
            gm.badEndingReplicaMonster = badMonster;

            // 2. 해피 엔딩 세트 : 따스한 아침 햇살이 비치는 거실 & 1989 TV 뉴스 특보 방송!
            GameObject happySet = new GameObject("HappyEnding_LivingRoomTVSet");
            happySet.transform.SetParent(zone4.transform);
            happySet.transform.localPosition = new Vector3(20f, 0f, 0f);
            BuildRoomBox(happySet.transform, "LivingRoom", Vector3.zero, new Vector3(7.0f, 3.8f, 8.0f), mats["Mat_WoodTrim"], mats["Mat_GalleryUpperWall"]);

            GameObject happyCamPt = new GameObject("HappyEnding_CamPoint");
            happyCamPt.transform.SetParent(happySet.transform);
            happyCamPt.transform.localPosition = new Vector3(0f, 0.15f, -1.6f);
            happyCamPt.transform.localRotation = Quaternion.identity;
            gm.happyEndingCamPoint = happyCamPt.transform;

            GameObject sunLight = new GameObject("MorningSunLight");
            sunLight.transform.SetParent(happySet.transform);
            sunLight.transform.localPosition = new Vector3(0f, 3.1f, 0f);
            CreateConfiguredLight(sunLight, LightType.Point, new Color(1f, 0.94f, 0.80f), 1100f, 14f);

            // 거실 TV 장식장 + 1980년대 브라운관 TV (Tex_TVNewsScreen.png)
            CreatePrimitive(PrimitiveType.Cube, "TVStand", happySet.transform, new Vector3(0f, 0.5f, 2.0f), new Vector3(2.2f, 1.0f, 0.8f), mats["Mat_WoodTrim"], true);
            CreatePrimitive(PrimitiveType.Cube, "RetroTVBody", happySet.transform, new Vector3(0f, 1.55f, 2.0f), new Vector3(1.55f, 1.15f, 0.65f), mats["Mat_DarkJoint"], true);
            CreatePrimitive(PrimitiveType.Cube, "RetroTVScreen_BreakingNews", happySet.transform, new Vector3(0f, 1.55f, 1.66f), new Vector3(1.38f, 0.98f, 0.04f), mats["Mat_TVNewsScreen"], false);
        }

        // ========================================================================================
        // 6. 전체 UI Canvas 구축 (나노바나나 메인 메뉴 [게임 시작/설정/언어/나가기], 설정창, HUD, 단어 입력창, 엔딩 화면)
        // ========================================================================================
        private static ResonanceUIManager BuildCompleteRemakeUI(Transform worldRoot, ResonanceGameManager gm)
        {
            EventSystem existingEs = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (existingEs == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.transform.SetParent(worldRoot);
                existingEs = es.AddComponent<EventSystem>();
            }
#if ENABLE_INPUT_SYSTEM
            if (existingEs.GetComponent<InputSystemUIInputModule>() == null)
            {
                var oldMod = existingEs.GetComponent<StandaloneInputModule>();
                if (oldMod != null) UnityEngine.Object.DestroyImmediate(oldMod);
                existingEs.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (existingEs.GetComponent<StandaloneInputModule>() == null)
            {
                existingEs.gameObject.AddComponent<StandaloneInputModule>();
            }
#endif

            GameObject canvasObj = new GameObject("RESONANCE_Remake_Canvas");
            canvasObj.transform.SetParent(worldRoot);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObj.AddComponent<GraphicRaycaster>();

            ResonanceUIManager ui = canvasObj.AddComponent<ResonanceUIManager>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // ---------------- 1) 인게임 HUD Root ----------------
            GameObject hudRoot = CreateUIPanel(canvasObj.transform, "HUD_Root", Vector2.zero, Vector2.one, new Color(0, 0, 0, 0), false);
            ui.hudRoot = hudRoot;

            // 좌상단 현재 목표(Objective) 박스
            GameObject objBox = CreateUIPanel(hudRoot.transform, "ObjectiveBox", new Vector2(0.015f, 0.78f), new Vector2(0.42f, 0.97f), new Color(0.04f, 0.05f, 0.08f, 0.86f), false);
            ui.objectiveTitleText = CreateUIText(objBox.transform, "ObjectiveTitle", "PART 1 : 폐쇄된 바이스만 미술관", 24, FontStyle.Bold, new Color(1f, 0.84f, 0.35f), TextAnchor.UpperLeft, new Vector2(0.04f, 0.66f), new Vector2(0.96f, 0.94f), font);
            ui.objectiveBodyText = CreateUIText(objBox.transform, "ObjectiveBody", "• 각 층의 그림 앞 [붉은 버튼]을 조사하세요.", 18, FontStyle.Normal, new Color(0.92f, 0.94f, 0.98f), TextAnchor.UpperLeft, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.64f), font);

            // 우상단 조작법 & 챕터 이동 안내 배지
            GameObject ctrlBadge = CreateUIPanel(hudRoot.transform, "ControlsGuideBadge", new Vector2(0.66f, 0.85f), new Vector2(0.985f, 0.97f), new Color(0.05f, 0.06f, 0.09f, 0.80f), false);
            ui.controlsGuideText = CreateUIText(ctrlBadge.transform, "ControlsGuideText",
                "[WASD] 이동  |  [Shift] 달리기  |  [E] 조사  |  [F] 손전등\n[우클릭 / C] 카메라 조준  |  [좌클릭] 플래시 촬영 📸\n(시연 단축키 — F1: 미술관 / F2: 지하연구소 / F3: 미로 / F4: 탈출로비)",
                15, FontStyle.Normal, new Color(0.82f, 0.88f, 0.95f), TextAnchor.MiddleRight, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.94f), font);

            // 중앙 십자선 점
            CreateUIPanel(hudRoot.transform, "CenterCrosshairDot", new Vector2(0.4985f, 0.497f), new Vector2(0.5015f, 0.503f), new Color(1f, 1f, 1f, 0.75f), false);

            // 중앙 하단 호버 상호작용 프롬프트 ([E] ...)
            GameObject hoverBox = CreateUIPanel(hudRoot.transform, "HoverPromptBox", new Vector2(0.28f, 0.14f), new Vector2(0.72f, 0.21f), new Color(0.03f, 0.04f, 0.06f, 0.88f), false);
            ui.hoverPromptBox = hoverBox;
            ui.hoverPromptText = CreateUIText(hoverBox.transform, "HoverPromptText", "[E] 조사하기", 24, FontStyle.Bold, new Color(1f, 0.92f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.95f), font);
            hoverBox.SetActive(false);

            // 하단 토스트 알림 메시지
            GameObject toastBox = CreateUIPanel(hudRoot.transform, "ToastMessageBox", new Vector2(0.18f, 0.04f), new Vector2(0.82f, 0.11f), new Color(0.08f, 0.12f, 0.18f, 0.92f), false);
            ui.toastBox = toastBox;
            ui.toastText = CreateUIText(toastBox.transform, "ToastText", "", 21, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f), font);
            toastBox.SetActive(false);

            // ---------------- 2) 1989 취재 카메라 뷰파인더 오버레이 ----------------
            GameObject vfRoot = CreateUIPanel(hudRoot.transform, "CameraViewfinder_HUD", Vector2.zero, Vector2.one, new Color(0.02f, 0.12f, 0.06f, 0.22f), false);
            ui.cameraViewfinderRoot = vfRoot;
            CreateUIPanel(vfRoot.transform, "VF_BorderTop", new Vector2(0.12f, 0.82f), new Vector2(0.88f, 0.826f), new Color(0.3f, 1f, 0.5f, 0.8f), false);
            CreateUIPanel(vfRoot.transform, "VF_BorderBottom", new Vector2(0.12f, 0.174f), new Vector2(0.88f, 0.18f), new Color(0.3f, 1f, 0.5f, 0.8f), false);
            CreateUIPanel(vfRoot.transform, "VF_BorderLeft", new Vector2(0.12f, 0.18f), new Vector2(0.124f, 0.82f), new Color(0.3f, 1f, 0.5f, 0.8f), false);
            CreateUIPanel(vfRoot.transform, "VF_BorderRight", new Vector2(0.876f, 0.18f), new Vector2(0.88f, 0.82f), new Color(0.3f, 1f, 0.5f, 0.8f), false);
            ui.viewfinderStatusText = CreateUIText(vfRoot.transform, "VF_StatusText",
                "● REC  [1989 PRESS CAMERA]   |   [좌클릭] 플래시 촬영으로 벽면 안구 & 원혼 정화 📸",
                24, FontStyle.Bold, new Color(0.4f, 1f, 0.55f), TextAnchor.MiddleCenter, new Vector2(0.15f, 0.84f), new Vector2(0.85f, 0.91f), font);
            vfRoot.SetActive(false);

            // 화면 플래시 연출 이미지
            GameObject flashObj = CreateUIPanel(canvasObj.transform, "ScreenFlashOverlay", Vector2.zero, Vector2.one, new Color(1, 1, 1, 0), false);
            ui.screenFlashImage = flashObj.GetComponent<Image>();
            flashObj.SetActive(false);

            // ---------------- 3) ★ 나노바나나 일러스트 메인 메뉴 화면 (Main Menu Screen) ★ ----------------
            GameObject mainMenu = CreateUIPanel(canvasObj.transform, MainMenuVersionMarker, Vector2.zero, Vector2.one, new Color(0.02f, 0.02f, 0.04f, 1f), true);
            ui.mainMenuPanel = mainMenu;

            // 나노바나나 배경 원화 (Tex_MainMenuBG.jpg) 전체화면 적용!
            string bgPath = "Assets/IbArtMuseum/RemakeAssets/Tex_MainMenuBG.jpg";
            if (File.Exists(bgPath))
            {
                AssetDatabase.ImportAsset(bgPath, ImportAssetOptions.ForceSynchronousImport);
            }
            Texture2D mainMenuTex = AssetDatabase.LoadAssetAtPath<Texture2D>(bgPath);

            GameObject bgArtObj = new GameObject("NanoBanana_BackgroundArt");
            bgArtObj.transform.SetParent(mainMenu.transform, false);
            RectTransform bgRt = bgArtObj.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            RawImage rawImg = bgArtObj.AddComponent<RawImage>();
            rawImg.texture = mainMenuTex;
            rawImg.color = Color.white;
            rawImg.raycastTarget = false;
            ui.mainMenuBackgroundRawImage = rawImg;

            // 좌측 메뉴 가독성을 위한 세련된 다크 비네팅 사이드바 패널
            CreateUIPanel(mainMenu.transform, "LeftMenuBackdrop", new Vector2(0.0f, 0.0f), new Vector2(0.45f, 1.0f), new Color(0.02f, 0.02f, 0.04f, 0.78f), false);
            CreateUIPanel(mainMenu.transform, "LeftAccentLine", new Vector2(0.055f, 0.12f), new Vector2(0.058f, 0.88f), new Color(0.82f, 0.65f, 0.25f, 0.75f), false);

            // 타이틀 및 서브타이틀 (좌측 정렬)
            ui.mainMenuSubHeaderText = CreateUIText(mainMenu.transform, "SubHeader",
                "1989 WEISSMANN ART MUSEUM — INVESTIGATIVE HORROR",
                18, FontStyle.Bold, new Color(0.92f, 0.35f, 0.32f), TextAnchor.LowerLeft, new Vector2(0.075f, 0.82f), new Vector2(0.43f, 0.87f), font);

            ui.mainMenuTitleText = CreateUIText(mainMenu.transform, "MainTitle",
                "R E S O N A N C E\n<size=34>공 명  :  폐 쇄 된  미 술 관 의  기 록</size>",
                56, FontStyle.Bold, new Color(0.98f, 0.96f, 0.90f), TextAnchor.MiddleLeft, new Vector2(0.075f, 0.66f), new Vector2(0.44f, 0.82f), font);

            ui.mainMenuDescText = CreateUIText(mainMenu.transform, "Desc",
                "폐관된 바이스만 미술관에서 벌어지는 기묘한 실종 사건과 이상현상.\n손전등과 취재 카메라를 들고 숨겨진 지하 연구소와 진실을 파헤치십시오.",
                18, FontStyle.Normal, new Color(0.82f, 0.85f, 0.90f), TextAnchor.UpperLeft, new Vector2(0.075f, 0.55f), new Vector2(0.43f, 0.65f), font);

            // ★ 실제 게임 스타일 4대 메인 메뉴 버튼 배열 (게임 시작 / 설정 / 언어 / 나가기) ★
            ui.btnStartGame = CreateUIButton(mainMenu.transform, "Btn_1_StartGame", "▶   게 임 시 작   (START GAME)",
                new Vector2(0.075f, 0.42f), new Vector2(0.40f, 0.505f), new Color(0.68f, 0.14f, 0.16f, 0.96f), font, 24, out ui.txtStartGame);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(ui.btnStartGame.onClick, ui.OnClickMainMenuStartGame);

            ui.btnOpenSettings = CreateUIButton(mainMenu.transform, "Btn_2_Settings", "⚙   설 정   (SETTINGS)",
                new Vector2(0.075f, 0.315f), new Vector2(0.40f, 0.395f), new Color(0.14f, 0.16f, 0.22f, 0.94f), font, 22, out ui.txtOpenSettings);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(ui.btnOpenSettings.onClick, ui.OnClickOpenSettings);

            ui.btnToggleLanguage = CreateUIButton(mainMenu.transform, "Btn_3_Language", "🌐   언 어 : 한국어   (LANGUAGE : KR)",
                new Vector2(0.075f, 0.21f), new Vector2(0.40f, 0.29f), new Color(0.14f, 0.16f, 0.22f, 0.94f), font, 22, out ui.txtToggleLanguage);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(ui.btnToggleLanguage.onClick, ui.OnClickToggleLanguage);

            ui.btnExitGame = CreateUIButton(mainMenu.transform, "Btn_4_ExitGame", "✖   나 가 기   (EXIT GAME)",
                new Vector2(0.075f, 0.105f), new Vector2(0.40f, 0.185f), new Color(0.22f, 0.12f, 0.14f, 0.94f), font, 22, out ui.txtExitGame);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(ui.btnExitGame.onClick, ui.OnClickExitGame);

            CreateUIText(mainMenu.transform, "VersionFooter", "VER 2.0 REMAKE  |  © 1989 WEISSMANN FOUNDATION ARCHIVES",
                14, FontStyle.Normal, new Color(0.6f, 0.64f, 0.7f, 0.8f), TextAnchor.MiddleLeft, new Vector2(0.075f, 0.03f), new Vector2(0.43f, 0.08f), font);

            // ---------------- 4) ★ 환경설정 모달 (Settings Modal) ★ ----------------
            GameObject setPanel = CreateUIPanel(mainMenu.transform, "Modal_Settings", new Vector2(0.46f, 0.16f), new Vector2(0.93f, 0.84f), new Color(0.05f, 0.06f, 0.10f, 0.96f), true);
            ui.settingsPanel = setPanel;
            ui.settingsTitleText = CreateUIText(setPanel.transform, "SettingsTitle", "⚙️  환 경 설 정 (SETTINGS)",
                30, FontStyle.Bold, new Color(1f, 0.86f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.94f), font);

            // 1. 마우스 감도 조절 행
            ui.sensLabelText = CreateUIText(setPanel.transform, "SensLabel", "마우스 회전 감도 (Mouse Sensitivity)",
                20, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, new Vector2(0.08f, 0.64f), new Vector2(0.58f, 0.76f), font);
            ui.btnSensMinus = CreateUIButton(setPanel.transform, "Btn_SensMinus", "◀  -", new Vector2(0.60f, 0.65f), new Vector2(0.70f, 0.75f), new Color(0.22f, 0.26f, 0.34f, 1f), font, 20, out _);
            ui.sensValueText = CreateUIText(setPanel.transform, "SensVal", "120", 22, FontStyle.Bold, new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter, new Vector2(0.71f, 0.65f), new Vector2(0.81f, 0.75f), font);
            ui.btnSensPlus = CreateUIButton(setPanel.transform, "Btn_SensPlus", "+  ▶", new Vector2(0.82f, 0.65f), new Vector2(0.92f, 0.75f), new Color(0.22f, 0.26f, 0.34f, 1f), font, 20, out _);

            // 2. 전체 음량 조절 행
            ui.volLabelText = CreateUIText(setPanel.transform, "VolLabel", "전체 음량 및 배경음 (Master Volume)",
                20, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, new Vector2(0.08f, 0.46f), new Vector2(0.58f, 0.58f), font);
            ui.btnVolMinus = CreateUIButton(setPanel.transform, "Btn_VolMinus", "◀  -", new Vector2(0.60f, 0.47f), new Vector2(0.70f, 0.57f), new Color(0.22f, 0.26f, 0.34f, 1f), font, 20, out _);
            ui.volValueText = CreateUIText(setPanel.transform, "VolVal", "50 %", 22, FontStyle.Bold, new Color(0.45f, 0.95f, 0.6f), TextAnchor.MiddleCenter, new Vector2(0.71f, 0.47f), new Vector2(0.81f, 0.57f), font);
            ui.btnVolPlus = CreateUIButton(setPanel.transform, "Btn_VolPlus", "+  ▶", new Vector2(0.82f, 0.47f), new Vector2(0.92f, 0.57f), new Color(0.22f, 0.26f, 0.34f, 1f), font, 20, out _);

            // 3. 손전등 밝기 조절 행
            ui.lightLabelText = CreateUIText(setPanel.transform, "LightLabel", "손전등 밝기 배율 (Flashlight Power)",
                20, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, new Vector2(0.08f, 0.28f), new Vector2(0.58f, 0.40f), font);
            ui.btnLightMinus = CreateUIButton(setPanel.transform, "Btn_LightMinus", "◀  -", new Vector2(0.60f, 0.29f), new Vector2(0.70f, 0.39f), new Color(0.22f, 0.26f, 0.34f, 1f), font, 20, out _);
            ui.lightValueText = CreateUIText(setPanel.transform, "LightVal", "100 %", 22, FontStyle.Bold, new Color(0.45f, 0.85f, 1f), TextAnchor.MiddleCenter, new Vector2(0.71f, 0.29f), new Vector2(0.81f, 0.39f), font);
            ui.btnLightPlus = CreateUIButton(setPanel.transform, "Btn_LightPlus", "+  ▶", new Vector2(0.82f, 0.29f), new Vector2(0.92f, 0.39f), new Color(0.22f, 0.26f, 0.34f, 1f), font, 20, out _);

            ui.btnCloseSettings = CreateUIButton(setPanel.transform, "Btn_CloseSettings", "적용 및 닫기 (APPLY & CLOSE)",
                new Vector2(0.26f, 0.08f), new Vector2(0.74f, 0.20f), new Color(0.18f, 0.52f, 0.32f, 1f), font, 22, out ui.txtCloseSettings);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(ui.btnCloseSettings.onClick, ui.OnClickCloseSettings);
            setPanel.SetActive(false);

            // ---------------- 5) 대화 및 조사 모달 (Dialogue Modal) ----------------
            GameObject dlgPanel = CreateUIPanel(canvasObj.transform, "Modal_Dialogue", new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.78f), new Color(0.04f, 0.05f, 0.08f, 0.96f), true);
            ui.dialoguePanel = dlgPanel;
            ui.dialogueTitleText = CreateUIText(dlgPanel.transform, "DlgTitle", "제목", 30, FontStyle.Bold, new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.93f), font);
            ui.dialogueBodyText = CreateUIText(dlgPanel.transform, "DlgBody", "내용", 22, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft, new Vector2(0.07f, 0.24f), new Vector2(0.93f, 0.76f), font);
            Button dlgCloseBtn = CreateUIButton(dlgPanel.transform, "Btn_CloseDialogue", "확인 (닫기)", new Vector2(0.36f, 0.07f), new Vector2(0.64f, 0.18f), new Color(0.22f, 0.36f, 0.58f, 1f), font, 22, out ui.dialogueButtonText);
            ui.btnCloseDialogue = dlgCloseBtn;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(dlgCloseBtn.onClick, ui.OnClickCloseDialogueModal);
            dlgPanel.SetActive(false);

            // ---------------- 6) 바이스만 박사 기밀 실험 일지 모달 (Document Modal) ----------------
            GameObject docPanel = CreateUIPanel(canvasObj.transform, "Modal_SecretDocument", new Vector2(0.15f, 0.12f), new Vector2(0.85f, 0.88f), new Color(0.12f, 0.10f, 0.08f, 0.98f), true);
            ui.documentPanel = docPanel;
            ui.documentTitleText = CreateUIText(docPanel.transform, "DocTitle", "TOP SECRET DOCUMENT", 32, FontStyle.Bold, new Color(0.95f, 0.25f, 0.25f), TextAnchor.MiddleCenter, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.95f), font);
            ui.documentBodyText = CreateUIText(docPanel.transform, "DocBody", "", 21, FontStyle.Normal, new Color(0.95f, 0.92f, 0.84f), TextAnchor.MiddleLeft, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.82f), font);
            Button docCloseBtn = CreateUIButton(docPanel.transform, "Btn_CloseDoc", "기밀 일지 덮기 (전기 제어실 암호 [1989] 메모 완료)", new Vector2(0.25f, 0.06f), new Vector2(0.75f, 0.16f), new Color(0.55f, 0.18f, 0.18f, 1f), font, 22, out _);
            ui.btnCloseDocument = docCloseBtn;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(docCloseBtn.onClick, ui.OnClickCloseDocumentModal);
            docPanel.SetActive(false);

            // ---------------- 7) J.A.N.I.T.O.R. 단어 입력 & 전기 제어실 키패드 모달 (Word Input Modal) ----------------
            GameObject wordPanel = CreateUIPanel(canvasObj.transform, "Modal_WordInput", new Vector2(0.20f, 0.18f), new Vector2(0.80f, 0.82f), new Color(0.05f, 0.07f, 0.11f, 0.97f), true);
            ui.wordInputPanel = wordPanel;
            ui.wordInputTitleText = CreateUIText(wordPanel.transform, "WordTitle", "J.A.N.I.T.O.R. 보안 인증", 30, FontStyle.Bold, new Color(0.45f, 0.9f, 1f), TextAnchor.MiddleCenter, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.94f), font);
            ui.wordInputHintText = CreateUIText(wordPanel.transform, "WordHint", "", 20, FontStyle.Normal, new Color(0.9f, 0.92f, 0.96f), TextAnchor.MiddleCenter, new Vector2(0.06f, 0.56f), new Vector2(0.94f, 0.80f), font);

            GameObject inputBox = CreateUIPanel(wordPanel.transform, "WordInputFieldBox", new Vector2(0.20f, 0.38f), new Vector2(0.80f, 0.52f), new Color(0.14f, 0.16f, 0.22f, 1f), true);
            InputField inputField = inputBox.AddComponent<InputField>();
            Text inputText = CreateUIText(inputBox.transform, "Text", "", 28, FontStyle.Bold, new Color(1f, 0.95f, 0.4f), TextAnchor.MiddleCenter, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), font);
            Text placeholder = CreateUIText(inputBox.transform, "Placeholder", "여기에 단어나 암호를 입력하세요...", 22, FontStyle.Italic, new Color(0.6f, 0.65f, 0.7f, 0.7f), TextAnchor.MiddleCenter, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), font);
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;
            ui.wordInputField = inputField;

            ui.wordFeedbackText = CreateUIText(wordPanel.transform, "WordFeedback", "", 20, FontStyle.Bold, new Color(1f, 0.4f, 0.4f), TextAnchor.MiddleCenter, new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.36f), font);

            Button btnAutoFill = CreateUIButton(wordPanel.transform, "Btn_AutoFill", "💡 찾은 단서 자동 입력", new Vector2(0.10f, 0.08f), new Vector2(0.38f, 0.21f), new Color(0.35f, 0.42f, 0.20f, 1f), font, 20, out _);
            ui.btnAutoFillWord = btnAutoFill;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btnAutoFill.onClick, ui.OnClickAutoFillAnswer);

            Button btnSubmit = CreateUIButton(wordPanel.transform, "Btn_SubmitWord", "✅ 인증 확인 (ENTER)", new Vector2(0.41f, 0.08f), new Vector2(0.69f, 0.21f), new Color(0.16f, 0.55f, 0.34f, 1f), font, 22, out _);
            ui.btnSubmitWord = btnSubmit;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btnSubmit.onClick, ui.OnClickSubmitWordInput);

            Button btnCancel = CreateUIButton(wordPanel.transform, "Btn_CancelWord", "닫기", new Vector2(0.72f, 0.08f), new Vector2(0.90f, 0.21f), new Color(0.35f, 0.22f, 0.25f, 1f), font, 20, out _);
            ui.btnCancelWord = btnCancel;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btnCancel.onClick, ui.OnClickCloseWordInput);
            wordPanel.SetActive(false);

            // ---------------- 8) 멀티 엔딩 결과 화면 (Ending Screen Panel) ----------------
            GameObject endPanel = CreateUIPanel(canvasObj.transform, "Modal_EndingScreen", new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.90f), new Color(0.03f, 0.04f, 0.07f, 0.96f), true);
            ui.endingPanel = endPanel;
            ui.endingBadgeText = CreateUIText(endPanel.transform, "EndingBadge", "ENDING", 28, FontStyle.Bold, Color.yellow, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.82f), new Vector2(0.9f, 0.92f), font);
            ui.endingTitleText = CreateUIText(endPanel.transform, "EndingTitle", "엔딩 타이틀", 42, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.68f), new Vector2(0.92f, 0.82f), font);
            ui.endingDescriptionText = CreateUIText(endPanel.transform, "EndingDesc", "", 22, FontStyle.Normal, new Color(0.9f, 0.92f, 0.95f), TextAnchor.MiddleCenter, new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.65f), font);

            Button btnRetryLobby = CreateUIButton(endPanel.transform, "Btn_RetryEscapeLobby", "🔄 1층 탈출 로비로 돌아가기 (다른 엔딩 분기 체험)", new Vector2(0.14f, 0.08f), new Vector2(0.52f, 0.20f), new Color(0.20f, 0.48f, 0.62f, 1f), font, 21, out _);
            ui.btnRetryLobby = btnRetryLobby;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btnRetryLobby.onClick, ui.OnClickRetryFromEscapeLobby);

            Button btnTitle = CreateUIButton(endPanel.transform, "Btn_ReturnToTitle", "🏠 메인 메뉴(타이틀)로 돌아가기", new Vector2(0.56f, 0.08f), new Vector2(0.86f, 0.20f), new Color(0.45f, 0.22f, 0.25f, 1f), font, 21, out _);
            ui.btnReturnToTitle = btnTitle;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btnTitle.onClick, ui.OnClickReturnToTitle);
            endPanel.SetActive(false);

            return ui;
        }

        // ========================================================================================
        // 유틸리티 헬퍼 메서드
        // ========================================================================================
        private static void BuildRoomBox(Transform parent, string name, Vector3 localCenter, Vector3 size, Material floorMat, Material wallMat)
        {
            GameObject box = new GameObject(name);
            box.transform.SetParent(parent);
            box.transform.localPosition = localCenter;

            CreatePrimitive(PrimitiveType.Cube, "Floor", box.transform, new Vector3(0f, -0.1f, 0f), new Vector3(size.x, 0.2f, size.z), floorMat, true);
            CreatePrimitive(PrimitiveType.Cube, "Ceiling", box.transform, new Vector3(0f, size.y + 0.1f, 0f), new Vector3(size.x, 0.2f, size.z), wallMat, true);
            CreatePrimitive(PrimitiveType.Cube, "Wall_North", box.transform, new Vector3(0f, size.y * 0.5f, size.z * 0.5f), new Vector3(size.x, size.y, 0.3f), wallMat, true);
            CreatePrimitive(PrimitiveType.Cube, "Wall_South", box.transform, new Vector3(0f, size.y * 0.5f, -size.z * 0.5f), new Vector3(size.x, size.y, 0.3f), wallMat, true);
            CreatePrimitive(PrimitiveType.Cube, "Wall_West", box.transform, new Vector3(-size.x * 0.5f, size.y * 0.5f, 0f), new Vector3(0.3f, size.y, size.z), wallMat, true);
            CreatePrimitive(PrimitiveType.Cube, "Wall_East", box.transform, new Vector3(size.x * 0.5f, size.y * 0.5f, 0f), new Vector3(0.3f, size.y, size.z), wallMat, true);
        }

        private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            if (mat != null)
            {
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;
            }

            if (!keepCollider)
            {
                Collider c = go.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }

            return go;
        }

        private static Light CreateConfiguredLight(GameObject go, LightType type, Color color, float intensity, float range, float spotAngle = 60f)
        {
            Light l = go.GetComponent<Light>();
            if (l == null) l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.range = range;
            if (type == LightType.Spot) l.spotAngle = spotAngle;

            HDAdditionalLightData hd = go.GetComponent<HDAdditionalLightData>();
            if (hd == null) hd = go.AddComponent<HDAdditionalLightData>();
            l.intensity = intensity;
            hd.intensity = intensity;

            return l;
        }

        private static GameObject CreateUIPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color bgColor, bool raycastTarget = false)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = raycastTarget;
            return go;
        }

        private static Text CreateUIText(Transform parent, string name, string content, int fontSize, FontStyle style, Color color, TextAnchor align, Vector2 anchorMin, Vector2 anchorMax, Font font)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Text txt = go.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = align;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.supportRichText = true;
            txt.raycastTarget = false;
            return txt;
        }

        private static Button CreateUIButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Color bgColor, Font font, int fontSize, out Text labelText)
        {
            GameObject go = CreateUIPanel(parent, name, anchorMin, anchorMax, bgColor, true);
            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            btn.colors = cb;

            labelText = CreateUIText(go.transform, "Label", label, fontSize, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f), font);
            return btn;
        }
    }
}
