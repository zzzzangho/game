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
        readonly List<KeyValuePair<uint, byte[]>> rom = new List<KeyValuePair<uint, byte[]>>();

        public void AddRom(uint start, byte[] data)
        {
            rom.Add(new KeyValuePair<uint, byte[]>(start, data));
            rom.Sort((a, b) => a.Key.CompareTo(b.Key));
        }
        public void ShareRom(OrigMem other) { rom.Clear(); rom.AddRange(other.rom); }
        public bool HasRom { get { return rom.Count > 0; } }

        static uint Mirror(uint a) { return EwramBase | (a & (EwramSize - 1)); }

        public uint Read(uint a, int size)
        {
            if (a >= 0x02000000 && a < 0x03000000)
            {
                uint o = Mirror(a) - EwramBase;
                if (o + (uint)size > EwramSize) throw new OrigUnmodeled("EWRAM 경계 " + a.ToString("X8"));
                uint v = 0;
                for (int i = size - 1; i >= 0; i--) v = (v << 8) | Ewram[o + i];
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
