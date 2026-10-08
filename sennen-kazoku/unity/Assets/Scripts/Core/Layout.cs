using System;

namespace SennenKazoku.Core
{
    public struct RectPx { public float X, Y, W, H; public float Bottom { get { return Y + H; } } }

    /// <summary>
    /// 세로 화면 레이아웃 계산 (Unity 비의존, 단위 테스트 가능). 좌표는 화면 왼쪽 위 기준 픽셀.
    /// 위→아래: 상단바(날짜·무드·자산) / 가족 띠 / 생활 장면(남는 공간) / 이벤트 패널 / 조작 바.
    /// 고정 높이는 dp, 이벤트 패널과 장면은 안전영역 높이에 비례한다.
    /// </summary>
    public static class LayoutCalculator
    {
        public const float MinTouchDp = 48f;

        public sealed class Result
        {
            public RectPx Safe, TopBar, FamilyStrip, Scene, EventPanel, Controls;
            public float Dp;                      // 1dp 의 픽셀 수
            public bool Valid;
        }

        public static Result Compute(float screenW, float screenH, float safeX, float safeY, float safeW, float safeH, float dpi)
        {
            var r = new Result();
            r.Dp = Math.Max(0.5f, dpi / 160f);
            r.Safe = new RectPx { X = safeX, Y = safeY, W = safeW, H = safeH };
            float dp = r.Dp;
            bool compact = safeH / dp < 640f;                   // 320x568dp 급 소형 화면
            float top = (compact ? 52 : 64) * dp, strip = (compact ? 72 : 88) * dp, controls = (compact ? 56 : 60) * dp;
            // 이벤트 패널: 긴 화면일수록 장면을 늘리고 패널은 32% 상한
            float panel = Clamp(safeH * 0.34f, 230 * dp, 340 * dp);
            float scene = safeH - top - strip - controls - panel;
            float minScene = (compact ? 140 : 180) * dp;
            if (scene < minScene)                       // 매우 짧은 화면: 패널을 줄여 장면 최소치 확보
            {
                float need = minScene - scene;
                panel = Math.Max((compact ? 170 : 190) * dp, panel - need);
                scene = safeH - top - strip - controls - panel;
            }
            float y = safeY;
            r.TopBar = new RectPx { X = safeX, Y = y, W = safeW, H = top }; y += top;
            r.FamilyStrip = new RectPx { X = safeX, Y = y, W = safeW, H = strip }; y += strip;
            r.Scene = new RectPx { X = safeX, Y = y, W = safeW, H = scene }; y += scene;
            r.EventPanel = new RectPx { X = safeX, Y = y, W = safeW, H = panel }; y += panel;
            r.Controls = new RectPx { X = safeX, Y = y, W = safeW, H = controls };
            r.Valid = scene >= minScene - 0.5f && safeW > 0;
            return r;
        }

        static float Clamp(float v, float a, float b) { return Math.Max(a, Math.Min(b, v)); }
    }
}
