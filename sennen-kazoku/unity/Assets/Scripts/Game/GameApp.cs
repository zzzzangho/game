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
    /// 앱 진입점. 씬 파일 없이 런타임에 UI 전체를 코드로 구성한다 (Bootstrap 참고).
    /// 규칙/이벤트/저장 로직은 전부 SennenKazoku.Core 에 있고, 여기서는 표시와 입력만 담당한다.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        // ---- 코어 ----
        PackStore packStore;
        SaveSystem saves;
        List<Pack> bundled;
        ContentCatalog catalog;
        GameSession session;
        readonly List<string> contentWarnings = new List<string>();
        readonly ContentUpdater updater = new ContentUpdater();

        // ---- UI ----
        Canvas canvas;
        RectTransform root, gameRoot, titleRoot, menuRoot;
        RectTransform topBar, strip, scene, eventPanel, controls, sceneFigures, stripContent, choiceHost;
        Text dateText, moodText, assetsText, eventTitle, eventBadge, eventText, eventSpeaker, captionText, toastText;
        Button nextBtn, speedBtn, pauseBtn, menuBtn;
        Image toastBox;
        LayoutCalculator.Result lay;
        Vector2 lastScreen; Rect lastSafe;
        float dp = 1f;
        float panelH;                        // 이벤트 패널의 현재 높이 (선택지가 많으면 장면 위로 확장)

        // ---- 진행 ----
        int speed = 1;                       // 0 정지, 1/2/4 배속
        float acc;
        const float SecondsPerDay = 0.35f;   // 임시 값
        bool playing, menuOpen;
        int lastAutoDay;
        float toastUntil;
        string menuTab = "save";
        readonly List<RectTransform> figures = new List<RectTransform>();

        // =============================================================== 시작
        void Start()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            Camera.main.clearFlags = CameraClearFlags.SolidColor; Camera.main.backgroundColor = UiKit.Bg;
            LoadContent();
            saves = new SaveSystem(Path.Combine(Application.persistentDataPath, "saves"));
            BuildRoot();
            ShowTitle();
        }

        void LoadContent()
        {
            bundled = new List<Pack>();
            foreach (var folder in new[] { "BundledPacks", "LocalPacks" })
            {
                foreach (var ta in Resources.LoadAll<TextAsset>(folder))
                {
                    var errs = new List<string>(); var p = Pack.Load(ta.text, errs);
                    if (p == null) { contentWarnings.Add("번들 팩 " + ta.name + " 오류: " + string.Join("; ", errs)); continue; }
                    bundled.Add(p);
                }
            }
            packStore = new PackStore(Path.Combine(Application.persistentDataPath, "content"));
            catalog = packStore.LoadCatalog(bundled, contentWarnings);
            if (catalog == null) catalog = new ContentCatalog();
        }

        // =============================================================== 레이아웃
        void BuildRoot()
        {
            var cgo = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            root = (RectTransform)cgo.transform;
            var bg = UiKit.Box(root, "bg", UiKit.Bg); UiKit.Stretch(bg.rectTransform, 0, 0, 0, 0);
            titleRoot = UiKit.Node(root, "title"); gameRoot = UiKit.Node(root, "game"); menuRoot = UiKit.Node(root, "menu");
            BuildGameViews();
            lastScreen = Vector2.zero;
        }

        void ApplyLayout()
        {
            float w = Screen.width, h = Screen.height;
            var sa = Screen.safeArea;
            float safeTop = h - (sa.y + sa.height);
            lay = LayoutCalculator.Compute(w, h, sa.x, safeTop, sa.width, sa.height, 160f * (w / 360f));
            dp = lay.Dp;
            foreach (var rt in new[] { titleRoot, gameRoot, menuRoot }) UiKit.SetPx(rt, 0, 0, w, h);
            UiKit.SetPx(topBar, lay.TopBar); UiKit.SetPx(strip, lay.FamilyStrip); UiKit.SetPx(scene, lay.Scene);
            SetPanel(lay.EventPanel.H); UiKit.SetPx(controls, lay.Controls);
            LayoutTop(); LayoutScene(); LayoutEventPanel(); LayoutControls();
            if (playing) { RefreshStrip(); RefreshEvent(); }
            if (titleRoot.gameObject.activeSelf) ShowTitle();
            if (menuOpen) OpenMenu(menuTab);
            lastScreen = new Vector2(w, h); lastSafe = sa;
        }

        int Px(float dpUnits) { return Mathf.RoundToInt(dpUnits * dp); }

        // =============================================================== 게임 화면 뼈대
        void BuildGameViews()
        {
            topBar = UiKit.Node(gameRoot, "topBar"); strip = UiKit.Node(gameRoot, "strip"); scene = UiKit.Node(gameRoot, "scene");
            eventPanel = UiKit.Node(gameRoot, "eventPanel"); controls = UiKit.Node(gameRoot, "controls");

            var tb = UiKit.Box(topBar, "bg", UiKit.Accent); UiKit.Stretch(tb.rectTransform, 0, 0, 0, 0);
            dateText = UiKit.Label(topBar, "date", "", 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            moodText = UiKit.Label(topBar, "mood", "", 16, Color.white, TextAnchor.MiddleLeft);
            assetsText = UiKit.Label(topBar, "assets", "", 16, Color.white, TextAnchor.MiddleRight);

            var sb = UiKit.Box(strip, "bg", UiKit.Soft); UiKit.Stretch(sb.rectTransform, 0, 0, 0, 0);
            stripContent = UiKit.Node(strip, "content");

            var sc = UiKit.Box(scene, "bg", new Color32(0xBF, 0xE3, 0xF2, 255)); UiKit.Stretch(sc.rectTransform, 0, 0, 0, 0);
            sceneFigures = UiKit.Node(scene, "figures");
            captionText = UiKit.Label(scene, "caption", "", 14, UiKit.Ink, TextAnchor.LowerCenter);

            var ep = UiKit.Box(eventPanel, "bg", UiKit.Panel); UiKit.Stretch(ep.rectTransform, 0, 0, 0, 0);
            eventBadge = UiKit.Label(eventPanel, "badge", "", 12, UiKit.AccentDark, TextAnchor.MiddleLeft, FontStyle.Bold);
            eventTitle = UiKit.Label(eventPanel, "title", "", 18, UiKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            eventSpeaker = UiKit.Label(eventPanel, "speaker", "", 14, UiKit.AccentDark, TextAnchor.MiddleLeft, FontStyle.Bold);
            eventText = UiKit.Label(eventPanel, "text", "", 18, UiKit.Ink, TextAnchor.UpperLeft);
            choiceHost = UiKit.Node(eventPanel, "choices");
            nextBtn = UiKit.Btn(eventPanel, "next", "다음 ▶", 18, UiKit.Accent, Color.white, OnNext);

            var cb = UiKit.Box(controls, "bg", UiKit.Soft); UiKit.Stretch(cb.rectTransform, 0, 0, 0, 0);
            pauseBtn = UiKit.Btn(controls, "pause", "정지", 20, UiKit.AccentDark, Color.white, () => { speed = 0; RefreshControls(); });
            speedBtn = UiKit.Btn(controls, "speed", "▶ ×1", 18, UiKit.Accent, Color.white, CycleSpeed);
            menuBtn = UiKit.Btn(controls, "menu", "메뉴", 18, UiKit.AccentDark, Color.white, () => OpenMenu(menuTab));

            toastBox = UiKit.Box(gameRoot, "toast", new Color(0, 0, 0, 0.8f));
            toastText = UiKit.Label(toastBox.transform, "t", "", 16, Color.white, TextAnchor.MiddleCenter);
            toastBox.gameObject.SetActive(false);
        }

        void LayoutTop()
        {
            float pad = Px(12), w = lay.TopBar.W, h = lay.TopBar.H;
            dateText.fontSize = Px(18); moodText.fontSize = Px(13); assetsText.fontSize = Px(13);
            UiKit.SetPx(dateText.rectTransform, pad, 0, w * 0.55f, h * 0.55f);
            UiKit.SetPx(moodText.rectTransform, pad, h * 0.5f, w * 0.5f, h * 0.45f);
            UiKit.SetPx(assetsText.rectTransform, w * 0.45f, h * 0.35f, w * 0.55f - pad, h * 0.6f);
        }

        void LayoutScene()
        {
            captionText.fontSize = Px(13);
            UiKit.SetPx(captionText.rectTransform, 0, lay.Scene.H - Px(34), lay.Scene.W, Px(30));
            UiKit.SetPx(sceneFigures.GetComponent<RectTransform>(), 0, 0, lay.Scene.W, lay.Scene.H);
        }

        /// <summary>이벤트 패널을 아래쪽 고정, 위쪽으로 h 만큼 키운다.</summary>
        void SetPanel(float h)
        {
            panelH = h;
            UiKit.SetPx(eventPanel, lay.EventPanel.X, lay.EventPanel.Bottom - h, lay.EventPanel.W, h);
        }

        void LayoutEventPanel()
        {
            if (panelH <= 0) panelH = lay.EventPanel.H;
            float pad = Px(14), w = lay.EventPanel.W, h = panelH;
            eventBadge.fontSize = Px(11); eventTitle.fontSize = Px(16); eventSpeaker.fontSize = Px(13); eventText.fontSize = Px(16);
            UiKit.SetPx(eventBadge.rectTransform, pad, Px(4), w - 2 * pad, Px(16));
            UiKit.SetPx(eventTitle.rectTransform, pad, Px(20), w - 2 * pad, Px(24));
            UiKit.SetPx(eventSpeaker.rectTransform, pad, Px(46), w - 2 * pad, Px(18));
            float textH = h - Px(66) - Px(60);
            UiKit.SetPx(eventText.rectTransform, pad, Px(64), w - 2 * pad, Mathf.Max(Px(60), textH));
            UiKit.SetPx(nextBtn.GetComponent<RectTransform>(), w - pad - Px(130), h - Px(54), Px(130), Px(48));
            UiKit.SetPx(choiceHost, pad, h - Px(60) - Px(0), w - 2 * pad, Px(56));
            nextBtn.GetComponentInChildren<Text>().fontSize = Px(16);
        }

        void LayoutControls()
        {
            float w = lay.Controls.W, h = lay.Controls.H, pad = Px(8), bh = Mathf.Max(Px(LayoutCalculator.MinTouchDp), h - 2 * pad);
            float bw = (w - 4 * pad) / 3f;
            UiKit.SetPx(pauseBtn.GetComponent<RectTransform>(), pad, (h - bh) / 2, bw, bh);
            UiKit.SetPx(speedBtn.GetComponent<RectTransform>(), 2 * pad + bw, (h - bh) / 2, bw, bh);
            UiKit.SetPx(menuBtn.GetComponent<RectTransform>(), 3 * pad + 2 * bw, (h - bh) / 2, bw, bh);
            foreach (var b in new[] { pauseBtn, speedBtn, menuBtn }) b.GetComponentInChildren<Text>().fontSize = Px(16);
        }

        // =============================================================== 타이틀
        void ShowTitle()
        {
            playing = false; menuOpen = false;
            gameRoot.gameObject.SetActive(false); menuRoot.gameObject.SetActive(false); titleRoot.gameObject.SetActive(true);
            Clear(titleRoot);
            if (lay == null) return;
            float w = lay.Safe.W, x = lay.Safe.X, y = lay.Safe.Y + lay.Safe.H * 0.08f, pad = Px(24), bw = w - 2 * pad, bh = Px(56);
            var t = UiKit.Label(titleRoot, "t", "천년가족", Px(40), UiKit.AccentDark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.SetPx(t.rectTransform, x, y, w, Px(60)); y += Px(60);
            var sub = UiKit.Label(titleRoot, "s", "모바일 재구현 프로토타입 (비공식 팬 프로젝트)\n임시 그래픽 · 원작 에셋 미포함", Px(13), UiKit.Ink, TextAnchor.UpperCenter);
            UiKit.SetPx(sub.rectTransform, x, y, w, Px(44)); y += Px(60);
            bool hasSave = saves.Exists("auto");
            var cont = UiKit.Btn(titleRoot, "continue", hasSave ? "이어하기 (자동 저장)" : "이어하기 (저장 없음)", Px(18), hasSave ? UiKit.Accent : Color.gray, Color.white, () => { if (hasSave) LoadSlot("auto"); });
            UiKit.SetPx(cont.GetComponent<RectTransform>(), x + pad, y, bw, bh); y += bh + Px(12);
            var ng = UiKit.Btn(titleRoot, "new", "새 게임", Px(18), UiKit.Accent, Color.white, StartNew);
            UiKit.SetPx(ng.GetComponent<RectTransform>(), x + pad, y, bw, bh); y += bh + Px(12);
            var ld = UiKit.Btn(titleRoot, "load", "저장 슬롯 불러오기", Px(18), UiKit.AccentDark, Color.white, () => OpenMenu("save"));
            UiKit.SetPx(ld.GetComponent<RectTransform>(), x + pad, y, bw, bh); y += bh + Px(20);
            var info = UiKit.Label(titleRoot, "info", "콘텐츠 팩 " + catalog.Packs.Count + "개 · 이벤트 " + catalog.Events.Count + "개" + (contentWarnings.Count > 0 ? "\n⚠ 콘텐츠 경고 " + contentWarnings.Count + "건 (메뉴 > 콘텐츠)" : ""), Px(12), UiKit.Ink, TextAnchor.UpperCenter);
            UiKit.SetPx(info.rectTransform, x, y, w, Px(50));
        }

                void StartNew()
        {
            var f = NewGame.Create((ulong)DateTime.UtcNow.Ticks);
            session = new GameSession(f, catalog); EnterGame();
            Toast("새 가족이 시작되었습니다");
        }

        void LoadSlot(string slot)
        {
            try
            {
                var r = saves.Load(slot);
                // 저장 후 팩이 바뀌었을 수 있음: 현재 카탈로그로 이어간다. 진행 중 이벤트는 스냅샷이 있어 안전.
                session = new GameSession(r.Family, catalog); EnterGame();
                Toast(r.Warning.Length > 0 ? r.Warning : "불러왔습니다 (" + slot + ")");
            }
            catch (Exception e) { Toast("불러오기 실패: " + e.Message); }
        }

        void EnterGame()
        {
            menuOpen = false; playing = true; speed = 1; acc = 0; lastAutoDay = session.Family.Today;
            titleRoot.gameObject.SetActive(false); menuRoot.gameObject.SetActive(false); gameRoot.gameObject.SetActive(true);
            RebuildFigures(); RefreshAll();
        }

        void SaveSlot(string slot)
        {
            try { saves.Save(slot, session.Family, catalog); Toast("저장했습니다 (" + SlotLabel(slot) + ")"); }
            catch (Exception e) { Toast("저장 실패: " + e.Message); }
        }

        static string SlotLabel(string s) { return s == "auto" ? "자동 저장" : s.Replace("slot", "슬롯 "); }

        // =============================================================== 매 프레임
        void Update()
        {
            if (lastScreen.x != Screen.width || lastScreen.y != Screen.height || lastSafe != Screen.safeArea) ApplyLayout();
            if (toastBox.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBox.gameObject.SetActive(false);
            if (!playing || menuOpen || session == null) return;
            Animate();
            if (session.Paused || speed == 0) return;
            acc += Time.deltaTime * speed;
            bool started = false;
            while (acc >= SecondsPerDay && !started)
            {
                acc -= SecondsPerDay;
                started = session.StepDay();
                if (session.Family.Today - lastAutoDay >= 30) { lastAutoDay = session.Family.Today; SaveSlot("auto"); }
            }
            if (started) { acc = 0; SaveSlot("auto"); }
            RefreshTop(); if (started) RefreshAll();
        }

        void Animate()
        {
            for (int i = 0; i < figures.Count; i++)
            {
                var p = figures[i].anchoredPosition;
                figures[i].anchoredPosition = new Vector2(p.x, -(figures[i].GetComponent<FigureTag>().BaseY + Mathf.Sin(Time.time * 2f + i) * Px(3)));
            }
        }

        void OnApplicationPause(bool paused) { if (paused && playing && session != null) SaveSlot("auto"); }
        void OnApplicationQuit() { if (playing && session != null) { try { saves.Save("auto", session.Family, catalog); } catch (Exception) { } } }

        // =============================================================== 표시 갱신
        void RefreshAll() { RefreshTop(); RefreshStrip(); RebuildFigures(); RefreshEvent(); RefreshControls(); }

        void RefreshTop()
        {
            var f = session.Family;
            dateText.text = GameDate.Format(f.Today);
            moodText.text = "가족 무드 " + Family.MoodLevel(f.Mood) + "/5";
            assetsText.text = "자산 " + f.Assets.ToString("N0") + "엔 · " + f.Name;
        }

        void RefreshStrip()
        {
            Clear(stripContent);
            var f = session.Family; float pad = Px(8), cw = Px(78), ch = lay.FamilyStrip.H - 2 * pad, x = pad;
            UiKit.SetPx(stripContent, 0, 0, lay.FamilyStrip.W, lay.FamilyStrip.H);
            foreach (var p in f.Members)
            {
                if (!p.Alive) continue;
                var pp = p;
                var b = UiKit.Btn(stripContent, "m" + p.Id, "", Px(11), p.Gender == 0 ? new Color32(0x8F, 0xBC, 0xE3, 255) : new Color32(0xE8, 0xA0, 0xB5, 255), UiKit.Ink, () => { menuTab = "family"; OpenMenu("family"); });
                UiKit.SetPx(b.GetComponent<RectTransform>(), x, pad, cw, ch);
                b.GetComponentInChildren<Text>().text = p.Name + "\n" + p.Age(f.Today) + "세 · 지" + Stat.Rank(p.Stats[0]) + (p.Hearts > 0 ? " ♥" : "");
                b.GetComponentInChildren<Text>().fontSize = Px(12);
                x += cw + pad;
            }
        }

        void RebuildFigures()
        {
            foreach (var g in figures) if (g != null) Destroy(g.gameObject);
            figures.Clear();
            if (session == null || lay == null) return;
            var alive = session.Family.Members.FindAll(m => m.Alive);
            float w = lay.Scene.W, h = lay.Scene.H;
            var ground = UiKit.Box(sceneFigures, "ground", new Color32(0xA8, 0xD5, 0x8E, 255));
            UiKit.SetPx(ground.rectTransform, 0, h * 0.62f, w, h * 0.38f);
            var house = UiKit.Box(sceneFigures, "house", new Color32(0xE8, 0xC9, 0x9A, 255));
            UiKit.SetPx(house.rectTransform, w * 0.1f, h * 0.18f, w * 0.8f, h * 0.5f);
            var roof = UiKit.Box(sceneFigures, "roof", new Color32(0xB5, 0x5A, 0x3C, 255));
            UiKit.SetPx(roof.rectTransform, w * 0.06f, h * 0.08f, w * 0.88f, h * 0.12f);
            float fw = Mathf.Min(Px(64), w / Mathf.Max(3, alive.Count + 1)), step = w * 0.8f / Mathf.Max(1, alive.Count);
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i]; int age = p.Age(session.Family.Today);
                float size = fw * (age < 6 ? 0.6f : age < 15 ? 0.8f : 1f);
                float x = w * 0.1f + step * i + (step - size) / 2f, baseY = Mathf.Min(h * 0.55f - size * 0.2f, h - Px(36) - size);
                var im = UiKit.Box(sceneFigures, "fig" + p.Id, p.Gender == 0 ? new Color32(0x5B, 0x8F, 0xC9, 255) : new Color32(0xD9, 0x6A, 0x8A, 255), UiKit.Circle);
                UiKit.SetPx(im.rectTransform, x, baseY, size, size);
                im.gameObject.AddComponent<FigureTag>().BaseY = baseY;
                var l = UiKit.Label(im.transform, "n", p.Name, Px(11), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                l.horizontalOverflow = HorizontalWrapMode.Overflow;
                var lr = l.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
                figures.Add(im.rectTransform);
            }
            // 예정 상태 문구(원작은 가족 화면 아래쪽에 표시): 데이터가 있는 구성원 중 한 명을 순환 표시
            string cap = "";
            foreach (var p in alive)
            {
                PlannedStateDef st;
                if (!string.IsNullOrEmpty(p.PlannedStateId) && catalog.States.TryGetValue(p.PlannedStateId, out st)) { cap = p.Name + ": " + st.Title; break; }
            }
            captionText.text = cap + (cap.Length > 0 ? "\n" : "") + "※ 임시 그래픽";
            captionText.transform.SetAsLastSibling();
        }

        void RefreshControls()
        {
            UiKit.SetBtnText(speedBtn, speed == 0 ? "▶ 재생" : "▶ ×" + speed);
            pauseBtn.GetComponent<Image>().color = speed == 0 ? UiKit.Accent : UiKit.AccentDark;
        }

        void CycleSpeed() { speed = speed == 0 ? 1 : speed == 1 ? 2 : speed == 2 ? 4 : 1; RefreshControls(); }

        void RefreshEvent()
        {
            Clear(choiceHost);
            var v = session != null ? session.View() : null;
            if (v == null)
            {
                eventBadge.text = ""; eventTitle.text = "가족의 하루"; eventSpeaker.text = "";
                var last = session.Family.History.Count > 0 ? session.Family.History[session.Family.History.Count - 1] : null;
                eventText.text = "시간이 흘러가고 있습니다…" + (last != null ? "\n\n최근 이벤트: " + last.Title : "");
                nextBtn.gameObject.SetActive(false);
                return;
            }
            eventBadge.text = Badge(v);
            eventTitle.text = v.Title; eventSpeaker.text = v.Speaker; eventText.text = v.Text;
            if (v.NeedsChoice)
            {
                nextBtn.gameObject.SetActive(false);
                float pad = Px(14), w = lay.EventPanel.W - 2 * pad, bh = Px(48), gap = Px(6);
                float total = v.Choices.Count * (bh + gap);
                SetPanel(Mathf.Max(lay.EventPanel.H, Px(64) + Px(66) + total + gap * 2));   // 선택지가 많으면 장면 위로 확장
                UiKit.SetPx(choiceHost, pad, panelH - total - gap, w, total);
                for (int i = 0; i < v.Choices.Count; i++)
                {
                    var c = v.Choices[i]; string id = c.Id;
                    var b = UiKit.Btn(choiceHost, "c" + i, c.Text, Px(15), UiKit.Accent, Color.white, () => { session.Choose(id); AfterEventStep(); });
                    UiKit.SetPx(b.GetComponent<RectTransform>(), 0, i * (bh + gap), w, bh);
                }
                UiKit.SetPx(eventText.rectTransform, Px(14), Px(64), lay.EventPanel.W - Px(28), Mathf.Max(Px(40), panelH - Px(64) - total - gap * 2));
            }
            else
            {
                SetPanel(lay.EventPanel.H); LayoutEventPanel(); nextBtn.gameObject.SetActive(true);
            }
        }

        static string Badge(EventView v)
        {
            string src = v.Origin == "original" ? "원작 규칙" : "신규 이벤트";
            string c = v.Certainty == "confirmed" ? "확인됨" : v.Certainty == "estimated" ? "추정" : "임시";
            string t = v.TextSource == "original-translation" ? "원작 문구" : "임시 문구";
            return src + " · 규칙 " + c + " · " + t;
        }

        void OnNext() { session.Advance(); AfterEventStep(); }

        void AfterEventStep()
        {
            bool finished = !session.Paused;
            RefreshEvent();
            if (finished) { SaveSlot("auto"); RefreshAll(); }
        }

        // =============================================================== 메뉴
        void OpenMenu(string tab)
        {
            if (!playing) tab = "save";            // 타이틀에서는 불러오기만 가능
            menuOpen = true; menuTab = tab; menuRoot.gameObject.SetActive(true); Clear(menuRoot);
            var shade = UiKit.Box(menuRoot, "shade", new Color(0, 0, 0, 0.5f)); shade.raycastTarget = true; UiKit.Stretch(shade.rectTransform, 0, 0, 0, 0);
            float pad = Px(10), x = lay.Safe.X + pad, y = lay.Safe.Y + pad, w = lay.Safe.W - 2 * pad, h = lay.Safe.H - 2 * pad;
            var panel = UiKit.Box(menuRoot, "panel", UiKit.Panel); UiKit.SetPx(panel.rectTransform, x, y, w, h);
            string[,] tabs = { { "save", "저장" }, { "family", "가족" }, { "tree", "가계도" }, { "log", "기록" }, { "content", "콘텐츠" } };
            float tw = playing ? w / 5f : w, th = Px(46);
            for (int i = 0; i < (playing ? 5 : 1); i++)
            {
                string key = tabs[i, 0];
                var b = UiKit.Btn(menuRoot, "tab" + key, tabs[i, 1], Px(14), key == tab ? UiKit.Accent : UiKit.Soft, key == tab ? Color.white : UiKit.Ink, () => OpenMenu(key));
                UiKit.SetPx(b.GetComponent<RectTransform>(), x + i * tw, y, tw, th);
            }
            float cy = y + th + pad, ch = h - th - Px(56) - 2 * pad;
            var body = UiKit.Node(menuRoot, "body"); UiKit.SetPx(body, x + pad, cy, w - 2 * pad, ch);
            switch (tab)
            {
                case "save": BuildSaveTab(body, w - 2 * pad, ch); break;
                case "family": TextTab(body, w - 2 * pad, ch, FamilyText()); break;
                case "tree": TextTab(body, w - 2 * pad, ch, TreeText()); break;
                case "log": TextTab(body, w - 2 * pad, ch, LogText()); break;
                case "content": BuildContentTab(body, w - 2 * pad, ch); break;
            }
            var close = UiKit.Btn(menuRoot, "close", "닫기", Px(16), UiKit.AccentDark, Color.white, CloseMenu);
            UiKit.SetPx(close.GetComponent<RectTransform>(), x + pad, y + h - Px(50) - pad, w - 2 * pad, Px(50));
        }

        void CloseMenu() { menuOpen = false; menuRoot.gameObject.SetActive(false); if (!playing) ShowTitle(); else RefreshAll(); }

        void BuildSaveTab(RectTransform body, float w, float h)
        {
            float bh = Px(52), gap = Px(8), y = 0;
            foreach (var slot in SaveSystem.Slots)
            {
                string s = slot; string desc = SlotLabel(s);
                if (saves.Exists(s))
                {
                    try { var r = saves.Load(s); desc += " — " + r.Family.Name + " " + GameDate.Format(r.Family.Today) + (r.UsedBackup ? " (백업)" : ""); }
                    catch (Exception) { desc += " — 손상됨"; }
                }
                else desc += " — 비어 있음";
                var l = UiKit.Label(body, "l" + s, desc, Px(13), UiKit.Ink, TextAnchor.MiddleLeft);
                UiKit.SetPx(l.rectTransform, 0, y, w, Px(26)); y += Px(26);
                float half = (w - gap) / 2f;
                bool canSave = playing && s != "auto";
                var save = UiKit.Btn(body, "s" + s, "저장", Px(14), canSave ? UiKit.Accent : Color.gray, Color.white, () => { if (canSave) { SaveSlot(s); OpenMenu("save"); } });
                UiKit.SetPx(save.GetComponent<RectTransform>(), 0, y, half, bh);
                var load = UiKit.Btn(body, "o" + s, "불러오기", Px(14), saves.Exists(s) ? UiKit.AccentDark : Color.gray, Color.white, () => { if (saves.Exists(s)) LoadSlot(s); });
                UiKit.SetPx(load.GetComponent<RectTransform>(), half + gap, y, half, bh); y += bh + gap;
            }
            if (playing)
            {
                var t = UiKit.Btn(body, "totitle", "타이틀로", Px(14), UiKit.AccentDark, Color.white, () => { SaveSlot("auto"); menuRoot.gameObject.SetActive(false); ShowTitle(); });
                UiKit.SetPx(t.GetComponent<RectTransform>(), 0, y, w, bh);
            }
        }

        void TextTab(RectTransform body, float w, float h, string text)
        {
            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            scrollGo.transform.SetParent(body, false);
            var srt = (RectTransform)scrollGo.transform; UiKit.SetPx(srt, 0, 0, w, h);
            scrollGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            scrollGo.GetComponent<Mask>().showMaskGraphic = false;
            var content = UiKit.Node(srt, "content"); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, 100);
            var t = UiKit.Label(content, "t", text, Px(14), UiKit.Ink, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            var tr = t.rectTransform; tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1); tr.anchoredPosition = Vector2.zero; tr.sizeDelta = new Vector2(0, 100);
            var fit = t.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cfit = content.gameObject.AddComponent<ContentSizeFitter>(); cfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>(); vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            var sr = scrollGo.GetComponent<ScrollRect>(); sr.content = content; sr.horizontal = false; sr.vertical = true; sr.viewport = srt;
            sr.movementType = ScrollRect.MovementType.Elastic; sr.scrollSensitivity = 30;
        }

        string FamilyText()
        {
            var f = session.Family; var sb = new StringBuilder();
            sb.AppendLine(f.Name + " · 무드 " + Family.MoodLevel(f.Mood) + "단계 · 자산 " + f.Assets.ToString("N0") + "엔\n");
            foreach (var p in f.Members)
            {
                sb.AppendLine((p.Alive ? "" : "† ") + p.Name + " (" + (p.Gender == 0 ? "남" : "여") + ", " + p.Age(f.Today) + "세)");
                sb.AppendLine("  지력 " + Stat.Rank(p.Stats[0]) + "(" + p.Stats[0] + ")  체력 " + Stat.Rank(p.Stats[1]) + "(" + p.Stats[1] + ")  매력 " + Stat.Rank(p.Stats[2]) + "(" + p.Stats[2] + ")  운 " + Stat.Rank(p.Stats[3]) + "(" + p.Stats[3] + ")");
                sb.AppendLine("  하트 " + p.Hearts + " · 몰입도 " + p.Immersion + " · 직업코드 " + p.Job + " · 숙련 " + p.JobMastery);
                PlannedStateDef st;
                if (!string.IsNullOrEmpty(p.PlannedStateId) && catalog.States.TryGetValue(p.PlannedStateId, out st)) sb.AppendLine("  예정: " + st.Title);
                sb.AppendLine();
            }
            sb.AppendLine("※ 하트 단위·직업 이름은 아직 원작 값을 확인하지 못해 임시 표기입니다.");
            return sb.ToString();
        }

        string TreeText()
        {
            var f = session.Family; var sb = new StringBuilder("가계도 (부모 기록이 없는 사람부터)\n\n"); var seen = new HashSet<int>();
            foreach (var p in f.Members) if (p.FatherId < 0 && p.MotherId < 0 && seen.Add(p.Id)) Tree(f, p, 0, sb, seen);
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
                sb.AppendLine(GameDate.Format(h.Day) + " · " + (p != null ? p.Name : "-") + "\n  " + h.Title + (h.Choice.Length > 0 ? "  [선택: " + h.Choice + "]" : "") + "  (" + h.EventId + " v" + h.EventVersion + ")\n");
            }
            return sb.ToString();
        }

        void BuildContentTab(RectTransform body, float w, float h)
        {
            var sb = new StringBuilder("설치된 콘텐츠 팩\n");
            foreach (var p in catalog.Packs) sb.AppendLine("• " + p.PackId + " v" + p.Version + " — " + p.Title + " (이벤트 " + p.Events.Count + ")");
            sb.AppendLine("\n저장 스키마 v" + SaveSystem.CurrentSchema + " · 데이터 계약 v" + Capabilities.Contract);
            if (contentWarnings.Count > 0) { sb.AppendLine("\n경고:"); foreach (var m in contentWarnings) sb.AppendLine("- " + m); }
            if (updater.Report.Count > 0) { sb.AppendLine("\n업데이트 결과:"); foreach (var m in updater.Report) sb.AppendLine("- " + m); }
            float bh = Px(50);
            TextTab(body, w, h - bh - Px(8), sb.ToString());
            var b = UiKit.Btn(body, "update", "콘텐츠 업데이트 확인", Px(15), UiKit.Accent, Color.white, () =>
            {
                StartCoroutine(updater.CheckAndInstall(packStore, bundled, n =>
                {
                    if (n > 0)
                    {
                        var warn = new List<string>(); var cat = packStore.LoadCatalog(bundled, warn); contentWarnings.AddRange(warn);
                        if (cat != null) { catalog = cat; if (session != null) session.ReplaceCatalog(cat); }
                    }
                    Toast(n > 0 ? n + "개 팩이 적용되었습니다" : "적용할 업데이트가 없습니다");
                    if (menuOpen) OpenMenu("content");
                }));
            });
            UiKit.SetPx(b.GetComponent<RectTransform>(), 0, h - bh, w, bh);
        }

        // =============================================================== 공통
        void Toast(string s)
        {
            toastText.text = s; toastBox.gameObject.SetActive(true); toastBox.transform.SetAsLastSibling(); toastUntil = Time.unscaledTime + 2.2f;
            if (lay == null) return;
            UiKit.SetPx(toastBox.rectTransform, lay.Safe.X + Px(20), lay.Safe.Y + lay.Safe.H * 0.5f, lay.Safe.W - Px(40), Px(48));
            toastText.fontSize = Px(14); UiKit.Stretch(toastText.rectTransform, 8, 4, 8, 4);
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
