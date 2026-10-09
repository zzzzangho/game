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

        /// <summary>추천 가족으로 새 게임을 만든다. 시작 날짜는 원작처럼 카트리지 시계 날짜가 없으면 2005-01-01.</summary>
        public static void Recommended(OrigVm vm, int year = 2005, int month = 1, int day = 1)
        {
            var m = vm.Mem;
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
