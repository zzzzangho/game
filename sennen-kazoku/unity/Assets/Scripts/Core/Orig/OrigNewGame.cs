using System;
using System.Collections.Generic;
namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 새 가족 만들기 — "신이 추천하는 가족" 경로.
    /// 원작 순서: 장면 시작(0x0804134C) → 추천 가족 틀(0x0804BC2C) → 확인 장면 시작(0x0802C9A0) → 구성원 마무리(0x0802D7A0,
    /// 구성원마다 채우기 0x0804B1F0, 아이는 0x08049AF0) → 가족 레코드 만들기(0x080417E0).
    /// 추천 가족 틀·마무리·레코드 만들기는 변환 트리로 실행한다. 장면 시작·확인 장면은 화면 처리와 섞여 있어
    /// 데이터를 바꾸는 부분만 아래에 옮겨 적었다(주소 표시).
    /// 플레이어가 직접 정하는 가족, 이름 입력(0x0202C660 등 14바이트)은 아직 옮기지 않았다.
    /// </summary>
    public static class OrigNewGame
    {
        public const uint Setup = 0x02001AF0, Members = Setup + 0x44, MemberSize = 0x9F0;
        /// <summary>마무리 장면(0x0802D7A0)에 넘기는 장면 객체 자리. [0] = 설정 블록, +0x11 = 0. 프레임 영역 맨 아래(쓰이지 않는 곳).</summary>
        public const uint SceneObj = 0x0F000000;

        /// <summary>
        /// 저장 영역 초기화 0x0800D778(방식). 원작은 전원을 켤 때 0, 제목 화면에서 새로 시작할 때
        /// (0x0202C030 의 0x40 비트가 켜져 있으면 1, 아니면 2) 를 부른다. 각 저장 구획을 DMA 로 0 또는 0xFF 로 채운다.
        /// </summary>
        public static void ResetSave(OrigVm vm, uint mode) { vm.Call("0800D778", mode); }

        /// <summary>
        /// 처음 켠 카트리지 상태: 전원을 켜면 원작은 플래시 세이브를 0x0202C010 부터 읽는데(IWRAM 루틴, 0x0824E610),
        /// 세이브가 없으면 플래시는 전부 0xFF 다. 원작 실행에서 0xFF 로 읽힌 범위는 0x0203BA38 앞까지이고, 그 뒤(~0x0203C43F)는 0 이다.
        /// 그 다음 0x0800D778(0) 으로 구획들을 초기화한다.
        /// </summary>
        public static void BlankCartridge(OrigVm vm)
        {
            for (uint a = OrigMem.SaveStart; a < OrigMem.SaveEnd; a++) vm.Mem.W8(a, a < FlashEnd ? 0xFFu : 0u);
            ResetSave(vm, 0);
        }
        public const uint FlashEnd = 0x0203BA38;

        /// <summary>
        /// 이전에 쓰던 카트리지(정상 세이브): 전원을 켜면 원작은 세이브를 읽고 검사(0x0800E358)해서 1 이하면 초기화하지 않는다
        /// (0x080089A0 — 2 이상일 때만 0x0800D778(0)). 그래서 세이브 영역을 그대로 둔다. 이어서 제목 화면 새로 시작(방식 1·2)이
        /// 지우는 구획만 지우고 가문의 기록(0x0202C038) 등은 남는다(시험으로 확인).
        /// </summary>
        public static void Cartridge(OrigVm vm, byte[] saveBlock) { vm.Mem.LoadSaveBlock(saveBlock); }

        /// <summary>제목 화면의 새로 시작 방식 (0x0809B5E0).</summary>
        public static uint TitleResetMode(OrigMem m) { return (m.R32(0x0202C030) & 0x40) != 0 ? 1u : 2u; }

        /// <summary>
        /// 제목 화면에서 새로 시작: 제목 장면 시작(0x08078650)이 부르는 0x0802B194(0x0203BD30~32 = 0xFF)와
        /// 새로 시작 선택 때의 저장 영역 초기화(0x0809B5E0 → 0x0800D778).
        /// </summary>
        public static void TitleNewGame(OrigVm vm)
        {
            for (uint i = 0; i < 3; i++) vm.Mem.W8(0x0203BD30 + i, 0xFF);
            ResetSave(vm, TitleResetMode(vm.Mem));
        }

        /// <summary>추천 가족으로 새 게임을 만든다. 시작 날짜는 원작처럼 카트리지 시계 날짜가 없으면 2005-01-01.</summary>
        public static void Recommended(OrigVm vm, int year = 2005, int month = 1, int day = 1)
        {
            var m = vm.Mem;
            // 0x0202C688: 확인 화면에서 카트리지 시계로 적는 날짜(0x0800E3F4) — 시작 날짜와 같게 둔다
            OrigDate.Set(m, 0x0202C688, year, month, day);
            Init(m, year, month, day);
            vm.Call("0804BC2C", Setup, Members);
            Confirm(m);
            m.W32(SceneObj, Setup); m.W8(SceneObj + 0x11, 0);
            // +0xD340 = 1: 추천 가족 경로 (원작 실행에서 이 값으로 구성원 채우기 루프를 돈다).
            // 0 이면 생일을 시작 날짜로 두는 등 다른 경로로 간다 — 플레이어가 정하는 가족 쪽으로 보이며 아직 확인하지 않았다.
            m.W32(Setup + 0xD340, 1);
            vm.Call("0802D7A0", SceneObj);
            // +0xD34A: 장면 시작에서 1, 확인 화면에서 결정하면 0. 0 이 아니면 0x080417E0 은 가족을 만들지 않고 끝부분(0x080423F2)으로 간다.
            m.W8(Setup + 0xD34A, 0);
            vm.Call("080417E0", Setup);
        }

        /// <summary>"내가 아는 가족" 구성원 입력 (원작 입력 화면 순서: 이름 → 생일 → 혈액형 → 성격 → 능력 순위 → 직업 → 체격·캐릭터).</summary>
        public sealed class CustomMember
        {
            /// <summary>구성 칸: 0 할아버지 · 1 할머니 · 2 아버지 · 3 어머니 · 4~7 자녀 칸.</summary>
            public int Slot;
            public bool Daughter;                    // 자녀 칸만
            public int Year, Month = 1, Day = 1;
            public int Blood;                        // 0 A · 1 B · 2 O · 3 AB · 4 ? (원작 선택지 순서)
            public int Personality;                  // 0 내향적 · 1 보통 · 2 외향적 (원작 선택지 순서)
            public int[] RankStats = { 0, 1, 2, 3 }; // 1~4번째로 높은 능력: 0 지력 · 1 체력 · 2 매력 · 3 운
            public int JobChoice;                    // 원작 직업 후보 목록(0x081114CC)에서 고른 번호
            public int Body;                         // 체격: 0 마름 · 1 보통 · 2 큼 (아이는 원작이 고르지 않음)
            public byte[] Name;                      // 원작 이름 글자(14바이트, 없으면 빈 이름) — 앱은 한국식 표시 이름을 따로 쓴다
            public byte[] Look;                      // 레코드 앞 16바이트 외형(+0x30~+0x3F). 없으면 마무리 기본값 그대로
        }

        /// <summary>
        /// 원작 "내가 아는 가족" 경로 (실기 측정: 구성 화면 → 구성 확인(0x0802CA1C/0x0802CAF8 = Confirm) → 마무리 0x0802D7A0(+0xD340 = 0,
        /// 역할 번호·성별·기본값) → 구성원마다 입력 화면(능력 순위 +0x55~, 직업 후보 0x081114CC → +0x59, 확인 0x08037344, 체격 +0x48, 캐릭터 +0x30~, 완료 +0x4C)
        /// → 가족 레코드 만들기 0x080417E0). 입력 확인 0x08037344 는 원작 함수(변환 트리)를 그대로 부른다(임시 입력 구조체를 원작과 같은 배치로 만든다).
        /// </summary>
        public static void Custom(OrigVm vm, int year, int month, int day, IList<CustomMember> members, bool create = true)
        {
            var m = vm.Mem;
            OrigDate.Set(m, 0x0202C688, year, month, day);
            Init(m, year, month, day);
            uint mask = 0;
            foreach (var c in members)
            {
                mask |= 1u << c.Slot;
                if (c.Slot >= 4) m.W8(Setup + 0x1C + (uint)(c.Slot - 4), c.Daughter ? 1u : 0u);
            }
            m.W8(Setup + 0x28, mask);
            Confirm(m);
            m.W32(SceneObj, Setup); m.W8(SceneObj + 0x11, 0);
            m.W32(Setup + 0xD340, 0);
            vm.Call("0802D7A0", SceneObj);
            // 구성원 순서 = 구성 비트 순서
            var order = new List<CustomMember>(members); order.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            const uint Temp = 0x0F000400, Done = 0x0F000380, DateTmp = 0x0F000390;
            m.W32(Done, 0);
            for (int k = 0; k < order.Count; k++)
            {
                var c = order[k]; uint mem = Members + (uint)k * MemberSize;
                for (uint i = 0; i < 4; i++) m.W8(mem + 0x55 + i, (uint)c.RankStats[i]);
                // 나이 = 0x08095D3C(생일, 시작 날짜) — 입력 화면이 직업 후보를 만들 때 쓰는 값
                m.W16(DateTmp, (uint)c.Year); m.W8(DateTmp + 2, (uint)c.Month); m.W8(DateTmp + 3, (uint)c.Day);
                uint age = vm.Call("08095D3C", DateTmp, Setup) & 0xFF;
                uint gender = m.R8(mem + 0x20);
                // 직업 화면(0x08036EB6~): 관계 코드(+0x4D)가 0·1·2·4(조부모·부모)면 후보 종류 0x14, 아니면(자녀) 0x24
                uint rel = m.R8(mem + 0x4D);
                vm.Call("081114CC", mem + 0x5C, age, gender, rel == 0 || rel == 1 || rel == 2 || rel == 4 ? 0x14u : 0x24u);
                uint n = m.R8(mem + 0x5C); int pick = Math.Max(0, Math.Min(c.JobChoice, (int)n - 1));
                m.W8(mem + 0x59, (uint)pick);
                uint job = m.R8(mem + 0x60 + 4 * (uint)pick);
                // 입력 확인 0x08037344 의 임시 구조체 (원작 0x0200F254 와 같은 배치)
                for (uint i = 0; i < 0xC70; i += 4) m.W32(Temp + i, 0);
                m.W32(Temp, Setup); m.W32(Temp + 0x14, Done); m.W32(Temp + 0x18, mem);
                m.W16(Temp + 0x1C, (uint)c.Year); m.W16(Temp + 0x1E, (uint)c.Month); m.W16(Temp + 0x20, (uint)c.Day);
                m.W16(Temp + 0x24, age); m.W16(Temp + 0x26, age); m.W8(Temp + 0x28, gender);
                m.W8(Temp + 0x29, (uint)c.Blood); m.W8(Temp + 0x2A, (uint)c.Personality); m.W8(Temp + 0x2B, job);
                for (uint i = 0; i < 14; i++) m.W8(Temp + 0xC5C + i, c.Name != null && i < c.Name.Length ? c.Name[i] : 0u);
                m.W32(Setup + 0xD358, (uint)k);
                vm.Call("08037344", Temp);
                if (age > 6) m.W8(mem + 0x48, (uint)c.Body);
                if (c.Look != null) for (uint i = 0; i < 16 && i < c.Look.Length; i++) m.W8(mem + 0x30 + i, c.Look[i]);
                m.W8(mem + 0x4C, 1);
            }
            m.W8(Setup + 0xD34A, 0);
            if (create) vm.Call("080417E0", Setup);
        }

        /// <summary>장면 시작 0x0804134C 의 데이터 부분: 설정 블록을 0xFF 로 채우고(0x08049CC8, 0x08049C20) 시작 날짜를 넣는다.</summary>
        public static void Init(OrigMem m, int year, int month, int day)
        {
            m.W8(0x0202C6A0, 0);
            for (uint i = 0; i < 0x44; i++) m.W8(Setup + i, 0xFF);
            for (uint k = 0; k < 8; k++)
            {
                uint p = Members + k * MemberSize;
                for (uint i = 0; i < MemberSize; i++) m.W8(p + i, 0xFF);
                m.W8(p + 0xE, 0); m.W8(p + 0x4C, 0);
            }
            m.W16(Setup, (uint)year); m.W8(Setup + 2, (uint)month); m.W8(Setup + 3, (uint)day);
            m.W8(Setup + 0x28, 0);
        }

        /// <summary>
        /// 확인 장면 시작 0x0802C9A0 의 데이터 부분: +0x28 구성 비트(0x0804A468 = 켜진 비트 수 → +0x29 인원)를 차례로 구성원에 배정하고
        /// 관계 코드 +0x4D 를 정한다. 비트 0 → 0, 1 → 1, 2 → 2, 3 → 4, 4~7 → 머리말 +0x1C+(비트-4) 가 0 이면 6, 아니면 7.
        /// </summary>
        public static void Confirm(OrigMem m)
        {
            uint mask = m.R8(Setup + 0x28), n = 0;
            for (int i = 0; i < 8; i++) if (((mask >> i) & 1) != 0) n++;
            m.W8(Setup + 0x29, n);
            uint k = 0;
            for (int i = 0; i < 8; i++)
            {
                if (((mask >> i) & 1) == 0) continue;
                uint rel = i == 0 ? 0u : i == 1 ? 1u : i == 2 ? 2u : i == 3 ? 4u : (m.R8(Setup + 0x1C + (uint)(i - 4)) == 0 ? 6u : 7u);
                m.W8(Members + k * MemberSize + 0x4D, rel);
                k++;
            }
        }
    }
}
