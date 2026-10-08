using System;
using System.Collections.Generic;

namespace SennenKazoku.Core
{
    /// <summary>원작 ROM 캐릭터 파트 하나(4bpp 색 번호, 0 = 투명).</summary>
    public sealed class PartImage
    {
        public int W, H, Ax, Ay, Offset;
        public int[] Ext = new int[0];      // 헤더 6바이트째부터 (몸통: 목 위치, 얼굴: 기준점 표)
        public bool Empty { get { return W == 0 || H == 0; } }
    }

    /// <summary>파트 묶음 = 같은 번호의 연령별 변형(갤러리 성인 = 1).</summary>
    public sealed class PartGroup
    {
        public string Rom = "";
        public List<PartImage> Variants = new List<PartImage>();
        public PartImage At(int age) { return Variants.Count == 0 ? null : Variants[Math.Max(0, Math.Min(age, Variants.Count - 1))]; }
    }

    /// <summary>
    /// 원작 캐릭터 파트 라이브러리. tools/gba_capture/export_parts.py 가 사용자 ROM 에서 만든
    /// parts.json + parts.bin 을 읽는다(원작 추출물이라 저장소에는 없다 — 없으면 Available=false).
    /// </summary>
    public sealed class PartsLibrary
    {
        public readonly Dictionary<string, List<PartGroup>> Categories = new Dictionary<string, List<PartGroup>>();
        public byte[] Pixels = new byte[0];
        public List<int> BasePalette = new List<int>();
        public readonly List<int[]> HairColors = new List<int[]>(), SkinColors = new List<int[]>(), OutfitTable = new List<int[]>();
        public readonly Dictionary<string, CharacterLook> Presets = new Dictionary<string, CharacterLook>();
        public readonly Dictionary<string, AgeSlots> PresetAges = new Dictionary<string, AgeSlots>();   // 프리셋이 나온 목록의 연령 칸

        public bool Available { get { return Count("body") > 0 && Count("face") > 0 && Count("hairfront") > 0; } }
        public int Count(string cat) { List<PartGroup> l; return Categories.TryGetValue(cat, out l) ? l.Count : 0; }
        public PartGroup Group(string cat, int i)
        {
            List<PartGroup> l;
            if (!Categories.TryGetValue(cat, out l) || l.Count == 0) return null;
            return l[((i % l.Count) + l.Count) % l.Count];
        }

        public static PartsLibrary Load(string json, byte[] pixels)
        {
            var lib = new PartsLibrary { Pixels = pixels ?? new byte[0] };
            var root = J.Obj(MiniJson.Parse(json));
            if (root == null) return lib;
            var cats = J.Child(root, "categories");
            if (cats != null)
                foreach (var kv in cats)
                {
                    var list = new List<PartGroup>();
                    foreach (var go in J.Arr(kv.Value))
                    {
                        var g = J.Obj(go); var pg = new PartGroup { Rom = J.Str(g, "rom") };
                        foreach (var po in J.List(g, "parts"))
                        {
                            var p = J.Obj(po);
                            var ext = J.List(p, "ext"); var e = new int[ext.Count];
                            for (int i = 0; i < e.Length; i++) e[i] = Convert.ToInt32(ext[i]);
                            pg.Variants.Add(new PartImage { W = J.Int(p, "w"), H = J.Int(p, "h"), Ax = J.Int(p, "ax"), Ay = J.Int(p, "ay"), Offset = J.Int(p, "off"), Ext = e });
                        }
                        list.Add(pg);
                    }
                    lib.Categories[kv.Key] = list;
                }
            var pal = J.Child(root, "palettes");
            if (pal != null)
            {
                foreach (var c in J.List(pal, "base")) lib.BasePalette.Add(Convert.ToInt32(c));
                ReadColors(J.List(pal, "hair"), lib.HairColors);
                ReadColors(J.List(pal, "skin"), lib.SkinColors);
                ReadColors(J.List(pal, "outfitTable"), lib.OutfitTable);
            }
            var pre = J.Child(root, "presets");
            if (pre != null)
                foreach (var kv in pre)
                {
                    lib.Presets[kv.Key] = CharacterLook.FromJson(J.Obj(kv.Value));
                    lib.PresetAges[kv.Key] = AgeSlots.FromJson(J.Get(J.Obj(kv.Value), "age"), AgeSlots.Adult);
                }
            return lib;
        }

        static void ReadColors(List<object> src, List<int[]> dst)
        {
            foreach (var o in src) { var l = J.Arr(o); var a = new int[l.Count]; for (int i = 0; i < a.Length; i++) a[i] = Convert.ToInt32(l[i]); dst.Add(a); }
        }

        public byte Pixel(PartImage p, int x, int y) { int i = p.Offset + y * p.W + x; return i >= 0 && i < Pixels.Length ? Pixels[i] : (byte)0; }

        /// <summary>16색 팔레트(BGR555). 칸: 1 외곽선 · 3~5 머리 · 6~10 피부 · 11~14 의상 (원작 화면에서 확인).</summary>
        public int[] Palette(CharacterLook look)
        {
            var p = new int[16];
            for (int i = 0; i < 16 && i < BasePalette.Count; i++) p[i] = BasePalette[i];
            if (look.PresetPalette != null)
                for (int i = 1; i < 16 && i < look.PresetPalette.Length; i++) if (look.PresetPalette[i] >= 0) p[i] = look.PresetPalette[i];
            if (look.HairColor >= 0) Copy(HairColors, look.HairColor, p, 3);
            if (look.SkinColor >= 0) Copy(SkinColors, look.SkinColor, p, 6);
            if (look.OutfitColor >= 0) Copy(OutfitTable, CharacterComposer.OutfitEntry(look), p, 11);
            return p;
        }
        static void Copy(List<int[]> src, int i, int[] dst, int at)
        {
            if (src.Count == 0) return;
            var c = src[((i % src.Count) + src.Count) % src.Count];
            for (int k = 0; k < c.Length && at + k < dst.Length; k++) dst[at + k] = c[k];
        }
    }

    /// <summary>
    /// 캐릭터 외형 = 원작 파트 번호 조합 + 색 번호. 원작 "내가 아는 가족"의 캐릭터·색 선택과 같은 축.
    /// Body 는 정면 서기 몸통 묶음의 번호(0~23: 0~11 남, 12~23 여 — 원작 확인은 갤러리가 쓰는 0~3/12~15 뿐).
    /// </summary>
    public sealed class CharacterLook
    {
        public int Body, Face, Hair, Eyes, Nose, Mouth;
        public bool EyesFlip, NoseFlip, MouthFlip;
        public int HairColor = -1, SkinColor = -1, Outfit, OutfitColor = -1;   // -1 = 프리셋(또는 기본) 색 유지
        public string Preset = "";            // 원작 갤러리 프리셋 id (예: father_07)
        public int[] PresetPalette;           // 갤러리 캡처에서 읽은 프리셋 고유 색(BGR555, -1 = 없음)

        public CharacterLook Clone()
        {
            var c = (CharacterLook)MemberwiseClone();
            if (PresetPalette != null) c.PresetPalette = (int[])PresetPalette.Clone();
            return c;
        }

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object> {
                {"body", Body}, {"face", Face}, {"hair", Hair}, {"eyes", Eyes}, {"nose", Nose}, {"mouth", Mouth},
                {"eyesFlip", EyesFlip}, {"noseFlip", NoseFlip}, {"mouthFlip", MouthFlip},
                {"hairColor", HairColor}, {"skinColor", SkinColor}, {"outfit", Outfit}, {"outfitColor", OutfitColor}, {"preset", Preset},
                {"palette", PresetPalette == null ? null : new List<object>(Array.ConvertAll(PresetPalette, x => (object)x))} };
        }

        public static CharacterLook FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var body = J.Int(d, "body");
            if (body >= CharacterComposer.FrontBlock) body -= CharacterComposer.FrontBlock;     // gallery_fit 는 ROM 묶음 번호(48~71)를 쓴다
            return new CharacterLook {
                Body = body, Face = J.Int(d, "face"), Hair = J.Int(d, "hair"), Eyes = J.Int(d, "eyes"), Nose = J.Int(d, "nose"), Mouth = J.Int(d, "mouth"),
                EyesFlip = Flag(d, "eyesFlip"), NoseFlip = Flag(d, "noseFlip"), MouthFlip = Flag(d, "mouthFlip"),
                HairColor = J.Int(d, "hairColor", -1), SkinColor = J.Int(d, "skinColor", -1), Outfit = J.Int(d, "outfit"), OutfitColor = J.Int(d, "outfitColor", -1),
                Preset = J.Str(d, "preset"), PresetPalette = Ints(J.List(d, "palette")) };
        }
        static int[] Ints(List<object> l)
        {
            if (l == null || l.Count == 0) return null;
            var a = new int[l.Count]; for (int i = 0; i < a.Length; i++) a[i] = Convert.ToInt32(l[i]); return a;
        }
        static bool Flag(Dictionary<string, object> d, string k) { var v = J.Get(d, k); return v is bool ? (bool)v : J.Int(d, k) != 0; }
    }

    /// <summary>
    /// 연령 칸(파트 묶음 안 순번)을 분류별로. 원작 갤러리 대조 결과:
    /// 아이(7~12세) 모두 0 · 10대(13세~)와 성인 모두 1 · 노인(조부모 목록) = 몸통 2, 얼굴 1, 머리 1, 눈코입 2.
    /// 나이 → 칸 경계 중 6세 이하·노인 시작 나이는 미확인(추정).
    /// </summary>
    public struct AgeSlots
    {
        public int Body, Face, Hair, Feat;
        public AgeSlots(int body, int face, int hair, int feat) { Body = body; Face = face; Hair = hair; Feat = feat; }
        public static AgeSlots Uniform(int a) { return new AgeSlots(a, a, a, a); }
        public static readonly AgeSlots Child = Uniform(0), Adult = Uniform(1);
        public static readonly AgeSlots Elder = new AgeSlots(2, 1, 1, 2);
        public const int ElderFromAge = 60;          // 추정: 원작 노화 시점 미확인
        public static AgeSlots ForAge(int years) { return years < 13 ? Child : years >= ElderFromAge ? Elder : Adult; }
        public static AgeSlots FromJson(object o, AgeSlots def)
        {
            var d = J.Obj(o);
            if (d == null) { if (o is long || o is int || o is double) return Uniform(Convert.ToInt32(o)); return def; }
            return new AgeSlots(J.Int(d, "body", def.Body), J.Int(d, "face", def.Face), J.Int(d, "hair", def.Hair), J.Int(d, "feat", def.Feat));
        }
    }

    /// <summary>
    /// 원작 조합 규칙 (tools/gba_capture/charcompose.py 와 동일; 갤러리 252명 대조 결과는 docs/07 참고).
    /// 원점 O = 발 아래 중앙. 결과는 위→아래 행 순서의 색 번호 배열.
    /// </summary>
    public static class CharacterComposer
    {
        public const int Width = 32, Height = 64;
        public const int FrontBlock = 48;     // 몸통 묶음 중 정면 서기 24개의 시작 (갤러리 대조로 확인)
        public const int AdultAge = 1;        // 연령 칸: 갤러리(성인)는 1. 0/2/3 은 미확인

        /// <summary>의상색 표 항목. 1 + 48*색 + 4*의상 — 색 선택 화면 캡처 8장으로 추정(체형·성별 보정 미해명).</summary>
        public static int OutfitEntry(CharacterLook l) { return 1 + 48 * (l.OutfitColor & 3) + 4 * OutfitOf(l); }

        // 정면 몸통 24개 = 성별 2 × 12. 12개 = 체형 3 × 의상 4 로 추정(갤러리는 첫 4개만 씀 — 체형 순서 미확인).
        public static int GenderOf(CharacterLook l) { return l.Body >= 12 ? 1 : 0; }
        public static int BuildOf(CharacterLook l) { return (l.Body % 12) / 4; }
        public static int OutfitOf(CharacterLook l) { return l.Body % 4; }
        public static void SetBody(CharacterLook l, int gender, int build, int outfit)
        {
            l.Body = (gender == 1 ? 12 : 0) + 4 * (((build % 3) + 3) % 3) + (((outfit % 4) + 4) % 4); l.Outfit = OutfitOf(l);
        }

        /// <summary>역할별 원작 갤러리 프리셋 id 목록(정렬). 없으면 성별이 맞는 전체.</summary>
        public static List<string> PresetIds(PartsLibrary lib, string role)
        {
            var keys = new List<string>();
            foreach (var k in lib.Presets.Keys) if (k.StartsWith(role + "_", StringComparison.Ordinal)) keys.Add(k);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        /// <summary>프리셋의 얼굴·머리·눈·코·입·고유색을 가져온다(체형·의상은 유지 — 원작 선택 순서와 같음).</summary>
        public static void ApplyPreset(PartsLibrary lib, CharacterLook l, string key)
        {
            CharacterLook p;
            if (!lib.Presets.TryGetValue(key, out p)) return;
            l.Face = p.Face; l.Hair = p.Hair; l.Eyes = p.Eyes; l.Nose = p.Nose; l.Mouth = p.Mouth;
            l.EyesFlip = p.EyesFlip; l.NoseFlip = p.NoseFlip; l.MouthFlip = p.MouthFlip;
            l.PresetPalette = p.PresetPalette == null ? null : (int[])p.PresetPalette.Clone();
            l.HairColor = -1; l.SkinColor = -1; l.OutfitColor = -1; l.Preset = key;
        }

        public static byte[] Compose(PartsLibrary lib, CharacterLook look, int age = AdultAge) { return Compose(lib, look, AgeSlots.Uniform(age)); }

        /// <summary>
        /// 자세(몸통 묶음 24개 단위 블록). 본게임 걷기 캡처로 확인: 0·1 뒷모습 걷기, 2·3 앞모습 걷기(2 = 서기, 갤러리).
        /// 4~15 는 같은 구성의 다른 의상 세트로 보인다(계절 의상으로 추정, 미확인) → set*4 를 더해 쓴다.
        /// </summary>
        public enum Pose { BackA = 0, BackB = 1, FrontA = 2, FrontB = 3 }

        public static byte[] Compose(PartsLibrary lib, CharacterLook look, AgeSlots a) { return Compose(lib, look, a, Pose.FrontA, 0); }

        public static byte[] Compose(PartsLibrary lib, CharacterLook look, AgeSlots a, Pose pose, int outfitSet)
        {
            var c = new byte[Width * Height];
            if (lib == null || !lib.Available || look == null) return c;
            bool back = pose == Pose.BackA || pose == Pose.BackB;
            int ox = Width / 2 - (back ? 1 : 0), oy = Height;          // 뒷모습은 원점이 1 왼쪽 (캡처와 일치)
            int block = ((outfitSet & 3) * 4 + (int)pose) * 24;
            var b = lib.Group("body", block + (look.Body % 24 + 24) % 24).At(a.Body);
            var f = lib.Group("face", look.Face).At(a.Face);
            if (b == null || f == null || f.Ext.Length < 7 || b.Ext.Length < 2) return c;
            // 얼굴 메타 m[0..8] = 헤더 4바이트째부터 = (Ax, Ay, Ext[0..])
            int[] m = new int[9]; m[0] = f.Ax; m[1] = f.Ay; for (int i = 0; i < 7; i++) m[2 + i] = f.Ext[i];
            int nx = ox + b.Ext[0] - 16, ny = oy + b.Ext[1] - 32, refY = ny - m[0];
            if (back)
            {
                // 뒷모습: 몸통 → 뒷통수(faceB, 얼굴과 같은 번호) → 뒷머리(머리 번호 + 104)
                Put(lib, c, b, ox, oy - 16, false);
                var fb = Part(lib, "faceB", look.Face, a.Face);
                if (fb != null && fb.Ext.Length >= 1) Put(lib, c, fb, nx - fb.Ext[0] + fb.Ax, refY + m[1] - 2 + fb.Ay, false);
                var bh = Part(lib, "hairback", 104 + look.Hair, a.Hair);
                if (bh != null && !bh.Empty && bh.Ext.Length >= 2) Put(lib, c, bh, nx, refY + m[2] + 16 - bh.Ext[1], false);
                return c;
            }
            var hb = lib.Group("hairback", look.Hair).At(a.Hair);
            if (hb != null && !hb.Empty && hb.Ext.Length >= 2) Put(lib, c, hb, nx, refY + m[2] + 16 - hb.Ext[1], false);
            Put(lib, c, b, ox, oy - 16, false);
            // 얼굴 세로 위치: 아이 칸(0)은 1 아래 (원작 아이 목록 대조로 확인)
            Put(lib, c, f, nx - m[7] + f.Ax, refY + m[1] - 2 + (a.Face == 0 ? 1 : 0) + f.Ay, false);
            Put(lib, c, Part(lib, "nose", look.Nose, a.Feat), nx, refY + m[4], look.NoseFlip);
            Put(lib, c, Part(lib, "eyes", look.Eyes, a.Feat), nx, refY + m[3], look.EyesFlip);
            Put(lib, c, Part(lib, "mouth", look.Mouth, a.Feat), nx, refY + m[5], look.MouthFlip);
            Put(lib, c, Part(lib, "hairfront", look.Hair, a.Hair), nx, refY + m[1], false);
            return c;
        }

        static PartImage Part(PartsLibrary lib, string cat, int i, int age) { var g = lib.Group(cat, i); return g == null ? null : g.At(age); }

        static void Put(PartsLibrary lib, byte[] c, PartImage p, int axAbs, int ayAbs, bool flip)
        {
            if (p == null || p.Empty) return;
            int ax = flip ? p.W - p.Ax : p.Ax;
            int x0 = axAbs - ax, y0 = ayAbs - p.Ay;
            for (int y = 0; y < p.H; y++)
                for (int x = 0; x < p.W; x++)
                {
                    byte v = lib.Pixel(p, flip ? p.W - 1 - x : x, y);
                    int X = x0 + x, Y = y0 + y;
                    if (v != 0 && X >= 0 && X < Width && Y >= 0 && Y < Height) c[Y * Width + X] = v;
                }
        }

        /// <summary>
        /// 원작 파트를 무작위로 조합 ("신님이 아는 가족"용). 몸통은 갤러리에서 확인된 정면 묶음만 쓴다.
        /// 임시 구현: 원작의 무작위 규칙(범위·가중치)은 미해명이라 균등 추첨이다.
        /// </summary>
        public static CharacterLook Random(PartsLibrary lib, Rng rng, int gender)
        {
            var l = new CharacterLook {
                Body = (gender == 1 ? 12 : 0) + rng.Next(12),
                Face = rng.Next(Math.Max(1, lib.Count("face"))),
                Hair = rng.Next(Math.Max(1, lib.Count("hairfront"))),
                Eyes = rng.Next(Math.Max(1, lib.Count("eyes"))), EyesFlip = rng.Next(2) == 1,
                Nose = rng.Next(Math.Max(1, lib.Count("nose"))),
                Mouth = rng.Next(Math.Max(1, lib.Count("mouth"))),
                HairColor = rng.Next(Math.Max(1, lib.HairColors.Count)), SkinColor = rng.Next(Math.Max(1, lib.SkinColors.Count)),
                OutfitColor = rng.Next(4) };
            l.Outfit = OutfitOf(l);
            return l;
        }

        /// <summary>원작 갤러리 프리셋 중 하나 (역할: father/mother/son18 …). 없으면 무작위 조합.</summary>
        public static CharacterLook FromPreset(PartsLibrary lib, Rng rng, string role, int gender)
        {
            var keys = PresetIds(lib, role);
            if (keys.Count == 0) return Random(lib, rng, gender);
            var key = keys[rng.Next(keys.Count)];
            var l = lib.Presets[key].Clone(); l.Preset = key; l.Outfit = OutfitOf(l);
            return l;
        }
    }
}
