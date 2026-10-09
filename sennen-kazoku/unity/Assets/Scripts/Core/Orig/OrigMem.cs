using System;
using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>원작 규칙을 실행할 때 쓰는 메모리 상태가 원작에 없는 곳을 읽으려 할 때.</summary>
    public sealed class OrigUnmodeled : Exception { public OrigUnmodeled(string m) : base(m) { } }

    /// <summary>
    /// 원작 데이터 배치 그대로의 상태. EWRAM(0x02000000, 256KB)과 원작 규칙이 읽는 ROM 표 조각을 담는다.
    /// - 원작 세이브 영역 = EWRAM 0x0202C010~ (가족 정보·인물 레코드 976바이트×8·족보 60바이트×N·관계 슬롯표).
    /// - 난수 seed 는 EWRAM 0x02000000 (ROM 0x08000614).
    /// - EWRAM 은 실기처럼 0x02000000~0x02FFFFFF 에 256KB 단위로 반복된다(원작 코드가 0xFFFF 번 족보를 읽을 때 생김).
    /// ROM 조각은 사용자 ROM 에서 로컬로 뽑은 팩에만 있다(저장소에 없음).
    /// </summary>
    public sealed class OrigMem
    {
        public const uint EwramBase = 0x02000000, EwramSize = 0x40000;
        public const uint Fam = 0x0202C010, Person = 0x0202C6C4, PersonSize = 976, Gene = 0x0202EB9C, GeneSize = 60;
        public const uint Slots = 0x0203C3E0, Date = 0x0202C684, Seed = 0x02000000;
        public const uint SaveStart = 0x0202C010, SaveEnd = 0x0203C440;   // 원작 플래시 세이브와 같은 범위

        public readonly byte[] Ewram = new byte[EwramSize];
        /// <summary>IWRAM 32KB: 원작 함수의 지역 변수 프레임(옮긴 트리가 지역 변수 주소를 넘길 때)에만 쓴다. 저장하지 않는다.</summary>
        public readonly byte[] Iwram = new byte[0x8000];
        /// <summary>변환한 함수의 지역 변수 프레임 (0x0F000000~, 1MB, GBA 에 없는 영역). 저장하지 않는다.</summary>
        public readonly byte[] Frames = new byte[0x100000];
        readonly List<KeyValuePair<uint, byte[]>> rom = new List<KeyValuePair<uint, byte[]>>();

        public void AddRom(uint start, byte[] data)
        {
            rom.Add(new KeyValuePair<uint, byte[]>(start, data));
            rom.Sort((a, b) => a.Key.CompareTo(b.Key));
        }
        public void ShareRom(OrigMem other) { rom.Clear(); rom.AddRange(other.rom); }
        public bool HasRom { get { return rom.Count > 0; } }

        static uint Mirror(uint a) { return EwramBase | (a & (EwramSize - 1)); }

        /// <summary>입출력 레지스터 0x04000000~0x040003FF. 원작 코드가 DMA 로 메모리를 복사·채우므로(예: 0x08049C20) 즉시 시작 DMA 만 흉내 낸다.
        /// 그 밖의 레지스터(화면·소리·인터럽트)는 값만 저장한다.</summary>
        public readonly byte[] Io = new byte[0x400];

        void IoWrite(uint a, int size, uint v)
        {
            uint o = a - 0x04000000;
            if (o + (uint)size > (uint)Io.Length) throw new OrigUnmodeled("입출력 영역 밖 쓰기 " + a.ToString("X8"));
            for (int i = 0; i < size; i++) Io[o + i] = (byte)(v >> (8 * i));
            for (uint ch = 0; ch < 4; ch++)
            {
                uint cnt = 0xB8 + 12 * ch;   // DMAxCNT (아래 16비트 개수, 위 16비트 제어)
                if (o + (uint)size <= cnt || o >= cnt + 4) continue;
                uint ctl = (uint)(Io[cnt + 2] | (Io[cnt + 3] << 8));
                if ((ctl & 0x8000) == 0) continue;
                if (((ctl >> 12) & 3) == 0) Dma(ch, ctl);   // 즉시 시작만 (V/H 블랭크 DMA 는 화면용)
                Io[cnt + 3] &= 0x7F;   // 끝나면 사용 비트가 꺼진다
            }
        }

        void Dma(uint ch, uint ctl)
        {
            uint b = 0xB0 + 12 * ch;
            uint src = (uint)(Io[b] | Io[b + 1] << 8 | Io[b + 2] << 16 | Io[b + 3] << 24);
            uint dst = (uint)(Io[b + 4] | Io[b + 5] << 8 | Io[b + 6] << 16 | Io[b + 7] << 24);
            uint n = (uint)(Io[b + 8] | Io[b + 9] << 8);
            if (n == 0) n = ch == 3 ? 0x10000u : 0x4000u;
            int w = (ctl & 0x400) != 0 ? 4 : 2;
            src &= ~(uint)(w - 1); dst &= ~(uint)(w - 1);
            int ds = DmaStep((ctl >> 5) & 3, w), ss = DmaStep((ctl >> 7) & 3, w);
            for (uint i = 0; i < n; i++)
            {
                Write(dst, w, Read(src, w));
                dst = unchecked(dst + (uint)ds); src = unchecked(src + (uint)ss);
            }
        }

        static int DmaStep(uint mode, int w) { return mode == 1 ? -w : mode == 2 ? 0 : w; }   // 0 증가 · 1 감소 · 2 고정 · 3 증가(다시 불러오기)

        public uint Read(uint a, int size)
        {
            if (a >= 0x04000000 && a < 0x04000400)
            {
                uint o = a - 0x04000000, v = 0;
                if (o + (uint)size > (uint)Io.Length) throw new OrigUnmodeled("입출력 영역 밖 읽기 " + a.ToString("X8"));
                for (int i = size - 1; i >= 0; i--) v = (v << 8) | Io[o + i];
                return v;
            }
            if (a >= 0x02000000 && a < 0x03000000)
            {
                uint o = Mirror(a) - EwramBase;
                if (o + (uint)size > EwramSize) throw new OrigUnmodeled("EWRAM 경계 " + a.ToString("X8"));
                uint v = 0;
                for (int i = size - 1; i >= 0; i--) v = (v << 8) | Ewram[o + i];
                return v;
            }
            if (a >= 0x0F000000 && a < 0x0F100000)
            {
                uint fo = a - 0x0F000000, fv = 0;
                if (fo + (uint)size > 0x100000) throw new OrigUnmodeled("프레임 경계 " + a.ToString("X8"));
                for (int i = size - 1; i >= 0; i--) fv = (fv << 8) | Frames[fo + i];
                return fv;
            }
            if (a >= 0x03000000 && a < 0x04000000)
            {
                uint o = a & 0x7FFF, v = 0;
                if (o + (uint)size > 0x8000) throw new OrigUnmodeled("IWRAM 경계 " + a.ToString("X8"));
                for (int i = size - 1; i >= 0; i--) v = (v << 8) | Iwram[o + i];
                return v;
            }
            if (a >= 0x08000000 && a < 0x0A000000)
            {
                int lo = 0, hi = rom.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2; var seg = rom[mid];
                    if (a < seg.Key) hi = mid - 1;
                    else if (a + (uint)size > seg.Key + (uint)seg.Value.Length) lo = mid + 1;
                    else
                    {
                        uint o = a - seg.Key, v = 0;
                        for (int i = size - 1; i >= 0; i--) v = (v << 8) | seg.Value[o + i];
                        return v;
                    }
                }
                throw new OrigUnmodeled("팩에 없는 ROM 표 " + a.ToString("X8"));
            }
            throw new OrigUnmodeled("원작 메모리 밖 읽기 " + a.ToString("X8"));
        }

        public void Write(uint a, int size, uint v)
        {
            if (a >= 0x0F000000 && a < 0x0F100000)
            {
                uint fo = a - 0x0F000000;
                if (fo + (uint)size > 0x100000) throw new OrigUnmodeled("프레임 경계 " + a.ToString("X8"));
                for (int i = 0; i < size; i++) { Frames[fo + i] = (byte)v; v >>= 8; }
                return;
            }
            if (a >= 0x03000000 && a < 0x04000000)
            {
                uint io = a & 0x7FFF;
                if (io + (uint)size > 0x8000) throw new OrigUnmodeled("IWRAM 경계 " + a.ToString("X8"));
                for (int i = 0; i < size; i++) { Iwram[io + i] = (byte)v; v >>= 8; }
                return;
            }
            if (a >= 0x04000000 && a < 0x04000400) { IoWrite(a, size, v); return; }
            if (a < 0x02000000 || a >= 0x03000000) throw new OrigUnmodeled("원작 메모리 밖 쓰기 " + a.ToString("X8"));
            uint o = Mirror(a) - EwramBase;
            for (int i = 0; i < size; i++) { Ewram[o + i] = (byte)v; v >>= 8; }
        }

        public uint R8(uint a) { return Read(a, 1); }
        public uint R16(uint a) { return Read(a, 2); }
        public uint R32(uint a) { return Read(a, 4); }
        public void W8(uint a, uint v) { Write(a, 1, v); }
        public void W16(uint a, uint v) { Write(a, 2, v); }
        public void W32(uint a, uint v) { Write(a, 4, v); }

        /// <summary>원작 난수 (ROM 0x08000614): seed = seed × 0x6D + 0x3FD.</summary>
        public uint Rand() { uint s = unchecked(R32(Seed) * 0x6Du + 0x3FDu); W32(Seed, s); return s; }

        public static uint PersonAddr(int n) { return Person + PersonSize * (uint)n; }
        public static uint GeneAddr(uint id) { return unchecked(Gene + GeneSize * id); }

        /// <summary>원작 세이브 범위(0x0202C010~0x0203C440)를 그대로 읽고 쓴다 (원작 플래시 세이브 앞부분과 같은 배치).</summary>
        public byte[] SaveBlock()
        {
            var b = new byte[SaveEnd - SaveStart];
            Array.Copy(Ewram, SaveStart - EwramBase, b, 0, b.Length);
            return b;
        }
        public void LoadSaveBlock(byte[] b)
        {
            Array.Copy(b, 0, Ewram, SaveStart - EwramBase, Math.Min(b.Length, (int)(SaveEnd - SaveStart)));
        }
    }
}
