using System;
using System.Collections.Generic;

namespace SennenKazoku.Core
{
    /// <summary>
    /// 원작 사건 장면의 인물 자리·말풍선 움직임·확대 (헤드리스 mGBA 실기 캡처로 잰 값, 원작 화면 240×160 좌표, 1프레임 = 1/60초).
    /// </summary>
    public static class SceneAnim
    {
        /// <summary>
        /// 인물 OBJ(32×64) 가운데 x. 액자 가운데 120 을 기준으로 인원수별 간격: 1명 0 · 2명 30 · 3명 28 · 4~8명 20
        /// (기록 0x08923648 의 대사에 인물 등장 토큰을 더 넣은 ROM 사본에서 OAM 을 읽음 — OBJ 왼쪽 x: 2명 89·119, 3명 76·104·132,
        /// 4명 74~134, 8명 34~174). 위 끝 y = 36 (발 = 100).
        /// </summary>
        public static float ActorCenterX(int i, int n)
        {
            float gap = n <= 1 ? 0 : n == 2 ? 30 : n == 3 ? 28 : 20;
            return 120f + gap * (i - (n - 1) / 2f);
        }
        public const float ActorTop = 36, ActorFoot = 100;

        /// <summary>
        /// 확대(장면 명령 1A 0F 칸 02, 실기 OAM 확대 회전 값): 역배율 p 가 244 부터 프레임마다 24 씩 줄어 128(2배)에서 멈춘다.
        /// 진행도 f = (256 − p)/128 만큼 인물 가운데가 (cx, 68) 에서 (120, 60) 으로 옮겨 간다. 그림은 액자(56,40 128×64) 안으로 잘린다.
        /// 1A 0F 칸 01 은 원래대로(p = 256).
        /// </summary>
        public static float ZoomInverse(float frames) { return Math.Max(128f, 244f - 24f * Math.Max(0f, frames)); }
        public static void ZoomPlace(float cx, float frames, out float scale, out float centerX, out float centerY)
        {
            float p = ZoomInverse(frames), f = (256f - p) / 128f;
            scale = 256f / p; centerX = cx + (120f - cx) * f; centerY = 68f + (60f - 68f) * f;
        }

        /// <summary>
        /// 감정 말풍선 움직임 표 (tools/gba_capture/event_art.py 가 실기에서 매 프레임 찍어 만든 ev_emo_anim.json — 원작 추출물, 로컬 전용).
        /// 감정마다 [그림 번호(−1 숨김), 프레임 수] 열을 되풀이한다. 예: 02(♪) 작게4·빈4·C6·D8·…·빈4·작게4·숨김30 = 122프레임.
        /// Crop = [인물 OBJ 왼쪽 기준 x, 화면 y, 폭, 높이].
        /// </summary>
        public sealed class EmotionTable
        {
            public int CropX, CropY = 33, CropW = 37, CropH = 39;
            public readonly Dictionary<int, List<int[]>> Runs = new Dictionary<int, List<int[]>>();

            public static EmotionTable Parse(string json)
            {
                var t = new EmotionTable();
                var root = J.Obj(MiniJson.Parse(json ?? ""));
                if (root == null) return t;
                foreach (var kv in root)
                {
                    var l = J.Arr(kv.Value) ?? new List<object>();
                    if (kv.Key == "crop") { if (l.Count >= 4) { t.CropX = Convert.ToInt32(l[0]); t.CropY = Convert.ToInt32(l[1]); t.CropW = Convert.ToInt32(l[2]); t.CropH = Convert.ToInt32(l[3]); } continue; }
                    int e; if (!int.TryParse(kv.Key, System.Globalization.NumberStyles.HexNumber, null, out e)) continue;
                    var runs = new List<int[]>();
                    foreach (var r in l) { var p = J.Arr(r); if (p != null && p.Count >= 2) runs.Add(new[] { Convert.ToInt32(p[0]), Convert.ToInt32(p[1]) }); }
                    t.Runs[e] = runs;
                }
                return t;
            }

            /// <summary>감정 e 를 띄운 뒤 frames 프레임째의 그림 번호 (−1 = 숨김, 표가 없으면 −2).</summary>
            public int Frame(int e, int frames)
            {
                List<int[]> runs;
                if (!Runs.TryGetValue(e, out runs) || runs.Count == 0) return -2;
                int total = 0; foreach (var r in runs) total += r[1];
                if (total <= 0) return -2;
                int t = ((frames % total) + total) % total;
                foreach (var r in runs) { if (t < r[1]) return r[0]; t -= r[1]; }
                return -1;
            }
        }
    }
}
