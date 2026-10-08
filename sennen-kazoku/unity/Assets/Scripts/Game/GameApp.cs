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
        PackStore packStore; SaveSystem saves; List<Pack> bundled; ContentCatalog catalog; GameSession session;
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
        int speed = 1; float acc; const float SecondsPerDay = 0.6f;   // 임시 진행 속도(원작은 실시간 시계 기반으로 보이나 미확인)
        bool playing, popupOpen, follow = true; int lastAutoDay; float toastUntil;
        int selectedId = -1; float scrollX;                             // GBA px
        sealed class Actor { public int Id; public float X, Target; public RectTransform Rt; public Image Img; public float Anim, NextMove; public bool Back; public Image Bubble; }
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
            UiKit.SetPx(hud, lay.TopBar); UiKit.SetPx(scene, lay.Scene); UiKit.SetPx(bar, lay.FamilyStrip);
            UiKit.SetPx(dialog, lay.EventPanel); UiKit.SetPx(menu, lay.Controls);
            LayoutHud(); LayoutBar(); LayoutDialog(); LayoutMenu();
            lastScreen = new Vector2(w, h); lastSafe = sa;
            if (playing) { RebuildActors(); RefreshAll(); }
            if (titleRoot.gameObject.activeSelf || !playing) ShowTitle();
        }

        // =============================================================== 화면 뼈대
        Button[] menuBtns = new Button[4];
        Text[] menuIcons = new Text[4];

        void BuildGame()
        {
            hud = UiKit.Node(gameRoot, "hud"); scene = UiKit.Node(gameRoot, "scene"); bar = UiKit.Node(gameRoot, "bar");
            dialog = UiKit.Node(gameRoot, "dialog"); menu = UiKit.Node(gameRoot, "menu");

            var hb = UiKit.Box(hud, "bg", new Color32(0xF6, 0xE3, 0x9A, 255)); UiKit.Stretch(hb.rectTransform, 0, 0, 0, 0);
            hudText = UiKit.Label(hud, "t", "", 20, new Color32(0x1E, 0x46, 0x9A, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
            var ol = hudText.gameObject.AddComponent<Outline>(); ol.effectColor = Color.white; ol.effectDistance = new Vector2(2, -2);

            // 집: RawImage 의 uvRect 로 가로 무한 스크롤
            var hgo = new GameObject("house", typeof(RectTransform), typeof(RawImage)); hgo.transform.SetParent(scene, false);
            house = hgo.GetComponent<RawImage>(); UiKit.Stretch(house.rectTransform, 0, 0, 0, 0);
            house.texture = art.HouseTexture(); house.raycastTarget = true;
            if (house.texture == null) { house.color = new Color32(0xBF, 0xE3, 0xF2, 255); }
            var drag = hgo.AddComponent<HouseDrag>();
            drag.OnBegin = () => { follow = false; };
            drag.OnDragX = dx => { scrollX -= dx / hs; };
            figures = UiKit.Node(scene, "figures");
            cupid = UiKit.Box(scene, "cupid", Color.white, art.Cupid(0)); cupid.preserveAspect = true;
            if (cupid.sprite == null) { cupid.sprite = UiKit.Circle; cupid.color = new Color32(0xFF, 0xE0, 0x70, 255); }
            marker = UiKit.Box(scene, "marker", Color.white, art.Ui("marker_select"));
            if (marker.sprite == null) marker.color = new Color32(0x50, 0xC0, 0x50, 255);

            // 선택 인물 바 (원작 하단 바 구성)
            var bb = UiKit.Box(bar, "bg", new Color32(0x10, 0x4E, 0x6E, 255)); UiKit.Stretch(bb.rectTransform, 0, 0, 0, 0);
            barPortrait = UiKit.Box(bar, "portrait", Color.white); barPortrait.preserveAspect = true; barPortrait.raycastTarget = true;
            barPortrait.gameObject.AddComponent<Button>().onClick.AddListener(() => { var p = Sel(); if (p != null) OpenDetail(p); });
            barName = UiKit.Label(bar, "name", "", 16, UiKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            var nb = UiKit.Box(barName.transform.parent, "nameBg", Color.white); nb.transform.SetSiblingIndex(barName.transform.GetSiblingIndex());
            nameBg = nb;
            for (int i = 0; i < 3; i++) { barHearts[i] = UiKit.Box(bar, "heart" + i, Color.white); barHearts[i].preserveAspect = true; }
            barPlanned = UiKit.Label(bar, "planned", "", 15, Color.white, TextAnchor.MiddleLeft);
            barGauge = UiKit.Box(bar, "gauge", Color.white); barGauge.preserveAspect = true;
            prevBtn = UiKit.Btn(bar, "prev", "◀", 16, new Color32(0x0A, 0x38, 0x50, 255), Color.white, () => CycleSel(-1));
            nextSelBtn = UiKit.Btn(bar, "nextSel", "▶", 16, new Color32(0x0A, 0x38, 0x50, 255), Color.white, () => CycleSel(1));

            // 대화창 (원작: 흰 바탕 둥근 상자, 파란 테두리)
            var dbg = UiKit.Box(dialog, "frame", new Color32(0x3A, 0x6E, 0xC8, 255)); UiKit.Stretch(dbg.rectTransform, 6, 6, 6, 6);
            var din = UiKit.Box(dialog, "inner", Color.white); UiKit.Stretch(din.rectTransform, 10, 10, 10, 10);
            dlgTitle = UiKit.Label(dialog, "title", "", 13, new Color32(0x9C, 0x52, 0x20, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            dlgSpeaker = UiKit.Label(dialog, "speaker", "", 15, new Color32(0x1E, 0x46, 0xC8, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            dlgText = UiKit.Label(dialog, "text", "", 17, UiKit.Ink, TextAnchor.UpperLeft);
            dlgText.supportRichText = true;
            choiceHost = UiKit.Node(dialog, "choices");
            nextBtn = UiKit.Btn(dialog, "next", "▼", 18, new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, OnNext);

            // 하단 메뉴
            var mb = UiKit.Box(menu, "bg", new Color32(0xE8, 0xD0, 0x90, 255)); UiKit.Stretch(mb.rectTransform, 0, 0, 0, 0);
            string[] labels = { "활쏘기", "아이템", "큐피트", "관찰" };
            Action[] acts = { () => OpenTools("arrow"), () => OpenTools("item"), OpenCupid, OpenObserve };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                menuBtns[i] = UiKit.Btn(menu, "m" + i, labels[i], 16, new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => acts[k]());
            }
            var bow = art.Ui("btn_bow");
            if (bow != null) { var ic = UiKit.Box(menuBtns[0].transform, "icon", Color.white, bow); ic.preserveAspect = true; bowIcon = ic; }
        }
        Image nameBg, bowIcon; Button prevBtn, nextSelBtn;

        void LayoutHud()
        {
            hudText.fontSize = Px(18);
            UiKit.Stretch(hudText.rectTransform, Px(8), 0, Px(8), 0);
        }

        void LayoutBar()
        {
            float h = lay.FamilyStrip.H, w = lay.FamilyStrip.W, pad = Px(6), ar = Px(30);
            UiKit.SetPx(prevBtn.GetComponent<RectTransform>(), 0, 0, ar, h);
            UiKit.SetPx(nextSelBtn.GetComponent<RectTransform>(), w - ar, 0, ar, h);
            float x = ar + pad, ph = h - 2 * pad;
            UiKit.SetPx(barPortrait.rectTransform, x, pad, ph, ph); x += ph + pad;
            float nameW = Px(110);
            UiKit.SetPx(nameBg.rectTransform, x, pad, nameW, h * 0.45f);
            UiKit.SetPx(barName.rectTransform, x + Px(6), pad, nameW - Px(6), h * 0.45f); barName.fontSize = Px(15);
            float hx = x + nameW + pad, hsz = h * 0.42f;
            for (int i = 0; i < 3; i++) UiKit.SetPx(barHearts[i].rectTransform, hx + i * (hsz + Px(2)), pad, hsz, hsz);
            UiKit.SetPx(barPlanned.rectTransform, x, h * 0.5f, w - x - ar - Px(40), h * 0.45f); barPlanned.fontSize = Px(14);
            UiKit.SetPx(barGauge.rectTransform, w - ar - Px(36), pad, Px(30), h - 2 * pad);
        }

        void LayoutDialog()
        {
            float w = lay.EventPanel.W, h = lay.EventPanel.H, pad = Px(18);
            dlgTitle.fontSize = Px(12); dlgSpeaker.fontSize = Px(14); dlgText.fontSize = Px(17);
            UiKit.SetPx(dlgTitle.rectTransform, pad, Px(12), w - 2 * pad, Px(18));
            UiKit.SetPx(dlgSpeaker.rectTransform, pad, Px(30), w - 2 * pad, Px(20));
            UiKit.SetPx(dlgText.rectTransform, pad, Px(52), w - 2 * pad, h - Px(52) - Px(56));
            UiKit.SetPx(nextBtn.GetComponent<RectTransform>(), w - pad - Px(56), h - Px(56), Px(48), Px(44));
        }

        void LayoutMenu()
        {
            float w = lay.Controls.W, h = lay.Controls.H, pad = Px(6), bw = (w - 5 * pad) / 4f, bh = h - 2 * pad;
            for (int i = 0; i < 4; i++)
            {
                UiKit.SetPx(menuBtns[i].GetComponent<RectTransform>(), pad + i * (bw + pad), pad, bw, bh);
                var t = menuBtns[i].GetComponentInChildren<Text>(); t.fontSize = Px(15);
                if (i == 0 && bowIcon != null)
                {
                    UiKit.SetPx(bowIcon.rectTransform, bw * 0.3f, Px(4), bw * 0.4f, bh * 0.55f);
                    t.alignment = TextAnchor.LowerCenter;
                }
            }
        }

        // =============================================================== 타이틀
        void ShowTitle()
        {
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
            UiKit.SetPx(l.GetComponent<RectTransform>(), x + pad, y, bw, Px(48));
        }
        RawImage titleHouse;

        void StartNew()
        {
            if (!art.Parts.Available) { BeginNew(false); return; }
            // 원작 도입부의 신님 질문 (원작 문구 확인됨: docs/07)
            playing = false;
            var body = Window("신님", 300); float w = BodyW(body);
            var q = UiKit.Label(body, "q", "내가 아는 가족을 지켜봐 주지 않겠느냐?", Px(16), UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.SetPx(q.rectTransform, 0, 0, w, Px(60));
            var a = UiKit.Btn(body, "yes", "네, 알겠습니다!", Px(16), new Color32(0xE8, 0x70, 0x40, 255), Color.white, () => BeginNew(false));
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
            EditLooks(0);
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

        string RoleOf(Person p)
        {
            int age = p.Age(session.Family.Today);
            return p.Gender == 0 ? (age >= 25 ? "father" : "son18") : "mother";
        }

        void OpenLookEditor(Person p, Action done)
        {
            if (p.Look == null) p.Look = DefaultLook(p);
            var L = p.Look; var lib = art.Parts;
            var body = Window(p.Name + " — 캐릭터 선택", 640); float w = BodyW(body), h = BodyH(body);
            // 미리보기
            var fig = UiKit.Box(body, "fig", Color.white, art.LookSprite(L) ?? UiKit.Circle); fig.preserveAspect = true;
            UiKit.SetPx(fig.rectTransform, 0, 0, Px(84), Px(150));
            float x = Px(92), cw = w - x, y = 0, rh = Px(40);
            Action redraw = () => OpenLookEditor(p, done);
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
            var ids = CharacterComposer.PresetIds(lib, RoleOf(p));
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
                var im = UiKit.Box(bg.transform, "i", Color.white, art.LookSprite(look)); im.preserveAspect = true; UiKit.Stretch(im.rectTransform, 0, 0, 0, 0);
                bg.gameObject.AddComponent<Button>().onClick.AddListener(() => { CharacterComposer.ApplyPreset(lib, L, id); redraw(); });
            }
            float py = gy + 2 * cell + Px(4);
            Stepper(body, "캐릭터 목록", (lookPage + 1) + " / " + pages + "쪽", 0, py, w, rh, d => { lookPage = Wrap(lookPage + d, pages); redraw(); });
            // 원작에 없는 추가 기능: 무작위 원작 파트 조합
            var rnd = UiKit.Btn(body, "rnd", "무작위 조합 (원작 파트 · 추가 기능)", Px(14), new Color32(0x10, 0x4E, 0x6E, 255), Color.white, () => {
                var r = CharacterComposer.Random(lib, new Rng((ulong)DateTime.UtcNow.Ticks), g); p.Look = r; redraw(); });
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
            try { var r = saves.Load(slot); session = new GameSession(r.Family, catalog); EnterGame(); Toast(r.Warning.Length > 0 ? r.Warning : "불러왔습니다"); }
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
            try { saves.Save(slot, session.Family, catalog); if (!quiet) Toast("저장했습니다 (" + SlotLabel(slot) + ")"); }
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
                img.gameObject.AddComponent<Button>().onClick.AddListener(() => { selectedId = pid; follow = true; RefreshBar(); OpenDetail(session.Family.Get(pid)); });
                var bub = UiKit.Box(img.transform, "bubble", Color.white, art.Ui("bubble_note_white") ?? art.Ui("bubble_note_pink")); bub.preserveAspect = true; bub.gameObject.SetActive(false);
                float x = UnityEngine.Random.Range(room[0] + 4, room[1] - 36);
                actors[p.Id] = new Actor { Id = p.Id, X = x, Target = x, Rt = img.rectTransform, Img = img, NextMove = Time.time + UnityEngine.Random.Range(2f, 6f), Bubble = bub };
            }
        }

        /// <summary>인물 그림: 원작 파트 조합(있으면) → 캡처 프레임 → null. 파트 조합의 걸음·뒷모습 프레임은 아직 없다(정면 고정).</summary>
        Sprite Figure(Person p, bool back, int frame)
        {
            var s = art.LookSprite(p.Look);
            return s ?? art.Frame(p.Character, back ? "back" : "front", frame);
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
                bool showBubble = !string.IsNullOrEmpty(p.PlannedStateId) && Mathf.Repeat(Time.time + a.Id * 1.7f, 6f) < 1.5f;
                a.Bubble.gameObject.SetActive(showBubble && a.Bubble.sprite != null);
                if (showBubble) UiKit.SetPx(a.Bubble.rectTransform, 0, -14 * hs, 32 * hs, 32 * hs);
            }
            if (sa != null)
            {
                float dx = Mod(sa.X - scrollX, W); if (dx > viewW + 32) dx -= W;
                float bob = Mathf.Sin(Time.time * 3f) * 2f;
                UiKit.SetPx(marker.rectTransform, (dx + 8) * hs, (floorTop + 6 + bob) * hs, 16 * hs, 16 * hs);
                UiKit.SetPx(cupid.rectTransform, (dx - 26) * hs, (floorTop - 18 + bob * 1.5f) * hs, 32 * hs, 32 * hs);
                var cs = art.Cupid((int)(Time.time * 6) % 4); if (cs != null) cupid.sprite = cs;
                cupid.transform.SetAsLastSibling();
            }
        }

        static float Mod(float a, float m) { return m <= 0 ? a : a - Mathf.Floor(a / m) * m; }

        void CycleSel(int dir)
        {
            var alive = session.Family.Members.FindAll(m => m.Alive); if (alive.Count == 0) return;
            int i = alive.FindIndex(m => m.Id == selectedId);
            selectedId = alive[((i + dir) % alive.Count + alive.Count) % alive.Count].Id; follow = true; RefreshBar(); RefreshDialog();
        }

        // =============================================================== 매 프레임
        void Update()
        {
            if (lastScreen.x != Screen.width || lastScreen.y != Screen.height || lastSafe != Screen.safeArea) ApplyLayout();
            if (toastBox.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBox.gameObject.SetActive(false);
            if (titleHouse != null && titleRoot.gameObject.activeSelf) { var u = titleHouse.uvRect; u.x += Time.deltaTime * 0.01f; titleHouse.uvRect = u; }
            if (!playing || session == null) return;
            UpdateActors();
            if (popupOpen || session.Paused || speed == 0) return;
            acc += Time.deltaTime * speed;
            bool started = false;
            while (acc >= SecondsPerDay && !started)
            {
                acc -= SecondsPerDay;
                int before = session.Family.Members.Count;
                started = session.StepDay();
                if (session.Family.Members.Count != before) RebuildActors();
                if (session.Family.Today - lastAutoDay >= 30) { lastAutoDay = session.Family.Today; SaveSlot("auto", true); }
            }
            if (started)
            {
                acc = 0; SaveSlot("auto", true);
                int self; if (session.Family.Active != null && session.Family.Active.Cast.TryGetValue("self", out self)) { selectedId = self; follow = true; }
                RefreshAll();
            }
            else RefreshHud();
        }

        void OnApplicationPause(bool paused) { if (paused && playing && session != null) SaveSlot("auto", true); }
        void OnApplicationQuit() { if (playing && session != null) { try { saves.Save("auto", session.Family, catalog); } catch (Exception) { } } }

        // =============================================================== 표시 갱신
        void RefreshAll() { RefreshHud(); RefreshBar(); RefreshDialog(); }

        void RefreshHud()
        {
            var f = session.Family;
            hudText.text = f.YearsAsFamily + "년가족   " + GameDate.Format(f.Today) + (speed == 0 ? "  ⏸" : speed > 1 ? "  ×" + speed : "");
        }

        void RefreshBar()
        {
            var p = Sel(); if (p == null) return;
            var por = art.LookSprite(p.Look) ?? art.Portrait(p.Character);
            barPortrait.sprite = por ?? UiKit.Circle; barPortrait.color = por != null ? Color.white : (p.Gender == 0 ? new Color32(0x5B, 0x8F, 0xC9, 255) : new Color32(0xD9, 0x6A, 0x8A, 255));
            barName.text = p.Name;
            for (int i = 0; i < 3; i++)
            {
                int v = p.Hearts - i * Person.HeartUnit;
                string key = v >= Person.HeartUnit ? "heart_full" : v >= Person.HeartUnit / 2 ? "heart_half" : "heart_empty";
                var s = art.Ui(key); barHearts[i].sprite = s ?? UiKit.Circle;
                barHearts[i].color = s != null ? Color.white : (key == "heart_full" ? Color.red : key == "heart_half" ? new Color(1, 0.5f, 0.6f) : new Color(0.4f, 0.5f, 1f));
            }
            barPlanned.text = "★" + PlannedTitle(p);
            var g = art.Ui(p.Immersion >= 170 ? "gauge_full" : p.Immersion >= 85 ? "gauge_mid" : "gauge_empty");
            barGauge.sprite = g; barGauge.enabled = g != null;
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
                dlgText.text = p == null ? "" : "<color=#1E46C8>" + p.Name + "</color>(" + p.Age(session.Family.Today) + "세)는\n이런 생각을 하는 모양이야!\n「" + PlannedTitle(p) + "」";
                nextBtn.gameObject.SetActive(false); LayoutDialog(); return;
            }
            dlgTitle.text = v.Title + "   [" + (v.Origin == "original" ? "원작 규칙" : "신규") + " · " + (v.TextSource == "original-translation" ? "원작 문구" : "임시 문구") + "]";
            dlgSpeaker.text = v.Speaker; dlgText.text = v.Text;
            if (v.NeedsChoice)
            {
                nextBtn.gameObject.SetActive(false);
                float pad = Px(18), w = lay.EventPanel.W - 2 * pad, bh = Px(46), gap = Px(6), total = v.Choices.Count * (bh + gap);
                float top = Mathf.Max(Px(52) + Px(40), lay.EventPanel.H - Px(14) - total);
                UiKit.SetPx(choiceHost, pad, top, w, total);
                UiKit.SetPx(dlgText.rectTransform, pad, Px(52), w, top - Px(56));
                for (int i = 0; i < v.Choices.Count; i++)
                {
                    string id = v.Choices[i].Id;
                    var b = UiKit.Btn(choiceHost, "c" + i, "▶ " + v.Choices[i].Text, Px(15), new Color32(0xE8, 0xF0, 0xFF, 255), new Color32(0x1E, 0x46, 0x9A, 255), () => { session.Choose(id); AfterEventStep(); });
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
            float x = Px(106), cw = w - x;
            Line(body, "꿈", string.IsNullOrEmpty(p.Dream) ? "지금은 없어…" : p.Dream, x, 0, cw);
            Line(body, "화살", string.IsNullOrEmpty(p.ArrowId) ? "맞은 화살 없음" : Interventions.Find(p.ArrowId).Name + " (" + (p.ArrowUntil - f.Today + 1) + "일 남음)", x, Px(34), cw);
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
            var gr = UiKit.Label(body, "grat", "신님에게 감사  " + f.Gratitude + "개", Px(14), UiKit.Ink, TextAnchor.MiddleLeft); UiKit.SetPx(gr.rectTransform, x, Px(166), cw, Px(24));
            PlannedStateDef st; catalog.States.TryGetValue(p.PlannedStateId ?? "", out st);
            var now = UiKit.Label(body, "now", "현재  " + PlannedTitle(p), Px(16), new Color32(0x1E, 0x46, 0x9A, 255), TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.SetPx(now.rectTransform, 0, Px(200), w, Px(48));
            string desc = st != null && !string.IsNullOrEmpty(st.Desc) ? st.Desc : "(예정 상태 설명은 원작 문구 팩에서 제공)";
            var dd = UiKit.Label(body, "desc", desc + "\n\n직업 코드 " + p.Job + " · 숙련 " + p.JobMastery + " · 몰입도 " + p.Immersion + "\n※ 직업 이름·하트 단위는 원작 확인 전 임시 표기", Px(14), UiKit.Ink, TextAnchor.UpperLeft);
            UiKit.SetPx(dd.rectTransform, 0, Px(250), w, BodyH(body) - Px(250));
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
            foreach (var t in Interventions.Tools)
            {
                if (t.Kind != kind) continue;
                int n = Interventions.Count(session.Family, t.Id);
                if (kind == "item" && n == 0 && t.Implemented) continue;
                string id = t.Id;
                var b = UiKit.Btn(body, t.Id, "", Px(14), n > 0 && t.Implemented ? Color.white : new Color(0.88f, 0.88f, 0.9f), UiKit.Ink, () =>
                {
                    var err = Interventions.Use(session.Family, Sel(), id);
                    ClosePopup(); Toast(err ?? (Interventions.Find(id).Name + "을(를) " + Sel().Name + "에게 썼습니다"));
                    if (err == null) SaveSlot("auto", true);
                });
                var lbl = b.GetComponentInChildren<Text>(); lbl.alignment = TextAnchor.MiddleLeft; lbl.fontStyle = FontStyle.Normal; lbl.supportRichText = true;
                lbl.text = "<b>" + t.Name + "</b>  ×" + n + (t.Implemented ? "" : "  (미구현)") + "\n<size=" + Px(12) + ">" + t.Desc + "</size>";
                UiKit.SetPx(b.GetComponent<RectTransform>(), 0, y, w, bh); y += bh + Px(6);
            }
            if (kind == "item" && y == 0)
            {
                var l = UiKit.Label(body, "none", "가진 아이템이 없습니다.\n(원작의 아이템 입수 경로는 아직 구현 전)", Px(15), UiKit.Ink, TextAnchor.UpperLeft); UiKit.SetPx(l.rectTransform, 0, 0, w, Px(80));
            }
        }

        // ---- 큐피트 (저장·기록·가계도·콘텐츠) ----
        void OpenCupid()
        {
            var body = Window("큐피트 메뉴", 460); float w = BodyW(body), bh = Px(54), y = 0;
            var items = new List<KeyValuePair<string, Action>> {
                new KeyValuePair<string, Action>("저장 / 불러오기", () => OpenSaveSlots()),
                new KeyValuePair<string, Action>("가계도", () => TextWindow("가계도", TreeText())),
                new KeyValuePair<string, Action>("이벤트 기록", () => TextWindow("이벤트 기록", LogText())),
                new KeyValuePair<string, Action>("콘텐츠 · 업데이트", OpenContent),
                new KeyValuePair<string, Action>("타이틀로", () => { SaveSlot("auto", true); ShowTitle(); }) };
            foreach (var kv in items)
            {
                var act = kv.Value;
                var b = UiKit.Btn(body, kv.Key, kv.Key, Px(16), Color.white, new Color32(0x1E, 0x46, 0x9A, 255), () => act());
                UiKit.SetPx(b.GetComponent<RectTransform>(), 0, y, w, bh); y += bh + Px(8);
            }
        }

        // ---- 관찰 (시간 속도·따라가기) ----
        void OpenObserve()
        {
            var body = Window("관찰", 300); float w = BodyW(body), bw = (w - Px(18)) / 4f;
            string[] l = { "정지", "×1", "×2", "×4" }; int[] sp = { 0, 1, 2, 4 };
            for (int i = 0; i < 4; i++)
            {
                int v = sp[i];
                var b = UiKit.Btn(body, "s" + i, l[i], Px(16), speed == v ? new Color32(0xE8, 0x70, 0x40, 255) : new Color32(0x3A, 0x6E, 0xC8, 255), Color.white, () => { speed = v; ClosePopup(); });
                UiKit.SetPx(b.GetComponent<RectTransform>(), i * (bw + Px(6)), 0, bw, Px(56));
            }
            var fb = UiKit.Btn(body, "follow", follow ? "선택한 사람 따라가기: 켜짐" : "선택한 사람 따라가기: 꺼짐", Px(15), Color.white, UiKit.Ink, () => { follow = !follow; ClosePopup(); });
            UiKit.SetPx(fb.GetComponent<RectTransform>(), 0, Px(70), w, Px(52));
            var hint = UiKit.Label(body, "hint", "집 화면을 좌우로 끌면 자유롭게 둘러볼 수 있습니다.", Px(13), UiKit.Ink, TextAnchor.UpperLeft); UiKit.SetPx(hint.rectTransform, 0, Px(130), w, Px(40));
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
                sb.AppendLine(GameDate.Format(h.Day) + " · " + (p != null ? p.Name : "-") + "\n  " + h.Title + (h.Choice.Length > 0 ? "  [선택: " + h.Choice + "]" : "") + "\n");
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
