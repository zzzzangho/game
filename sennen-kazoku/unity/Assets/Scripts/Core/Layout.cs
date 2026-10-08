using System;

namespace SennenKazoku.Core
{
    public struct RectPx { public float X, Y, W, H; public float Bottom { get { return Y + H; } } }

    /// <summary>
    /// 세로 화면 레이아웃 (Unity 비의존, 테스트 가능). 좌표는 화면 왼쪽 위 기준 픽셀.
    /// 위→아래: HUD(N년가족·날짜·시계) / 집 단면도(원작 그래픽, 가로 스크롤) / 선택 인물 바 / 대화창 / 하단 메뉴(활쏘기·아이템·큐피트·관찰).
    /// 원작 GBA 화면폭 240px 을 기준 배율로 삼아, 집 그림이 남는 높이를 최대한 쓰도록 배율을 키운다.
    /// </summary>
    public static class LayoutCalculator
    {
        public const float MinTouchDp = 48f;

        public sealed class Result
        {
            public RectPx Safe, TopBar, Scene, FamilyStrip, EventPanel, Controls;
            public float Dp;           // 1dp 의 픽셀 수
            public float HouseScale;   // GBA 1px → 화면 px
            public bool Valid;
        }

        public static Result Compute(float screenW, float screenH, float safeX, float safeY, float safeW, float safeH, float dpi)
        {
            var r = new Result { Dp = Math.Max(0.5f, dpi / 160f) };
            r.Safe = new RectPx { X = safeX, Y = safeY, W = safeW, H = safeH };
            float dp = r.Dp, s0 = safeW / 240f;
            bool compact = safeH / dp < 640f;
            float top = Math.Max((compact ? 40 : 48) * dp, 16 * s0);
            float bar = Math.Max((compact ? 56 : 64) * dp, 30 * s0);
            float menu = (compact ? 60 : 72) * dp;
            float dialogMin = (compact ? 120 : 150) * dp;
            float avail = safeH - top - bar - menu - dialogMin;
            // 집: 원작 세로 160px 를 avail 에 맞춘다(가로는 스크롤). 배율은 폭 기준의 0.75~1.3배 사이(원작 화면폭 240px 중 185px 이상 보이게).
            float scale = Math.Max(s0 * 0.75f, Math.Min(avail / 160f, s0 * 1.3f));
            float house = Math.Min(avail, 160f * scale);
            float dialog = safeH - top - bar - menu - house;
            float y = safeY;
            r.TopBar = new RectPx { X = safeX, Y = y, W = safeW, H = top }; y += top;
            r.Scene = new RectPx { X = safeX, Y = y, W = safeW, H = house }; y += house;
            r.FamilyStrip = new RectPx { X = safeX, Y = y, W = safeW, H = bar }; y += bar;
            r.EventPanel = new RectPx { X = safeX, Y = y, W = safeW, H = dialog }; y += dialog;
            r.Controls = new RectPx { X = safeX, Y = y, W = safeW, H = menu };
            r.HouseScale = house / 160f;
            r.Valid = house >= 100f * s0 * 0.75f && dialog >= dialogMin - 0.5f && safeW > 0;
            return r;
        }
    }
}
