using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 직업: 이름(원작 "내가 아는 가족" 직업 화면에서 읽은 것 — 직업 번호는 같은 때 메모리의 후보 목록에서 확인)과
    /// 입력 화면의 직업 후보 목록(원작 0x081114CC — 직업 표 0x0889D63C 44칸을 나이·성별·후보 종류로 거른다).
    /// 이름을 모르는 번호(게임 중에 얻는 직업 등)는 "직업 #번호"로 보인다.
    /// </summary>
    public static class OrigJobs
    {
        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { 0, "없음" }, { 1, "없음" }, { 2, "공립 유치원생" }, { 3, "사립 유치원생" }, { 4, "공립 초등학생" }, { 5, "사립 초등학생" },
            { 6, "공립 중학생" }, { 7, "사립 중학생" }, { 8, "사립 엘리트 고등학생" }, { 9, "공립 상급 고등학생" }, { 10, "공립 일반 고등학생" },
            { 11, "전문학교생" }, { 12, "일류 대학생" }, { 13, "삼류 대학생" },
            { 15, "의사" }, { 16, "학자" }, { 17, "아나운서" }, { 18, "대기업 회사원" }, { 19, "저널리스트" }, { 20, "교사" },
            { 21, "요리사" }, { 22, "경찰관" }, { 23, "매장 직원" }, { 24, "중소기업 회사원" }, { 25, "프리터" }, { 26, "전업주부" }, { 27, "은거인" },
            { 29, "미용사" }, { 30, "정비사" },
        };

        public static string Name(int id) { string s; return Names.TryGetValue(id, out s) ? s : "직업 #" + id; }

        /// <summary>원작 관계 코드(가족 구성 확인 0x0802C9A0): 조부 0 · 조모 1 · 부 2 · 모 4 · 아들 6 · 딸 7.</summary>
        public static int Relation(FamilyRole r, int gender)
        {
            switch (r)
            {
                case FamilyRole.Grandfather: return 0;
                case FamilyRole.Grandmother: return 1;
                case FamilyRole.Father: return 2;
                case FamilyRole.Mother: return 4;
                default: return gender == 1 ? 7 : 6;
            }
        }

        /// <summary>
        /// 입력 화면의 직업 후보 (원작 0x08036EB6~ → 0x081114CC): 관계 코드 0·1·2·4 면 후보 종류 0x14, 자녀면 0x24.
        /// 나이는 원작 0x08095D3C(생일, 시작 날짜). 돌려주는 값 = 직업 번호 목록(원작 화면 순서).
        /// </summary>
        public static List<int> Candidates(OrigRules rules, int startDay, int birthDay, int gender, int relation)
        {
            var m = new OrigMem(); var vm = rules.CreateVm(m);
            const uint Buf = 0x0F000600, D0 = 0x0F000500, D1 = 0x0F000504;
            m.W16(D1, (uint)GameDate.Year(startDay)); m.W8(D1 + 2, (uint)GameDate.Month(startDay)); m.W8(D1 + 3, (uint)GameDate.Day(startDay));
            m.W16(D0, (uint)GameDate.Year(birthDay)); m.W8(D0 + 2, (uint)GameDate.Month(birthDay)); m.W8(D0 + 3, (uint)GameDate.Day(birthDay));
            uint age = vm.Call("08095D3C", D0, D1) & 0xFF;
            bool adult = relation == 0 || relation == 1 || relation == 2 || relation == 4;
            vm.Call("081114CC", Buf, age, (uint)gender, adult ? 0x14u : 0x24u);
            var r = new List<int>(); uint n = m.R8(Buf);
            for (uint k = 0; k < n && k < 44; k++) r.Add((int)m.R8(Buf + 4 + 4 * k));
            return r;
        }
    }
}
