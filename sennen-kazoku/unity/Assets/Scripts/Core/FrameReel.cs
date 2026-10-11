using System;
using System.Collections.Generic;

namespace SennenKazoku.Core
{
    /// <summary>
    /// 프레임 묶음(로컬 추출 에셋 "SKF1" — tools/gba_capture/opening_frames.py): u16 장 수 · u16 fps · 장마다 [u16 반복 수 · u32 PNG 길이 · PNG].
    /// 반복 수 = 그 장이 이어지는 틱 수(1틱 = 1/fps 초). 그림을 푸는 일은 화면 쪽(Texture2D.LoadImage)이 한다.
    /// </summary>
    public sealed class FrameReel
    {
        public int Fps { get; private set; }
        public int Count { get { return offsets.Count; } }
        /// <summary>전체 길이(틱).</summary>
        public int Ticks { get; private set; }
        public float Seconds { get { return Fps > 0 ? (float)Ticks / Fps : 0f; } }
        readonly byte[] data;
        readonly List<int> offsets = new List<int>(), lengths = new List<int>(), starts = new List<int>();

        FrameReel(byte[] d) { data = d; }

        /// <summary>형식이 맞지 않으면 null.</summary>
        public static FrameReel Parse(byte[] b)
        {
            if (b == null || b.Length < 8 || b[0] != 'S' || b[1] != 'K' || b[2] != 'F' || b[3] != '1') return null;
            var r = new FrameReel(b);
            int n = U16(b, 4); r.Fps = U16(b, 6);
            if (r.Fps <= 0) return null;
            int o = 8, t = 0;
            for (int i = 0; i < n; i++)
            {
                if (o + 6 > b.Length) return null;
                int rep = U16(b, o), len = (int)U32(b, o + 2); o += 6;
                if (rep <= 0 || len <= 0 || o + len > b.Length) return null;
                r.offsets.Add(o); r.lengths.Add(len); r.starts.Add(t);
                o += len; t += rep;
            }
            r.Ticks = t;
            return r.Count > 0 ? r : null;
        }

        /// <summary>재생 시작부터 seconds 초에 보일 장 번호. 끝을 지났으면 −1.</summary>
        public int FrameAt(float seconds)
        {
            int tick = (int)Math.Floor(seconds * Fps);
            if (tick < 0) tick = 0;
            if (tick >= Ticks) return -1;
            int lo = 0, hi = starts.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (starts[mid] <= tick) lo = mid; else hi = mid - 1; }
            return lo;
        }

        /// <summary>장 i 의 PNG 바이트 (새 배열).</summary>
        public byte[] Png(int i)
        {
            var r = new byte[lengths[i]];
            Buffer.BlockCopy(data, offsets[i], r, 0, r.Length);
            return r;
        }

        static int U16(byte[] b, int o) { return b[o] | (b[o + 1] << 8); }
        static uint U32(byte[] b, int o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }
    }
}
