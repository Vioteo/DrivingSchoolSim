using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DrivingSchool.Editor
{
    public static class UIBuilder
    {
        private static Color BgColor = new Color(0.08f, 0.10f, 0.12f, 1f);
        private static Color PanelColor = new Color(0.12f, 0.15f, 0.18f, 0.95f);
        private static Color CardBgColor = new Color(0.15f, 0.19f, 0.23f, 1f);
        private static Color AccentGold = new Color(0.98f, 0.69f, 0.23f, 1f);
        private static Color AccentCyan = new Color(0.35f, 0.60f, 0.83f, 1f);
        private static Color TextWhite = new Color(0.95f, 0.95f, 0.95f, 1f);
        private static Color TextDim = new Color(0.60f, 0.65f, 0.70f, 1f);
        private static Color DarkOverlay = new Color(0f, 0f, 0f, 0.85f);
        private static Color RedColor = new Color(0.85f, 0.2f, 0.2f, 1f);
        private static Color GreenColor = new Color(0.2f, 0.75f, 0.3f, 1f);

        [MenuItem("Driving School/Import TMP Essentials")]
        public static void ImportTMP()
        {
            string pkg = "Library/PackageCache/com.unity.ugui@bb329a87fcdc/Package Resources/TMP Essential Resources.unitypackage";
            if (File.Exists(pkg))
            {
                AssetDatabase.ImportPackage(pkg, false);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            Debug.Log("TMP_ESSENTIALS_IMPORTED");
        }

        [MenuItem("Driving School/Build UI Prefabs")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory("Assets/DrivingSchool/Prefabs/UI");
            
            BuildHUD();
            BuildMainMenu();
            BuildLessonCatalog();
            BuildConditionsSetup();
            BuildG27Calibration();
            BuildPauseMenu();
            BuildTheoryExam();
            
            Debug.Log("UI_PREFABS_BUILT");
        }

        private static void SavePrefab(GameObject go, string name)
        {
            PrefabUtility.SaveAsPrefabAsset(go, $"Assets/DrivingSchool/Prefabs/UI/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        private static GameObject CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private static RectTransform CreateFill(string name, Transform parent, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        private static RectTransform CreateFixed(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 position, Vector2? pivot = null)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            return rt;
        }

        private static Image AddImage(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static TMP_FontAsset s_DefaultFont;
        private static TMP_FontAsset GetFont()
        {
            if (s_DefaultFont != null) return s_DefaultFont;
            s_DefaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (s_DefaultFont == null)
            {
                var guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
                if (guids.Length > 0)
                    s_DefaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            return s_DefaultFont;
        }

        private static TextMeshProUGUI AddText(GameObject go, string text, int fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            var font = GetFont();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        // ==========================================
        // 1. HUD
        // ==========================================
        private static void BuildHUD()
        {
            var canvas = CreateCanvas("HUD");
            
            // Speedometer: Bottom Left (420 x 200)
            var speedo = CreateFixed("Speedometer", canvas.transform, new Vector2(0, 0), new Vector2(420, 200), new Vector2(50, 50));
            AddImage(speedo.gameObject, PanelColor);
            
            // Speed number (Left side)
            var speedText = CreateFixed("SpeedText", speedo, new Vector2(0, 0.5f), new Vector2(150, 80), new Vector2(30, 20), new Vector2(0, 0.5f));
            AddText(speedText.gameObject, "48", 72, TextWhite, TextAlignmentOptions.Left, FontStyles.Bold);
            
            // Speed label (below speed number)
            var speedLabel = CreateFixed("SpeedLabel", speedo, new Vector2(0, 0.5f), new Vector2(150, 30), new Vector2(30, -35), new Vector2(0, 0.5f));
            AddText(speedLabel.gameObject, "km/h", 22, TextDim, TextAlignmentOptions.Left);

            // Gear Box (Center of Speedometer)
            var gearBox = CreateFixed("GearBox", speedo, new Vector2(0, 0.5f), new Vector2(80, 100), new Vector2(175, 0), new Vector2(0, 0.5f));
            AddImage(gearBox.gameObject, new Color(0.08f, 0.10f, 0.13f));
            var gearTxt = CreateFill("GearTxt", gearBox);
            AddText(gearTxt.gameObject, "<size=65%><color=#8090A0>GEAR</color></size>\n<size=150%><b>3</b></size>", 22, TextWhite, TextAlignmentOptions.Center, FontStyles.Bold);

            // Speed Limit Sign inside speedometer (Right side): Authentic Russian 3.24 round sign
            var sign = CreateFixed("SpeedLimit", speedo, new Vector2(1, 0.5f), new Vector2(110, 110), new Vector2(-25, 0), new Vector2(1, 0.5f));
            AddImage(sign.gameObject, RedColor); // Outer red ring
            var signWhite = CreateFill("White", sign, 10, 10, 10, 10);
            AddImage(signWhite.gameObject, Color.white);
            var signText = CreateFill("LimitText", signWhite);
            AddText(signText.gameObject, "60", 48, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);

            // Instructor Hint: Bottom Center
            var hint = CreateFixed("InstructorHint", canvas.transform, new Vector2(0.5f, 0), new Vector2(750, 120), new Vector2(0, 50), new Vector2(0.5f, 0));
            AddImage(hint.gameObject, AccentGold);
            var innerHint = CreateFill("Inner", hint, 3, 3, 3, 3);
            AddImage(innerHint.gameObject, PanelColor);

            var hintText = CreateFill("HintText", innerHint, 25, 25, 15, 15);
            AddText(hintText.gameObject, "<color=#FFD700><b>Instructor:</b></color> Prepare to turn right at the intersection. Check right mirror and activate turn signal.", 24, TextWhite, TextAlignmentOptions.Left);

            // GPS / Navigator: Top Right
            var gps = CreateFixed("GPS", canvas.transform, new Vector2(1, 1), new Vector2(320, 260), new Vector2(-50, -50), new Vector2(1, 1));
            AddImage(gps.gameObject, PanelColor);
            
            var gpsTop = CreateFixed("TopBar", gps, new Vector2(0.5f, 1), new Vector2(320, 55), new Vector2(0, 0), new Vector2(0.5f, 1));
            AddImage(gpsTop.gameObject, CardBgColor);
            var gpsTopText = CreateFill("Text", gpsTop, 15, 15, 0, 0);
            AddText(gpsTopText.gameObject, "Turn Right in 80 m", 24, AccentGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var gpsMap = CreateFill("MapArea", gps, 15, 15, 70, 15);
            AddImage(gpsMap.gameObject, new Color(0.08f, 0.10f, 0.12f));
            var gpsStreet = CreateFixed("Street", gpsMap, new Vector2(0.5f, 0), new Vector2(280, 30), new Vector2(0, 10), new Vector2(0.5f, 0));
            AddText(gpsStreet.gameObject, "ul. Tsentralnaya", 18, TextDim, TextAlignmentOptions.Center);

            SavePrefab(canvas, "HUD");
        }

        // ==========================================
        // 2. MAIN MENU
        // ==========================================
        private static void BuildMainMenu()
        {
            var canvas = CreateCanvas("MainMenu");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, BgColor);

            // Top Bar
            var topBar = CreateFixed("TopBar", bg, new Vector2(0.5f, 1), new Vector2(1920, 100), new Vector2(0, 0), new Vector2(0.5f, 1));
            AddImage(topBar.gameObject, new Color(0.05f, 0.07f, 0.09f, 0.98f));

            var logo = CreateFixed("Logo", topBar, new Vector2(0, 0.5f), new Vector2(700, 70), new Vector2(60, 0), new Vector2(0, 0.5f));
            AddText(logo.gameObject, "<b>DRIVING ACADEMY</b>  <color=#5999D3>SIMULATOR</color>", 32, TextWhite, TextAlignmentOptions.Left);

            var profile = CreateFixed("StudentCard", topBar, new Vector2(1, 0.5f), new Vector2(450, 70), new Vector2(-60, 0), new Vector2(1, 0.5f));
            AddImage(profile.gameObject, PanelColor);
            var pName = CreateFixed("Name", profile, new Vector2(0, 0.5f), new Vector2(280, 50), new Vector2(25, 0), new Vector2(0, 0.5f));
            AddText(pName.gameObject, "Cadet: Alexey S.\n<size=75%><color=#A0B0C0>Category B (Manual)</color></size>", 20, AccentGold, TextAlignmentOptions.Left);
            var pText = CreateFixed("Progress", profile, new Vector2(1, 0.5f), new Vector2(120, 50), new Vector2(-20, 0), new Vector2(1, 0.5f));
            AddText(pText.gameObject, "<b>68%</b>\n<size=60%><color=#8090A0>Completed</color></size>", 24, TextWhite, TextAlignmentOptions.Right);

            // Continue Training (Left Middle)
            var continuePanel = CreateFixed("ContinuePanel", bg, new Vector2(0, 0.5f), new Vector2(880, 340), new Vector2(60, 110), new Vector2(0, 0.5f));
            AddImage(continuePanel.gameObject, PanelColor);
            
            var cImage = CreateFixed("Image", continuePanel, new Vector2(0, 0.5f), new Vector2(340, 300), new Vector2(20, 0), new Vector2(0, 0.5f));
            AddImage(cImage.gameObject, CardBgColor);
            var cImgTxt = CreateFill("Txt", cImage);
            AddText(cImgTxt.gameObject, "[ LESSON 14 PREVIEW ]\n<size=60%>City Intersections</size>", 22, TextDim, TextAlignmentOptions.Center);

            var cTitle = CreateFixed("Title", continuePanel, new Vector2(0, 1), new Vector2(480, 35), new Vector2(380, -30), new Vector2(0, 1));
            AddText(cTitle.gameObject, "CONTINUE TRAINING:", 20, AccentGold, TextAlignmentOptions.Left, FontStyles.Bold);
            
            var cLesson = CreateFixed("Lesson", continuePanel, new Vector2(0, 1), new Vector2(480, 80), new Vector2(380, -70), new Vector2(0, 1));
            AddText(cLesson.gameObject, "Lesson 14 - Complex City Intersections", 28, AccentCyan, TextAlignmentOptions.Left, FontStyles.Bold);
            
            var cDesc = CreateFixed("Desc", continuePanel, new Vector2(0, 1), new Vector2(480, 80), new Vector2(380, -155), new Vector2(0, 1));
            AddText(cDesc.gameObject, "Master multi-lane turns, tram tracks, priority signs, and busy pedestrian crossings.", 20, TextWhite, TextAlignmentOptions.Left);
            
            var cBtn = CreateFixed("BtnStart", continuePanel, new Vector2(0, 0), new Vector2(260, 55), new Vector2(380, 25), new Vector2(0, 0));
            AddImage(cBtn.gameObject, AccentGold);
            var cBtnTxt = CreateFill("Txt", cBtn);
            AddText(cBtnTxt.gameObject, "START LESSON", 24, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);

            // Right Hero: Vehicle & Simulator Status
            var heroPanel = CreateFixed("HeroPanel", bg, new Vector2(1, 0.5f), new Vector2(880, 600), new Vector2(-60, -20), new Vector2(1, 0.5f));
            AddImage(heroPanel.gameObject, PanelColor);
            
            var hTitle = CreateFixed("Title", heroPanel, new Vector2(0.5f, 1), new Vector2(820, 50), new Vector2(0, -25), new Vector2(0.5f, 1));
            AddText(hTitle.gameObject, "VEHICLE STATUS & SIMULATION PROFILE", 24, AccentCyan, TextAlignmentOptions.Left, FontStyles.Bold);

            var carFrame = CreateFixed("CarFrame", heroPanel, new Vector2(0.5f, 1), new Vector2(820, 300), new Vector2(0, -85), new Vector2(0.5f, 1));
            AddImage(carFrame.gameObject, CardBgColor);
            var carTxt = CreateFill("Txt", carFrame);
            AddText(carTxt.gameObject, "[ 3D VEHICLE VIEWPORT ]\n<size=70%>Training Sedan - Category B\n5-Speed Manual | Rear Parking Sensors | ABS Active</size>", 24, TextDim, TextAlignmentOptions.Center);

            var specGrid = CreateFixed("SpecGrid", heroPanel, new Vector2(0.5f, 0), new Vector2(820, 180), new Vector2(0, 25), new Vector2(0.5f, 0));
            AddImage(specGrid.gameObject, new Color(0.09f, 0.12f, 0.15f));
            var specTxt = CreateFill("Txt", specGrid, 25, 25, 20, 20);
            AddText(specTxt.gameObject, "<b>Selected Route:</b> Autodrome Training Ground (120x120m)\n<b>Physics Profile:</b> Hardcore Realistic (Clutch bite simulation, engine stall on)\n<b>Weather:</b> Clear Day (Dry Asphalt, 22°C)\n<b>Instructor:</b> Active Voice Prompts & Rule Violation Auditing", 20, TextWhite, TextAlignmentOptions.Left);

            // 3 Cards below ContinuePanel (Left side)
            var cards = CreateFixed("Cards", bg, new Vector2(0, 0.5f), new Vector2(880, 240), new Vector2(60, -200), new Vector2(0, 0.5f));
            
            string[] cardHeaders = { "AUTODROME", "TRAFFIC RULES", "FREE DRIVE" };
            string[] cardSub = { "8/8 Completed", "Ticket 12 of 40", "Open City Map" };
            string[] cardDetails = { 
                "- Slalom: <color=#4CAF50>[PASSED]</color>\n- Hill Start: <color=#4CAF50>[PASSED]</color>\n- Parallel Park: <color=#4CAF50>[PASSED]</color>",
                "Score: 19/20 Avg\nReady for exam\nTime limit: 20 min",
                "Explore 10x10 km\nDynamic traffic\nCustom weather"
            };
            
            for(int i = 0; i < 3; i++)
            {
                var card = CreateFixed($"Card{i}", cards, new Vector2(0, 0.5f), new Vector2(280, 240), new Vector2(i * 300, 0), new Vector2(0, 0.5f));
                AddImage(card.gameObject, PanelColor);
                
                var topH = CreateFixed("TopH", card, new Vector2(0.5f, 1), new Vector2(260, 65), new Vector2(0, -15), new Vector2(0.5f, 1));
                AddText(topH.gameObject, $"<b>{cardHeaders[i]}</b>\n<size=70%><color=#80A0C0>{cardSub[i]}</color></size>", 22, AccentGold, TextAlignmentOptions.Center);

                var stat = CreateFixed("Stat", card, new Vector2(0.5f, 0), new Vector2(250, 135), new Vector2(0, 15), new Vector2(0.5f, 0));
                AddText(stat.gameObject, cardDetails[i], 19, TextWhite, TextAlignmentOptions.Left);
            }

            // G27 Status Bar
            var g27 = CreateFixed("G27", bg, new Vector2(0, 0), new Vector2(880, 45), new Vector2(60, 95), new Vector2(0, 0));
            AddImage(g27.gameObject, PanelColor);
            var g27Txt = CreateFill("Txt", g27, 20, 20, 0, 0);
            AddText(g27Txt.gameObject, "Hardware: Logitech G27 <color=#4CAF50>[CONNECTED]</color>   |   Clutch: <color=#4CAF50>[CALIBRATED]</color>   |   H-Shifter: <color=#4CAF50>[READY]</color>", 18, TextDim, TextAlignmentOptions.Left);

            // Bottom Nav Bar
            var navBar = CreateFixed("NavBar", bg, new Vector2(0.5f, 0), new Vector2(1920, 75), new Vector2(0, 0), new Vector2(0.5f, 0));
            AddImage(navBar.gameObject, new Color(0.05f, 0.07f, 0.09f, 1f));
            var navTxt = CreateFill("Txt", navBar);
            AddText(navTxt.gameObject, "<color=#FFD700>HOME</color>        CAREER        AUTODROME        THEORY        CONTROLS        SETTINGS        EXIT", 22, TextDim, TextAlignmentOptions.Center, FontStyles.Bold);

            SavePrefab(canvas, "MainMenu");
        }

        // ==========================================
        // 3. LESSON CATALOG
        // ==========================================
        private static void BuildLessonCatalog()
        {
            var canvas = CreateCanvas("LessonCatalog");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, BgColor);

            var title = CreateFixed("Title", bg, new Vector2(0, 1), new Vector2(1000, 60), new Vector2(80, -60), new Vector2(0, 1));
            AddText(title.gameObject, "AUTODROME TRAINING CATALOG", 48, TextWhite, TextAlignmentOptions.Left, FontStyles.Bold);

            var subtitle = CreateFixed("Subtitle", bg, new Vector2(0, 1), new Vector2(1000, 35), new Vector2(80, -125), new Vector2(0, 1));
            AddText(subtitle.gameObject, "Official Russian Category B Qualification Exercises (Standard 120x120m Circuit)", 22, TextDim, TextAlignmentOptions.Left);

            // 6 exercises in 3x2 grid
            var grid = CreateFixed("Grid", bg, new Vector2(0.5f, 0.5f), new Vector2(1760, 700), new Vector2(0, -60), new Vector2(0.5f, 0.5f));
            var lg = grid.gameObject.AddComponent<GridLayoutGroup>();
            lg.cellSize = new Vector2(550, 320);
            lg.spacing = new Vector2(55, 40);

            string[] lessons = { 
                "01. SLALOM (S-CURVE)", 
                "02. 90° CORNER TURNS", 
                "03. REVERSE GARAGE 90°", 
                "04. HILL START (10% RAMP)", 
                "05. PARALLEL PARKING", 
                "06. RAILWAY CROSSING & STOP" 
            };

            string[] descriptions = {
                "Continuous maneuvering through slalom cones without stopping or knocking markers.",
                "Precise 90-degree corridor navigation within tight boundaries.",
                "Reverse entry into a confined perpendicular garage stall and wheel stop.",
                "Stop on incline, apply handbrake, restart uphill without rolling back > 30cm.",
                "Reverse parallel docking into standard 3.6 x 8.5m parking pocket.",
                "Approach railway crossing, full stop at STOP marking, check both directions."
            };

            for (int i = 0; i < lessons.Length; i++)
            {
                var card = new GameObject($"Card_{i}").AddComponent<RectTransform>();
                card.SetParent(grid, false);
                AddImage(card.gameObject, PanelColor);
                
                var cImg = CreateFixed("Img", card, new Vector2(0.5f, 1), new Vector2(510, 150), new Vector2(0, -20), new Vector2(0.5f, 1));
                AddImage(cImg.gameObject, CardBgColor);
                var cImgTxt = CreateFill("Txt", cImg);
                AddText(cImgTxt.gameObject, $"[ DIAGRAM: EXERCISE {i+1} ]", 20, TextDim, TextAlignmentOptions.Center);

                var cTitle = CreateFixed("Title", card, new Vector2(0.5f, 1), new Vector2(510, 35), new Vector2(0, -180), new Vector2(0.5f, 1));
                AddText(cTitle.gameObject, lessons[i], 22, AccentGold, TextAlignmentOptions.Left, FontStyles.Bold);

                var cDesc = CreateFixed("Desc", card, new Vector2(0.5f, 1), new Vector2(510, 45), new Vector2(0, -220), new Vector2(0.5f, 1));
                AddText(cDesc.gameObject, descriptions[i], 16, TextDim, TextAlignmentOptions.Left);
                
                var cStart = CreateFixed("BtnStart", card, new Vector2(1, 0), new Vector2(160, 40), new Vector2(-20, 15), new Vector2(1, 0));
                AddImage(cStart.gameObject, AccentCyan);
                var cStartTxt = CreateFill("Txt", cStart);
                AddText(cStartTxt.gameObject, "PRACTICE", 18, BgColor, TextAlignmentOptions.Center, FontStyles.Bold);

                var cStatus = CreateFixed("Status", card, new Vector2(0, 0), new Vector2(300, 40), new Vector2(20, 15), new Vector2(0, 0));
                AddText(cStatus.gameObject, "Status: <color=#4CAF50>PASSED (0 penalty)</color>", 16, TextWhite, TextAlignmentOptions.Left);
            }

            SavePrefab(canvas, "LessonCatalog");
        }

        // ==========================================
        // 4. CONDITIONS SETUP
        // ==========================================
        private static void BuildConditionsSetup()
        {
            var canvas = CreateCanvas("ConditionsSetup");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, BgColor);

            var title = CreateFixed("Title", bg, new Vector2(0, 1), new Vector2(1000, 60), new Vector2(80, -60), new Vector2(0, 1));
            AddText(title.gameObject, "SIMULATION & SESSION SETUP", 48, TextWhite, TextAlignmentOptions.Left, FontStyles.Bold);

            // Left Panel: Configuration Rows (width 1050)
            var panel = CreateFixed("SettingsPanel", bg, new Vector2(0, 1), new Vector2(1050, 720), new Vector2(80, -160), new Vector2(0, 1));
            AddImage(panel.gameObject, PanelColor);

            var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(35, 35, 35, 35);
            vl.spacing = 25;
            vl.childControlWidth = true;
            vl.childControlHeight = false;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            string[,] options = {
                { "TRANSMISSION TYPE", "<  MANUAL 5-SPEED + CLUTCH (G27 H-SHIFTER)  >" },
                { "WEATHER CONDITIONS", "<  CLEAR SKY (DRY ASPHALT)  >" },
                { "TIME OF DAY", "<  DAY (14:00)  >" },
                { "AI TRAFFIC DENSITY", "<  MODERATE (30%)  >" },
                { "PEDESTRIAN BEHAVIOR", "<  ACTIVE (CROSSINGS & JAYWALKING)  >" },
                { "INSTRUCTOR AUDITING", "<  STRICT EXAM RULES (5 PENALTY POINTS LIMIT)  >" }
            };

            for (int i = 0; i < options.GetLength(0); i++)
            {
                var row = new GameObject($"Row_{i}").AddComponent<RectTransform>();
                row.SetParent(panel, false);
                row.sizeDelta = new Vector2(980, 80);
                AddImage(row.gameObject, CardBgColor);
                
                var label = CreateFixed("Label", row, new Vector2(0, 0.5f), new Vector2(380, 60), new Vector2(25, 0), new Vector2(0, 0.5f));
                AddText(label.gameObject, options[i, 0], 22, TextDim, TextAlignmentOptions.Left, FontStyles.Bold);

                var value = CreateFixed("Value", row, new Vector2(1, 0.5f), new Vector2(560, 60), new Vector2(-25, 0), new Vector2(1, 0.5f));
                AddText(value.gameObject, options[i, 1], 22, AccentGold, TextAlignmentOptions.Right, FontStyles.Bold);
            }

            // Right Panel: Summary & Launch (width 650)
            var rightPanel = CreateFixed("RightPanel", bg, new Vector2(1, 1), new Vector2(650, 720), new Vector2(-80, -160), new Vector2(1, 1));
            AddImage(rightPanel.gameObject, PanelColor);

            var rTitle = CreateFixed("Title", rightPanel, new Vector2(0.5f, 1), new Vector2(570, 50), new Vector2(0, -30), new Vector2(0.5f, 1));
            AddText(rTitle.gameObject, "SESSION SUMMARY", 28, AccentCyan, TextAlignmentOptions.Left, FontStyles.Bold);

            var rSummary = CreateFixed("Summary", rightPanel, new Vector2(0.5f, 1), new Vector2(570, 420), new Vector2(0, -90), new Vector2(0.5f, 1));
            AddImage(rSummary.gameObject, CardBgColor);
            var rTxt = CreateFill("Txt", rSummary, 25, 25, 25, 25);
            AddText(rTxt.gameObject, 
                "<b>Location:</b> Official Autodrome Ground\n" +
                "<b>Dimensions:</b> 120 x 120 meters\n" +
                "<b>Vehicle:</b> Training Sedan Category B\n" +
                "<b>Controls:</b> Logitech G27 (900° Steering)\n" +
                "<b>Clutch Stall:</b> Enabled (RPM < 600)\n" +
                "<b>Handbrake Hill Start:</b> Required on 10% ramp\n\n" +
                "<color=#80A0C0>Make sure pedals and steering are calibrated before launching the simulation.</color>", 
                22, TextWhite, TextAlignmentOptions.Left);

            var startBtn = CreateFixed("StartBtn", rightPanel, new Vector2(0.5f, 0), new Vector2(570, 70), new Vector2(0, 40), new Vector2(0.5f, 0));
            AddImage(startBtn.gameObject, AccentGold);
            var startTxt = CreateFill("Txt", startBtn);
            AddText(startTxt.gameObject, "START SIMULATION", 28, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);

            SavePrefab(canvas, "ConditionsSetup");
        }

        // ==========================================
        // 5. G27 CALIBRATION
        // ==========================================
        private static void BuildG27Calibration()
        {
            var canvas = CreateCanvas("G27Calibration");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, BgColor);

            var title = CreateFixed("Title", bg, new Vector2(0, 1), new Vector2(1200, 60), new Vector2(80, -60), new Vector2(0, 1));
            AddText(title.gameObject, "LOGITECH G27 HARDWARE CALIBRATION", 48, TextWhite, TextAlignmentOptions.Left, FontStyles.Bold);

            // Left Side: Wheel Calibration (850 x 700)
            var wheel = CreateFixed("WheelPanel", bg, new Vector2(0, 1), new Vector2(850, 700), new Vector2(80, -160), new Vector2(0, 1));
            AddImage(wheel.gameObject, PanelColor);
            
            var wTitle = CreateFixed("Title", wheel, new Vector2(0.5f, 1), new Vector2(750, 50), new Vector2(0, -30), new Vector2(0.5f, 1));
            AddText(wTitle.gameObject, "STEERING WHEEL (900° LOCK-TO-LOCK)", 28, AccentGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var wGaugeBg = CreateFixed("GaugeBg", wheel, new Vector2(0.5f, 1), new Vector2(750, 80), new Vector2(0, -130), new Vector2(0.5f, 1));
            AddImage(wGaugeBg.gameObject, new Color(0.08f, 0.10f, 0.13f));
            
            // Center mark line
            var wCenter = CreateFixed("CenterLine", wGaugeBg, new Vector2(0.5f, 0.5f), new Vector2(4, 80), new Vector2(0, 0), new Vector2(0.5f, 0.5f));
            AddImage(wCenter.gameObject, Color.white);

            // Wheel indicator bar
            var wIndicator = CreateFixed("Indicator", wGaugeBg, new Vector2(0.5f, 0.5f), new Vector2(180, 60), new Vector2(60, 0), new Vector2(0.5f, 0.5f));
            AddImage(wIndicator.gameObject, AccentCyan);

            var wAngle = CreateFixed("Angle", wheel, new Vector2(0.5f, 1), new Vector2(750, 40), new Vector2(0, -230), new Vector2(0.5f, 1));
            AddText(wAngle.gameObject, "Current Angle: <color=#5999D3>+14.5°</color> (Centered: OK)", 24, TextWhite, TextAlignmentOptions.Center);

            // FFB Settings Box
            var ffbBox = CreateFixed("FFBBox", wheel, new Vector2(0.5f, 1), new Vector2(750, 240), new Vector2(0, -300), new Vector2(0.5f, 1));
            AddImage(ffbBox.gameObject, CardBgColor);
            var ffbTxt = CreateFill("Txt", ffbBox, 30, 30, 25, 25);
            AddText(ffbTxt.gameObject, 
                "<b>Force Feedback Profile:</b> Road Surface + Curb Rumble\n" +
                "<b>Overall FFB Strength:</b> 100%\n" +
                "<b>Damper / Friction:</b> 20% (Realistic hydraulic steering feel)\n" +
                "<b>Centering Spring:</b> Off (Hardware controlled by simulator physics)", 
                22, TextWhite, TextAlignmentOptions.Left);

            var ffbBtn = CreateFixed("TestFFBBtn", wheel, new Vector2(0.5f, 0), new Vector2(320, 55), new Vector2(0, 35), new Vector2(0.5f, 0));
            AddImage(ffbBtn.gameObject, AccentCyan);
            var ffbBtnTxt = CreateFill("Txt", ffbBtn);
            AddText(ffbBtnTxt.gameObject, "TEST FORCE FEEDBACK", 22, BgColor, TextAlignmentOptions.Center, FontStyles.Bold);

            // Right Side: 3 Pedals (850 x 700)
            var pedals = CreateFixed("PedalsPanel", bg, new Vector2(1, 1), new Vector2(850, 700), new Vector2(-80, -160), new Vector2(1, 1));
            AddImage(pedals.gameObject, PanelColor);

            var pTitle = CreateFixed("Title", pedals, new Vector2(0.5f, 1), new Vector2(750, 50), new Vector2(0, -30), new Vector2(0.5f, 1));
            AddText(pTitle.gameObject, "PEDALS (CLUTCH, BRAKE, THROTTLE)", 28, AccentGold, TextAlignmentOptions.Center, FontStyles.Bold);

            string[] pNames = { "CLUTCH", "BRAKE", "THROTTLE" };
            Color[] pColors = { AccentCyan, RedColor, AccentGold };
            float[] pFills = { 0.45f, 0.15f, 0.0f };
            string[] pVals = { "45% (Bite)", "15%", "0%" };

            for (int i = 0; i < 3; i++)
            {
                var pedalCol = CreateFixed($"Pedal_{i}", pedals, new Vector2(0, 1), new Vector2(210, 480), new Vector2(50 + i * 260, -110), new Vector2(0, 1));
                AddImage(pedalCol.gameObject, CardBgColor);

                // Vertical gauge track (140 x 320)
                var track = CreateFixed("Track", pedalCol, new Vector2(0.5f, 1), new Vector2(130, 320), new Vector2(0, -25), new Vector2(0.5f, 1));
                AddImage(track.gameObject, new Color(0.08f, 0.10f, 0.13f));

                // Vertical fill from bottom
                float fillHeight = 320f * pFills[i];
                if (fillHeight > 5f)
                {
                    var fill = CreateFixed("Fill", track, new Vector2(0.5f, 0), new Vector2(130, fillHeight), new Vector2(0, 0), new Vector2(0.5f, 0));
                    AddImage(fill.gameObject, pColors[i]);
                }

                // Clutch Bite Point Marker
                if (i == 0)
                {
                    var biteLine = CreateFixed("BiteLine", track, new Vector2(0.5f, 0.45f), new Vector2(130, 4), new Vector2(0, 0), new Vector2(0.5f, 0.5f));
                    AddImage(biteLine.gameObject, Color.white);
                }

                var pLabel = CreateFixed("Label", pedalCol, new Vector2(0.5f, 0), new Vector2(190, 35), new Vector2(0, 70), new Vector2(0.5f, 0));
                AddText(pLabel.gameObject, pNames[i], 24, TextWhite, TextAlignmentOptions.Center, FontStyles.Bold);

                var pVal = CreateFixed("Val", pedalCol, new Vector2(0.5f, 0), new Vector2(190, 30), new Vector2(0, 30), new Vector2(0.5f, 0));
                AddText(pVal.gameObject, pVals[i], 20, pColors[i], TextAlignmentOptions.Center, FontStyles.Bold);
            }

            var calBtn = CreateFixed("CalibrateBtn", pedals, new Vector2(0.5f, 0), new Vector2(400, 55), new Vector2(0, 35), new Vector2(0.5f, 0));
            AddImage(calBtn.gameObject, AccentGold);
            var calBtnTxt = CreateFill("Txt", calBtn);
            AddText(calBtnTxt.gameObject, "CALIBRATE DEADZONES", 22, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);

            SavePrefab(canvas, "G27Calibration");
        }

        // ==========================================
        // 6. PAUSE MENU
        // ==========================================
        private static void BuildPauseMenu()
        {
            var canvas = CreateCanvas("PauseMenu");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, DarkOverlay);

            var panel = CreateFixed("MenuPanel", bg, new Vector2(0.5f, 0.5f), new Vector2(680, 720), new Vector2(0, 0), new Vector2(0.5f, 0.5f));
            AddImage(panel.gameObject, PanelColor);

            var title = CreateFixed("Title", panel, new Vector2(0.5f, 1), new Vector2(560, 60), new Vector2(0, -40), new Vector2(0.5f, 1));
            AddText(title.gameObject, "SIMULATION PAUSED", 48, AccentGold, TextAlignmentOptions.Center, FontStyles.Bold);
            
            var taskCard = CreateFixed("TaskCard", panel, new Vector2(0.5f, 1), new Vector2(580, 180), new Vector2(0, -120), new Vector2(0.5f, 1));
            AddImage(taskCard.gameObject, CardBgColor);
            var tTxt = CreateFill("Txt", taskCard, 25, 25, 20, 20);
            AddText(tTxt.gameObject, 
                "<b>Current Exercise: Parallel Parking</b>\n\n" +
                "<color=#4CAF50>[OK]</color> Approach marker cones within 0.5m\n" +
                "<color=#4CAF50>[OK]</color> Reverse into pocket at 45° angle\n" +
                "<color=#FFD700>[IN PROGRESS]</color> Align wheels and stop inside box", 
                20, TextWhite, TextAlignmentOptions.Left);

            string[] btns = { "RESUME DRIVING", "RESTART EXERCISE", "CONTROLS & FFB SETTINGS", "EXIT TO MAIN MENU" };
            for(int i = 0; i < btns.Length; i++)
            {
                var btn = CreateFixed($"Btn_{i}", panel, new Vector2(0.5f, 1), new Vector2(580, 65), new Vector2(0, -330 - i * 85), new Vector2(0.5f, 1));
                AddImage(btn.gameObject, i == 0 ? AccentCyan : CardBgColor);
                var txt = CreateFill("Txt", btn);
                AddText(txt.gameObject, btns[i], 24, i == 0 ? BgColor : TextWhite, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            SavePrefab(canvas, "PauseMenu");
        }

        // ==========================================
        // 7. THEORY EXAM
        // ==========================================
        private static void BuildTheoryExam()
        {
            var canvas = CreateCanvas("TheoryExam");
            var bg = CreateFill("Background", canvas.transform);
            AddImage(bg.gameObject, BgColor);

            // Header
            var header = CreateFixed("Header", bg, new Vector2(0.5f, 1), new Vector2(1920, 90), new Vector2(0, 0), new Vector2(0.5f, 1));
            AddImage(header.gameObject, PanelColor);
            
            var hTxt = CreateFixed("Txt", header, new Vector2(0, 0.5f), new Vector2(700, 60), new Vector2(80, 0), new Vector2(0, 0.5f));
            AddText(hTxt.gameObject, "TICKET 12 - QUESTION 14 OF 20", 32, AccentCyan, TextAlignmentOptions.Left, FontStyles.Bold);
            
            // Question indicator strip (20 boxes of fixed size 24x24)
            var strip = CreateFixed("Strip", header, new Vector2(1, 0.5f), new Vector2(650, 30), new Vector2(-80, 0), new Vector2(1, 0.5f));
            for(int i = 0; i < 20; i++) 
            {
                var qb = CreateFixed($"QBox_{i}", strip, new Vector2(0, 0.5f), new Vector2(24, 24), new Vector2(i * 32, 0), new Vector2(0, 0.5f));
                Color boxColor = (i < 13) ? GreenColor : (i == 13 ? AccentGold : new Color(0.25f, 0.30f, 0.35f));
                AddImage(qb.gameObject, boxColor);
                var qTxt = CreateFill("Txt", qb);
                AddText(qTxt.gameObject, (i+1).ToString(), 12, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            // 3D Illustration Viewport
            var ill = CreateFixed("Illustration", bg, new Vector2(0.5f, 1), new Vector2(1400, 420), new Vector2(0, -120), new Vector2(0.5f, 1));
            AddImage(ill.gameObject, CardBgColor);
            var illTxt = CreateFill("Txt", ill);
            AddText(illTxt.gameObject, "[ 3D TRAFFIC SITUATION VIEWPORT ]\n<size=70%>Unregulated 4-way intersection. Blue sedan (Your vehicle) going straight.\nWhite delivery van approaching from your right turning left.</size>", 28, TextDim, TextAlignmentOptions.Center);

            // Question Box
            var qBox = CreateFixed("Question", bg, new Vector2(0.5f, 1), new Vector2(1400, 70), new Vector2(0, -560), new Vector2(0.5f, 1));
            AddImage(qBox.gameObject, PanelColor);
            var qTxt2 = CreateFill("Txt", qBox, 20, 20, 0, 0);
            AddText(qTxt2.gameObject, "WHO HAS THE RIGHT OF WAY AT THIS EQUAL UNREGULATED INTERSECTION?", 28, TextWhite, TextAlignmentOptions.Center, FontStyles.Bold);

            // 4 Answer Options
            string[] answers = {
                "1. You have right of way because you are moving straight without turning.",
                "2. The white delivery van has right of way because it approaches from your right (Rule 13.11).",
                "3. The heavier vehicle with greater tonnage passes first.",
                "4. Right of way is determined by mutual gesture agreement between drivers."
            };

            for (int i = 0; i < answers.Length; i++)
            {
                var aBox = CreateFixed($"Answer_{i}", bg, new Vector2(0.5f, 1), new Vector2(1400, 65), new Vector2(0, -650 - i * 78), new Vector2(0.5f, 1));
                AddImage(aBox.gameObject, i == 1 ? new Color(0.18f, 0.28f, 0.38f) : PanelColor);
                
                var border = CreateFill("Border", aBox, 2, 2, 2, 2);
                AddImage(border.gameObject, i == 1 ? AccentCyan : CardBgColor);
                
                var aTxt = CreateFill("Txt", border, 25, 25, 0, 0);
                AddText(aTxt.gameObject, answers[i], 22, i == 1 ? AccentGold : TextWhite, TextAlignmentOptions.Left);
            }

            // Footer
            var footer = CreateFixed("Footer", bg, new Vector2(0.5f, 0), new Vector2(1400, 50), new Vector2(0, 30), new Vector2(0.5f, 0));
            AddImage(footer.gameObject, PanelColor);
            var fTxt = CreateFill("Txt", footer, 20, 20, 0, 0);
            AddText(fTxt.gameObject, "Exam Timer: <color=#5999D3>16:42 remaining</color>   |   Errors Allowed: 2   |   Current Mistakes: <color=#4CAF50>0</color>", 20, TextDim, TextAlignmentOptions.Center);

            SavePrefab(canvas, "TheoryExam");
        }
    }
}
