using System.Collections.Generic;
using UnityEngine;
using SennenKazoku.Core;

namespace SennenKazoku.Game
{
    /// <summary>
    /// 그래픽 공급자. Resources/LocalArt (사용자 ROM 에서 tools/gba_capture/build_local_art.py 로 추출, git 제외)가 있으면
    /// 원작 그래픽을, 없으면 null 을 돌려 호출부가 임시 도형을 쓰게 한다.
    /// 교체: 같은 이름의 PNG(.png.bytes)와 manifest.json 만 바꾸면 직접 그린 그림으로 대체된다.
    /// </summary>
    public sealed class ArtLibrary
    {
        public bool Available { get; private set; }
        public Dictionary<string, object> Manifest { get; private set; }
        public Dictionary<string, object> StartFamily { get; private set; }
        readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        public readonly List<int[]> Rooms = new List<int[]>();
        public PartsLibrary Parts = new PartsLibrary();                  // 원작 캐릭터 파트 (LocalArt/parts.json + parts.bin.bytes)
        readonly Dictionary<string, Sprite> lookCache = new Dictionary<string, Sprite>();
        public int HouseWidth = 960, HouseHeight = 160, FloorY = 112;

        public static ArtLibrary Load()
        {
            var a = new ArtLibrary();
            var pj = Resources.Load<TextAsset>("LocalArt/parts"); var pb = Resources.Load<TextAsset>("LocalArt/parts.bin");
            if (pj != null && pb != null) a.Parts = PartsLibrary.Load(pj.text, pb.bytes);
            var mt = Resources.Load<TextAsset>("LocalArt/manifest");
            if (mt == null) return a;
            a.Manifest = J.Obj(MiniJson.Parse(mt.text));
            var house = J.Child(a.Manifest, "house");
            if (house != null)
            {
                a.HouseWidth = J.Int(house, "width", 960); a.HouseHeight = J.Int(house, "height", 160); a.FloorY = J.Int(house, "floorY", 112);
                foreach (var r in J.List(house, "rooms")) { var l = J.Arr(r); a.Rooms.Add(new[] { System.Convert.ToInt32(l[0]), System.Convert.ToInt32(l[1]) }); }
            }
            var sf = Resources.Load<TextAsset>("LocalArt/start_family");
            if (sf != null) a.StartFamily = J.Obj(MiniJson.Parse(sf.text));
            a.Available = a.Texture(J.Str(house, "file")) != null;
            return a;
        }

        public Texture2D Texture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D t;
            if (texCache.TryGetValue(name, out t)) return t;
            var ta = Resources.Load<TextAsset>("LocalArt/" + name + ".png");     // 파일명 name.png.bytes
            if (ta == null) { texCache[name] = null; return null; }
            t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(ta.bytes);
            t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp;
            texCache[name] = t;
            return t;
        }

        /// <summary>LocalArt 의 글 파일(이름.bytes, 예: "ev_emo_anim.json") — 없으면 null.</summary>
        public string Text(string name)
        {
            var ta = Resources.Load<TextAsset>("LocalArt/" + name);
            return ta == null ? null : ta.text;
        }

        /// <summary>
        /// 원작 오프닝 데모 프레임 묶음(LocalArt/opening_frames.bin — tools/gba_capture/opening_frames.py 가 사용자 ROM 에서 뜬다). 없으면 null.
        /// 원작은 엔딩 카드 뒤 다시 시작해 이 데모를 보여 준다(실기).
        /// </summary>
        public FrameReel OpeningReel()
        {
            if (openingLoaded) return opening;
            openingLoaded = true;
            var ta = Resources.Load<TextAsset>("LocalArt/opening_frames.bin");
            opening = ta == null ? null : FrameReel.Parse(ta.bytes);
            return opening;
        }
        FrameReel opening; bool openingLoaded;

        /// <summary>원작 제목 화면 프레임 (LocalArt/title_frames.bin.bytes — tools/gba_capture/title_frames.py). 없으면 null.</summary>
        public FrameReel TitleReel()
        {
            if (titleLoaded) return title;
            titleLoaded = true;
            var ta = Resources.Load<TextAsset>("LocalArt/title_frames.bin");
            title = ta == null ? null : FrameReel.Parse(ta.bytes);
            return title;
        }
        FrameReel title; bool titleLoaded;

        /// <summary>원작 화살 쏘기 하늘 장면 프레임 (LocalArt/arrow_shot_frames.bin.bytes — tools/gba_capture/arrow_fx.py). 없으면 null.</summary>
        public FrameReel ShotReel()
        {
            if (shotLoaded) return shot;
            shotLoaded = true;
            var ta = Resources.Load<TextAsset>("LocalArt/arrow_shot_frames.bin");
            shot = ta == null ? null : FrameReel.Parse(ta.bytes);
            return shot;
        }
        FrameReel shot; bool shotLoaded;

        /// <summary>원작 도입부 천계 장면 프레임 (LocalArt/heaven_frames.bin.bytes — tools/gba_capture/heaven_frames.py). 없으면 null.</summary>
        public FrameReel HeavenReel()
        {
            if (heavenLoaded) return heaven;
            heavenLoaded = true;
            var ta = Resources.Load<TextAsset>("LocalArt/heaven_frames.bin");
            heaven = ta == null ? null : FrameReel.Parse(ta.bytes);
            return heaven;
        }
        FrameReel heaven; bool heavenLoaded;

        public Texture2D HouseTexture()
        {
            Texture2D t = null;
            if (houseKey.Length > 0)
            {
                var h = J.Child(J.Child(Manifest, "houses"), houseKey);
                t = Texture(J.Str(J.Child(h, "slots"), houseSlot.ToString()));   // 시간대 칸 그림 (없으면 기본)
                if (t == null) t = Texture(J.Str(h, "file"));
            }
            else t = Texture(J.Str(J.Child(Manifest, "house"), "file"));
            if (t != null) t.wrapMode = TextureWrapMode.Repeat;               // 세계가 한 바퀴 이어진다
            return t;
        }

        string houseKey = ""; int houseSlot;
        /// <summary>지금 집이 원작 집 정의에서 뽑은 2차원 집(높이 &gt; 160, 방마다 층)인가.</summary>
        public bool HouseIsWorld { get { return houseKey.Length > 0; } }
        public string HouseKey { get { return houseKey; } }
        /// <summary>
        /// 원작 집 그림 고르기: 집 번호 = 등급(0x0202C6AF 아래 4비트) + 4·시대(가족 +0x682) (원작 0x080171BC) — 시대·등급마다 집이 다르다(실기).
        /// manifest "houses"(tools/gba_capture/house_rom.py, 로컬 추출: 집 정의 표 0x08581768 의 그림 자원 + 방 자리)에 있으면 그 집으로 바꾼다.
        /// slot = 시간대 팔레트 칸(HouseClock). 그림이나 방이 바뀌었으면 true. 없으면 기본 집("house").
        /// </summary>
        public bool SelectHouse(int era, int grade, int slot)
        {
            var hs = J.Child(Manifest, "houses");
            string key = "e" + era + "g" + grade;
            var h = hs != null ? J.Child(hs, key) : null;
            if (h == null || Texture(J.Str(h, "file")) == null) key = "";
            int sl = slot;
            if (key.Length > 0 && Texture(J.Str(J.Child(h, "slots"), sl.ToString())) == null) sl = (slot & ~1) % 8;   // 사이 색 칸이 없으면 앞 칸
            if (key == houseKey && sl == houseSlot) return false;
            bool sameHouse = key == houseKey; houseKey = key; houseSlot = sl;
            if (sameHouse) return true;
            var src = key.Length > 0 ? h : J.Child(Manifest, "house");
            if (src == null) return true;
            HouseWidth = J.Int(src, "width", 960); HouseHeight = J.Int(src, "height", 160); FloorY = J.Int(src, "floorY", 112);
            Rooms.Clear();
            foreach (var r in J.List(src, "rooms")) { var l = J.Arr(r); var a = new int[l.Count]; for (int i = 0; i < l.Count; i++) a[i] = System.Convert.ToInt32(l[i]); Rooms.Add(a); }
            return true;
        }

        public Sprite Sprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Sprite s;
            if (cache.TryGetValue(name, out s)) return s;
            var t = Texture(name);
            s = t == null ? null : UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0f), 1f);
            cache[name] = s;
            return s;
        }

        /// <summary>9칸 늘이기 그림 (border = 왼·아래·오른·위 원본 점, 점 100개 = 1단위 — 화면 크기는 Image.pixelsPerUnitMultiplier 로). 없으면 null.</summary>
        public Sprite Sliced(string name, Vector4 border)
        {
            Sprite s; string key = name + "#9";
            if (cache.TryGetValue(key, out s)) return s;
            var t = Texture(name);
            s = t == null ? null : UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            cache[key] = s;
            return s;
        }

        public Sprite Ui(string key) { return Manifest == null ? null : Sprite(J.Str(J.Child(Manifest, "ui"), key)); }

        public Dictionary<string, object> Character(string id)
        {
            if (Manifest == null) return null;
            foreach (var c in J.List(Manifest, "characters")) { var d = J.Obj(c); if (J.Str(d, "id") == id) return d; }
            return null;
        }

        public List<string> CharacterIds()
        {
            var r = new List<string>();
            if (Manifest != null) foreach (var c in J.List(Manifest, "characters")) r.Add(J.Str(J.Obj(c), "id"));
            return r;
        }

        public Sprite Frame(string charId, string set, int i)
        {
            var c = Character(charId); if (c == null) return null;
            var l = J.List(c, set); if (l.Count == 0) l = J.List(c, "front"); if (l.Count == 0) return null;
            return Sprite((string)l[((i % l.Count) + l.Count) % l.Count]);
        }

        /// <summary>원작 파트 조합 그림 (발 아래 중앙 기준 32x64). 파트 자료가 없으면 null.</summary>
        public Sprite LookSprite(CharacterLook look) { return LookSprite(look, AgeSlots.Adult, CharacterComposer.Pose.FrontA, 0); }

        /// <param name="mirror">좌우 반전 (사건 장면 토큰 1A 0E 01 둘째 인자 비트0).</param>
        public Sprite LookSprite(CharacterLook look, AgeSlots ages, CharacterComposer.Pose pose, int outfitSet, bool mirror = false)
        {
            if (look == null || !Parts.Available) return null;
            var key = MiniJson.Serialize(look.ToJson()) + "|" + ages.Body + ages.Face + ages.Hair + ages.Feat + "c" + ages.Class + "|" + (int)pose + "|" + outfitSet
                + "|" + (look.PresetPalette != null ? string.Join(",", look.PresetPalette) : "") + "|o" + look.OutfitBody + (mirror ? "|m" : "");
            Sprite s;
            if (lookCache.TryGetValue(key, out s)) return s;
            var idx = CharacterComposer.Compose(Parts, look, ages, pose, outfitSet); var pal = Parts.Palette(look);
            if (mirror) idx = CharacterComposer.Mirror(idx);
            int W = CharacterComposer.Width, H = CharacterComposer.Height;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int v = idx[y * W + x], c = pal[v];
                    px[(H - 1 - y) * W + x] = v == 0 ? new Color32(0, 0, 0, 0)
                        : new Color32((byte)((c & 31) * 255 / 31), (byte)(((c >> 5) & 31) * 255 / 31), (byte)(((c >> 10) & 31) * 255 / 31), 255);
                }
            t.SetPixels32(px); t.Apply();
            s = UnityEngine.Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 1f);
            lookCache[key] = s;
            return s;
        }

        public Sprite Portrait(string charId) { var c = Character(charId); return c == null ? null : Sprite(J.Str(c, "portrait")); }

        public Sprite Cupid(int i)
        {
            if (Manifest == null) return null;
            var l = J.List(Manifest, "cupid"); return l.Count == 0 ? null : Sprite((string)l[i % l.Count]);
        }
    }
}
