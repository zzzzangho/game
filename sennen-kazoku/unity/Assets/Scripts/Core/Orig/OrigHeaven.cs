using System;
using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 천계 보고 (원작 장면 조정자 0x0809AF60 의 모드 3 = 상태 기계 0x0809F938, 객체 0xB4 바이트 — 정적 해독).
    /// 원작 흐름: 후계자 결혼식처럼 결과 첫 단어가 1 인 사건이 끝나면(0x0804DB9C → 끝 종류 1 → 0x0804DF6C) 진입값 2 로 천계에 간다.
    ///   진입값 2: 상태 0 → 1 "가계도에 N대째 가족을 추가하마"(+ 2대째 처음이면 3 가계도 설명) → 4: 끝값 3 → 가계도 화면(0x08054F58(1)) →
    ///   가계도 화면이 끝나면 진입값 5: 작명서(2대째 처음) → 가족 칭호 → 가족 유형 평 → 지켜본 방식 평 → 고인마다 기록·인생 평가 →
    ///   남긴 꿈(선대의 꿈의 화살) → 남긴 기술(선대의 마음의 결정) → 2대째면 "100년 동안 이끌어다오" + 통신 결혼의 화살(다 썼으면 다시) →
    ///   세기 확인(0x080A113C): 새 세기면 축하, 새 시대면 끝값 4 → 시대 연출(0x0806AC04) → 진입값 6: 새 시대 축하 + 시간의 책갈피(+ 처음이면 기록의 전당) →
    ///   0x45 → 마무리 대사 → 끝(끝값 −1 이면 0x0800F40C 로 고인 목록을 비우고 메인 장면으로).
    /// 화면 처리(그림·글상자·아이템 그림·대기)는 하지 않고, 상태 전이와 메모리 변화는 원작대로 한다:
    /// 데이터 처리 함수(고인 차례 0x080A0A98 · 남긴 꿈 0x080A0BC0 · 남긴 기술 0x080A0EA0 · 고인 수 0x0800F474 · 고인 목록 비우기 0x0800F40C ·
    /// 글 묶음 찾기 0x0809F510 · 세기 확인 0x080A113C)는 자동 변환 트리로 원작 코드를 그대로 돌린다.
    /// 대사는 로컬 글 팩 orig_text.json "heaven"(export_charset.py --heaven-only): 상태별 글 목록(0x085BC2E4) → 글 번호 → 원작 글 바이트(0x085D5CB0).
    /// 실기 확인: 아직 없음 (결혼 상대가 있는 저장 상태가 없어 실기에서 이 흐름을 일으키지 못했다).
    /// </summary>
    public sealed class OrigHeaven
    {
        /// <summary>한 장. Speaker: 1 높으신 신 · 2 큐피트 (글 앞 토큰 1A 0A xx), 0 = 원작 화면 자리(앱 요약, Screen 이 이름).</summary>
        public sealed class Page { public int Speaker; public string Text = "", Screen = ""; public int State, MsgId = -1; }

        public sealed class Data
        {
            public readonly List<int> Lists = new List<int>();
            public readonly Dictionary<int, int> States = new Dictionary<int, int>();
            public readonly Dictionary<int, byte[]> Msgs = new Dictionary<int, byte[]>();
            public readonly List<int[]> Types = new List<int[]>(), Lives = new List<int[]>();
            public readonly Dictionary<int, string> Dreams = new Dictionary<int, string>();
            /// <summary>시대 연출(0x0806AC04): 시대−1 → 큐피트 글 번호(0x0858A854) · 띠 화면 글 번호(0x0858A874).</summary>
            public readonly List<int> EraMsgs = new List<int>(), EraBands = new List<int>();
            public bool Ok { get { return Lists.Count > 0 && Msgs.Count > 0; } }

            public static Data FromJson(Dictionary<string, object> h)
            {
                var d = new Data();
                if (h == null) return d;
                foreach (var x in J.Arr(J.Get(h, "lists")) ?? new List<object>()) d.Lists.Add(Convert.ToInt32(x));
                var st = J.Child(h, "states"); if (st != null) foreach (var kv in st) d.States[int.Parse(kv.Key)] = Convert.ToInt32(kv.Value);
                var ms = J.Child(h, "msgs"); if (ms != null) foreach (var kv in ms) d.Msgs[int.Parse(kv.Key)] = Hex(kv.Value as string ?? "");
                foreach (var x in J.Arr(J.Get(h, "types")) ?? new List<object>()) { var p = J.Arr(x); d.Types.Add(new[] { Convert.ToInt32(p[0]), Convert.ToInt32(p[1]) }); }
                foreach (var x in J.Arr(J.Get(h, "lives")) ?? new List<object>()) { var p = J.Arr(x); d.Lives.Add(new[] { Convert.ToInt32(p[0]), Convert.ToInt32(p[1]) }); }
                var dr = J.Child(h, "dreams"); if (dr != null) foreach (var kv in dr) d.Dreams[int.Parse(kv.Key)] = kv.Value as string ?? "";
                foreach (var x in J.Arr(J.Get(h, "eraMsgs")) ?? new List<object>()) d.EraMsgs.Add(Convert.ToInt32(x));
                foreach (var x in J.Arr(J.Get(h, "eraBands")) ?? new List<object>()) d.EraBands.Add(Convert.ToInt32(x));
                return d;
            }
            static byte[] Hex(string s)
            {
                var b = new byte[s.Length / 2];
                for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(2 * i, 2), 16);
                return b;
            }
        }

        public const uint Fam = OrigMem.Fam, Obj = 0x0F03B600, Out = 0x0F03B700;
        /// <summary>진입값(0x0203BDFC — 진입값 2·3·4 일 때만 적힌다, 마무리 대사를 고른다) · 세기(0x0203BE00 — 진입값 6 이 이어받는다).</summary>
        public const uint EntryVar = 0x0203BDFC, CenturyVar = 0x0203BE00;

        readonly OrigVm vm; readonly OrigMem m; readonly OrigText text; readonly Data data;
        readonly Func<int, string> personName; readonly string player;
        public readonly List<Page> Pages = new List<Page>();
        /// <summary>지나간 상태 차례 (시험·점검용).</summary>
        public readonly List<int> Trace = new List<int>();
        /// <summary>마지막 진입의 끝값 (원작 +0xA4: −1 이면 진입값으로 정한 보통 끝, 3 가계도 화면, 4 시대 연출).</summary>
        public int Exit { get; private set; }

        public OrigHeaven(OrigVm vm, OrigText text, Data data, Func<int, string> personName, string player)
        {
            this.vm = vm; m = vm.Mem; this.text = text; this.data = data; this.personName = personName; this.player = player;
        }

        /// <summary>
        /// 결혼식 등 결과 첫 단어 1 의 장면 뒤 원작 차례 전체: 진입값 2 → (끝값 3) 가계도 → 진입값 5 → (끝값 4) 시대 연출 → 진입값 6.
        /// </summary>
        public void RunAfterWedding()
        {
            Run(2);
            if (Exit == 3) { Screen("가계도", "[원작 가계도 화면 — 앱 요약] " + m.R8(Fam + 0x6A3) + "대째 가족이 가계도에 더해졌다."); Run(5); }
            if (Exit == 4)
            {
                // 시대 연출 0x0806AC04 (실기: 도시 그림 → 새 시대 도시, 띠 "근미래" 등, 큰 글씨 "과학 미래 시대가 열렸다!", 큐피트 한마디)
                int era = (int)m.R8(Fam + 0x682), k = era - 1;
                string band = k >= 0 && k < data.EraBands.Count ? text.UiText(data.EraBands[k], "") : "";
                Screen("시대 연출", "[원작 시대 연출 화면 — 앱 요약] " + (band.Length > 0 ? band + " — " : "") + text.UiText(392 + era, "새 시대가 열렸다!"));
                if (k >= 0 && k < data.EraMsgs.Count) Say(-2, data.EraMsgs[k], 2);   // 말하는 사람 토큰이 없다 — 실기에서 큐피트 말풍선
                Run(6);
            }
        }

        /// <summary>객체를 원작 0x0809F578 처럼 만들고 진입값 k 의 시작 상태(표 0x0809F844)부터 끝(0x49)까지 돈다.</summary>
        public void Run(int k)
        {
            for (uint i = 0; i < 0xB4; i += 4) m.W32(Obj + i, 0);
            m.W32(Obj + 0x10, 0xFFFFFFFF); m.W32(Obj + 0x0C, 0xFFFFFFFF); m.W32(Obj + 0xA4, 0xFFFFFFFF);
            m.W32(Obj + 0x1C, 1); m.W32(Obj + 0x20, 1); m.W32(Obj + 0x24, 0xFF); m.W32(Obj + 0x68, (uint)k);
            m.W8(Obj + 0xA1, vm.Call("0800F474") & 0xFF);
            m.W32(Obj + 0x74, Obj + 0x71);
            m.W16(Obj + 0x9C, (m.R8(Fam + 0x6A3) - 2) & 0xFFFF);
            m.W32(Fam + 0x668, m.R32(Fam + 0x668) & ~4u);
            int st;
            switch (k)
            {
                case 2: case 3: case 4: st = 0; m.W32(EntryVar, (uint)k); break;
                case 6: st = 0x39; m.W8(Obj + 0x94, m.R32(CenturyVar) & 0xFF); break;
                default: st = 5; break;
            }
            for (int guard = 0; st != 0x49 && guard < 1000; guard++) { Trace.Add(st); st = Step(st); }
            // 끝 처리 0x0809F8B0: 세기 값을 넘기고, 끝값이 −1 이면 고인 목록을 비운다
            m.W32(CenturyVar, m.R8(Obj + 0x94));
            Exit = unchecked((int)m.R32(Obj + 0xA4));
            if (Exit == -1) vm.Call("0800F40C");
        }

        uint F20 { get { return m.R32(Fam + 0x20); } set { m.W32(Fam + 0x20, value); } }
        uint Gen { get { return m.R8(Fam + 0x6A3); } }
        int Era1 { get { return (int)((m.R8(Fam + 0x682) - 1) & 0xFF); } }
        void Set60(uint v) { m.W32(Obj + 0x60, v); }
        uint Group(int era1, int set) { return vm.Call("0809F510", (uint)era1 & 0xFF, (uint)set); }

        /// <summary>대사 상태 (상태 표 0x0809F96C 에서 0x080A01E8 로 가는 것 — 글 팩이 없어도 전이는 같다).</summary>
        static readonly HashSet<int> MessageStates = new HashSet<int> { 1, 3, 6, 7, 8, 0x0A, 0x0C, 0x0E, 0x10, 0x14, 0x16, 0x1E, 0x20, 0x24, 0x26, 0x29, 0x2C,
            0x2E, 0x31, 0x34, 0x36, 0x37, 0x3A, 0x3C, 0x3F, 0x42, 0x44, 0x46, 0x47 };

        int Step(int st)
        {
            if (MessageStates.Contains(st)) return Message(st);
            switch (st)
            {
                case 0x00: return 1;                                                   // 화면 밝아지기 대기
                case 0x02: return (F20 & 4) == 0 && Gen == 2 ? 3 : 4;
                case 0x04: m.W32(Obj + 0xA4, 3); return 0x49;
                case 0x05:
                    if ((F20 & 0x10) == 0 && Gen == 2) { F20 |= 0x10; return 6; }      // 작명서
                    return 7;
                case 0x09:
                {
                    // 0x080CC554(앞 장면이 끝났나 — 0x02029BA0)는 화면 쪽이라 끝난 것으로 본다
                    uint x1 = TypeByte() + 1;
                    bool hint = (m.R8(Fam + 0x664) & 1) != 0;
                    if (x1 == 6 || x1 == 8 || x1 == 9)
                    {
                        if (hint) Set60(0);
                        else if ((F20 & 0x400) != 0) Set60(6);
                        else { Set60(4); F20 |= 0x400; }
                    }
                    else Set60(hint ? 2u : 8u);
                    m.W8(Fam + 0x664, m.R8(Fam + 0x664) & 0xFE);
                    return 0x0A;
                }
                case 0x0B: case 0x0F: return (int)vm.Call("080A0A98", Obj);
                case 0x0D:
                {
                    uint id = DeceasedId();
                    int life = id != 0xFFFF ? (int)m.R8(OrigMem.GeneAddr(id) + 0x33) : -1;
                    string label = life >= 0 && life < data.Lives.Count ? text.UiText(data.Lives[life][0], "") : "";
                    Screen("고인 기록", "[원작 기록 화면 — 앱 요약] " + (id != 0xFFFF ? personName((int)id) : "○○") + (label.Length > 0 ? " — " + label : ""));
                    return 0x0E;
                }
                case 0x11: case 0x17:
                {
                    int r = (int)vm.Call("080A0BC0", Obj);
                    if (r == 0x14) Set60(3);
                    return r;
                }
                case 0x15: if ((F20 & 0x100) != 0) return 0x17; F20 |= 0x100; return 0x16;
                case 0x1A: case 0x21:
                {
                    int r = (int)vm.Call("080A0EA0", Obj);
                    if (r == 0x1E) Set60(6);
                    return r;
                }
                case 0x1D: Set60((m.R8(Obj + 0x70) & 1) != 0 ? 0u : 3u); return 0x1E;
                case 0x1F: if ((F20 & 0x200) != 0) return 0x21; F20 |= 0x200; return 0x20;
                case 0x25:
                    if (Gen == 2) { m.W8(Fam + 0x63A, 1); return 0x26; }                // 통신 결혼의 화살 (화살 칸 10)
                    return 0x2D;
                case 0x28: case 0x30: Set60(2); return st + 1;                         // 아이템 그림 대기 뒤
                case 0x2B: case 0x33: Set60(4); return st + 1;
                case 0x2D:
                    if (m.R8(Fam + 0x63A) == 0) { m.W8(Fam + 0x63A, 1); return 0x2E; }
                    return 0x35;
                case 0x35:
                    vm.Call("080A113C", Out);
                    Set60(m.R32(Out + 4)); m.W8(Obj + 0x94, m.R8(Out + 8));
                    return (int)m.R32(Out);
                case 0x38: m.W32(Obj + 0xA4, 4); return 0x49;
                case 0x39: Set60(Group(Era1, 0x3A)); return 0x3A;
                case 0x3B: Set60(Group(Era1, 0x3C)); m.W8(Fam + 0x64A, (m.R8(Fam + 0x64A) + 1) & 0xFF); return 0x3C;   // 시간의 책갈피 (아이템 칸 10)
                case 0x3E: Set60(Group(Era1, 0x3F)); return 0x3F;
                case 0x41: Set60(Group(Era1, 0x42)); return 0x42;
                case 0x43: if ((F20 & 0x800) != 0) return 0x45; F20 |= 0x800; return 0x44;   // 처음 새 시대: 책갈피 설명 + 기록의 전당
                case 0x45: return m.R32(EntryVar) == 4 ? 0x46 : 0x47;
                default: return st + 1;                                                // 아이템 그림·대기 상태(0x12·0x13·0x18·0x19·0x1B·0x1C·0x22·0x23·0x27·0x2A·0x2F·0x32·0x3D·0x40), 0x48
            }
        }

        /// <summary>앞 세대의 세대 기록 +6 (가족 유형 0~8): 가족 +0x253A + 28·(+0x9C), +0x9C = 세대 수 − 2.</summary>
        uint TypeByte() { return m.R8(Fam + 0x253A + 28 * (uint)m.R16(Obj + 0x9C)); }
        uint DeceasedId() { uint p = m.R32(Obj + 0x98); return p != 0 ? m.R16(p) : 0xFFFF; }

        /// <summary>대사 상태 (0x080A01E8 → 0x080A0530): 목록[+0x60 부터] 의 글을 −1 까지. 상태 8 = 가족 유형 평, 0xE = 고인 인생 평가.</summary>
        int Message(int st)
        {
            if (st == 8) { uint x = TypeByte(); if (x < data.Types.Count) Say(st, data.Types[(int)x][1]); }
            else if (st == 0x0E)
            {
                uint id = DeceasedId();
                if (id != 0xFFFF) { uint x = m.R8(OrigMem.GeneAddr(id) + 0x33); if (x < data.Lives.Count) Say(st, data.Lives[(int)x][1]); }
            }
            else if (data.States.ContainsKey(st))
            {
                int i = data.States[st] + (int)m.R32(Obj + 0x60);
                for (; i >= 0 && i < data.Lists.Count && data.Lists[i] >= 0; i++) Say(st, data.Lists[i]);
            }
            m.W32(Obj + 0x5C, 0); Set60(0);
            if (st == 0x10) return 0x0D;
            if (st == 0x36) return 0x45;
            if (st == 0x46) return 0x49;
            return st + 1;
        }

        void Say(int st, int id, int speaker = 0)
        {
            byte[] b; if (!data.Msgs.TryGetValue(id, out b)) return;
            for (int i = 0; i + 2 < b.Length && i < 16; i++) if (b[i] == 0x1A && b[i + 1] == 0x0A) { speaker = b[i + 2]; break; }
            foreach (var pg in text.Decode(b, Name, null, Number)) Pages.Add(new Page { Speaker = speaker, Text = pg, State = st, MsgId = id });
        }

        void Screen(string name, string summary) { Pages.Add(new Page { Speaker = 0, Screen = name, Text = summary, State = -1 }); }

        /// <summary>이름 자리 (원작 글 엔진 0x080A7A00 의 분기 — 천계 대사에 나오는 것만).</summary>
        string Name(int code, int fmt)
        {
            if (code == 0x26) return player;
            if (code == 0x27)
            {
                if (fmt == 0x1B) { uint x = TypeByte(); return x < data.Types.Count ? text.UiText(data.Types[(int)x][0], null) : null; }   // 0x0809C144: 가족 칭호
                return personName((int)OrigResultScript.MarkFamily);
            }
            if (code == 0x2A) return personName((int)m.R16(Fam + 0x66C));                      // 세대주 (0x080AA1F4: 가족 +0x66C)
            if (code == 0x2B)
            {
                // 남긴 기술 이름: 0x080A0EA0 이 객체 +0x78 에 고인 항목 +2 (스킬 번호)의 이름(0x081194F8)을 넣고, 글 엔진 0x080AA6EC 가 그 글을 쓴다
                uint p = m.R32(Obj + 0x98);
                if (p == 0) return null;
                uint sk = m.R8(p + 2);
                return sk < text.Skills.Count ? text.Skills[(int)sk].Name : null;
            }
            if (code < 0x1C)
            {
                uint id = m.R16(OrigMem.Slots + 2 * (uint)code);
                if (id == 0xFFFF) return null;
                if (fmt == 0x1F)   // 그 사람이 남긴 꿈 (글 엔진 0x080A80E0: 가계 기록 +0x1E 관심사 → 0x0811978C = 특별 기록 +0 글)
                {
                    string s; return data.Dreams.TryGetValue((int)(m.R16(OrigMem.GeneAddr(id) + 0x1E) & 0x7FFF), out s) ? s : null;
                }
                return personName((int)id);
            }
            return null;
        }

        /// <summary>숫자 자리 1A 05 04 00 = 세대 수(가족 +0x6A3, 화면의 "N대째").</summary>
        string Number(int a, int b) { return a == 4 ? Gen.ToString() : null; }
    }
}
