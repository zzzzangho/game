namespace SennenKazoku.Core
{
    /// <summary>
    /// 집 화면 시간대 팔레트 (원작 0x08017AF8: 메인 장면 시각 +0x150 (하루 7200 = 24시간, 300 = 1시간) → 집 정의의 시간대 팔레트 칸, 실기 확인).
    /// 04:00 이하 4 밤 · 04:00~04:01 5 · ~08:00 6 새벽 · 08:00~08:01 7 · ~16:00 0 낮 · 16:00~16:01 1 · ~19:00 2 저녁 · 19:00~19:01 3 · 그 뒤 4 밤.
    /// 홀수 칸은 경계의 4단위(약 0.8 원작 분) 동안만 쓰는 사이 색이다.
    /// </summary>
    public static class HouseClock
    {
        public const int DayUnits = 7200, Morning = 1800;   // 06:00 = 하루 처리 시각

        public static int Slot(int t)
        {
            t = ((t % DayUnits) + DayUnits) % DayUnits;
            if (t > 5704) return 4;
            if (t > 5700) return 3;
            if (t > 4804) return 2;
            if (t > 4800) return 1;
            if (t > 2404) return 0;
            if (t > 2400) return 7;
            if (t > 1204) return 6;
            if (t > 1200) return 5;
            return 4;
        }

        /// <summary>앱의 하루 진행(0~1, 06:00 에 시작) → 원작 시각 단위.</summary>
        public static int FromDayFraction(float f) { return (int)(Morning + f * DayUnits) % DayUnits; }
    }
}
