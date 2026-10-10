using System;
using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 코드(변환 트리)로 진행하는 세션 — 화면은 이전 GameSession 과 같은 방식(IGameSession)으로 쓴다.
    /// 원작 메모리가 기준이고, 화면용 Family 는 하루마다 원작 레코드에서 다시 만든다(투영).
    ///  - 인물: 레코드 0x0202C6C4+976n 중 번호(+0x3C)가 있는 것. 생일 +0x2E, 성별 +0x31, 능력치 +0x50~+0x56, 게이지 +0x5A,
    ///    하트(몰입도) +0x5B, 직업 +0x58, 직업 숙련 +0x5E, 스킬 +0x62~+0x64, 관심사 날짜 +0x48, 화살 표시 +0x69, 성격 +0x32, 관심사 +0x80/+0x82.
    ///  - 가족: 무드 0x0202C6AE · 집 등급 0x0202C6AF · 자산 0x0202C6B8.
    ///  - 관계: 족보 0x0202EB9C+60×번호 의 +0x10 아버지 · +0x12 어머니 · +0x14 배우자. 가장 0x0202C67C, 시작 날짜 0x0202C688.
    /// 사건은 원작처럼 그날 진행(OrigGame.TickDay) 안에서 효과까지 끝나고, 화면에는 그 뒤 대사(로컬 글 대응표)로 보여 준다.
    /// 아직 원작대로 하지 못한 것(표시):
    ///  - 이름: 한국식 이름을 앱이 붙인다(OrigNames, 가족 안에서 겹치지 않게). 원작 이름(자체 글자표)은 쓰지 않는다 — 사용자 결정.
    ///  - 아직 보여 주지 않은 그날 사건 목록은 저장하지 않는다(효과는 이미 원작 메모리에 들어가 있다).
    /// </summary>
    public sealed class OrigSession : IGameSession
    {
        public Family Family { get; private set; }
        public readonly OrigGame Game;
        public readonly OrigRules Rules;
        public readonly OrigText Text;
        public readonly List<EffectChange> LastChanges = new List<EffectChange>();

        readonly Queue<OrigGame.DayEvent> pending = new Queue<OrigGame.DayEvent>();
        OrigGame.DayEvent cur; List<string> pages; int page;

        public bool Paused { get { return cur != null; } }

        /// <summary>표시 이름이 없는 새 인물에게 붙이는 이름 (앱 표시용, 원작 이름 아님).</summary>
        static readonly string[] MaleNames = { "민준", "서준", "도윤", "하준", "지호", "준우", "현우", "건우", "우진", "선우", "유준", "정우", "승현", "시우", "지훈", "태윤" };
        static readonly string[] Surnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권", "황", "안", "송", "류", "홍" };
        static readonly string[] FemaleNames = { "서연", "지우", "하윤", "서윤", "민서", "하은", "지아", "수아", "지유", "채원", "윤서", "다은", "예린", "소율", "가은", "나연" };

        OrigSession(OrigRules rules, OrigText text, Family f, OrigMem m)
        {
            Rules = rules; Text = text ?? new OrigText(); Family = f;
            Game = new OrigGame(m, rules);
            Project();
        }

        /// <summary>
        /// 원작 난수 시작값: 부팅 때 0x080089DC 가 VCOUNT(0x04000006, 그 순간의 주사선 0~227) × 0xAD 를 0x02000000 에 넣고(실측 26×173 = 4498),
        /// 그 뒤 타이틀·인트로 화면이 매 프레임 난수를 여러 번 돌린다(0x080BE6A6 등) — 그래서 새 게임 때의 값은 누르는 시점에 따라 달라진다.
        /// 앱: vcount 는 앱이 고르고, 타이틀 화면에서 돈 횟수는 앱이 넘겨준 수(타이틀에 머문 프레임 수 등)로 대신한다(원작 타이틀 화면의 호출 횟수는 옮기지 않음).
        /// </summary>
        public static uint BootSeed(uint vcount, uint titleSteps)
        {
            uint s = (vcount % 228) * 0xADu;
            for (uint i = 0; i < titleSteps; i++) s = unchecked(s * 0x6Du + 0x3FDu);
            return s;
        }

        /// <summary>원작 "신이 추천하는 가족"으로 새로 시작. seed = 원작 난수(0x02000000) 값(BootSeed).</summary>
        public static OrigSession NewRecommended(OrigRules rules, OrigText text, string surname, uint seed, int year = 2005, int month = 1, int day = 1)
        {
            var m = new OrigMem(); var vm = rules.CreateVm(m);
            OrigNewGame.BlankCartridge(vm);
            OrigNewGame.TitleNewGame(vm);
            m.W32(OrigMem.Seed, seed);
            OrigNewGame.Recommended(vm, year, month, day);
            if (string.IsNullOrEmpty(surname)) surname = Surnames[(int)(seed % (uint)Surnames.Length)];
            var f = new Family { Name = surname };
            GiveStartingArrows(m);
            var s = new OrigSession(rules, text, f, m);
            s.PrepareSave();
            return s;
        }

        /// <summary>
        /// 원작 "내가 아는 가족" 경로로 새로 시작 (OrigNewGame.Custom). 앱 입력 화면의 값(구성·생일·혈액형·성격·능력 순위·직업)을 원작 입력 화면과 같은 형식으로 넣는다.
        /// 능력치 등은 원작 가족 레코드 만들기가 정한다. 입력한 이름은 표시 이름(OrigNames)으로 쓴다.
        /// 체격·외형 바이트는 원작 기본값(화면 외형은 앱의 Look) — 규칙 판정에는 쓰이지 않는 값이다.
        /// </summary>
        public static OrigSession NewCustom(OrigRules rules, OrigText text, FamilySetup setup, uint seed)
        {
            var ordered = new List<MemberSetup>(); var slots = new List<int>();
            int child = 4;
            foreach (var r in new[] { FamilyRole.Grandfather, FamilyRole.Grandmother, FamilyRole.Father, FamilyRole.Mother })
                foreach (var ms in setup.Members) if (ms.Role == r) { ordered.Add(ms); slots.Add((int)r); }
            foreach (var ms in setup.Members) if (ms.Role == FamilyRole.Child && child < 8) { ordered.Add(ms); slots.Add(child++); }
            var cms = new List<OrigNewGame.CustomMember>();
            for (int k = 0; k < ordered.Count; k++)
            {
                var ms = ordered[k];
                var c = new OrigNewGame.CustomMember
                {
                    Slot = slots[k], Daughter = ms.Gender == 1, Year = GameDate.Year(ms.BirthDay), Month = GameDate.Month(ms.BirthDay), Day = GameDate.Day(ms.BirthDay),
                    Blood = Math.Max(0, Array.IndexOf(MemberSetup.Bloods, ms.Blood)), Personality = ms.Personality
                };
                for (int st = 0; st < 4; st++) { int rk = ms.AbilityRank[st]; if (rk >= 1 && rk <= 4) c.RankStats[rk - 1] = st; }
                // 외형 바이트: 원작 캐릭터 화면은 목록을 그때그때 무작위로 만든다(0x08048B4C). 여기서는 실기 캐릭터 화면이 만든 한 예를 성별로 쓴다.
                // (규칙 판정 속성 49종은 이 바이트를 읽지 않는다. 6세 이하 아이는 원작처럼 부모 외형에서 만든다(0x08049AF0).)
                c.Look = (byte[])(ms.Gender == 1 ? DefaultLookF : DefaultLookM).Clone();
                var cand = OrigJobs.Candidates(rules, setup.StartDay, ms.BirthDay, ms.Gender, OrigJobs.Relation(ms.Role, ms.Gender));
                c.JobChoice = Math.Max(0, cand.IndexOf(ms.Job));
                cms.Add(c);
            }
            var m = new OrigMem(); var vm = rules.CreateVm(m);
            OrigNewGame.BlankCartridge(vm);
            OrigNewGame.TitleNewGame(vm);
            m.W32(OrigMem.Seed, seed);
            OrigNewGame.Custom(vm, GameDate.Year(setup.StartDay), GameDate.Month(setup.StartDay), GameDate.Day(setup.StartDay), cms);
            string surname = string.IsNullOrEmpty(setup.Surname) ? Surnames[(int)(seed % (uint)Surnames.Length)] : setup.Surname;
            var f = new Family { Name = surname };
            GiveStartingArrows(m);
            // 원작 가족 레코드 만들기는 설정 블록 순서대로 레코드를 만든다 → n 번째 레코드 = n 번째 구성원
            for (int n = 0; n < ordered.Count && n < 8; n++)
                if (OrigGame.Present(m, n) && !string.IsNullOrEmpty(ordered[n].Name)) f.OrigNames[(int)m.R16(OrigMem.PersonAddr(n) + 0x3C)] = ordered[n].Name;
            var s = new OrigSession(rules, text, f, m);
            for (int n = 0; n < ordered.Count && n < s.Family.Members.Count; n++)
            {
                var p = s.Family.Get((int)m.R16(OrigMem.PersonAddr(n) + 0x3C));
                if (p != null && ordered[n].Look != null) p.Look = ordered[n].Look.Clone();
            }
            s.PrepareSave();
            return s;
        }

        /// <summary>시작 화살: 원작 큐피트 튜토리얼이 힘내라·진정해 화살을 각 5개 준다(원작 화면). 튜토리얼 화면은 옮기지 않아 보유 수(0x0202C640)만 넣는다.</summary>
        static void GiveStartingArrows(OrigMem m) { m.W8(ArrowInv, 5); m.W8(ArrowInv + 1, 5); }

        static readonly byte[] DefaultLookM = { 0x00, 0x43, 0x22, 0x00, 0x00, 0x05, 0x43, 0x01, 0x00, 0x02, 0x03, 0, 0, 0, 0, 0 };
        static readonly byte[] DefaultLookF = { 0x0E, 0x12, 0x12, 0x00, 0x12, 0x22, 0x12, 0x0F, 0x00, 0x02, 0x03, 0, 0, 0, 0, 0 };

        /// <summary>저장한 원작 가족 이어 하기 (Family.OrigState).</summary>
        public static OrigSession Load(OrigRules rules, OrigText text, Family f)
        {
            if (f == null || !f.IsOriginal) throw new ArgumentException("원작 가족 저장이 아님");
            var b = Convert.FromBase64String(f.OrigState);
            var m = new OrigMem();
            int n = (int)(OrigMem.SaveEnd - OrigMem.SaveStart);
            var blk = new byte[n]; Array.Copy(b, 0, blk, 0, Math.Min(n, b.Length));
            m.LoadSaveBlock(blk);
            if (b.Length >= n + 4) m.W32(OrigMem.Seed, BitConverter.ToUInt32(b, n));
            var s = new OrigSession(rules, text, f, m);
            if (b.Length >= n + 5 && b[n + 4] != 0) s.Game.ResumeMorning();
            return s;
        }

        /// <summary>원작 메모리(세이브 영역 + 난수 seed + 장면이 날을 끝냈는지)를 Family.OrigState 에 적는다.</summary>
        public void PrepareSave()
        {
            SyncIn(); Project();
            var blk = Game.Mem.SaveBlock();
            var b = new byte[blk.Length + 5];
            Array.Copy(blk, b, blk.Length);
            Array.Copy(BitConverter.GetBytes(Game.Mem.R32(OrigMem.Seed)), 0, b, blk.Length, 4);
            b[blk.Length + 4] = (byte)(Game.SceneEndedDay ? 1 : 0);
            Family.OrigState = Convert.ToBase64String(b);
        }

        public void ReplaceCatalog(ContentCatalog c) { }

        // ---------- 하루 진행 ----------
        public bool StepDay()
        {
            if (cur != null) return false;
            if (pending.Count > 0) { StartNext(); return true; }
            SyncIn();
            Game.NextDate();
            foreach (var e in Game.TickDay()) pending.Enqueue(e);
            Project();
            if (pending.Count > 0) { StartNext(); return true; }
            return false;
        }

        /// <summary>
        /// 화면 쪽 Family 를 직접 바꾼 값(시험·옛 경로)을 원작 메모리에 옮긴다. 마지막 투영 값과 다른 것만 쓴다.
        /// 화살·아이템은 Use 가 원작 함수로 원작 메모리를 바로 바꾸므로 여기를 거치지 않는다(보유 수를 직접 바꾼 경우만 옮긴다).
        /// </summary>
        void SyncIn()
        {
            var m = Game.Mem;
            for (int n = 0; n < 8; n++)
            {
                if (!OrigGame.Present(m, n)) continue;
                uint p = OrigMem.PersonAddr(n);
                var q = Family.Get((int)m.R16(p + 0x3C)); Snap sn;
                if (q == null || !snaps.TryGetValue(q.Id, out sn)) continue;
                if (q.ArrowFlags != sn.Arrow) m.W8(p + 0x69, (uint)q.ArrowFlags & 0xFF);
                for (int i = 0; i < 4; i++) if (q.Stats[i] != sn.Stats[i]) m.W16(p + 0x50 + 2 * (uint)i, (uint)Math.Max(0, Math.Min(Stat.Max, q.Stats[i])));
            }
            if (Family.Mood != famSnap.Mood) m.W8(0x0202C6AE, (uint)Math.Max(0, Math.Min(255, Family.Mood)));
            foreach (var t in Interventions.Tools)
            {
                if (t.OrigSlot < 0) continue;
                int n = Interventions.Count(Family, t.Id), was;
                if (!famSnap.Counts.TryGetValue(t.Id, out was) || n != was) m.W8(SlotAddr(t), (uint)Math.Max(0, Math.Min(255, n)));
            }
        }

        static uint SlotAddr(ToolDef t) { return (t.Kind == "arrow" ? ArrowInv : ItemInv) + (uint)t.OrigSlot; }
        /// <summary>원작 보유 칸: 화살 16칸·아이템 16칸 (0x0202C010+0x630 / +0x640, 실기 메뉴로 칸 순서 확인).</summary>
        public const uint ArrowInv = 0x0202C640, ItemInv = 0x0202C650;
        sealed class Snap { public int Arrow, Hearts; public int[] Stats = new int[4]; }
        readonly Dictionary<int, Snap> snaps = new Dictionary<int, Snap>();
        sealed class FamSnap { public int Mood; public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(); }
        readonly FamSnap famSnap = new FamSnap();

        // ---------- 화살·아이템 (원작 함수) ----------
        // 원작 메뉴 객체·인물 객체 대신 쓰는 작업 자리 (기록 범위 0x0F000000~0x0F03FFFF 안, 비교 범위 0x0F000000~0x0F000FFF 밖)
        const uint ToolObj = 0x0F038000, ToolChar = 0x0F038400;
        /// <summary>옮기지 않은 도구: 통신 결혼(통신 기능), 시간의 책갈피(원작 0x080108BC 의 따로 저장), 선대 마음의 결정(스킬 고르기 화면 0x0801D8B0 의 100번대 경로).</summary>
        static readonly HashSet<string> NotPorted = new HashSet<string> { "arrow.link", "item.bookmark", "item.heart_crystal" };

        public bool CanUse(string toolId) { var t = Interventions.Find(toolId); return t != null && t.OrigSlot >= 0 && !NotPorted.Contains(toolId); }

        /// <summary>
        /// 원작 흐름 그대로 화살·아이템을 쓴다.
        ///  - 화살: 쏠 수 있는지 0x0801CECC(메뉴, 종류) → 맞으면 0x08024440(메뉴, 종류, 인물 객체, -) → 보유 수 −1 (0x0801CDC2).
        ///    못 쏘면 원작은 메뉴+0x200 에 거절 문구(0x0858683C~ 표)를 둔다 — 앱은 그 문구 번호로 이유를 고른다.
        ///  - 아이템: 0x08024B80(칸, 인물 레코드, 스킬, 메뉴) 이 0 이면 성공 → 보유 수 −1 (0x0801D9AE). 1~3 은 못 쓰는 경우.
        /// 원작 메뉴 객체에서 이 함수들이 읽는 자리는 "선택 인물 번호(+0x154) = 0, 인물 객체 표(+4)" 뿐이라 그 둘만 채운다.
        /// </summary>
        public string Use(Person target, string toolId)
        {
            var t = Interventions.Find(toolId);
            if (t == null || t.OrigSlot < 0) return "알 수 없는 도구";
            if (NotPorted.Contains(toolId)) return t.Name + "은(는) 아직 옮기지 않았습니다";
            if (target == null) return "대상이 없습니다";
            SyncIn();
            var m = Game.Mem; uint rec = 0;
            for (int k = 0; k < 8; k++) if (OrigGame.Present(m, k) && (int)m.R16(OrigMem.PersonAddr(k) + 0x3C) == target.Id) { rec = OrigMem.PersonAddr(k); break; }
            if (rec == 0) return "대상이 없습니다";
            uint inv = SlotAddr(t);
            if (m.R8(inv) == 0) return t.Name + "이(가) 없습니다";
            var ew = (byte[])m.Ewram.Clone(); var iw = (byte[])m.Iwram.Clone();
            try
            {
                for (uint i = 0; i < 0x800; i += 4) m.W32(ToolObj + i, 0);
                m.W32(ToolObj + 4, ToolChar); m.W32(ToolObj + 0x154, 0); m.W32(ToolChar + 4, rec);
                if (t.Kind == "arrow")
                {
                    if (Game.Vm.Call("0801CECC", ToolObj, (uint)t.OrigSlot) == 0) return ArrowRefusal(RefusalEntry(m, m.R32(ToolObj + 0x200)), t);
                    Game.Vm.Call("08024440", ToolObj, (uint)t.OrigSlot, ToolChar, m.R16(ToolObj + 0x18C));
                }
                else
                {
                    uint r = Game.Vm.Call("08024B80", (uint)t.OrigSlot, rec, 0, ToolObj);
                    if (r != 0) return ItemRefusal(t, r);
                }
                m.W8(inv, m.R8(inv) - 1);
                Project();
                return null;
            }
            catch (OrigUnmodeled e)
            {
                Array.Copy(ew, m.Ewram, ew.Length); Array.Copy(iw, m.Iwram, iw.Length);
                return t.Name + ": 원작 함수 실행 실패 (" + e.Message + ")";
            }
        }

        /// <summary>메뉴+0x200 의 글 포인터가 원작 문구 표(0x0858683C 부터 4바이트씩)의 어느 자리 값인지 → 그 자리 주소 (모르면 0).</summary>
        static uint RefusalEntry(OrigMem m, uint text)
        {
            for (uint a = 0x0858683C; a < 0x085868C0; a += 4) { try { if (m.R32(a) == text) return a; } catch (OrigUnmodeled) { } }
            return 0;
        }

        /// <summary>화살 거절 이유 (원작 문구 표 0x0858683C~ 의 자리 → 0x0801CECC 의 조건을 읽어 앱이 쓴 설명. 원작 글을 옮긴 것이 아니다).</summary>
        static string ArrowRefusal(uint msg, ToolDef t)
        {
            switch (msg)
            {
                case 0x0858683C: return "화살의 효과가 계속되고 있습니다. 힘내라의 화살·진정해의 화살은 쏠 수 없습니다";
                case 0x08586840: return "열중 게이지가 바닥이라 힘내라의 화살을 쏠 수 없습니다";
                case 0x08586844: return "열중 게이지가 가득이라 진정해의 화살을 쏠 수 없습니다";
                default: return "지금 이 사람에게는 " + t.Name + "을(를) 쏠 수 없습니다 (원작 판정, 문구 " + msg.ToString("X8") + ")";
            }
        }

        /// <summary>아이템 실패 (0x08024B80 의 돌려준 값 — 횃불: 악마 없음, 사랑의 고리: 연인 없음(+0x74), 왕관: 1 세대주의 자녀 아님 · 2·3 지금은 안 됨).</summary>
        static string ItemRefusal(ToolDef t, uint r)
        {
            if (t.Id == "item.torch") return "악마가 와 있을 때만 쓸 수 있습니다";
            if (t.Id == "item.ring.love") return "연인이 있을 때만 쓸 수 있습니다";
            if (t.Id == "item.crown") return r == 1 ? "세대주의 자녀에게만 쓸 수 있습니다" : "지금은 후계자의 왕관을 쓸 수 없습니다";
            return "지금은 " + t.Name + "을(를) 쓸 수 없습니다 (원작 판정 " + r + ")";
        }

        void StartNext()
        {
            cur = pending.Dequeue(); page = 0;
            OrigText.Rec rec;
            if (Text.Records.TryGetValue(cur.Data, out rec) && !string.IsNullOrEmpty(rec.Script))
                pages = OrigText.Pages(rec.Script, SlotName);
            else pages = new List<string>();
            if (pages.Count == 0) pages.Add("(원작 사건 " + cur.Data.ToString("X8") + " — 대사 자료 없음)");
        }

        string SlotName(int code)
        {
            if (code == 0x27) return Family.Name;
            if (code < 0 || code >= 28 || cur == null) return null;
            uint id = cur.Slots[code];
            return id == 0xFFFF ? null : NameOf((int)id, -1);
        }

        /// <summary>표시 이름: 이미 붙인 이름, 없으면 성별 이름 목록에서 지금 가족(살아 있는 사람)과 겹치지 않는 것을 고른다.</summary>
        string NameOf(int id, int gender)
        {
            string s;
            if (Family.OrigNames.TryGetValue(id, out s) && !string.IsNullOrEmpty(s)) return s;
            if (gender < 0) { var p = Family.Get(id); gender = p != null ? p.Gender : 0; }
            var list = gender == 1 ? FemaleNames : MaleNames;
            var used = new HashSet<string>(); foreach (var q in Family.Members) used.Add(q.Name);
            for (int n = 0; n < 8; n++)
                if (OrigGame.Present(Game.Mem, n)) { string t; if (Family.OrigNames.TryGetValue((int)Game.Mem.R16(OrigMem.PersonAddr(n) + 0x3C), out t)) used.Add(t); }
            s = null;
            for (int k = 0; k < list.Length && s == null; k++) { var c = list[(id + k) % list.Length]; if (!used.Contains(c)) s = c; }
            if (s == null) s = list[id % list.Length] + id;
            Family.OrigNames[id] = s;
            return s;
        }

        public EventView View()
        {
            if (cur == null) return null;
            OrigText.Rec rec; Text.Records.TryGetValue(cur.Data, out rec);
            return new EventView
            {
                EventId = rec != null ? rec.Id : "0x" + cur.Data.ToString("X8"),
                Title = rec != null && rec.Title.Length > 0 ? rec.Title : "원작 사건",
                Speaker = "", Text = pages[Math.Min(page, pages.Count - 1)], Phase = "pages",
                Origin = "original", Certainty = "rom", TextSource = rec != null ? "reference-translation" : "none",
                PersonId = (int)cur.PersonId
            };
        }

        public bool Advance()
        {
            if (cur == null) return false;
            if (page < pages.Count - 1) { page++; return false; }
            OrigText.Rec rec; Text.Records.TryGetValue(cur.Data, out rec);
            Family.History.Add(new EventRecord { Day = Family.Today, EventId = rec != null ? rec.Id : "0x" + cur.Data.ToString("X8"),
                Title = rec != null ? rec.Title : "원작 사건", PersonId = (int)cur.PersonId });
            cur = null;
            return true;
        }

        public bool Choose(string choiceId) { return false; }
        /// <summary>
        /// 결과 예고: 지금 관심사가 MAX(255)/MIN(0) 에서 끝나면 무엇이 오르고 내리는지. 원작 메모리를 잠시 보관해 두고
        /// 같은 VM 으로 그 사건(변형 고르기 + 효과 함수, OrigEvents.Run)을 실제로 돌린 뒤 능력치 차이를 보고 메모리를 되돌린다.
        /// 변형 판정·효과에 난수가 섞이면 지금 난수 상태에서의 한 경우다(HasRandom). 원작 화면이 예고를 어떻게 계산하는지는 확인하지 않았다.
        /// </summary>
        public Prediction Predict(int personId, bool max)
        {
            SyncIn();   // 화면에서 바꾼 값을 먼저 원작 메모리에 (가족 목록은 다시 만들지 않는다 — 화면이 목록을 도는 중일 수 있다)
            var m = Game.Mem; int n = -1;
            for (int k = 0; k < 8; k++) if (OrigGame.Present(m, k) && (int)m.R16(OrigMem.PersonAddr(k) + 0x3C) == personId) { n = k; break; }
            if (n < 0 || m.R16(OrigMem.PersonAddr(n) + 0x80) == 0xFFFF) return null;
            var ew = (byte[])m.Ewram.Clone(); var iw = (byte[])m.Iwram.Clone(); var fr = (byte[])m.Frames.Clone(); var io = (byte[])m.Io.Clone();
            var before = new Dictionary<int, int[]>();
            for (int k = 0; k < 8; k++) if (OrigGame.Present(m, k)) before[k] = Stats(m, k);
            try
            {
                uint data = OrigEvents.Run(Game.Vm, Rules, n, max);
                var pr = new Prediction { OutcomeId = "0x" + data.ToString("X8"), HasRandom = true };
                OrigText.Rec rec; if (Text.Records.TryGetValue(data, out rec)) { pr.OutcomeId = rec.Id; pr.Title = rec.Title; }
                foreach (var kv in before)
                {
                    if (!OrigGame.Present(m, kv.Key)) continue;
                    var after = Stats(m, kv.Key); int id = (int)m.R16(OrigMem.PersonAddr(kv.Key) + 0x3C);
                    for (int i = 0; i < 4; i++) if (after[i] != kv.Value[i]) pr.Changes.Add(new EffectChange { PersonId = id, Key = "s" + i, Delta = after[i] - kv.Value[i] });
                }
                return pr;
            }
            catch (OrigUnmodeled) { return null; }
            finally { Array.Copy(ew, m.Ewram, ew.Length); Array.Copy(iw, m.Iwram, iw.Length); Array.Copy(fr, m.Frames, fr.Length); Array.Copy(io, m.Io, io.Length); }
        }

        static int[] Stats(OrigMem m, int n)
        {
            uint a = OrigMem.PersonAddr(n); var r = new int[4];
            for (int i = 0; i < 4; i++) r[i] = (int)m.R16(a + 0x50 + 2 * (uint)i);
            return r;
        }

        // ---------- 원작 레코드 → 화면용 가족 ----------
        public void Project()
        {
            var m = Game.Mem; var f = Family;
            var old = new Dictionary<int, Person>(); foreach (var p in f.Members) old[p.Id] = p;
            f.Members.Clear();
            for (int n = 0; n < 8; n++)
            {
                if (!OrigGame.Present(m, n)) continue;
                uint a = OrigMem.PersonAddr(n);
                int id = (int)m.R16(a + 0x3C);
                Person p; if (!old.TryGetValue(id, out p)) p = new Person { Id = id };
                p.Gender = (int)m.R8(a + 0x31) == 0 ? 0 : 1;
                OrigDate.Get(m, a + 0x2E, out int by, out int bm, out int bd);
                p.BirthDay = SafeDay(by, bm, bd);
                for (int k = 0; k < 4; k++) p.Stats[k] = (int)m.R16(a + 0x50 + 2 * (uint)k);
                p.Gauge = (int)m.R8(a + 0x5A); p.Immersion = (int)m.R8(a + 0x5B);
                // 하트 = +0x5B (0~255). 화면 3칸은 원작 0x08021F14 의 반 칸 수(0~6)로 보인다.
                p.Hearts = HalfHearts(m.R8(a + 0x5B)) * Person.HeartUnit / 2;
                p.Job = (int)m.R8(a + 0x58); p.JobMastery = (int)m.R8(a + 0x5E);
                p.Skills.Clear(); for (uint k = 0; k < 3; k++) { uint sk = m.R8(a + 0x62 + k); if (sk != 0xFF) p.Skills.Add((int)sk); }
                p.InterestDay = (int)m.R16(a + 0x48); p.ArrowFlags = (int)m.R8(a + 0x69);
                p.PersonalityCode = (int)(m.R8(a + 0x32) & 0xF);
                int t = (int)m.R16(a + 0x80), i = (int)m.R16(a + 0x82);
                p.PlannedStateId = t == 0xFFFF ? "" : "orig:" + t + "," + i;
                p.PlannedTitle = t == 0xFFFF ? "" : Text.InterestTitle(t, i);
                uint g = OrigMem.Gene + OrigMem.GeneSize * (uint)id;
                p.FatherId = Rel(m.R16(g + 0x10)); p.MotherId = Rel(m.R16(g + 0x12)); p.SpouseId = Rel(m.R16(g + 0x14));
                p.Alive = true;
                p.Name = NameOf(id, p.Gender);
                f.Members.Add(p);
                snaps[id] = new Snap { Arrow = p.ArrowFlags, Hearts = p.Hearts, Stats = (int[])p.Stats.Clone() };
            }
            f.HeadId = (int)m.R16(0x0202C67C);
            // 가족 값 (가족 기준 0x0202C010: +0x69E 무드 · +0x69F 집 등급 · +0x6A8 자산 — 참고 자료 family-save-layout 과 0x08111D54)
            f.Mood = (int)m.R8(0x0202C6AE); f.HouseGrade = (int)m.R8(0x0202C6AF); f.Assets = m.R32(0x0202C6B8);
            foreach (var t in Interventions.Tools)
                if (t.OrigSlot >= 0) { f.Items[t.Id] = (int)m.R8(SlotAddr(t)); famSnap.Counts[t.Id] = f.Items[t.Id]; }
            famSnap.Mood = f.Mood;
            OrigDate.Get(m, OrigMem.Date, out int y, out int mo, out int d);
            f.Today = SafeDay(y, mo, d);
            OrigDate.Get(m, 0x0202C688, out int sy, out int sm, out int sd);
            f.StartDay = sy > 0 ? SafeDay(sy, sm, sd) : f.Today;
        }

        /// <summary>원작 하트 표시(0x08021F14): 0 → 0, 1~47 → 1, ~95 → 2, ~143 → 3, ~191 → 4, ~239 → 5, 그 위 6 (반 칸 수).</summary>
        public static int HalfHearts(uint raw)
        {
            if (raw == 0) return 0;
            if (raw <= 0x2F) return 1; if (raw <= 0x5F) return 2; if (raw <= 0x8F) return 3; if (raw <= 0xBF) return 4; if (raw <= 0xEF) return 5;
            return 6;
        }

        static int SafeDay(int y, int mo, int d) { return GameDate.Make(Math.Max(1, Math.Min(9999, y)), Math.Max(1, Math.Min(12, mo)), Math.Max(1, d)); }

        static int Rel(uint v) { return v == 0xFFFF ? -1 : (int)v; }
    }
}
