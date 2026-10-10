using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SennenKazoku.Core;

namespace SennenKazoku.Game
{
    /// <summary>
    /// 앱 진입점(씬 파일 없이 런타임 구성). 화면 구성은 원작을 따른다:
    ///   HUD(N년가족·날짜) / 집 단면도(원작 그래픽, 가로로 한 바퀴 이어짐, 드래그 스크롤) / 선택 인물 바 / 대화창 / 하단 메뉴.
    /// 가족을 터치하면 상세창. 하단 메뉴: 활쏘기 · 아이템 · 큐피트(저장·기록·가계도·콘텐츠) · 관찰(속도·따라가기).
    /// 규칙·저장 로직은 SennenKazoku.Core, 그래픽은 ArtLibrary(로컬 추출본 또는 임시 도형).
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        // ---- 코어 ----
        PackStore packStore; SaveSystem saves; List<Pack> bundled; ContentCatalog catalog; IGameSession session;
        readonly List<string> contentWarnings = new List<string>();
        readonly ContentUpdater updater = new ContentUpdater();
        ArtLibrary art;

        // ---- UI ----
        RectTransform root, titleRoot, gameRoot, popupRoot;
        RectTransform hud, scene, bar, dialog, menu, figures, choiceHost;
        RawImage house; Image housePlaceholder;
        Text hudText, barName, barPlanned, dlgTitle, dlgSpeaker, dlgText, toastText;
        Image barPortrait, barGauge, cupid, marker, toastBox; Image[] barHearts = new Image[3];
        Button nextBtn;
        LayoutCalculator.Result lay; Vector2 lastScreen; Rect lastSafe; float dp = 1f, hs = 4f;

        // ---- 상태 ----
        int speed = 1; float acc;
        // 원작 측정(에뮬레이터, 사건 없는 날): 하루 = 5,220~5,280 프레임 ≈ 5,250 / 59.7275fps ≈ 87.9초. R 을 누르고 있으면 780~840 프레임 → ×6.48
        const float SecondsPerDay = 5250f / 59.7275f, HoldSpeed = 5250f / 810f;
        bool playing, popupOpen, follow = true; int lastAutoDay; float toastUntil;
        int selectedId = -1; float scrollX;                             // GBA px
        sealed class Actor { public int Id; public float X, Target; public RectTransform Rt; public Image Img; public float Anim, NextMove; public bool Back; public Image Bubble; public Text BubbleText; }
        readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();

        // =============================================================== 시작
        void Start()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            Camera.main.clearFlags = CameraClearFlags.SolidColor; Camera.main.backgroundColor = Color.black;
            art = ArtLibrary.Load();
            LoadContent();
            saves = new SaveSystem(Path.Combine(Application.persistentDataPath, "saves"));
            var cgo = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            root = (RectTransform)cgo.transform;
            var bg = UiKit.Box(root, "bg", new Color32(0xF8, 0xEC, 0xC0, 255)); UiKit.Stretch(bg.rectTransform, 0, 0, 0, 0);
            gameRoot = UiKit.Node(root, "game"); titleRoot = UiKit.Node(root, "title"); popupRoot = UiKit.Node(root, "popup");
            BuildGame();
            toastBox = UiKit.Box(root, "toast", new Color(0.1f, 0.15f, 0.3f, 0.92f));
            toastText = UiKit.Label(toastBox.transform, "t", "", 16, Color.white, TextAnchor.MiddleCenter);
            toastBox.gameObject.SetActive(false);
        }

        // ---- 원작 코드 진행 (로컬 원작 팩: tools/romlift 이 사용자 ROM 에서 만든 규칙·트리·글 대응표. 배포 불가) ----
        // 찾는 곳: persistentDataPath/orig/ (orig_rules.json, orig_rules.json.trees, orig_text.json) 또는
        // Resources/LocalOrig/ (orig_rules.json, orig_trees.bytes, orig_text.json). 없으면 이전 방식(팩 규칙)으로만 시작한다.
        Core.Orig.OrigRules origRules; Core.Orig.OrigText origText; bool origTried;
        bool OrigAvailable()
        {
            string d = Path.Combine(Application.persistentDataPath, "orig");
            return File.Exists(Path.Combine(d, "orig_rules.json")) || Resources.Load<TextAsset>("LocalOrig/orig_rules") != null;
        }
        bool LoadOrig()
        {
            if (origRules != null) return true;
            if (origTried) return false;
            origTried = true;
            try
            {
                string d = Path.Combine(Application.persistentDataPath, "orig");
                string rulesJson, textJson = null; byte[] trees;
                if (File.Exists(Path.Combine(d, "orig_rules.json")))
                {
                    rulesJson = File.ReadAllText(Path.Combine(d, "orig_rules.json"));
                    trees = File.ReadAllBytes(Path.Combine(d, "orig_rules.json.trees"));
                    if (File.Exists(Path.Combine(d, "orig_text.json"))) textJson = File.ReadAllText(Path.Combine(d, "orig_text.json"));
                }
                else
                {
                    rulesJson = Resources.Load<TextAsset>("LocalOrig/orig_rules").text;
                    trees = Resources.Load<TextAsset>("LocalOrig/orig_trees").bytes;
                    var tt = Resources.Load<TextAsset>("LocalOrig/orig_text"); if (tt != null) textJson = tt.text;
                }
                var r = Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(rulesJson)));
                r.TreesText = trees;
                origText = textJson != null ? Core.Orig.OrigText.FromJson(J.Obj(MiniJson.Parse(textJson))) : new Core.Orig.OrigText();
                origRules = r;
                return true;
            }
            catch (Exception e) { contentWarnings.Add("원작 팩 불러오기 실패: " + e.Message); return false; }
        }

        /// <summary>
        /// 이전 원작 카트리지(세이브 영역): 원작은 정상 세이브가 있는 카트리지에서 새로 시작해도 가문의 기록 등을 남긴다.
        /// 지금 세션 또는 자동 저장이 원작 저장이면 그 세이브 영역, 아니면 null(빈 카트리지).
        /// </summary>
        byte[] LastCartridge()
        {
            if (session is Core.Orig.OrigSession os) { os.PrepareSave(); return Core.Orig.OrigSession.CartridgeOf(os.Family); }
            try { if (saves.Exists("auto")) return Core.Orig.OrigSession.CartridgeOf(saves.Load("auto").Family); } catch (Exception) { }
            return null;
        }

        // 가문의 기록에 보일 앱 가문 이름 (원작 기록에는 원작 이름 칸이 남으므로, 앱이 붙인 성을 따로 둔다 — 로컬 파일)
        string RecordNamesPath { get { return Path.Combine(Application.persistentDataPath, "record_names.json"); } }
        Dictionary<string, object> LoadRecordNames()
        {
            try { if (File.Exists(RecordNamesPath)) return J.Obj(MiniJson.Parse(File.ReadAllText(RecordNamesPath))) ?? new Dictionary<string, object>(); } catch (Exception) { }
            return new Dictionary<string, object>();
        }
        string RecordName(Dictionary<string, object> names, Core.Orig.OrigSession.FamilyRecord r)
        {
            object v; if (names.TryGetValue(Core.Orig.OrigSession.RecordKey(r), out v) && v is string sv && sv.Length > 0) return sv;
            var o = Core.Orig.OrigSession.RecordOrigName(origText, r); return o.Length > 0 ? o : "?";
        }
        string RecordLine(Dictionary<string, object> names, Core.Orig.OrigSession.FamilyRecord r, Core.Orig.OrigSession os)
        {
            string rank = origText != null ? origText.UiText(380 + r.Rank, r.Rank + "위") : r.Rank + "위";
            string type = os != null ? os.FamilyTypeName(r.Type) : "";
            return rank + "  " + RecordName(names, r) + "가문  " + r.Years + "년 가족  " + type
                + "\n      " + r.StartY + "년 " + r.StartM + "월 " + r.StartD + "일 ~ " + r.EndY + "년 " + r.EndM + "월 " + r.EndD + "일 · 역대 가족 " + r.Members + "명";
        }

        // 플레이어 이름 (결과 문구의 플레이어 이름 자리 — 원작은 카트리지 0x0202C660 에 둔다). 앱은 로컬 파일에 두고 새 가족마다 넣는다.
        string PlayerNamePath { get { return Path.Combine(Application.persistentDataPath, "player_name.txt"); } }
        string SavedPlayerName() { try { return File.Exists(PlayerNamePath) ? File.ReadAllText(PlayerNamePath).Trim() : ""; } catch (Exception) { return ""; } }

        /// <summary>새 원작 가족을 연 뒤: 저장해 둔 플레이어 이름을 넣고, 없으면 한 번 묻는다.</summary>
        void ApplyPlayerName()
        {
            if (!(session is Core.Orig.OrigSession os)) return;
            var n = SavedPlayerName();
            if (n.Length > 0) os.PlayerName = n; else OpenPlayerName();
        }

        void OpenPlayerName()
        {
            if (!(session is Core.Orig.OrigSession os)) return;
            string name = os.PlayerName;
            var body = Window("플레이어 이름", 260); float w = BodyW(body);
            var q = UiKit.Label(body, "q", "신님에게 감사가 도착할 때 부를 이름이에요.", Px(14), UiKit.Ink, TextAnchor.MiddleLeft); UiKit.SetPx(q.rectTransform, 0, 0, w, Px(30));
            var f = UiKit.Input(body, "name", name, Px(18), 6, v => name = v.Trim());
            UiKit.SetPx(f.GetComponent<RectTransform>(), 0, Px(36), w, Px(52));
            f.ActivateInputField();
            NextBtn(body, Px(104), "결정", () =>
            {
                if (name.Length == 0) { Toast("이름을 입력해 줘"); return; }
                os.PlayerName = name;
                try { File.WriteAllText(PlayerNamePath, name); } catch (Exception) { }
                ClosePopup(); SaveSlot("auto", true);
            });
        }

        /// <summary>종합 진단 (원작 0x0806614C 값, 줄 순서·글은 원작 진단 패널 0x08061770 과 같게: 종합 진단 / 후계자 / 풍요로움 / 유대).</summary>
        void OpenDiagnosis()
        {
            if (!(session is Core.Orig.OrigSession os)) return;
            int[] d; try { d = os.Diagnosis(); } catch (Exception e) { Toast("진단 실패: " + e.Message); return; }
            var body = Window(origText.UiText(338, "종합 진단"), 300); float w = BodyW(body), y = 0;
            string[] label = { origText.UiText(338, "종합 진단"), origText.UiText(339, "후계자"), origText.UiText(614, "풍요로움"), origText.UiText(341, "유대") };
            int[] val = { d[3], d[0], d[1], d[2] };
            for (int i = 0; i < 4; i++)
            {
                Line(body, label[i], origText.UiText(342 + 1, "…") + "  " + os.DiagnosisWord(val[i]), 0, y, w); y += Px(40);
            }
        }

        /// <summary>제목 화면의 가문의 기록 (카트리지에 남은 상위 3).</summary>
        void OpenRecords()
        {
            if (!LoadOrig()) { Toast("원작 팩이 없어 기록을 볼 수 없습니다"); return; }
            var recs = Core.Orig.OrigSession.ReadRecords(origRules, LastCartridge());
            var body = Window("가문의 기록", 420); float w = BodyW(body);
            var names = LoadRecordNames(); var os = session as Core.Orig.OrigSession;
            string txt = recs.Count == 0 ? "아직 기록이 없습니다." : string.Join("\n\n", recs.ConvertAll(r => RecordLine(names, r, os)));
            var l = UiKit.Label(body, "recs", txt, Px(14), UiKit.Ink, TextAnchor.UpperLeft); UiKit.SetPx(l.rectTransform, 0, 0, w, BodyH(body));
        }

        /// <summary>원작 코드로 새 가족 — 원작 "신이 추천하는 가족" 경로 그대로(OrigNewGame).</summary>
        void BeginOrig()
        {
            if (!LoadOrig()) { Toast("원작 팩을 불러오지 못했습니다"); return; }
            try
            {
                session = Core.Orig.OrigSession.NewRecommended(origRules, origText, "", OrigSeed(), cartridge: LastCartridge());
                EnterGame(); Toast("원작 규칙으로 시작합니다 (신님이 추천하는 가족)"); ApplyPlayerName();
            }
            catch (Exception e) { Toast("원작 진행 시작 실패: " + e.Message); session = null; }
        }

        void LoadContent()
        {
            bundled = new List<Pack>();
            foreach (var folder in new[] { "BundledPacks", "LocalPacks" })
                foreach (var ta in Resources.LoadAll<TextAsset>(folder))
                {
                    var errs = new List<string>(); var p = Pack.Load(ta.text, errs);
                    if (p == null) contentWarnings.Add("번들 팩 " + ta.name + " 오류: " + string.Join("; ", errs)); else bundled.Add(p);
                }
            packStore = new PackStore(Path.Combine(Application.persistentDataPath, "content"));
            catalog = packStore.LoadCatalog(bundled, contentWarnings) ?? new ContentCatalog();
        }

        int Px(float d) { return Mathf.RoundToInt(d * dp); }

        // =============================================================== 레이아웃
        void ApplyLayout()
        {
            float w = Screen.width, h = Screen.height; var sa = Screen.safeArea;
            lay = LayoutCalculator.Compute(w, h, sa.x, h - (sa.y + sa.height), sa.width, sa.height, 160f * (w / 360f));
            dp = lay.Dp; hs = lay.HouseScale;
            foreach (var rt in new[] { titleRoot, gameRoot, popupRoot }) UiKit.SetPx(rt, 0, 0, w, h);
            UiKit.SetPx(hud, lay.TopBar); UiKit.SetPx(scene, lay.Scene); UiKit.SetPx(menu, lay.Controls);
            LayoutHud(); LayoutBar(); LayoutDialog(); LayoutMenu();
            lastScreen = new Vector2(w, h); lastSafe = sa;
            if (playing) { RebuildActors(); RefreshAll(); }
            if (titleRoot.gameObject.activeSelf || !playing) ShowTitle();
        }

        // =============================================================== 화면 뼈대 (사용자 스케치 기준)
        //  ┌ HUD: 집 아이콘 · "○○가 소지금" · 무드 포인트 · 날짜 │ 오른쪽 위 코너 = 배속(누르고 있는 동안, 원작 R 버튼)
        //  ├ 집 장면: 가족(머리 위 ! / 감정 말풍선) · 큐피트(게임 팁 말풍선) · 왼쪽 아래 코너 = 아이템, 오른쪽 아래 코너 = 활
        //  ├ 인물 패널: ◀ 초상(→상세)·이름 │ 체·지·매·운 등급 │ 글상자(대사·선택지) │ 사건 결과(무드↑ 하트↓ …) ▶
        //  └ 탭: 상세 · 가계도 · 사건 · 설정
        Button[] menuBtns = new Button[4];
        RectTransform panel; Text rankText, effectText, tipText; Image tipBox, itemCorner, bowCorner, speedCorner;
        Text speedText; bool holdingSpeed; string[] tips; int tipIndex; float tipUntil;

        void BuildGame()
        {
            hud = UiKit.Node(gameRoot, "hud"); scene = UiKit.Node(gameRoot, "scene");
            panel = UiKit.Node(gameRoot, "panel"); bar = UiKit.Node(panel, "bar"); dialog = UiKit.Node(panel, "dialog");
            menu = UiKit.Node(gameRoot, "menu");

            var hb = UiKit.Box(hud, "bg", new Color32(0xF6, 0xE3, 0x9A, 255)); UiKit.Stretch(hb.rectTransform, 0, 0, 0, 0);
            hudIcon = UiKit.Box(hud, "icon", Color.white, art.Ui("hud_family_a")); hudIcon.preserveAspect = true;
            if (hudIcon.sprite == null) { hudIcon.sprite = UiKit.Circle; hudIcon.color = new Color32(0xE8, 0x70, 0x40, 255); }
            hudText = UiKit.Label(hud, "t", "", 16, new Color32(0x1E, 0x46, 0x9A, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            hudSub = UiKit.Label(hud, "sub", "", 13, UiKit.Ink, TextAnchor.MiddleLeft);

            // 집: RawImage 의 uvRect 로 가로 무한 스크롤
            var hgo = new GameObject("house", typeof(RectTransform), typeof(RawImage)); hgo.transform.SetParent(scene, false);
            house = hgo.GetComponent<RawImage>(); UiKit.Stretch(house.rectTransform, 0, 0, 0, 0);
            house.texture = art.HouseTexture(); house.raycastTarget = true;
            if (house.texture == null) { house.color = new Color32(0xBF, 0xE3, 0xF2, 255); }
            var drag = hgo.AddComponent<HouseDrag>();
            drag.OnBegin = () => { follow = false; };
            drag.OnDragX = dx => { scrollX -= dx / hs; };
            figures = UiKit.Node(scene, "figures");
            marker = UiKit.Box(scene, "marker", Color.white, art.Ui("marker_select"));
            if (marker.sprite == null) marker.color = new Color32(0x50, 0xC0, 0x50, 255);
            // 큐피트 + 게임 팁 (팁 문구는 새로 쓴 안내 — 원작 문구 아님)
            cupid = UiKit.Box(scene, "cupid", Color.white, art.Cupid(0)); cupid.preserveAspect = true; cupid.raycastTarget = true;
            if (cupid.sprite == null) { cupid.sprite = UiKit.Circle; cupid.color = new Color32(0xFF, 0xE0, 0x70, 255); }
            cupid.gameObject.AddComponent<Button>().onClick.AddListener(() => { tipIndex++; tipUntil = Time.time + 8f; RefreshTip(); });
            tipBox = UiKit.Box(scene, "tip", new Color(1, 1, 1, 0.94f));
            tipText = UiKit.Label(tipBox.transform, "t", "", 13, UiKit.Ink, TextAnchor.MiddleCenter);
            tips = new[] {
                "가족을 누르면 자세히 볼 수 있어!", "머리 위에 ! 가 뜨면 곧 무슨 일이 생겨",
                "오른쪽 위 배속을 누르고 있으면 시간이 빨리 가", "힘내라의 화살을 쏘면 열중 게이지가 매일 쑥쑥 올라가",
                "진정해의 화살을 쏘면 열중 게이지가 매일 뚝뚝 떨어져", "게이지가 꽉 차거나 바닥나면 무슨 일이 생겨",
                "◀ ▶ 로 지켜볼 사람을 바꿔 봐", "아래 줄은 게이지가 255·0 이 되면 바뀌는 것",
                "집을 좌우로 끌면 다른 방도 볼 수 있어" };
            // 장면 코너 버튼: 왼쪽 아래 아이템, 오른쪽 아래 활, 오른쪽 위 배속
            itemCorner = Corner(scene, "아이템", new Color32(0xF0, 0xA0, 0x40, 235), () => OpenTools("item"));
            bowCorner = Corner(scene, "활", new Color32(0xE8, 0x50, 0x70, 235), () => OpenTools("arrow"));
            var bow = art.Ui("btn_bow");
            if (bow != null) { var ic = UiKit.Box(bowCorner.transform, "icon", Color.white, bow); ic.preserveAspect = true; bowIcon = ic; }
            speedCorner = UiKit.Box(hud, "speed", new Color32(0x3A, 0x6E, 0xC8, 255)); speedCorner.raycastTarget = true;
            speedText = UiKit.Label(speedCorner.transform, "t", "배속", 14, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            var hold = speedCorner.gameObject.AddComponent<HoldButton>();
            hold.OnDown = () => { holdingSpeed = true; RefreshHud(); };
            hold.OnUp = () => { holdingSpeed = false; RefreshHud(); };

            // 인물 패널
            var pbg = UiKit.Box(panel, "bg", new Color32(0x10, 0x4E, 0x6E, 255)); UiKit.Stretch(pbg.rectTransform, 0, 0, 0, 0); pbg.transform.SetAsFirstSibling();
            barPortrait = UiKit.Box(bar, "portrait", Color.white); barPortrait.preserveAspect = true; barPortrait.raycastTarget = true;
            barPortrait.gameObject.AddComponent<Button>().onClick.AddListener(() => { var p = Sel(); if (p != null) OpenDetail(p); });
            nameBg = UiKit.Box(bar, "nameBg", Color.white);
            barName = UiKit.Label(bar, "name", "", 16, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            rankText = UiKit.Label(bar, "ranks", "", 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold); rankText.supportRichText = true;
            for (int i = 0; i < 3; i++) { barHearts[i] = UiKit.Box(bar, "heart" + i, Color.white); barHearts[i].preserveAspect = true; }
            barPlanned = UiKit.Label(bar, "planned", "", 13, Color.white, TextAnchor.MiddleLeft);
            // 열중 게이지 (현재 관심 몰입도 0~255): 힘내라의 화살 +95, 진정해의 화살 → 0
            immLabel = UiKit.Label(bar, "immL", "열중", 15, new Color32(0xFF, 0xC0, 0x40, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            immBg = UiKit.Box(bar, "immBg", new Color32(0x06, 0x26, 0x38, 255));
            immFill = UiKit.Box(immBg.transform, "immFill", new Color32(0xF0, 0x80, 0x30, 255));
            immVal = UiKit.Label(immBg.transform, "immV", "", 12, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            barGauge = UiKit.Box(bar, "gauge", Color.white); barGauge.preserveAspect = true;
            prevBtn = UiKit.Btn(panel, "prev", "◀", 18, new Color32(0x0A, 0x38, 0x50, 255), Color.white, () => CycleSel(-1));
            nextSelBtn = UiKit.Btn(panel, "nextSel", "▶", 18, new Color32(0x0A, 0x38, 0x50, 255), Color.white, () => CycleSel(1));

            // 글상자 (원작 대화창: 흰 바탕, 파란 테두리)
            var dbg = UiKit.Box(dialog, "frame", new Color32(0x3A, 0x6E, 0xC8, 255)); UiKit.Stretch(dbg.rectTransform, 2, 2, 2, 2);
            var din = UiKit.Box(dialog, "inner", Color.white); UiKit.Stretch(din.rectTransform, 5, 5, 5, 5);
            dlgTitle = UiKit.Label(dialog, "title", "", 12, new Color32(0x9C, 0x52, 0x20, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            dlgSpeaker = UiKit.Label(dialog, "speaker", "", 14, new Color32(0x1E, 0x46, 0xC8, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            dlgText = UiKit.Label(dialog, "text", "", 16, UiKit.Ink, TextAnchor.UpperLeft); dlgText.supportRichText = true;
            choiceHost = UiKit.Node(dialog, "choices");
            nextBtn = UiKit.Btn(dialog, "next", "▼", 18, new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, OnNext);
            effectText = UiKit.Label(panel, "effects", "", 17, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold); effectText.supportRichText = true;

            // 하단 탭
            var mb = UiKit.Box(menu, "bg", new Color32(0xE8, 0xD0, 0x90, 255)); UiKit.Stretch(mb.rectTransform, 0, 0, 0, 0);
            string[] labels = { "상세", "가계도", "사건", "설정" };
            Action[] acts = { () => { var p = Sel(); if (p != null) OpenDetail(p); }, () => TextWindow("가계도", TreeText()), () => TextWindow("사건 기록", LogText()), OpenSettings };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                menuBtns[i] = UiKit.Btn(menu, "m" + i, labels[i], 16, new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => acts[k]());
            }
        }
        Image nameBg, bowIcon, hudIcon, immBg, immFill; Text hudSub, immLabel, immVal; Button prevBtn, nextSelBtn; float shownImm = -1;

        Image Corner(RectTransform parent, string label, Color c, Action onClick)
        {
            var b = UiKit.Box(parent, "corner_" + label, c, UiKit.Circle); b.raycastTarget = true;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
            var t = UiKit.Label(b.transform, "t", label, 14, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.5f);
            return b;
        }

        void LayoutHud()
        {
            float h = lay.TopBar.H, w = lay.TopBar.W, pad = Px(6), corner = Mathf.Min(Px(76), h * 1.6f);
            UiKit.SetPx(hudIcon.rectTransform, pad, pad, h - 2 * pad, h - 2 * pad);
            float x = h + pad;
            hudText.fontSize = Px(15); hudSub.fontSize = Px(12);
            UiKit.SetPx(hudText.rectTransform, x, 0, w - x - corner, h * 0.55f);
            UiKit.SetPx(hudSub.rectTransform, x, h * 0.5f, w - x - corner, h * 0.5f);
            // 배속 코너: HUD 오른쪽 위에서 장면 쪽으로 걸쳐 둥근 모서리
            UiKit.SetPx(speedCorner.rectTransform, w - corner, 0, corner, h);
            UiKit.Stretch(speedText.rectTransform, 0, 0, 0, 0); speedText.fontSize = Px(14);
        }

        void LayoutBar()
        {
            // 패널 = 인물 줄(FamilyStrip) + 글상자(EventPanel) 를 합친 영역
            var pr = new RectPx { X = lay.FamilyStrip.X, Y = lay.FamilyStrip.Y, W = lay.FamilyStrip.W, H = lay.FamilyStrip.H + lay.EventPanel.H };
            UiKit.SetPx(panel, pr);
            float w = pr.W, H = pr.H, ar = Px(30), pad = Px(6), effH = Px(30);
            UiKit.SetPx(prevBtn.GetComponent<RectTransform>(), 0, 0, ar, H - effH);
            UiKit.SetPx(nextSelBtn.GetComponent<RectTransform>(), w - ar, 0, ar, H - effH);
            float leftW = Mathf.Min(Px(96), w * 0.26f), inner = w - 2 * ar;
            UiKit.SetPx(bar, ar, 0, inner, H - effH);
            float ph = Mathf.Min(leftW, (H - effH) * 0.55f);
            UiKit.SetPx(barPortrait.rectTransform, pad, pad, leftW - pad, ph);
            UiKit.SetPx(nameBg.rectTransform, pad, pad + ph + Px(2), leftW - pad, Px(26));
            UiKit.SetPx(barName.rectTransform, pad, pad + ph + Px(2), leftW - pad, Px(26)); barName.fontSize = Px(14);
            float hsz = Mathf.Min(Px(22), (leftW - pad) / 3f - Px(2)), hy = pad + ph + Px(32);
            for (int i = 0; i < 3; i++) UiKit.SetPx(barHearts[i].rectTransform, pad + i * (hsz + Px(2)), hy, hsz, hsz);
            UiKit.SetPx(barGauge.rectTransform, pad, hy + hsz + Px(4), leftW - pad, Px(18));
            float rx = leftW + pad * 2, rw = inner - rx - pad;
            UiKit.SetPx(rankText.rectTransform, rx, pad, rw, Px(30)); rankText.fontSize = Px(20);
            UiKit.SetPx(immLabel.rectTransform, rx, pad + Px(32), Px(44), Px(24)); immLabel.fontSize = Px(15);
            UiKit.SetPx(immBg.rectTransform, rx + Px(44), pad + Px(34), rw - Px(44), Px(20));
            immVal.fontSize = Px(12); UiKit.Stretch(immVal.rectTransform, 0, 0, 0, 0);
            UiKit.SetPx(barPlanned.rectTransform, rx, pad + Px(58), rw, Px(18)); barPlanned.fontSize = Px(12);
            UiKit.SetPx(dialog, ar + rx, pad + Px(80), rw, H - effH - Px(80) - pad * 2);
            UiKit.SetPx(effectText.rectTransform, ar, H - effH, inner, effH); effectText.fontSize = Px(16);
        }

        void LayoutDialog()
        {
            float w = dialog.sizeDelta.x, h = dialog.sizeDelta.y, pad = Px(10);
            dlgTitle.fontSize = Px(11); dlgSpeaker.fontSize = Px(13); dlgText.fontSize = Px(15);
            UiKit.SetPx(dlgTitle.rectTransform, pad, Px(6), w - 2 * pad, Px(16));
            UiKit.SetPx(dlgSpeaker.rectTransform, pad, Px(22), w - 2 * pad, Px(18));
            UiKit.SetPx(dlgText.rectTransform, pad, Px(40), w - 2 * pad, h - Px(40) - Px(8));
            UiKit.SetPx(nextBtn.GetComponent<RectTransform>(), w - Px(52), h - Px(46), Px(44), Px(40));
        }

        void LayoutMenu()
        {
            float w = lay.Controls.W, h = lay.Controls.H, pad = Px(4), bw = (w - 5 * pad) / 4f, bh = h - 2 * pad;
            for (int i = 0; i < 4; i++)
            {
                UiKit.SetPx(menuBtns[i].GetComponent<RectTransform>(), pad + i * (bw + pad), pad, bw, bh);
                menuBtns[i].GetComponentInChildren<Text>().fontSize = Px(16);
            }
            // 장면 코너 버튼(사분원)과 팁 말풍선
            float sw = lay.Scene.W, sh = lay.Scene.H, r = Mathf.Min(Px(84), sh * 0.42f);
            UiKit.SetPx(itemCorner.rectTransform, -r, sh - r, 2 * r, 2 * r);
            UiKit.SetPx(bowCorner.rectTransform, sw - r, sh - r, 2 * r, 2 * r);
            foreach (var c in new[] { itemCorner, bowCorner })
            {
                var t = c.transform.Find("t").GetComponent<Text>(); t.fontSize = Px(14);
                bool left = c == itemCorner;
                UiKit.SetPx(t.rectTransform, left ? r : r * 0.15f, r * 0.2f, r * 0.85f, r * 0.6f);
            }
            if (bowIcon != null) UiKit.SetPx(bowIcon.rectTransform, r * 0.3f, r * 0.05f, r * 0.5f, r * 0.3f);
            tipText.fontSize = Px(13);
        }

        // =============================================================== 타이틀
        /// <summary>
        /// "맞은 화살": 원작 세션은 레코드 +0x60 의 화살 종류(실기 상세 화면과 같음 — 힘내라·진정해는 여기 보이지 않는다).
        /// 옛 경로는 힘내라·진정해 효과 중일 때 그 이름.
        /// </summary>
        static string HitArrowName(Person p)
        {
            if (p.HitArrow >= 0)
            {
                var t = Interventions.Tools.Find(x => x.Kind == "arrow" && x.OrigSlot == p.HitArrow);
                return t != null ? t.Name : "화살 #" + p.HitArrow;
            }
            if (p.HitArrow == -1) return null;
            return (p.ArrowFlags & 1) != 0 && !string.IsNullOrEmpty(p.ArrowId) ? Interventions.Find(p.ArrowId).Name + " (지금 관심사가 끝날 때까지)" : null;
        }

        float titleAt;
        /// <summary>원작 난수 시작값 (OrigSession.BootSeed): 부팅 주사선 대신 시각, 타이틀에서 돈 횟수 대신 타이틀에 머문 프레임 수(60/초).</summary>
        uint OrigSeed() { return Core.Orig.OrigSession.BootSeed((uint)(DateTime.UtcNow.Ticks % 228), (uint)Mathf.Max(0f, (Time.realtimeSinceStartup - titleAt) * 60f)); }

        void ShowTitle()
        {
            titleAt = Time.realtimeSinceStartup;
            playing = false; popupOpen = false; ClearPopup();
            gameRoot.gameObject.SetActive(false); titleRoot.gameObject.SetActive(true); Clear(titleRoot);
            if (lay == null) return;
            float x = lay.Safe.X, w = lay.Safe.W, y = lay.Safe.Y + lay.Safe.H * 0.06f, pad = Px(28), bw = w - 2 * pad, bh = Px(56);
            if (art.Available)
            {
                var hgo = new GameObject("titleHouse", typeof(RectTransform), typeof(RawImage)); hgo.transform.SetParent(titleRoot, false);
                var ri = hgo.GetComponent<RawImage>(); ri.texture = art.HouseTexture();
                float th = lay.Safe.H * 0.32f; UiKit.SetPx(ri.rectTransform, x, lay.Safe.Y + lay.Safe.H * 0.36f, w, th);
                ri.uvRect = new Rect(0, 0, (w / (th / 160f)) / art.HouseWidth, 1);
                titleHouse = ri;
            }
            var t = UiKit.Label(titleRoot, "t", "천년가족", Px(44), new Color32(0xE8, 0x50, 0x70, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color32(0x30, 0x80, 0xD0, 255); o.effectDistance = new Vector2(Px(2), -Px(2));
            UiKit.SetPx(t.rectTransform, x, y, w, Px(64)); y += Px(64);
            var sub = UiKit.Label(titleRoot, "s", "MILLENNIAL FAMILY · 모바일 재구현 (비공식)\n" + (art.Available ? "원작 그래픽: 로컬 추출본 사용 중 (배포 불가)" : "임시 그래픽 (원작 그래픽 미추출)"), Px(12), UiKit.Ink, TextAnchor.UpperCenter);
            UiKit.SetPx(sub.rectTransform, x, y, w, Px(40));
            y = lay.Safe.Y + lay.Safe.H * 0.72f;
            bool hasSave = saves.Exists("auto");
            var c = UiKit.Btn(titleRoot, "cont", hasSave ? "이어하기" : "이어하기 (저장 없음)", Px(18), hasSave ? new Color32(0x3A, 0x6E, 0xC8, 255) : (Color)Color.gray, Color.white, () => { if (hasSave) LoadSlot("auto"); });
            UiKit.SetPx(c.GetComponent<RectTransform>(), x + pad, y, bw, bh); y += bh + Px(10);
            var n = UiKit.Btn(titleRoot, "new", "처음부터", Px(18), new Color32(0xE8, 0x70, 0x40, 255), Color.white, StartNew);
            UiKit.SetPx(n.GetComponent<RectTransform>(), x + pad, y, bw, bh); y += bh + Px(10);
            var l = UiKit.Btn(titleRoot, "load", "저장 슬롯", Px(16), new Color32(0x10, 0x4E, 0x6E, 255), Color.white, () => OpenSaveSlots());
            UiKit.SetPx(l.GetComponent<RectTransform>(), x + pad, y, (bw - Px(10)) / 2, Px(48));
            var rc = UiKit.Btn(titleRoot, "records", "가문의 기록", Px(16), new Color32(0x10, 0x4E, 0x6E, 255), Color.white, OpenRecords);
            UiKit.SetPx(rc.GetComponent<RectTransform>(), x + pad + (bw + Px(10)) / 2, y, (bw - Px(10)) / 2, Px(48));
        }
        RawImage titleHouse;

        void StartNew()
        {
            if (!art.Parts.Available) { if (OrigAvailable()) BeginOrig(); else BeginNew(false); return; }
            // 원작 도입부의 신님 질문 (원작 문구 확인됨: docs/07)
            playing = false;
            var body = Window("신님", 300); float w = BodyW(body);
            var q = UiKit.Label(body, "q", "내가 아는 가족을 지켜봐 주지 않겠느냐?", Px(16), UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.SetPx(q.rectTransform, 0, 0, w, Px(60));
            bool orig = OrigAvailable();
            var a = UiKit.Btn(body, "yes", "네, 알겠습니다!", Px(16), new Color32(0xE8, 0x70, 0x40, 255), Color.white, () => { if (orig) BeginOrig(); else BeginNew(false); });
            UiKit.SetPx(a.GetComponent<RectTransform>(), 0, Px(70), w, Px(52));
            var b = UiKit.Btn(body, "no", "어… 잠깐만요… (내가 아는 가족)", Px(16), new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => BeginNew(true));
            UiKit.SetPx(b.GetComponent<RectTransform>(), 0, Px(132), w, Px(52));
        }

        /// <summary>
        /// 새 가족. custom=false: 신님이 아는 가족 — 구성원 외형을 원작 갤러리 프리셋에서 고른다(원작의 선택 규칙은 미해명, 임시).
        /// custom=true: 내가 아는 가족 — 구성원마다 원작 순서(체형 → 캐릭터 → 색)로 외형을 고른다.
        /// 미구현: 가족 구성·이름·생일·혈액형·성격·능력 순위·직업 입력(현재는 시작 가족 구성을 그대로 쓴다).
        /// </summary>
        void BeginNew(bool custom)
        {
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            var f = art.StartFamily != null ? NewGame.FromOriginal(art.StartFamily, seed) : NewGame.Create(seed);
            session = new GameSession(f, catalog);
            if (art.Parts.Available) foreach (var p in f.Members) p.Look = DefaultLook(p);
            if (!custom) { EnterGame(); Toast(art.StartFamily != null ? "원작 시작 가족으로 시작합니다" : "새 가족이 시작되었습니다"); return; }
            session = null; setup = new FamilySetup(); OpenSetupStart();
        }

        // =============================================================== 내가 아는 가족 (원작 입력 순서)
        // 시작 연월일 → 가족 성 → 가족 구성 → 구성원마다 이름·성별·생일·혈액형·성격·능력 순위·직업 → 캐릭터(7세 이상) → 시작
        FamilySetup setup;
        readonly int[] childSlots = new int[4];          // 0 없음 · 1 아들 · 2 딸
        bool[] adultSlots = new bool[4];                 // 할아버지 · 할머니 · 아버지 · 어머니
        static readonly string[] RoleNames = { "할아버지", "할머니", "아버지", "어머니", "아들", "딸" };
        // 원작 직업 이름: 원작 직업 화면에서 읽은 것(Core.Orig.OrigJobs). 모르는 번호는 "직업 #번호"
        static readonly Dictionary<int, string> KnownJobs = Core.Orig.OrigJobs.Names;

        void OpenSetupStart()
        {
            var body = Window("시작 연월일을 알려줘!", 360); float w = BodyW(body), rh = Px(48), y = Px(8);
            int d = setup.StartDay, Y = GameDate.Year(d), M = GameDate.Month(d), D = GameDate.Day(d);
            Stepper(body, "연", Y + "년", 0, y, w, rh, k => { setup.StartDay = GameDate.Make(Mathf.Clamp(Y + k, 1900, 2999), M, D); OpenSetupStart(); }); y += rh;
            Stepper(body, "연 ±10", "", 0, y, w, rh, k => { setup.StartDay = GameDate.Make(Mathf.Clamp(Y + 10 * k, 1900, 2999), M, D); OpenSetupStart(); }); y += rh;
            Stepper(body, "월", M + "월", 0, y, w, rh, k => { setup.StartDay = GameDate.Make(Y, Wrap(M - 1 + k, 12) + 1, D); OpenSetupStart(); }); y += rh;
            Stepper(body, "일", D + "일", 0, y, w, rh, k => { setup.StartDay = GameDate.Make(Y, M, Wrap(D - 1 + k, DateTime.DaysInMonth(Y, M)) + 1); OpenSetupStart(); }); y += rh + Px(8);
            NextBtn(body, y, "다음", OpenSetupSurname);
        }

        void OpenSetupSurname()
        {
            var body = Window("가족 성을 알려줘!", 260); float w = BodyW(body);
            var f = UiKit.Input(body, "sur", setup.Surname, Px(18), 8, v => setup.Surname = v.Trim());
            UiKit.SetPx(f.GetComponent<RectTransform>(), 0, Px(10), w, Px(52));
            var l = UiKit.Label(body, "l", GameDate.Format(setup.StartDay) + "부터 지켜볼 집", Px(13), UiKit.Ink, TextAnchor.MiddleLeft); UiKit.SetPx(l.rectTransform, 0, Px(70), w, Px(24));
            NextBtn(body, Px(100), "다음", () => { if (setup.Surname.Length == 0) { Toast("가족 성을 입력해 줘"); return; } OpenSetupComposition(); });
        }

        void OpenSetupComposition()
        {
            var body = Window("가족 구성을 알려줘!", 520); float w = BodyW(body), bh = Px(48), half = (w - Px(8)) / 2f, y = 0;
            for (int i = 0; i < 4; i++)
            {
                int k = i; float bx = (i % 2) * (half + Px(8)), by = (i / 2) * (bh + Px(30));
                var b = UiKit.Btn(body, "a" + i, RoleNames[i], Px(15), adultSlots[i] ? new Color32(0x60, 0xB0, 0xF0, 255) : new Color32(0xD0, 0xD0, 0xD8, 255), UiKit.Ink, () => { adultSlots[k] = !adultSlots[k]; OpenSetupComposition(); });
                UiKit.SetPx(b.GetComponent<RectTransform>(), bx, by, half, bh);
            }
            y = 2 * (bh + Px(30)) + Px(4);
            float cw = (w - 3 * Px(6)) / 4f;
            for (int i = 0; i < 4; i++)
            {
                int k = i; string t = childSlots[i] == 0 ? "아들·딸" : childSlots[i] == 1 ? "아들" : "딸";
                var b = UiKit.Btn(body, "c" + i, t, Px(14), childSlots[i] != 0 ? new Color32(0x60, 0xB0, 0xF0, 255) : new Color32(0xD0, 0xD0, 0xD8, 255), UiKit.Ink, () => { childSlots[k] = (childSlots[k] + 1) % 3; OpenSetupComposition(); });
                UiKit.SetPx(b.GetComponent<RectTransform>(), i * (cw + Px(6)), y, cw, bh);
            }
            y += bh + Px(16);
            var note = UiKit.Label(body, "n", "칸을 눌러 고르고, 자녀 칸은 누를 때마다 아들 → 딸 → 없음", Px(12), UiKit.Ink, TextAnchor.UpperLeft); UiKit.SetPx(note.rectTransform, 0, y, w, Px(36)); y += Px(40);
            NextBtn(body, y, "OK", () =>
            {
                var old = new List<MemberSetup>(setup.Members); setup.Members.Clear();
                Func<FamilyRole, int, MemberSetup> reuse = (r, g) => { var m = old.Find(o => o.Role == r && o.Gender == g); if (m != null) { old.Remove(m); setup.Members.Add(m); return m; } return setup.Add(r, g); };
                FamilyRole[] ar = { FamilyRole.Grandfather, FamilyRole.Grandmother, FamilyRole.Father, FamilyRole.Mother };
                for (int i = 0; i < 4; i++) if (adultSlots[i]) reuse(ar[i], i == 1 || i == 3 ? 1 : 0);
                for (int i = 0; i < 4; i++) if (childSlots[i] != 0) reuse(FamilyRole.Child, childSlots[i] - 1);
                if (setup.Members.Count == 0) { Toast("가족을 한 명 이상 골라 줘"); return; }
                OpenMemberInfo(0);
            });
        }

        string RoleLabel(MemberSetup m) { return m.Role == FamilyRole.Child ? RoleNames[4 + m.Gender] : RoleNames[(int)m.Role]; }

        void OpenMemberInfo(int i)
        {
            if (i >= setup.Members.Count) { FinishSetup(); return; }
            var m = setup.Members[i];
            var body = Window(RoleLabel(m) + "에 대해 알려줘!  (" + (i + 1) + "/" + setup.Members.Count + ")", 700); float w = BodyW(body), rh = Px(42), y = 0;
            Action redraw = () => OpenMemberInfo(i);
            var f = UiKit.Input(body, "name", m.Name, Px(16), 6, v => m.Name = v.Trim());
            UiKit.SetPx(f.GetComponent<RectTransform>(), 0, y, w * 0.62f, rh);
            var gl = UiKit.Label(body, "g", "성별 " + (m.Gender == 0 ? "남" : "여"), Px(15), UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.SetPx(gl.rectTransform, w * 0.64f, y, w * 0.36f, rh); y += rh + Px(6);
            int Y = GameDate.Year(m.BirthDay), M = GameDate.Month(m.BirthDay), D = GameDate.Day(m.BirthDay);
            Stepper(body, "생일 연", Y + "년 (" + GameDate.AgeYears(m.BirthDay, setup.StartDay) + "세)", 0, y, w, rh, k => { m.BirthDay = GameDate.Make(Y + k, M, D); redraw(); }); y += rh;
            Stepper(body, "연 ±10", "", 0, y, w, rh, k => { m.BirthDay = GameDate.Make(Y + 10 * k, M, D); redraw(); }); y += rh;
            Stepper(body, "월 / 일", M + "월 " + D + "일", 0, y, w, rh, k => { m.BirthDay = GameDate.Make(Y, Wrap(M - 1 + k, 12) + 1, D); redraw(); }); y += rh;
            Stepper(body, "", "", 0, y, w, rh, k => { m.BirthDay = GameDate.Make(Y, M, Wrap(D - 1 + k, DateTime.DaysInMonth(Y, M)) + 1); redraw(); });
            var dl = UiKit.Label(body, "dl", "일", Px(14), UiKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); UiKit.SetPx(dl.rectTransform, 0, y, w * 0.3f, rh); y += rh + Px(4);
            Choice(body, "혈액형", MemberSetup.Bloods, Array.IndexOf(MemberSetup.Bloods, m.Blood), y, w, rh, k => { m.Blood = MemberSetup.Bloods[k]; redraw(); }); y += rh + Px(4);
            Choice(body, "성격", MemberSetup.Personalities, m.Personality, y, w, rh, k => { m.Personality = k; redraw(); }); y += rh + Px(4);
            // 능력 순위: 원작처럼 높은 것부터 차례로 누른다
            var al = UiKit.Label(body, "al", "능력 순위 (높은 것부터 누르기)", Px(13), UiKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); UiKit.SetPx(al.rectTransform, 0, y, w, Px(22)); y += Px(24);
            int assigned = 0; foreach (var r in m.AbilityRank) if (r > 0) assigned++;
            float cw = (w - Px(18) - Px(70)) / 4f;
            for (int s2 = 0; s2 < 4; s2++)
            {
                int k = s2; int r = m.AbilityRank[k];
                var b = UiKit.Btn(body, "ab" + k, Stat.Names[k] + (r > 0 ? "  " + r + "번" : ""), Px(14), r > 0 ? new Color32(0xF8, 0xC0, 0x60, 255) : Color.white, UiKit.Ink, () =>
                {
                    if (m.AbilityRank[k] > 0) return;
                    int next = 1; foreach (var q in m.AbilityRank) next = Math.Max(next, q + 1);
                    m.AbilityRank[k] = next; redraw();
                });
                UiKit.SetPx(b.GetComponent<RectTransform>(), k * (cw + Px(6)), y, cw, rh);
            }
            var rs = UiKit.Btn(body, "abr", "다시", Px(13), new Color32(0x10, 0x4E, 0x6E, 255), Color.white, () => { m.AbilityRank = new[] { 0, 0, 0, 0 }; redraw(); });
            UiKit.SetPx(rs.GetComponent<RectTransform>(), w - Px(64), y, Px(64), rh); y += rh + Px(6);
            if (OrigAvailable() && LoadOrig())
            {
                // 원작 직업 화면: 나이·성별·관계로 원작 후보 목록(0x081114CC)을 만들고 그중에서 고른다
                List<int> cand;
                try { cand = Core.Orig.OrigJobs.Candidates(origRules, setup.StartDay, m.BirthDay, m.Gender, Core.Orig.OrigJobs.Relation(m.Role, m.Gender)); }
                catch (Exception) { cand = new List<int>(); }
                if (cand.Count > 0 && !cand.Contains(m.Job)) m.Job = cand[0];
                int ci = Math.Max(0, cand.IndexOf(m.Job));
                Stepper(body, "직업", cand.Count == 0 ? "-" : Core.Orig.OrigJobs.Name(m.Job) + "  (" + (ci + 1) + "/" + cand.Count + ")", 0, y, w, rh,
                    k => { if (cand.Count > 0) { m.Job = cand[Wrap(ci + k, cand.Count)]; redraw(); } }); y += rh + Px(8);
            }
            else
            {
                string jn; KnownJobs.TryGetValue(m.Job, out jn);
                Stepper(body, "직업", "코드 " + m.Job + (jn != null ? " " + jn : ""), 0, y, w, rh, k => { m.Job = Wrap(m.Job + k, 172); redraw(); }); y += rh + Px(8);
            }
            NextBtn(body, y, "결정", () =>
            {
                if (m.Name.Length == 0) { Toast("이름을 입력해 줘"); return; }
                foreach (var q in m.AbilityRank) if (q == 0) { Toast("능력 순위를 끝까지 골라 줘"); return; }
                if (m.BirthDay > setup.StartDay) { Toast("생일이 시작일보다 늦어"); return; }
                if (art.Parts.Available && FamilySetup.AsksCharacter(m, setup.StartDay))
                {
                    int age = GameDate.AgeYears(m.BirthDay, setup.StartDay);
                    var role = FamilySetup.GalleryRole(m, setup.StartDay);
                    if (m.Look == null) m.Look = CharacterComposer.FromPreset(art.Parts, new Rng((ulong)(i + 1) * 7919UL), role, m.Gender);
                    OpenLookEditor(m.Name, m.Look, role, m.Gender, AgeSlots.ForAge(age), l => m.Look = l, () => OpenMemberInfo(i + 1));
                }
                else
                {
                    // 원작은 6세 이하에게 캐릭터를 묻지 않는다 → 아이 목록에서 자동 배정(배정 규칙은 임시)
                    if (m.Look == null && art.Parts.Available) m.Look = CharacterComposer.FromPreset(art.Parts, new Rng((ulong)(i + 1) * 7919UL), m.Gender == 0 ? "son7" : "daughter7", m.Gender);
                    OpenMemberInfo(i + 1);
                }
            });
        }

        void FinishSetup()
        {
            var err = setup.Validate();
            if (err.Count > 0) { Toast(err[0]); OpenMemberInfo(0); return; }
            if (OrigAvailable() && LoadOrig())
            {
                // 원작 "내가 아는 가족" 경로: 원작 마무리·입력 확인·가족 레코드 만들기(능력치도 원작이 정한다)
                try
                {
                    session = Core.Orig.OrigSession.NewCustom(origRules, origText, setup, OrigSeed(), LastCartridge());
                    var name = session.Family.Name; setup = null; EnterGame(); Toast("이제 모두 끝! " + name + "가를 지켜보자 (원작 규칙)"); ApplyPlayerName();
                }
                catch (Exception e) { Toast("원작 가족 만들기 실패: " + e.Message); }
                return;
            }
            var f = setup.Build((ulong)DateTime.UtcNow.Ticks);
            session = new GameSession(f, catalog); setup = null; EnterGame(); Toast("이제 모두 끝! " + f.Name + "을(를) 지켜보자");
        }

        void NextBtn(RectTransform body, float y, string label, Action a)
        {
            var b = UiKit.Btn(body, "next", label, Px(16), new Color32(0xE8, 0x70, 0x40, 255), Color.white, a);
            UiKit.SetPx(b.GetComponent<RectTransform>(), 0, y, BodyW(body), Px(50));
        }

        void Choice(RectTransform body, string label, string[] opts, int cur, float y, float w, float h, Action<int> pick)
        {
            var l = UiKit.Label(body, "cl" + label, label, Px(14), UiKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); UiKit.SetPx(l.rectTransform, 0, y, w * 0.22f, h);
            float bw = (w * 0.78f - Px(4) * (opts.Length - 1)) / opts.Length;
            for (int i = 0; i < opts.Length; i++)
            {
                int k = i;
                var b = UiKit.Btn(body, "co" + label + i, opts[i], Px(13), i == cur ? new Color32(0xE8, 0x70, 0x40, 255) : new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => pick(k));
                UiKit.SetPx(b.GetComponent<RectTransform>(), w * 0.22f + i * (bw + Px(4)), y, bw, h);
            }
        }

        void EditLooks(int i)
        {
            var ms = session.Family.Members;
            if (i >= ms.Count) { EnterGame(); Toast("가족 구성 완료"); return; }
            OpenLookEditor(ms[i], () => EditLooks(i + 1));
        }

        // ---- 캐릭터 선택 (원작 '내가 아는 가족' 순서: 체형 → 캐릭터 목록 → 색상 변경 → 이걸로 OK!) ----
        static readonly string[] BuildNames = { "얇은", "보통", "굵은" };
        int lookPage;

        /// <summary>
        /// 원작 캐릭터 목록 구분(확인됨: 조부·조모·부·모, 자녀 7~12세/13세~). 게임 중 인물은 역할 칸이 없어 나이로 고른다(추정):
        /// 60세 이상 조부모 목록, 20세 이상 부모 목록, 13세 이상 10대 목록, 그 밖 아이 목록.
        /// </summary>
        static string RoleFor(int gender, int age)
        {
            if (age >= AgeSlots.ElderFromAge) return gender == 0 ? "grandfather" : "grandmother";
            if (age >= 20) return gender == 0 ? "father" : "mother";
            if (age >= 13) return gender == 0 ? "son18" : "daughter13";
            return gender == 0 ? "son7" : "daughter7";
        }
        string RoleOf(Person p) { return RoleFor(p.Gender, p.Age(session.Family.Today)); }

        void OpenLookEditor(Person p, Action done)
        {
            if (p.Look == null) p.Look = DefaultLook(p);
            OpenLookEditor(p.Name, p.Look, RoleOf(p), p.Gender, AgeSlots.ForAge(p.Age(session.Family.Today)), l => p.Look = l, done);
        }

        void OpenLookEditor(string who, CharacterLook L, string role, int gender, AgeSlots ages, Action<CharacterLook> setLook, Action done)
        {
            var lib = art.Parts;
            var body = Window(who + " — 캐릭터 선택", 640); float w = BodyW(body), h = BodyH(body);
            // 미리보기
            var fig = UiKit.Box(body, "fig", Color.white, art.LookSprite(L, ages, CharacterComposer.Pose.FrontA, 0) ?? UiKit.Circle); fig.preserveAspect = true;
            UiKit.SetPx(fig.rectTransform, 0, 0, Px(84), Px(150));
            float x = Px(92), cw = w - x, y = 0, rh = Px(40);
            Action redraw = () => OpenLookEditor(who, L, role, gender, ages, setLook, done);
            // ① 체형 (몸통 묶음 대응은 추정)
            int g = CharacterComposer.GenderOf(L), bd = CharacterComposer.BuildOf(L), of = CharacterComposer.OutfitOf(L);
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                var b = UiKit.Btn(body, "bd" + k, BuildNames[k], Px(14), bd == k ? new Color32(0xE8, 0x70, 0x40, 255) : new Color32(0x3A, 0x6E, 0xC8, 255), Color.white,
                    () => { CharacterComposer.SetBody(L, g, kk, of); redraw(); });
                UiKit.SetPx(b.GetComponent<RectTransform>(), x + k * cw / 3f, y, cw / 3f - Px(4), rh - Px(4));
            }
            y += rh;
            // ③ 색상 변경
            Stepper(body, "머리색", L.HairColor < 0 ? "원래" : (L.HairColor + 1) + "/" + lib.HairColors.Count, x, y, cw, rh, d => { L.HairColor = Wrap(L.HairColor + d, lib.HairColors.Count); redraw(); }); y += rh;
            Stepper(body, "피부색", L.SkinColor < 0 ? "원래" : (L.SkinColor + 1) + "/" + lib.SkinColors.Count, x, y, cw, rh, d => { L.SkinColor = Wrap(L.SkinColor + d, lib.SkinColors.Count); redraw(); }); y += rh;
            Stepper(body, "의상", (of + 1) + "/4", 0, Px(156), w, rh, d => { CharacterComposer.SetBody(L, g, bd, of + d); redraw(); });
            Stepper(body, "의상색", L.OutfitColor < 0 ? "원래" : (L.OutfitColor + 1) + "/4", 0, Px(156) + rh, w, rh, d => { L.OutfitColor = Wrap(L.OutfitColor + d, 4); redraw(); });
            // ② 캐릭터 목록 (원작 갤러리 프리셋, 12개씩 쪽 넘김)
            var ids = CharacterComposer.PresetIds(lib, role);
            float gy = Px(156) + 2 * rh + Px(6);
            int pages = Mathf.Max(1, (ids.Count + 11) / 12); lookPage = Mathf.Clamp(lookPage, 0, pages - 1);
            float cell = Mathf.Min(w / 6f, (h - gy - Px(100)) / 2f);
            for (int k = 0; k < 12; k++)
            {
                int n = lookPage * 12 + k; if (n >= ids.Count) break;
                var look = L.Clone(); CharacterComposer.ApplyPreset(lib, look, ids[n]);
                string id = ids[n];
                var bg = UiKit.Box(body, "c" + k, L.Preset == id ? new Color32(0xFF, 0xE0, 0x90, 255) : new Color32(0xE4, 0xEC, 0xFF, 255)); bg.raycastTarget = true;
                UiKit.SetPx(bg.rectTransform, (k % 6) * cell, gy + (k / 6) * cell, cell - Px(3), cell - Px(3));
                AgeSlots pa; if (!lib.PresetAges.TryGetValue(id, out pa)) pa = ages;
                var im = UiKit.Box(bg.transform, "i", Color.white, art.LookSprite(look, pa, CharacterComposer.Pose.FrontA, 0)); im.preserveAspect = true; UiKit.Stretch(im.rectTransform, 0, 0, 0, 0);
                bg.gameObject.AddComponent<Button>().onClick.AddListener(() => { CharacterComposer.ApplyPreset(lib, L, id); redraw(); });
            }
            float py = gy + 2 * cell + Px(4);
            Stepper(body, "캐릭터 목록", (lookPage + 1) + " / " + pages + "쪽", 0, py, w, rh, d => { lookPage = Wrap(lookPage + d, pages); redraw(); });
            // 원작에 없는 추가 기능: 무작위 원작 파트 조합
            var rnd = UiKit.Btn(body, "rnd", "무작위 조합 (원작 파트 · 추가 기능)", Px(14), new Color32(0x10, 0x4E, 0x6E, 255), Color.white, () => {
                var r = CharacterComposer.Random(lib, new Rng((ulong)DateTime.UtcNow.Ticks), gender); setLook(r); OpenLookEditor(who, r, role, gender, ages, setLook, done); });
            UiKit.SetPx(rnd.GetComponent<RectTransform>(), 0, py + rh + Px(4), w * 0.5f - Px(4), rh);
            var ok = UiKit.Btn(body, "ok", "이걸로 OK!", Px(16), new Color32(0xE8, 0x70, 0x40, 255), Color.white, () => { ClearPopup(); RebuildActorsIfPlaying(); done?.Invoke(); });
            UiKit.SetPx(ok.GetComponent<RectTransform>(), w * 0.5f, py + rh + Px(4), w * 0.5f, rh);
        }

        void RebuildActorsIfPlaying() { if (playing) { RebuildActors(); RefreshAll(); } }

        static int Wrap(int v, int n) { return n <= 0 ? 0 : ((v % n) + n) % n; }

        void Stepper(RectTransform body, string label, string value, float x, float y, float w, float h, Action<int> step)
        {
            var l = UiKit.Label(body, "l" + label, label, Px(14), UiKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); UiKit.SetPx(l.rectTransform, x, y, w * 0.34f, h);
            var a = UiKit.Btn(body, "a" + label, "◀", Px(14), new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => step(-1));
            UiKit.SetPx(a.GetComponent<RectTransform>(), x + w * 0.34f, y + Px(2), h - Px(4), h - Px(4));
            var v = UiKit.Label(body, "v" + label, value, Px(14), UiKit.Ink, TextAnchor.MiddleCenter); UiKit.SetPx(v.rectTransform, x + w * 0.34f + h, y, w * 0.66f - 2 * h, h);
            var b = UiKit.Btn(body, "b" + label, "▶", Px(14), new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => step(1));
            UiKit.SetPx(b.GetComponent<RectTransform>(), x + w - h + Px(4), y + Px(2), h - Px(4), h - Px(4));
        }

        void LoadSlot(string slot)
        {
            try
            {
                var r = saves.Load(slot);
                if (r.Family.IsOriginal)
                {
                    if (!LoadOrig()) { Toast("원작 규칙 저장입니다 — 원작 팩이 없어 불러올 수 없습니다"); return; }
                    session = Core.Orig.OrigSession.Load(origRules, origText, r.Family);
                }
                else session = new GameSession(r.Family, catalog);
                EnterGame(); Toast(r.Warning.Length > 0 ? r.Warning : "불러왔습니다");
            }
            catch (Exception e) { Toast("불러오기 실패: " + e.Message); }
        }

        void EnterGame()
        {
            ClearPopup(); playing = true; speed = 1; acc = 0; lastAutoDay = session.Family.Today; follow = true;
            titleRoot.gameObject.SetActive(false); gameRoot.gameObject.SetActive(true);
            selectedId = session.Family.HeadId >= 0 ? session.Family.HeadId : session.Family.Members[0].Id;
            RebuildActors(); RefreshAll();
            var a = SelActor(); if (a != null) scrollX = a.X - lay.Scene.W / hs / 2f + 16;
        }

        void SaveSlot(string slot, bool quiet = false)
        {
            try { session.PrepareSave(); saves.Save(slot, session.Family, catalog); if (!quiet) Toast("저장했습니다 (" + SlotLabel(slot) + ")"); }
            catch (Exception e) { Toast("저장 실패: " + e.Message); }
        }
        static string SlotLabel(string s) { return s == "auto" ? "자동 저장" : s.Replace("slot", "슬롯 "); }

        // =============================================================== 인물(배우) 배치
        Person Sel() { return session == null ? null : session.Family.Get(selectedId); }
        Actor SelActor() { Actor a; return actors.TryGetValue(selectedId, out a) ? a : null; }

        List<int[]> Rooms()
        {
            if (art.Rooms.Count > 0) return art.Rooms;
            var r = new List<int[]>(); for (int i = 0; i < 5; i++) r.Add(new[] { 50 + i * 110, 150 + i * 110 }); return r;
        }

        int HomeRoom(Person p)
        {
            var rooms = Rooms(); var f = session.Family;
            int age = p.Age(f.Today);
            if (p.Id == f.HeadId || p.Id == (f.Get(f.HeadId) != null ? f.Get(f.HeadId).SpouseId : -2)) return Math.Min(3, rooms.Count - 1);   // 부부 침실(추정)
            if (age < 25) return Math.Min(1 + (p.Id % 2), rooms.Count - 1);                                                                       // 아이 방
            return rooms.Count - 1;                                                                                                               // 거실
        }

        void RebuildActors()
        {
            foreach (var a in actors.Values) if (a.Rt != null) Destroy(a.Rt.gameObject);
            actors.Clear();
            if (session == null) return;
            var ids = art.CharacterIds(); int k = 0;
            foreach (var p in session.Family.Members)
            {
                if (!p.Alive) continue;
                if (p.Look == null && art.Parts.Available) p.Look = DefaultLook(p);
                if (p.Look == null && string.IsNullOrEmpty(p.Character) && ids.Count > 0) p.Character = ids[k++ % ids.Count];
                var room = Rooms()[HomeRoom(p)];
                var img = UiKit.Box(figures, "p" + p.Id, Color.white, Figure(p, false, 0));
                img.raycastTarget = true; img.preserveAspect = true;
                if (img.sprite == null) { img.sprite = UiKit.Circle; img.color = p.Gender == 0 ? new Color32(0x5B, 0x8F, 0xC9, 255) : new Color32(0xD9, 0x6A, 0x8A, 255); }
                int pid = p.Id;
                img.gameObject.AddComponent<Button>().onClick.AddListener(() => { selectedId = pid; follow = true; shownImm = -1; RefreshBar(); OpenDetail(session.Family.Get(pid)); });
                var bub = UiKit.Box(img.transform, "bubble", Color.white, null); bub.preserveAspect = true; bub.gameObject.SetActive(false);
                var bubT = UiKit.Label(bub.transform, "t", "", 14, Color.red, TextAnchor.MiddleCenter, FontStyle.Bold);
                float x = UnityEngine.Random.Range(room[0] + 4, room[1] - 36);
                actors[p.Id] = new Actor { Id = p.Id, X = x, Target = x, Rt = img.rectTransform, Img = img, NextMove = Time.time + UnityEngine.Random.Range(2f, 6f), Bubble = bub, BubbleText = bubT };
            }
        }

        /// <summary>
        /// 인물 그림: 원작 파트 조합(있으면) → 캡처 프레임 → null.
        /// 파트 조합: 나이로 연령 칸(아이·성인·노인), 걸을 때 앞모습 A/B 번갈아(원작 걷기 프레임), back=true 면 뒷모습 A/B.
        /// 의상 세트(몸통 블록 4~15)의 쓰임(계절 추정)은 미확인이라 세트 0 만 쓴다.
        /// </summary>
        Sprite Figure(Person p, bool back, int frame)
        {
            if (p.Look != null && art.Parts.Available)
            {
                var ages = AgeSlots.ForAge(p.Age(session.Family.Today));
                var pose = back ? (frame % 2 == 0 ? CharacterComposer.Pose.BackA : CharacterComposer.Pose.BackB)
                                : (frame % 2 == 0 ? CharacterComposer.Pose.FrontA : CharacterComposer.Pose.FrontB);
                var s = art.LookSprite(p.Look, ages, pose, 0);
                if (s != null) return s;
            }
            return art.Frame(p.Character, back ? "back" : "front", frame);
        }

        /// <summary>
        /// 외형이 없는 인물에게 원작 갤러리 프리셋을 준다(성인 남 → 아빠 목록, 성인 여 → 엄마 목록, 그 밖 → 아들 18세 목록/엄마 목록).
        /// 임시 규칙: 원작이 "신님이 아는 가족"에서 외형을 고르는 방식은 미해명. 인물 id 로 고정된 난수라 다시 열어도 같다.
        /// </summary>
        CharacterLook DefaultLook(Person p)
        {
            ulong h = 1469598103934665603UL; foreach (var ch in session.Family.Name) h = (h ^ ch) * 1099511628211UL;   // 실행마다 같은 해시
            var rng = new Rng(h ^ (ulong)(p.Id * 2654435761L));
            return CharacterComposer.FromPreset(art.Parts, rng, RoleOf(p), p.Gender);
        }

        void UpdateActors()
        {
            float viewW = lay.Scene.W / hs; int W = art.HouseWidth; int floorTop = art.FloorY - 61;
            var sa = SelActor();
            if (follow && sa != null) scrollX = Mathf.Lerp(scrollX, sa.X - viewW / 2f + 16, Time.deltaTime * 3f);
            scrollX = Mod(scrollX, W);
            house.uvRect = new Rect(scrollX / W, 0, viewW / W, 1);
            foreach (var a in actors.Values)
            {
                var p = session.Family.Get(a.Id);
                if (Time.time > a.NextMove)                                     // 임시 행동: 집 안을 오간다(원작 이동 규칙 미해명)
                {
                    var rooms = Rooms(); var room = UnityEngine.Random.value < 0.7f ? rooms[HomeRoom(p)] : rooms[UnityEngine.Random.Range(1, rooms.Count)];
                    a.Target = UnityEngine.Random.Range(room[0] + 4, room[1] - 36); a.NextMove = Time.time + UnityEngine.Random.Range(4f, 10f);
                }
                float d = a.Target - a.X; bool moving = Mathf.Abs(d) > 0.5f;
                if (moving) a.X += Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), 18f * Time.deltaTime);
                a.Anim += Time.deltaTime;
                var spr = Figure(p, moving && d < 0, moving ? (int)(a.Anim * 4) : 0);
                if (spr != null) a.Img.sprite = spr;
                float dx = Mod(a.X - scrollX, W); if (dx > viewW + 32) dx -= W;
                UiKit.SetPx(a.Rt, dx * hs, floorTop * hs, 32 * hs, 64 * hs);
                // 머리 위 말풍선: 사건 직전(예정 상태 2일 이내)엔 "!", 평소엔 가끔 원작 감정 말풍선(음표·zzz 등)
                bool soon = !string.IsNullOrEmpty(p.PlannedStateId) && (p.Gauge >= 255 || p.Gauge <= 0);   // 게이지 끝 = MAX/MIN 사건 차례
                bool emote = !soon && Mathf.Repeat(Time.time + a.Id * 2.3f, 9f) < 1.6f;
                if (soon) { a.Bubble.sprite = null; a.Bubble.color = new Color(1, 1, 1, 0.95f); a.BubbleText.text = "!"; a.BubbleText.color = new Color32(0xE0, 0x30, 0x30, 255); }
                else if (emote)
                {
                    var emo = EmoteFor(p, a.Id);
                    a.Bubble.sprite = emo; a.Bubble.color = emo != null ? Color.white : new Color(1, 1, 1, 0.95f);
                    a.BubbleText.text = emo != null ? "" : (p.Hearts >= Person.HeartUnit * 2 ? "♥" : p.Hearts < Person.HeartUnit / 2 ? "💢" : "♪");
                    a.BubbleText.color = new Color32(0xE0, 0x50, 0x80, 255);
                }
                a.Bubble.gameObject.SetActive(soon || emote);
                if (soon || emote)
                {
                    float bob2 = soon ? Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 3f : 0f;
                    UiKit.SetPx(a.Bubble.rectTransform, 6 * hs, (-16 - bob2) * hs, 20 * hs, 20 * hs);
                    UiKit.Stretch(a.BubbleText.rectTransform, 0, 0, 0, 0); a.BubbleText.fontSize = Mathf.RoundToInt(14 * hs);
                }
            }
            if (sa != null)
            {
                float dx = Mod(sa.X - scrollX, W); if (dx > viewW + 32) dx -= W;
                float bob = Mathf.Sin(Time.time * 3f) * 2f;
                UiKit.SetPx(marker.rectTransform, (dx + 8) * hs, (floorTop + 6 + bob) * hs, 16 * hs, 16 * hs);
            }
            // 큐피트: 장면 왼쪽 위를 떠다니며 팁 말풍선을 보여 준다
            {
                float t = Time.time, cx = 10 + Mathf.Sin(t * 0.7f) * 6f, cy = 12 + Mathf.Sin(t * 2.1f) * 3f;
                UiKit.SetPx(cupid.rectTransform, cx * hs, cy * hs, 32 * hs, 32 * hs);
                var cs = art.Cupid((int)(t * 6) % 4); if (cs != null) cupid.sprite = cs;
                cupid.transform.SetAsLastSibling();
                float tw = Mathf.Min(lay.Scene.W - (cx + 36) * hs - Px(8), Px(240));
                UiKit.SetPx(tipBox.rectTransform, (cx + 34) * hs, (cy + 2) * hs, tw, Px(40));
                UiKit.Stretch(tipText.rectTransform, Px(6), Px(2), Px(6), Px(2));
                tipBox.transform.SetAsLastSibling();
                itemCorner.transform.SetAsLastSibling(); bowCorner.transform.SetAsLastSibling();
            }
        }

        Sprite EmoteFor(Person p, int seed)
        {
            int hour = (int)(Time.time / 20f) % 4;
            if (hour == 3 && art.Ui("zzz_a") != null) return art.Ui(new[] { "zzz_a", "zzz_b", "zzz_c", "zzz_d" }[(int)(Time.time * 3) % 4]);
            string[] notes = { "bubble_note_green", "bubble_note_pink", "bubble_note_green2", "bubble_note_pink2", "bubble_note_green3" };
            return art.Ui(notes[(seed + (int)(Time.time / 9f)) % notes.Length]);
        }

        void UpdateGauge()
        {
            var p = Sel(); if (p == null || immFill == null) return;
            shownImm = shownImm < 0 ? p.Gauge : Mathf.MoveTowards(shownImm, p.Gauge, Time.deltaTime * 180f);
            float w = immBg.rectTransform.sizeDelta.x, h = immBg.rectTransform.sizeDelta.y;
            UiKit.SetPx(immFill.rectTransform, 0, 0, w * Mathf.Clamp01(shownImm / 255f), h);
            immFill.color = shownImm >= 170 ? new Color32(0xF0, 0x50, 0x30, 255) : shownImm >= 85 ? new Color32(0xF0, 0x80, 0x30, 255) : new Color32(0x70, 0x90, 0xD0, 255);
            immVal.text = Mathf.RoundToInt(shownImm) + " / 255";
        }

        void UpdateTip()
        {
            if (Time.time > tipUntil) { tipIndex++; tipUntil = Time.time + 8f; RefreshTip(); }
        }

        static float Mod(float a, float m) { return m <= 0 ? a : a - Mathf.Floor(a / m) * m; }

        void CycleSel(int dir)
        {
            var alive = session.Family.Members.FindAll(m => m.Alive); if (alive.Count == 0) return;
            int i = alive.FindIndex(m => m.Id == selectedId);
            selectedId = alive[((i + dir) % alive.Count + alive.Count) % alive.Count].Id; follow = true; shownImm = -1; RefreshBar(); RefreshDialog();
        }

        // =============================================================== 매 프레임
        void Update()
        {
            if (lastScreen.x != Screen.width || lastScreen.y != Screen.height || lastSafe != Screen.safeArea) ApplyLayout();
            if (toastBox.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBox.gameObject.SetActive(false);
            if (titleHouse != null && titleRoot.gameObject.activeSelf) { var u = titleHouse.uvRect; u.x += Time.deltaTime * 0.01f; titleHouse.uvRect = u; }
            if (!playing || session == null) return;
            UpdateActors();
            UpdateGauge();
            UpdateTip();
            if (popupOpen || session.Paused) return;
            // 원작: 가장이 가장인 채로 죽으면 가문이 끊긴 장면 — 진행을 멈추고 책갈피 되돌리기를 묻는다
            if (session is Core.Orig.OrigSession eos && eos.LineageEnded) { OpenLineageEnd(eos); return; }
            if (EffectiveSpeed == 0) return;
            acc += Time.deltaTime * EffectiveSpeed;
            bool started = false;
            while (acc >= SecondsPerDay && !started)
            {
                acc -= SecondsPerDay;
                string before = MemberIds();
                started = session.StepDay();
                if (MemberIds() != before) RebuildActors();   // 결혼·출생·사망·독립 (같은 날 들고 나도 알아챈다)
                if (session.Family.Today - lastAutoDay >= 30) { lastAutoDay = session.Family.Today; SaveSlot("auto", true); }
            }
            if (started)
            {
                acc = 0; SaveSlot("auto", true);
                int self; if (session.Family.Active != null && session.Family.Active.Cast.TryGetValue("self", out self)) { selectedId = self; follow = true; }
                else { var vw = session.View(); if (vw != null && vw.PersonId >= 0 && session.Family.Get(vw.PersonId) != null) { selectedId = vw.PersonId; follow = true; } }
                RefreshAll();
            }
            else { RefreshHud(); RefreshEffects(); }
        }

        string MemberIds() { var sb = new StringBuilder(); foreach (var m in session.Family.Members) sb.Append(m.Id).Append(','); return sb.ToString(); }

        void OnApplicationPause(bool paused) { if (paused && playing && session != null) SaveSlot("auto", true); }
        void OnApplicationQuit() { if (playing && session != null) { try { session.PrepareSave(); saves.Save("auto", session.Family, catalog); } catch (Exception) { } } }

        // =============================================================== 표시 갱신
        void RefreshAll() { RefreshHud(); RefreshBar(); RefreshDialog(); }

        float EffectiveSpeed { get { return speed == 0 ? 0f : holdingSpeed ? HoldSpeed : 1f; } }

        void RefreshHud()
        {
            var f = session.Family;
            hudText.text = f.Name + "  소지금 " + f.Assets.ToString("N0") + "엔";
            hudSub.text = "무드 포인트 " + f.Mood + " (" + Family.MoodLevel(f.Mood) + "단계)   " + f.YearsAsFamily + "년가족 · " + GameDate.Format(f.Today);
            speedText.text = holdingSpeed ? "▶▶▶" : speed == 0 ? "⏸\n배속" : "배속";
            speedCorner.color = holdingSpeed ? new Color32(0xE8, 0x70, 0x40, 255) : new Color32(0x3A, 0x6E, 0xC8, 255);
        }

        static readonly string[] RankKeys = { "체", "지", "매", "운" };
        static readonly int[] RankOrder = { Stat.Stamina, Stat.Int, Stat.Charm, Stat.Luck };    // 스케치 순서: 체 · 지 · 매 · 운

        void RefreshBar()
        {
            var p = Sel(); if (p == null) return;
            var por = (p.Look != null && art.Parts.Available ? Figure(p, false, 0) : null) ?? art.Portrait(p.Character);
            barPortrait.sprite = por ?? UiKit.Circle; barPortrait.color = por != null ? Color.white : (p.Gender == 0 ? new Color32(0x5B, 0x8F, 0xC9, 255) : new Color32(0xD9, 0x6A, 0x8A, 255));
            barName.text = p.Name;
            var sb = new StringBuilder();
            for (int i = 0; i < 4; i++)
                sb.Append(RankKeys[i]).Append("<color=#FFC040>").Append(Stat.Rank(p.Stats[RankOrder[i]])).Append("</color>  ");
            rankText.text = sb.ToString();
            for (int i = 0; i < 3; i++)
            {
                int v = p.Hearts - i * Person.HeartUnit;
                string key = v >= Person.HeartUnit ? "heart_full" : v >= Person.HeartUnit / 2 ? "heart_half" : "heart_empty";
                var s = art.Ui(key); barHearts[i].sprite = s ?? UiKit.Circle;
                barHearts[i].color = s != null ? Color.white : (key == "heart_full" ? Color.red : key == "heart_half" ? new Color(1, 0.5f, 0.6f) : new Color(0.4f, 0.5f, 1f));
            }
            string hit = HitArrowName(p);
            barPlanned.text = p.Age(session.Family.Today) + "세" + (hit != null ? " · " + hit : "");
            if (shownImm < 0) shownImm = p.Gauge;
            var g = art.Ui(p.Gauge >= 170 ? "gauge_full" : p.Gauge >= 85 ? "gauge_mid" : "gauge_empty");     // 원작 게이지 그림 3단계(경계는 그림 고르기용)
            barGauge.sprite = g; barGauge.enabled = g != null;     // 원작 게이지 그림(작게) — 큰 막대는 immFill
            RefreshEffects();
        }

        /// <summary>
        /// 결과 예고: 지금 관심사의 열중 게이지가 255 가 되면 일어날 MAX 사건, 0 이 되면 일어날 MIN 사건의 변화.
        /// 원작 규칙(GameSession.Predict — 같은 규칙으로 복제 가족에 적용)으로 계산. 힘내라 = 255 쪽, 진정해 = 0 쪽.
        /// </summary>
        string predKey = "";
        void RefreshEffects()
        {
            var p = Sel();
            if (p == null || string.IsNullOrEmpty(p.PlannedStateId)) { effectText.text = ""; predKey = ""; return; }
            string key = p.Id + "|" + p.PlannedStateId + "|" + session.Family.Today;
            if (key == predKey) return;
            predKey = key;
            effectText.text = PredText("255", session.Predict(p.Id, true), p) + "   " + PredText("0", session.Predict(p.Id, false), p);
        }

        string PredText(string label, Prediction pr, Person p)
        {
            var sb = new StringBuilder("<color=#FFC040>" + label + "</color> ");
            if (pr == null || pr.OutcomeId == "") return sb.Append("?").ToString();
            if (pr.Changes.Count == 0) return sb.Append("변화 없음").ToString();
            int shown = 0;
            foreach (var c in pr.Changes)
            {
                if (shown >= 3) { sb.Append("…"); break; }
                var who = session.Family.Get(c.PersonId);
                sb.Append(who != null && who.Id != p.Id ? who.Name + " " : "").Append(c.Label).Append("<color=").Append(c.Delta > 0 ? "#FF9090" : "#90B8FF").Append(">").Append(c.Arrow).Append("</color> ");
                shown++;
            }
            if (pr.HasRandom) sb.Append("(운)");
            return sb.ToString();
        }

        void RefreshTip()
        {
            if (tips == null || tips.Length == 0) return;
            tipText.text = tips[((tipIndex % tips.Length) + tips.Length) % tips.Length];
        }

        string PlannedTitle(Person p)
        {
            PlannedStateDef st;
            if (!string.IsNullOrEmpty(p.PlannedStateId) && catalog.States.TryGetValue(p.PlannedStateId, out st)) return st.Title;
            if (!string.IsNullOrEmpty(p.PlannedTitle) && !string.IsNullOrEmpty(p.PlannedStateId)) return p.PlannedTitle;
            return "지금은 특별한 생각이 없어…";
        }

        void RefreshDialog()
        {
            Clear(choiceHost);
            var v = session.View();
            if (v == null)
            {
                var p = Sel(); dlgTitle.text = ""; dlgSpeaker.text = "";
                bool soon = p != null && !string.IsNullOrEmpty(p.PlannedStateId) && (p.Gauge >= 255 || p.Gauge <= 0);
                dlgText.text = p == null ? "" : "<color=#1E46C8>" + p.Name + "</color>는 이런 생각을 하는 모양이야" + (soon ? " <color=#E04040>!</color>" : "") + "\n「" + PlannedTitle(p) + "」";
                nextBtn.gameObject.SetActive(false); LayoutDialog(); return;
            }
            dlgTitle.text = v.Title + "   [" + (v.Origin == "original" ? "원작 규칙" : "신규") + " · " + (v.TextSource == "original-translation" ? "원작 문구" : "임시 문구") + "]";
            dlgSpeaker.text = v.Speaker; dlgText.text = v.Text;
            if (v.NeedsChoice)
            {
                nextBtn.gameObject.SetActive(false);
                float pad = Px(8), w = dialog.sizeDelta.x - 2 * pad, gap = Px(4), dh = dialog.sizeDelta.y;
                float bh = Mathf.Clamp((dh - Px(70)) / Mathf.Max(1, v.Choices.Count) - gap, Px(34), Px(44)), total = v.Choices.Count * (bh + gap);
                float top = Mathf.Max(Px(40) + Px(24), dh - Px(6) - total);
                UiKit.SetPx(choiceHost, pad, top, w, total);
                UiKit.SetPx(dlgText.rectTransform, pad, Px(40), w, top - Px(42));
                for (int i = 0; i < v.Choices.Count; i++)
                {
                    string id = v.Choices[i].Id;
                    var b = UiKit.Btn(choiceHost, "c" + i, "▶ " + v.Choices[i].Text, Px(14), new Color32(0xE8, 0xF0, 0xFF, 255), new Color32(0x1E, 0x46, 0x9A, 255), () => { session.Choose(id); AfterEventStep(); });
                    UiKit.SetPx(b.GetComponent<RectTransform>(), 0, i * (bh + gap), w, bh);
                }
            }
            else { LayoutDialog(); nextBtn.gameObject.SetActive(true); }
        }

        void OnNext() { session.Advance(); AfterEventStep(); }
        void AfterEventStep()
        {
            bool done = !session.Paused;
            if (done) { SaveSlot("auto", true); RebuildActors(); }
            RefreshAll();
        }

        // =============================================================== 팝업 공통 (원작풍 창)
        RectTransform Window(string title, float heightDp)
        {
            ClearPopup(); popupOpen = true; popupRoot.gameObject.SetActive(true);
            var shade = UiKit.Box(popupRoot, "shade", new Color(0, 0, 0, 0.45f)); shade.raycastTarget = true; UiKit.Stretch(shade.rectTransform, 0, 0, 0, 0);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(ClosePopup);
            float w = lay.Safe.W - Px(20), h = Mathf.Min(Px(heightDp), lay.Safe.H - Px(40)), x = lay.Safe.X + Px(10), y = lay.Safe.Y + (lay.Safe.H - h) / 2f;
            var frame = UiKit.Box(popupRoot, "frame", new Color32(0x3A, 0x6E, 0xC8, 255)); frame.raycastTarget = true; UiKit.SetPx(frame.rectTransform, x, y, w, h);
            var inner = UiKit.Box(frame.transform, "inner", new Color32(0xF4, 0xF8, 0xFF, 255)); UiKit.Stretch(inner.rectTransform, Px(4), Px(4), Px(4), Px(4));
            var head = UiKit.Box(frame.transform, "head", new Color32(0xC8, 0xD8, 0xFF, 255)); UiKit.SetPx(head.rectTransform, Px(4), Px(4), w - Px(8), Px(40));
            var t = UiKit.Label(head.transform, "t", title, Px(17), new Color32(0x1E, 0x46, 0x9A, 255), TextAnchor.MiddleCenter, FontStyle.Bold); UiKit.Stretch(t.rectTransform, 0, 0, 0, 0);
            var close = UiKit.Btn(frame.transform, "close", "닫기", Px(15), new Color32(0x10, 0x4E, 0x6E, 255), Color.white, ClosePopup);
            UiKit.SetPx(close.GetComponent<RectTransform>(), Px(10), h - Px(56), w - Px(20), Px(48));
            var body = UiKit.Node(frame.transform, "body"); UiKit.SetPx(body, Px(12), Px(50), w - Px(24), h - Px(50) - Px(64));
            return body;
        }

        void ClosePopup() { ClearPopup(); if (playing) RefreshAll(); }
        void ClearPopup() { popupOpen = false; Clear(popupRoot); }

        float BodyW(RectTransform b) { return b.sizeDelta.x; }
        float BodyH(RectTransform b) { return b.sizeDelta.y; }

        // ---- 상세창 (원작 '가족보기' 구성) ----
        void OpenDetail(Person p)
        {
            if (p == null) return;
            var f = session.Family; var body = Window(p.Name + (p.Id == f.HeadId ? "  👑" : ""), 560); float w = BodyW(body);
            var fig = UiKit.Box(body, "fig", Color.white, Figure(p, false, 0) ?? UiKit.Circle); fig.preserveAspect = true;
            UiKit.SetPx(fig.rectTransform, 0, 0, Px(96), Px(150));
            var age = UiKit.Label(body, "age", p.Age(f.Today) + "세", Px(16), UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold); UiKit.SetPx(age.rectTransform, 0, Px(150), Px(96), Px(24));
            var job = UiKit.Label(body, "job", Core.Orig.OrigJobs.Name(p.Job), Px(12), UiKit.Ink, TextAnchor.MiddleCenter); UiKit.SetPx(job.rectTransform, 0, Px(174), Px(96), Px(22));
            float x = Px(106), cw = w - x;
            Line(body, "꿈", string.IsNullOrEmpty(p.Dream) ? "지금은 없어…" : p.Dream, x, 0, cw);
            Line(body, "화살", HitArrowName(p) ?? "맞은 화살 없음", x, Px(34), cw);
            string[] nm = { "지력", "체력", "매력", "운" };
            float sw = cw / 4f;
            for (int i = 0; i < 4; i++)
            {
                var l = UiKit.Label(body, "sn" + i, nm[i], Px(14), UiKit.Ink, TextAnchor.MiddleCenter); UiKit.SetPx(l.rectTransform, x + i * sw, Px(72), sw, Px(22));
                var r = UiKit.Label(body, "sr" + i, Stat.Rank(p.Stats[i]), Px(24), new Color32(0xE8, 0x70, 0x20, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
                var ro = r.gameObject.AddComponent<Outline>(); ro.effectColor = new Color32(0x80, 0x30, 0x00, 255);
                UiKit.SetPx(r.rectTransform, x + i * sw, Px(94), sw, Px(34));
            }
            var hl = UiKit.Label(body, "hearts", "하트", Px(14), UiKit.Ink, TextAnchor.MiddleLeft); UiKit.SetPx(hl.rectTransform, x, Px(134), Px(50), Px(30));
            for (int i = 0; i < 3; i++)
            {
                int v = p.Hearts - i * Person.HeartUnit;
                var s = art.Ui(v >= Person.HeartUnit ? "heart_full" : v >= Person.HeartUnit / 2 ? "heart_half" : "heart_empty");
                var hi = UiKit.Box(body, "h" + i, s != null ? Color.white : Color.red, s ?? UiKit.Circle); hi.preserveAspect = true;
                UiKit.SetPx(hi.rectTransform, x + Px(50) + i * Px(34), Px(134), Px(30), Px(30));
            }
            var gr = UiKit.Label(body, "grat", "신님에게 감사  " + (p.Gratitude >= 0 ? p.Gratitude : f.Gratitude) + "개", Px(14), UiKit.Ink, TextAnchor.MiddleLeft); UiKit.SetPx(gr.rectTransform, x, Px(166), cw - Px(100), Px(24));
            var ren = UiKit.Btn(body, "rename", "이름 바꾸기", Px(12), new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => OpenRename(p));
            UiKit.SetPx(ren.GetComponent<RectTransform>(), x + cw - Px(96), Px(164), Px(96), Px(28));
            PlannedStateDef st; catalog.States.TryGetValue(p.PlannedStateId ?? "", out st);
            var now = UiKit.Label(body, "now", "현재  " + PlannedTitle(p), Px(16), new Color32(0x1E, 0x46, 0x9A, 255), TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.SetPx(now.rectTransform, 0, Px(200), w, Px(48));
            string desc = st != null && !string.IsNullOrEmpty(st.Desc) ? st.Desc : "(예정 상태 설명은 원작 문구 팩에서 제공)";
            var os2 = session as Core.Orig.OrigSession;
            string skills = p.Skills.Count == 0 ? "  없음" : string.Concat(p.Skills.ConvertAll(k =>
            {
                if (os2 == null) return "\n· 스킬 " + k + "번";
                string d = os2.SkillDesc(k);
                return "\n· " + os2.SkillName(k) + (d.Length > 0 ? " — " + d.Replace("\n", " ") : "");
            }));
            var dd = UiKit.Label(body, "desc", desc + "\n\n스킬" + skills + "\n직업 코드 " + p.Job + " · 숙련 " + p.JobMastery + " · 몰입도 " + p.Immersion + "\n※ 직업 이름·하트 단위는 원작 확인 전 임시 표기", Px(14), UiKit.Ink, TextAnchor.UpperLeft);
            UiKit.SetPx(dd.rectTransform, 0, Px(250), w, BodyH(body) - Px(250));
        }

        /// <summary>
        /// 이름 바꾸기: 기기 키보드(안드로이드 한글 입력)로 입력한다 — 원작의 자모 입력 화면은 옮기지 않았다.
        /// 원작 진행 가족은 표시 이름표(Family.OrigNames)에 넣어 다음 날·저장 뒤에도 남는다.
        /// </summary>
        void OpenRename(Person p)
        {
            string name = p.Name;
            var body = Window("이름 바꾸기", 220); float w = BodyW(body);
            var f = UiKit.Input(body, "name", name, Px(18), 8, v => name = v.Trim());
            UiKit.SetPx(f.GetComponent<RectTransform>(), 0, 0, w, Px(52));
            f.ActivateInputField();
            NextBtn(body, Px(70), "결정", () =>
            {
                if (name.Length == 0) { Toast("이름을 입력해 줘"); return; }
                p.Name = name;
                if (session.Family.IsOriginal) session.Family.OrigNames[p.Id] = name;
                SaveSlot("auto", true); RefreshAll(); OpenDetail(p);
            });
        }

        void Line(RectTransform body, string tag, string text, float x, float y, float w)
        {
            var bg = UiKit.Box(body, "lb" + tag, Color.white); UiKit.SetPx(bg.rectTransform, x, y, w, Px(30));
            var t = UiKit.Label(bg.transform, "t", "<b>" + tag + "</b>  " + text, Px(14), UiKit.Ink, TextAnchor.MiddleLeft); t.supportRichText = true;
            UiKit.Stretch(t.rectTransform, Px(8), 0, Px(4), 0);
        }

        // ---- 활쏘기 / 아이템 ----
        void OpenTools(string kind)
        {
            var p = Sel(); if (p == null || session.Paused) { Toast("지금은 쓸 수 없습니다"); return; }
            var body = Window((kind == "arrow" ? "화살 — " : "아이템 — ") + p.Name, 520); float w = BodyW(body), y = 0, bh = Px(58);
            bool orig = session is Core.Orig.OrigSession;
            foreach (var t in Interventions.Tools)
            {
                if (t.Kind != kind) continue;
                int n = Interventions.Count(session.Family, t.Id); bool can = session.CanUse(t.Id);
                // 원작처럼 가진 것만 보인다 (원작 세션). 옛 세션은 구현된 아이템 중 없는 것을 숨긴다.
                if (orig ? n == 0 : (kind == "item" && n == 0 && can)) continue;
                string id = t.Id;
                var b = UiKit.Btn(body, t.Id, "", Px(14), n > 0 && can ? Color.white : new Color(0.88f, 0.88f, 0.9f), UiKit.Ink, () =>
                {
                    if (id == "item.heart_crystal" && session is Core.Orig.OrigSession os) { ClosePopup(); OpenLegacyHearts(os); return; }
                    var err = session.Use(Sel(), id);
                    ClosePopup(); Toast(err ?? (Interventions.Find(id).Name + "을(를) " + Sel().Name + "에게 썼습니다"));
                    if (err == null) SaveSlot("auto", true);
                });
                var lbl = b.GetComponentInChildren<Text>(); lbl.alignment = TextAnchor.MiddleLeft; lbl.fontStyle = FontStyle.Normal; lbl.supportRichText = true;
                lbl.text = "<b>" + t.Name + "</b>  ×" + n + (can ? "" : "  (미구현)") + "\n<size=" + Px(12) + ">" + t.Desc + "</size>";
                UiKit.SetPx(b.GetComponent<RectTransform>(), 0, y, w, bh); y += bh + Px(6);
            }
            if (y == 0)
            {
                var l = UiKit.Label(body, "none", kind == "arrow" ? "가진 화살이 없습니다." : "가진 아이템이 없습니다.", Px(15), UiKit.Ink, TextAnchor.UpperLeft); UiKit.SetPx(l.rectTransform, 0, 0, w, Px(80));
            }
        }

        /// <summary>가문이 끊김: 원작은 이때 시간의 책갈피가 있으면 쓴 날로 돌아갈 수 있다(0x0809EE4E → 0x08010AB4).</summary>
        void OpenLineageEnd(Core.Orig.OrigSession os)
        {
            var body = Window("가문이 끊겼습니다", 520); float w = BodyW(body);
            // 원작 가문의 기록(0x0801045C 가 적은 칸) — 화면 배치는 앱이 정했다(원작 정리 화면 0x0809EAAC 의 그림 연출은 옮기지 않음)
            var names = LoadRecordNames(); var cur = os.CurrentRecord();
            if (cur != null)
            {
                names[Core.Orig.OrigSession.RecordKey(cur)] = os.Family.Name;
                try { File.WriteAllText(RecordNamesPath, MiniJson.Serialize(names)); } catch (Exception) { }
            }
            string head = cur != null ? os.Family.Name + "가문  " + cur.Years + "년 가족\n" + os.FamilyTypeName(cur.Type) + " · 역대 가족 " + cur.Members + "명"
                                      : os.Family.Name + "가문 (가문의 기록 상위 3에 들지 못했습니다)";
            var hd = UiKit.Label(body, "head", head, Px(17), new Color32(0x1E, 0x46, 0x9A, 255), TextAnchor.UpperCenter, FontStyle.Bold);
            UiKit.SetPx(hd.rectTransform, 0, 0, w, Px(60));
            var recs = os.FamilyRecords();
            var rl = UiKit.Label(body, "recs", "가문의 기록\n" + string.Join("\n", recs.ConvertAll(r => RecordLine(names, r, os))), Px(13), UiKit.Ink, TextAnchor.UpperLeft);
            UiKit.SetPx(rl.rectTransform, 0, Px(66), w, Px(170));
            var btns = new GameObject("btns", typeof(RectTransform)).GetComponent<RectTransform>(); btns.SetParent(body, false);
            UiKit.SetPx(btns, 0, Px(244), w, Px(130));
            body = btns;
            if (os.HasBookmark)
            {
                var b = UiKit.Btn(body, "bm", "시간의 책갈피로 돌아가기", Px(16), Color.white, UiKit.Ink, () =>
                {
                    ClosePopup();
                    if (os.RestoreBookmark()) { RebuildActors(); RefreshAll(); SaveSlot("auto", true); Toast("책갈피를 쓴 날로 돌아왔습니다"); }
                    else Toast("되돌리지 못했습니다");
                });
                UiKit.SetPx(b.GetComponent<RectTransform>(), 0, 0, w, Px(56));
            }
            else
            {
                var l = UiKit.Label(body, "none", "시간의 책갈피가 없습니다.", Px(15), UiKit.Ink, TextAnchor.UpperLeft); UiKit.SetPx(l.rectTransform, 0, 0, w, Px(40));
            }
            var t = UiKit.Btn(body, "title", "타이틀로", Px(16), Color.white, UiKit.Ink, () => { ClosePopup(); ShowTitle(); });
            UiKit.SetPx(t.GetComponent<RectTransform>(), 0, Px(66), w, Px(56));
        }

        /// <summary>선대 마음의 결정: 원작처럼 선대의 마음 목록(0x0202C328)을 보여 주고 고른 마음을 이어받게 한다.</summary>
        static string GenLabel(int g) { return (g > 0 ? g.ToString() : "?") + "대"; }

        void OpenLegacyHearts(Core.Orig.OrigSession os)
        {
            var p = Sel(); if (p == null) return;
            var body = Window("선대 마음의 결정 — " + p.Name, 520); float w = BodyW(body), y = 0, bh = Px(52);
            var list = os.LegacyHearts();
            for (int k = 0; k < list.Count; k++)
            {
                int idx = k;
                var b = UiKit.Btn(body, "lg" + k, GenLabel(os.Generation(list[k][0])) + "  " + os.LegacyName(list[k][0]) + "의 마음  (" + os.SkillName(list[k][1]) + ")", Px(15), Color.white, UiKit.Ink, () =>
                {
                    var err = os.UseLegacyHeart(Sel(), idx);
                    ClosePopup(); Toast(err ?? (Sel().Name + "이(가) 선대의 마음을 이어받았습니다"));
                    if (err == null) SaveSlot("auto", true);
                });
                UiKit.SetPx(b.GetComponent<RectTransform>(), 0, y, w, bh); y += bh + Px(6);
            }
            if (list.Count == 0)
            {
                var l = UiKit.Label(body, "none", "이어받을 선대의 마음이 없습니다.", Px(15), UiKit.Ink, TextAnchor.UpperLeft); UiKit.SetPx(l.rectTransform, 0, 0, w, Px(60));
            }
        }

        // ---- 설정 (속도·따라가기·저장·콘텐츠·타이틀) ----
        void OpenSettings()
        {
            var body = Window("설정", 720); float w = BodyW(body), bw = (w - Px(18)) / 4f, y = 0;
            if (session.Family.GodRank >= 0)
            {
                string gen = session is Core.Orig.OrigSession gos ? GenLabel(gos.Generation(session.Family.HeadId)) + "째 " + session.Family.Name + "가 · " : "";   // 원작 메뉴 머리 "N대째 ○○가" — 가장의 세대로 보여 준다(추정: 실기 "1대째" = 가장 세대 1 과 일치, 머리 글을 만드는 원작 코드는 확인하지 않음)
                var gl = UiKit.Label(body, "god", gen + "신님 랭크 " + session.Family.GodRank + "성 · 감사의 마음 " + session.Family.Gratitude + "개", Px(14), UiKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.SetPx(gl.rectTransform, 0, y, w, Px(24)); y += Px(28);
            }
            var sl = UiKit.Label(body, "sl", "진행 (원작: 하루 약 88초, 배속 코너를 누르고 있으면 약 ×6.5)", Px(13), UiKit.Ink, TextAnchor.MiddleLeft); UiKit.SetPx(sl.rectTransform, 0, y, w, Px(22)); y += Px(24);
            string[] l = { "멈춤", "진행" }; int[] sp = { 0, 1 }; bw = (w - Px(6)) / 2f;
            for (int i = 0; i < 2; i++)
            {
                int v = sp[i];
                var b = UiKit.Btn(body, "s" + i, l[i], Px(16), speed == v ? new Color32(0xE8, 0x70, 0x40, 255) : new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => { speed = v; OpenSettings(); });
                UiKit.SetPx(b.GetComponent<RectTransform>(), i * (bw + Px(6)), y, bw, Px(50));
            }
            y += Px(60);
            var items = new List<KeyValuePair<string, Action>> {
                new KeyValuePair<string, Action>(follow ? "선택한 사람 따라가기: 켜짐" : "선택한 사람 따라가기: 꺼짐", () => { follow = !follow; OpenSettings(); }),
                new KeyValuePair<string, Action>(session is Core.Orig.OrigSession ? "종합 진단" : null, OpenDiagnosis),
                new KeyValuePair<string, Action>("저장 / 불러오기", () => OpenSaveSlots()),
                new KeyValuePair<string, Action>(session is Core.Orig.OrigSession pos ? "플레이어 이름: " + pos.PlayerName : null, OpenPlayerName),
                new KeyValuePair<string, Action>("콘텐츠 · 업데이트", OpenContent),
                new KeyValuePair<string, Action>("타이틀로", () => { SaveSlot("auto", true); ShowTitle(); }) };
            foreach (var kv in items)
            {
                if (kv.Key == null) continue;
                var act = kv.Value;
                var b = UiKit.Btn(body, kv.Key, kv.Key, Px(16), Color.white, new Color32(0x1E, 0x46, 0x9A, 255), () => act());
                UiKit.SetPx(b.GetComponent<RectTransform>(), 0, y, w, Px(52)); y += Px(60);
            }
        }

        // ---- 저장 슬롯 ----
        void OpenSaveSlots()
        {
            var body = Window("저장 / 불러오기", 520); float w = BodyW(body), y = 0, bh = Px(48), gap = Px(6);
            foreach (var slot in SaveSystem.Slots)
            {
                string s = slot; string desc = SlotLabel(s);
                if (saves.Exists(s)) { try { var r = saves.Load(s); desc += " — " + r.Family.Name + " " + GameDate.Format(r.Family.Today); } catch (Exception) { desc += " — 손상됨"; } }
                else desc += " — 비어 있음";
                var lb = UiKit.Label(body, "l" + s, desc, Px(13), UiKit.Ink, TextAnchor.MiddleLeft); UiKit.SetPx(lb.rectTransform, 0, y, w, Px(24)); y += Px(24);
                float half = (w - gap) / 2f; bool canSave = playing && s != "auto";
                var sv = UiKit.Btn(body, "s" + s, "저장", Px(14), canSave ? new Color32(0x3A, 0x6E, 0xC8, 255) : (Color)Color.gray, Color.white, () => { if (canSave) { SaveSlot(s); OpenSaveSlots(); } });
                UiKit.SetPx(sv.GetComponent<RectTransform>(), 0, y, half, bh);
                var ld = UiKit.Btn(body, "o" + s, "불러오기", Px(14), saves.Exists(s) ? new Color32(0x10, 0x4E, 0x6E, 255) : (Color)Color.gray, Color.white, () => { if (saves.Exists(s)) LoadSlot(s); });
                UiKit.SetPx(ld.GetComponent<RectTransform>(), half + gap, y, half, bh); y += bh + gap * 2;
            }
        }

        void OpenContent()
        {
            var sb = new StringBuilder("콘텐츠 팩\n");
            foreach (var p in catalog.Packs) sb.AppendLine("• " + p.PackId + " v" + p.Version + " — " + p.Title + " (이벤트 " + p.Events.Count + ")");
            sb.AppendLine("\n그래픽: " + (art.Available ? "원작 로컬 추출본(배포 불가)" : "임시 도형"));
            sb.AppendLine("저장 스키마 v" + SaveSystem.CurrentSchema + " · 데이터 계약 v" + Capabilities.Contract);
            if (contentWarnings.Count > 0) { sb.AppendLine("\n경고:"); foreach (var m in contentWarnings) sb.AppendLine("- " + m); }
            if (updater.Report.Count > 0) { sb.AppendLine("\n업데이트 결과:"); foreach (var m in updater.Report) sb.AppendLine("- " + m); }
            var body = TextWindow("콘텐츠", sb.ToString(), Px(60));
            var b = UiKit.Btn(body, "update", "업데이트 확인", Px(15), new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () =>
                StartCoroutine(updater.CheckAndInstall(packStore, bundled, n =>
                {
                    if (n > 0)
                    {
                        var warn = new List<string>(); var cat = packStore.LoadCatalog(bundled, warn); contentWarnings.AddRange(warn);
                        if (cat != null) { catalog = cat; if (session != null) session.ReplaceCatalog(cat); }
                    }
                    Toast(n > 0 ? n + "개 팩이 적용되었습니다" : "적용할 업데이트가 없습니다"); OpenContent();
                })));
            UiKit.SetPx(b.GetComponent<RectTransform>(), 0, BodyH(body) - Px(52), BodyW(body), Px(50));
        }

        RectTransform TextWindow(string title, string text, float bottomReserve = 0)
        {
            var body = Window(title, 640);
            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            scrollGo.transform.SetParent(body, false);
            var srt = (RectTransform)scrollGo.transform; UiKit.SetPx(srt, 0, 0, BodyW(body), BodyH(body) - bottomReserve);
            scrollGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f); scrollGo.GetComponent<Mask>().showMaskGraphic = false;
            var content = UiKit.Node(srt, "content"); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, 100);
            var t = UiKit.Label(content, "t", text, Px(14), UiKit.Ink, TextAnchor.UpperLeft); t.verticalOverflow = VerticalWrapMode.Overflow;
            var tr = t.rectTransform; tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1); tr.anchoredPosition = Vector2.zero; tr.sizeDelta = new Vector2(0, 100);
            t.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>(); vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            var sr = scrollGo.GetComponent<ScrollRect>(); sr.content = content; sr.horizontal = false; sr.viewport = srt; sr.scrollSensitivity = 30;
            return body;
        }

        string TreeText()
        {
            var f = session.Family; var sb = new StringBuilder(); var seen = new HashSet<int>();
            foreach (var p in f.Members) if (p.FatherId < 0 && p.MotherId < 0 && seen.Add(p.Id)) Tree(f, p, 0, sb, seen);
            sb.AppendLine("\n※ 부부·자녀 관계는 원작 레코드의 관계 필드가 미해명이라 나이·성별로 추정했습니다.");
            return sb.ToString();
        }

        void Tree(Family f, Person p, int d, StringBuilder sb, HashSet<int> seen)
        {
            string sp = "";
            if (p.SpouseId >= 0) { var s = f.Get(p.SpouseId); if (s != null) { sp = " ♥ " + s.Name; seen.Add(s.Id); } }
            sb.AppendLine(new string(' ', d * 4) + (d > 0 ? "└ " : "") + p.Name + sp + " (" + GameDate.Year(p.BirthDay) + "년생" + (p.Alive ? "" : " †") + ")");
            foreach (var c in f.Members) if ((c.FatherId == p.Id || c.MotherId == p.Id) && seen.Add(c.Id)) Tree(f, c, d + 1, sb, seen);
        }

        string LogText()
        {
            var f = session.Family; var sb = new StringBuilder();
            if (f.History.Count == 0) return "아직 기록된 이벤트가 없습니다.";
            for (int i = f.History.Count - 1; i >= 0; i--)
            {
                var h = f.History[i]; var p = f.Get(h.PersonId);
                sb.AppendLine(GameDate.Format(h.Day) + " · " + (p != null ? p.Name : "-") + "\n  " + h.Title + (h.Choice.Length > 0 ? "  [선택: " + h.Choice + "]" : "")
                    + (string.IsNullOrEmpty(h.Changes) ? "" : "\n  → " + h.Changes) + "\n");
            }
            return sb.ToString();
        }

        // =============================================================== 공통
        void Toast(string s)
        {
            if (lay == null) return;
            toastText.text = s; toastBox.gameObject.SetActive(true); toastBox.transform.SetAsLastSibling(); toastUntil = Time.unscaledTime + 2.4f;
            UiKit.SetPx(toastBox.rectTransform, lay.Safe.X + Px(16), lay.Safe.Y + lay.Safe.H * 0.42f, lay.Safe.W - Px(32), Px(64));
            toastText.fontSize = Px(14); UiKit.Stretch(toastText.rectTransform, 10, 4, 10, 4);
        }

        static void Clear(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject); }
    }

    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (UnityEngine.Object.FindFirstObjectByType<GameApp>() != null) return;
            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera", typeof(Camera)); cam.tag = "MainCamera";
                cam.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            }
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }
            new GameObject("App", typeof(GameApp));
        }
    }
}
