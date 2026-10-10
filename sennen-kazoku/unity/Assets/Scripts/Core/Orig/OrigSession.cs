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
        List<List<OrigText.StageActor>> stages = new List<List<OrigText.StageActor>>();   // 장마다 액자 안 인물 상태 (사건 장면 그림)

        public bool Paused { get { return cur != null; } }

        /// <summary>표시 이름이 없는 새 인물에게 붙이는 이름 (앱 표시용, 원작 이름 아님).</summary>
        static readonly string[] MaleNames = { "민준", "서준", "도윤", "하준", "지호", "준우", "현우", "건우", "우진", "선우", "유준", "정우", "승현", "시우", "지훈", "태윤" };
        static readonly string[] Surnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권", "황", "안", "송", "류", "홍" };
        static readonly string[] FemaleNames = { "서연", "지우", "하윤", "서윤", "민서", "하은", "지아", "수아", "지유", "채원", "윤서", "다은", "예린", "소율", "가은", "나연" };

        OrigSession(OrigRules rules, OrigText text, Family f, OrigMem m)
        {
            Rules = rules; Text = text ?? new OrigText(); Family = f;
            Game = new OrigGame(m, rules);
            Game.TextOverride = a => Text.Overrides.ContainsKey(a);
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
        /// <param name="cartridge">이전 원작 세이브 영역(CartridgeOf) — 있으면 빈 카트리지 대신 그 카트리지에서 새로 시작한다(가문의 기록 등이 원작처럼 남는다).</param>
        public static OrigSession NewRecommended(OrigRules rules, OrigText text, string surname, uint seed, int year = 2005, int month = 1, int day = 1, byte[] cartridge = null)
        {
            var m = new OrigMem(); var vm = rules.CreateVm(m);
            if (cartridge != null) OrigNewGame.Cartridge(vm, cartridge); else OrigNewGame.BlankCartridge(vm);
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
        public static OrigSession NewCustom(OrigRules rules, OrigText text, FamilySetup setup, uint seed, byte[] cartridge = null)
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
            if (cartridge != null) OrigNewGame.Cartridge(vm, cartridge); else OrigNewGame.BlankCartridge(vm);
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

        /// <summary>저장한 원작 가족의 세이브 영역(카트리지 상태) — 다음 새 게임을 이 카트리지에서 시작할 때 쓴다. 원작 저장이 아니면 null.</summary>
        public static byte[] CartridgeOf(Family f)
        {
            if (f == null || !f.IsOriginal) return null;
            var b = Convert.FromBase64String(f.OrigState);
            int n = (int)(OrigMem.SaveEnd - OrigMem.SaveStart);
            if (b.Length < n) return null;
            var blk = new byte[n]; Array.Copy(b, blk, n); return blk;
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
            foreach (var e in Game.TickDay()) { pending.Enqueue(e); if (e.Result[0] == 2) lineageEnded = true; }
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
        /// <summary>옮기지 않은 도구: 통신 결혼(통신 기능).</summary>
        static readonly HashSet<string> NotPorted = new HashSet<string> { "arrow.link" };

        /// <summary>
        /// 시간의 책갈피: 아이템 함수 칸 10 이 0x080108BC 로 가족 저장 내용을 세이브 영역 안 책갈피 자리(0x0202C010+0xD2CC~)에 복사한다.
        /// 되돌리기 0x08010AB4 는 0x0202C678 의 0x80 이 서 있을 때 책갈피 자리를 되살리고 1 을 돌려준다(원작은 가문이 끊긴 뒤 장면 0x0809EE4E 에서 부른다).
        /// </summary>
        public bool HasBookmark { get { return (Game.Mem.R32(0x0202C678) & 0x80) != 0; } }

        /// <summary>
        /// 가문이 끊김 (원작): 가장(0x0202C67C)이 가장인 채로 죽으면 죽음 처리 0x08114658 이 가문의 기록을 남기고(0x0801045C)
        /// 결과 첫 단어 = 2 → 사건 장면 끝 0x0804DB9C 가 가문이 끊긴 장면(0x0809AF60(5,2) → 0x0809EAAC)으로 간다.
        /// 가장 자리는 후계자가 결혼할 때 넘어가므로(장기 진행 시험에서 확인) 그 전에 가장이 죽으면 끝난다.
        /// 앱 판정: 그 장면 끝이 나왔거나, 가장 번호가 살아 있는 가족에 없으면.
        /// </summary>
        public bool LineageEnded { get { return lineageEnded || (Family.Members.Count > 0 ? Family.Get(Family.HeadId) == null : true); } }
        bool lineageEnded;

        /// <summary>책갈피를 쓴 날로 돌아간다 (원작 0x08010AB4). 성공하면 true.</summary>
        public bool RestoreBookmark()
        {
            if (!HasBookmark) return false;
            uint r = Game.Vm.Call("08010AB4");
            pending.Clear(); cur = null; lineageEnded = false;
            Project();
            return r != 0;
        }

        /// <summary>선대의 마음 목록 (0x0202C328, 4바이트씩 최대 99개: u16 인물 번호, u8 스킬, 끝 0xFFFF). "선대 마음의 결정"을 고르면 원작이 이 목록을 보여 준다(실기).</summary>
        public const uint LegacyList = 0x0202C328;

        /// <summary>선대의 마음 목록: [인물 번호, 스킬 번호] 차례대로.</summary>
        public List<int[]> LegacyHearts()
        {
            var r = new List<int[]>(); var m = Game.Mem;
            for (uint k = 0; k < 99; k++)
            {
                uint id = m.R16(LegacyList + 4 * k); if (id == 0xFFFF) break;
                r.Add(new[] { (int)id, (int)m.R8(LegacyList + 4 * k + 2) });
            }
            return r;
        }

        /// <summary>스킬 이름 (로컬 팩의 원작 스킬 표). 없으면 "스킬 N번".</summary>
        public string SkillName(int k) { return k >= 0 && k < Text.Skills.Count && Text.Skills[k].Name.Length > 0 ? Text.Skills[k].Name : "스킬 " + k + "번"; }

        /// <summary>스킬 설명 (원작 설명 + 효과 줄, 로컬 번역이 있을 때). 없으면 빈 글.</summary>
        public string SkillDesc(int k)
        {
            if (k < 0 || k >= Text.Skills.Count) return "";
            var sk = Text.Skills[k];
            return sk.Effect.Length > 0 ? sk.Desc + " (" + sk.Effect + ")" : sk.Desc;
        }

        /// <summary>선대의 마음 목록에 보이는 이름 (앱이 붙인 이름 — 세상을 떠난 사람도 저장된 이름을 쓴다).</summary>
        public string LegacyName(int personId) { return NameOf(personId, -1); }

        /// <summary>
        /// 가문의 기록 (원작 0x0801045C 가 가문이 끝날 때 적는 상위 3칸, 0x0202C038 + 32·k, 햇수 순).
        /// +0 [0x0202C6B8] · +4 u16 역대 가족 수(족보 +0x37 의 8 비트가 없는 사람 수, 0 = 빈 칸) · +6 가문 이름 칸 14바이트(0x0202C6A0) ·
        /// +0x14 시작 날(0x0202C688) · +0x17 끝난 날 · +0x1A 가족 유형(0x08118D28: 재산 3단계×3 + 무드 3단계 → 반짝반짝가족~밑바닥 가족) ·
        /// +0x1B [0x0202C692] · +0x1C [0x0202C6AF]&amp;0xF · +0x1D [0x0202C6B3]. 햇수 = 0x08095EE8(시작, 끝) (원작 순위 기준과 같은 함수).
        /// </summary>
        public sealed class FamilyRecord
        {
            public int Rank, Years, Members, Type;
            public int StartY, StartM, StartD, EndY, EndM, EndD;
            public byte[] Raw = new byte[32];
        }
        public const uint RecordTable = 0x0202C038, RecordSize = 32;

        public List<FamilyRecord> FamilyRecords() { return ReadRecords(Game.Vm); }

        public static List<FamilyRecord> ReadRecords(OrigVm vm)
        {
            var m = vm.Mem; var r = new List<FamilyRecord>();
            for (uint k = 0; k < 3; k++)
            {
                uint a = RecordTable + RecordSize * k;
                if (m.R16(a + 4) == 0) break;
                var e = new FamilyRecord { Rank = (int)k + 1, Members = (int)m.R16(a + 4), Type = (int)m.R8(a + 0x1A) };
                for (uint i = 0; i < 32; i++) e.Raw[i] = (byte)m.R8(a + i);
                OrigDate.Get(m, a + 0x14, out e.StartY, out e.StartM, out e.StartD);
                OrigDate.Get(m, a + 0x17, out e.EndY, out e.EndM, out e.EndD);
                const uint tmp = 0x0F03B000;   // 작업 영역에 날짜를 옮겨 원작 햇수 함수로 센다
                for (uint i = 0; i < 4; i++) { m.W8(tmp + i, i < 3 ? m.R8(a + 0x14 + i) : 0); m.W8(tmp + 4 + i, i < 3 ? m.R8(a + 0x17 + i) : 0); }
                try { e.Years = (int)vm.Call("08095EE8", tmp, tmp + 4); } catch (OrigUnmodeled) { e.Years = e.EndY - e.StartY; }
                r.Add(e);
            }
            return r;
        }

        /// <summary>카트리지(세이브 영역)의 가문의 기록 — 제목 화면에서 보기.</summary>
        public static List<FamilyRecord> ReadRecords(OrigRules rules, byte[] cartridge)
        {
            if (cartridge == null) return new List<FamilyRecord>();
            var m = new OrigMem(); m.LoadSaveBlock(cartridge);
            return ReadRecords(rules.CreateVm(m));
        }

        /// <summary>기록에 남은 원작 가문 이름 칸(+6, 14바이트)을 글자표로 푼 것. 글자표가 없으면 빈 글.</summary>
        public static string RecordOrigName(OrigText text, FamilyRecord r)
        {
            var b = new List<byte>(); for (int i = 6; i < 0x14 && r.Raw[i] != 0; i++) b.Add(r.Raw[i]);
            if (text == null || !text.HasCharset) return "";
            var pages = text.Decode(b, null, null); return pages.Count > 0 ? pages[0] : "";
        }

        /// <summary>앱 이름표 열쇠 — 시작·끝 날과 가족 유형, 역대 인원.</summary>
        public static string RecordKey(FamilyRecord r) { return BitConverter.ToString(r.Raw, 0x14, 7).Replace("-", "") + "-" + r.Members; }

        /// <summary>지금 가족이 끝나며 남긴 기록 (시작 날 0x0202C688 · 끝난 날 = 오늘). 없으면(상위 3 밖) null.</summary>
        public FamilyRecord CurrentRecord()
        {
            var m = Game.Mem;
            foreach (var r in FamilyRecords())
            {
                bool same = true;
                for (uint i = 0; i < 3; i++) if (r.Raw[0x14 + i] != m.R8(0x0202C688 + i) || r.Raw[0x17 + i] != m.R8(OrigMem.Date + i)) same = false;
                if (same) return r;
            }
            return null;
        }

        /// <summary>
        /// 종합 진단 (원작 0x0806614C): [0] 후계자 0x08065874 · [1] 풍요로움 0x08065B9C · [2] 유대 0x0806605C · [3] 종합 0x080660D8(앞의 셋).
        /// 값 1~5 → 화면 글 0x0858A600[값−1] = 344~348 (최악… ~ 정말 좋아！). 원작 진단 패널(0x08061770)은 종합을 첫 줄에 놓는다.
        /// </summary>
        public int[] Diagnosis() { return Diagnosis(Game.Vm); }

        public static int[] Diagnosis(OrigVm vm)
        {
            const uint tmp = 0x0F03B100;
            vm.Mem.W32(tmp, 0);
            vm.Call("0806614C", tmp);
            return new[] { (int)vm.Mem.R8(tmp), (int)vm.Mem.R8(tmp + 1), (int)vm.Mem.R8(tmp + 2), (int)vm.Mem.R8(tmp + 3) };
        }

        public string DiagnosisWord(int v)
        {
            string[] fb = { "최악…", "별로…", "아주 평범", "꽤 좋아♪", "정말 좋아！" };
            return v >= 1 && v <= 5 ? Text.UiText(343 + v, fb[v - 1]) : "?";
        }

        /// <summary>가족 유형 이름 (원작 화면 글 349 + 유형). 글자표가 없으면 앱 표기.</summary>
        public string FamilyTypeName(int type)
        {
            string[] fb = { "반짝반짝 가족", "느긋한 가족", "냉랭한 가족", "북적북적 가족", "화목한 가족", "침울한 가족", "노력하는 가족", "버티는 가족", "밑바닥 가족" };
            return type >= 0 && type < 9 ? Text.UiText(349 + type, fb[type]) : "?";
        }

        /// <summary>세대 (원작 0x08025DFC — 족보의 부모를 거슬러 센다, 0 = 모름 → 원작 목록은 "?"). 실기 목록과 같음(시험).</summary>
        public int Generation(int personId)
        {
            try { return (int)Game.Vm.Call("08025DFC", (uint)personId); } catch (OrigUnmodeled) { return 0; }
        }

        /// <summary>
        /// 선대 마음의 결정: 목록 k 번째 마음을 target 에게 (원작 0x0801D8B0 의 100번대 경로 그대로).
        /// 메뉴+0x18E = 인물 번호, +0x190 = 스킬, +0x192 = 0xFFFF → 0x08024B80(100+k, 레코드, 스킬, 메뉴) 가 0 이면
        /// 목록에서 빼기 0x0800F9AC(1, k). 이미 가진 스킬이면 1(못 씀), 스킬 칸이 차 있으면 원작이 하나를 골라 바꾼다.
        /// 결정 보유 수(칸 11) −1 도 원작 코드 안에서 일어난다(실기에서도 1 → 0).
        /// </summary>
        public string UseLegacyHeart(Person target, int k)
        {
            var t = Interventions.Find("item.heart_crystal");
            if (target == null) return "대상이 없습니다";
            SyncIn();
            var m = Game.Mem; uint inv = SlotAddr(t);
            if (m.R8(inv) == 0) return t.Name + "이(가) 없습니다";
            var list = LegacyHearts(); if (k < 0 || k >= list.Count) return "선대의 마음이 없습니다";
            uint rec = 0;
            for (int n = 0; n < 8; n++) if (OrigGame.Present(m, n) && (int)m.R16(OrigMem.PersonAddr(n) + 0x3C) == target.Id) { rec = OrigMem.PersonAddr(n); break; }
            if (rec == 0) return "대상이 없습니다";
            var ew = (byte[])m.Ewram.Clone(); var iw = (byte[])m.Iwram.Clone();
            try
            {
                for (uint i = 0; i < 0x800; i += 4) m.W32(ToolObj + i, 0);
                m.W32(ToolObj + 4, ToolChar); m.W32(ToolObj + 0x154, 0); m.W32(ToolChar + 4, rec);
                m.W16(ToolObj + 0x18E, (uint)list[k][0]); m.W16(ToolObj + 0x190, (uint)list[k][1]); m.W16(ToolObj + 0x192, 0xFFFF);
                uint r = Game.Vm.Call("08024B80", 100 + (uint)k, rec, (uint)list[k][1], ToolObj);
                if (r != 0) return "이미 익힌 스킬이라 이어받을 수 없습니다";
                Game.Vm.Call("0800F9AC", 1, (uint)k);   // 목록에서 빼기 — 결정 보유 수(칸 11) −1 도 원작 코드가 한다
                Project();
                return null;
            }
            catch (OrigUnmodeled e)
            {
                Array.Copy(ew, m.Ewram, ew.Length); Array.Copy(iw, m.Iwram, iw.Length);
                return t.Name + ": 원작 함수 실행 실패 (" + e.Message + ")";
            }
        }

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
            if (toolId == "item.heart_crystal") return "이어받을 선대의 마음을 고르세요 (UseLegacyHeart)";
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
            cur = pending.Dequeue(); page = 0; stages = new List<List<OrigText.StageActor>>();
            OrigText.Rec rec;
            if (Text.Records.TryGetValue(cur.Data, out rec) && !string.IsNullOrEmpty(rec.Script))
                pages = OrigText.ScenePages(rec.Script, SlotName, stages, null);
            else if (Text.HasCharset)   // 참고 대사가 없으면 ROM 대사(결과 기록 +0x14)를 글자표로 직접 푼다
                pages = Text.Decode(OrigText.ReadRaw(Game.Mem, Game.Mem.R32(cur.Data + 0x14)), SlotName, MarkName);
            else pages = new List<string>();
            if (pages.Count == 0) pages.Add("(원작 사건 " + cur.Data.ToString("X8") + " — 대사 자료 없음)");
            // 결과 스크립트가 낸 원작 결과 문구(능력 변화·감사·랭크·보상 안내). 글자표가 없으면 앱이 쓴 요약.
            // 번역 패치가 옮기지 않은 원문은 대체 글(OrigText.Overrides)로 나오고, 대체 글도 없는 일본어 장만 빼고 앱 요약(받은 것 목록)을 붙인다.
            bool decoded = Text.HasCharset && cur.Shown.Length > 0, untranslated = false;
            if (decoded)
                foreach (var pg in Text.Decode(cur.Shown, SlotName, MarkName))
                    if (OrigText.Untranslated(pg)) { untranslated = true; if (System.Environment.GetEnvironmentVariable("SK_UNTR") != null) System.Console.WriteLine("    [UNTR] " + cur.Data.ToString("X8") + " " + pg.Replace("\n", " ")); }
                    else pages.Add(pg);
            // 글자표가 있는데 결과 문구가 없으면 원작도 결과를 글로 보여 주지 않은 것이므로 요약을 붙이지 않는다.
            if (!Text.HasCharset || untranslated) { var god = GodPage(cur, untranslated); if (god != null) pages.Add(god); }
        }

        /// <summary>플레이어 이름 — 원작은 카트리지(0x0202C660)에 두는 이름. 앱은 입력받은 이름을 OrigNames[-2] 에 두고, 아직 없으면 "신님"(앱 기본값).</summary>
        public string PlayerName
        {
            get { string s; return Family.OrigNames.TryGetValue(PlayerNameKey, out s) && !string.IsNullOrEmpty(s) ? s : "신님"; }
            set { Family.OrigNames[PlayerNameKey] = value; }
        }
        const int PlayerNameKey = -2;

        /// <summary>결과 문구의 이름 표지(OrigResultScript.NameMarks) → 앱 이름.</summary>
        string MarkName(int id)
        {
            if (id == (int)OrigResultScript.MarkFamily) return Family.Name;
            if (id == (int)OrigResultScript.MarkPlayer) return PlayerName;
            return NameOf(id, -1);
        }

        /// <summary>
        /// 결과 스크립트가 바꾼 신님 쪽 값(감사·랭크·보상)을 알리는 장 — 글자표(로컬 팩 charset)가 없을 때만 쓰는, 앱이 쓴 요약 문장이다.
        /// </summary>
        /// <param name="giftsOnly">원작 문구가 감사·랭크는 보여 줬고 보상 설명만 번역이 없을 때 — 받은 것 목록만.</param>
        string GodPage(OrigGame.DayEvent e, bool giftsOnly = false)
        {
            var sb = new System.Text.StringBuilder();
            if (!giftsOnly && e.Points1 > e.Points0) sb.Append(NameOf((int)e.PersonId, -1)).Append("이(가) 신님에게 감사! 감사의 마음 +").Append(e.Points1 - e.Points0).Append(" (모두 ").Append(e.Points1).Append("개)");
            if (!giftsOnly && e.Rank1 > e.Rank0) { if (sb.Length > 0) sb.Append('\n'); sb.Append("신님 랭크가 ").Append(e.Rank1).Append("성이 됐다!"); }
            var gifts = new List<string>();
            foreach (var t in Interventions.Tools)
            {
                if (t.OrigSlot < 0) continue;
                int k = (t.Kind == "arrow" ? 0 : 16) + t.OrigSlot, d = e.Inv1[k] - e.Inv0[k];
                if (d > 0) gifts.Add(t.Name + " ×" + d);
            }
            if (gifts.Count > 0) { if (sb.Length > 0) sb.Append('\n'); sb.Append("받은 것: ").Append(string.Join(", ", gifts)); }
            return sb.Length > 0 ? "[앱 요약] " + sb : null;   // 원작 글이 아님을 표시
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
                PersonId = (int)cur.PersonId,
                Scene = SceneOf(Math.Min(page, pages.Count - 1))
            };
        }

        /// <summary>
        /// 장면 그림: 결과 기록 +0x20/+0x24/+0x28 (그림 표 0x0888xxxx 안이 아니면 특수 기록 — 그림 없음) + 이 장의 인물 상태.
        /// 결과 문구 장(대사 뒤)은 마지막 대사 장의 상태를 이어 쓴다. 인물 칸 → 장면 시작 때 슬롯표(cur.Slots)의 족보 번호.
        /// </summary>
        SceneView SceneOf(int k)
        {
            var m = Game.Mem; uint band = m.R32(cur.Data + 0x20), back = m.R32(cur.Data + 0x24), pic = m.R32(cur.Data + 0x28);
            bool Ok(uint v) { return v >= 0x08880000 && v < 0x08890000; }
            if (!Ok(pic)) return null;
            var sv = new SceneView { Band = Ok(band) ? "ev_band_" + band.ToString("X8") : "", Back = Ok(back) ? "ev_back_" + back.ToString("X8") : "", Pic = "ev_pic_" + pic.ToString("X8") };
            if (stages != null && stages.Count > 0)
                foreach (var a in stages[Math.Min(k, stages.Count - 1)])
                {
                    int id = -1;
                    if (a.Slot >= 0 && a.Slot < 28 && cur.Slots[a.Slot] != 0xFFFF) id = (int)cur.Slots[a.Slot];
                    sv.Actors.Add(new SceneActor { PersonId = id, Anim = a.Anim });
                }
            return sv;
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
                p.Gratitude = (int)(m.R8(a + 0x65) | m.R8(a + 0x66) << 8);
                { uint ha = m.R8(a + 0x60); p.HitArrow = ha == 0xFF ? -1 : (int)ha; }   // 맞은 화살 (화살 0x08024440 이 종류 2~5 등을 +0x60 에, 날짜 +0x46 = 0)   // 신님에게 감사 (실기 상세 화면: +0x65=11, +0x66=12 → 3083개)
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
            // 신님의 정보 화면(실기 확인): 감사의 마음 0x0202C670, 신님 랭크 0x0202C66E
            f.Gratitude = (int)m.R32(0x0202C670); f.GodRank = (int)m.R16(0x0202C66E);
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
