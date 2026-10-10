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

        public Texture2D HouseTexture()
        {
            var t = Texture(J.Str(J.Child(Manifest, "house"), "file"));
            if (t != null) t.wrapMode = TextureWrapMode.Repeat;               // 세계가 한 바퀴 이어진다
            return t;
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

        public Sprite LookSprite(CharacterLook look, AgeSlots ages, CharacterComposer.Pose pose, int outfitSet)
        {
            if (look == null || !Parts.Available) return null;
            var key = MiniJson.Serialize(look.ToJson()) + "|" + ages.Body + ages.Face + ages.Hair + ages.Feat + "c" + ages.Class + "|" + (int)pose + "|" + outfitSet
                + "|" + (look.PresetPalette != null ? string.Join(",", look.PresetPalette) : "");
            Sprite s;
            if (lookCache.TryGetValue(key, out s)) return s;
            var idx = CharacterComposer.Compose(Parts, look, ages, pose, outfitSet); var pal = Parts.Palette(look);
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
