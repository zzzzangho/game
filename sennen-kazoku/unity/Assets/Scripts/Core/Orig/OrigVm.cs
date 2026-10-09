using System;
using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 판정 함수를 옮긴 결정 트리(tools/romlift/lift.py 가 만든 것)를 실행한다.
    /// 트리 노드:  ["r", e] 반환 · ["i", cc, a, b, 참, 거짓] 분기 · ["l", 변수, 함수, [인자], 이후] 호출 ·
    ///             ["s", 크기, 주소, 값, 이후] 메모리 쓰기 · ["f", 이유] 옮기지 못한 경로 ·
    ///             ["j", cc, a, b, 참, 거짓, 합류 후] 다시 만나는 분기(가지 끝 ["e", 대입]) ·
    ///             ["o", 꼬리표, 초기 대입, 본문, 반복 후] 반복문(본문 끝 ["c", 꼬리표, 대입] 다시 · ["b", 꼬리표, 대입] 빠져나감)
    /// 식: 정수 · ["v", n] 변수 · ["a", k] 인자 · ["m", 크기, 주소] 메모리 · ["x", 비트, e] 부호 확장 · ["p", 오프셋] 지역 변수 주소 · [연산, a, b]
    /// 대입 목록은 동시에 계산한 뒤 넣는다.
    /// 함수 이름이 "0x0812xxxx" 면 다른 트리, rand/umod/rel 이면 이 클래스의 손 이식 함수.
    /// </summary>
    public sealed class OrigVm
    {
        public readonly OrigMem Mem;
        readonly Dictionary<string, Node> trees = new Dictionary<string, Node>();
        public int Calls;   // 실행한 호출 수 (무한 재귀 방지)

        public OrigVm(OrigMem mem) { Mem = mem; }

        public void AddTree(string addr, object json) { trees[Norm(addr)] = ParseNode(json); }
        public bool HasTree(string addr) { return trees.ContainsKey(Norm(addr)); }
        public int TreeCount { get { return trees.Count; } }
        static string Norm(string a) { return Convert.ToUInt32(a.StartsWith("0x") || a.StartsWith("0X") ? a.Substring(2) : a, 16).ToString("X8"); }

        int depth;
        public uint Call(string fn, params uint[] args)
        {
            if (depth == 0) Calls = 0;
            if (++Calls > 2_000_000 || depth > 200) throw new OrigUnmodeled("호출이 너무 많음");
            depth++;
            try { return CallInner(fn, args); }
            finally { depth--; }
        }

        uint CallInner(string fn, uint[] args)
        {
            switch (fn)
            {
                case "rand": return Mem.Rand();
                case "umod": return args[1] == 0 ? args[0] : args[0] % args[1];
                case "udiv": return args[1] == 0 ? 0 : args[0] / args[1];
                case "sdiv": return args[1] == 0 ? 0 : (uint)((int)args[0] / (int)args[1]);
                case "smod": return args[1] == 0 ? args[0] : (uint)((int)args[0] % (int)args[1]);
                case "rel": return OrigFamily.Rel(Mem, (int)args[0], (int)args[1]);
            }
            if (!trees.TryGetValue(Norm(fn), out var t)) throw new OrigUnmodeled("옮기지 않은 원작 함수 " + fn);
            return Run(t, args);
        }

        // ---------------- 실행 ----------------
        public const uint FrameTop = 0x03006000, FrameSize = 0x400;

        sealed class Cont { public char Kind; public int Tag; public Node A, B; }

        void Assign(Pair[] pairs, Dictionary<int, uint> env, uint[] args, uint frame, HashSet<int> undef)
        {
            var vals = new uint[pairs.Length]; var ok = new bool[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
            {
                try { vals[i] = Ev(pairs[i].E, env, args, frame, undef); ok[i] = true; }
                catch (UndefValue) { ok[i] = false; }   // 이후에 쓰지 않는 값
            }
            for (int i = 0; i < pairs.Length; i++)
            {
                if (ok[i]) { env[pairs[i].Var] = vals[i]; undef.Remove(pairs[i].Var); }
                else { env.Remove(pairs[i].Var); undef.Add(pairs[i].Var); }
            }
        }

        uint Run(Node t, uint[] args)
        {
            var env = new Dictionary<int, uint>(); var undef = new HashSet<int>();
            uint frame = FrameTop - FrameSize * (uint)depth;
            var conts = new List<Cont>();
            long steps = 0;
            while (true)
            {
                if (++steps > 20_000_000) throw new OrigUnmodeled("반복이 너무 많음");
                switch (t)
                {
                    case Ret r: return Ev(r.E, env, args, frame, undef);
                    case If f:
                        t = Cmp(f.Cc, Ev(f.A, env, args, frame, undef), Ev(f.B, env, args, frame, undef)) ? f.Then : f.Else; break;
                    case If2 f:
                        conts.Add(new Cont { Kind = 'k', A = f.After });
                        t = Cmp(f.Cc, Ev(f.A, env, args, frame, undef), Ev(f.B, env, args, frame, undef)) ? f.Then : f.Else; break;
                    case End e:
                        Assign(e.Pairs, env, args, frame, undef);
                        while (conts.Count > 0 && conts[conts.Count - 1].Kind != 'k') conts.RemoveAt(conts.Count - 1);
                        t = conts[conts.Count - 1].A; conts.RemoveAt(conts.Count - 1); break;
                    case Loop lp:
                        Assign(lp.Init, env, args, frame, undef);
                        conts.Add(new Cont { Kind = 'L', Tag = lp.Tag, A = lp.Body, B = lp.After });
                        t = lp.Body; break;
                    case Jump j:
                        Assign(j.Pairs, env, args, frame, undef);
                        while (!(conts[conts.Count - 1].Kind == 'L' && conts[conts.Count - 1].Tag == j.Tag)) conts.RemoveAt(conts.Count - 1);
                        if (j.Again) t = conts[conts.Count - 1].A;
                        else { t = conts[conts.Count - 1].B; conts.RemoveAt(conts.Count - 1); }
                        break;
                    case Let l:
                        var av = new uint[l.Args.Length];
                        for (int i = 0; i < av.Length; i++)
                        {
                            try { av[i] = Ev(l.Args[i], env, args, frame, undef); }
                            catch (UndefValue) { av[i] = 0; }   // 재귀 호출에서 쓰지 않는 인자
                        }
                        env[l.Var] = Call(l.Fn, av); undef.Remove(l.Var);
                        t = l.Body; break;
                    case Store s:
                        Mem.Write(Ev(s.Addr, env, args, frame, undef), s.Size, Ev(s.Val, env, args, frame, undef));
                        t = s.Body; break;
                    case Fail x: throw new OrigUnmodeled("옮기지 못한 원작 경로: " + x.Why);
                    default: throw new OrigUnmodeled("알 수 없는 노드");
                }
            }
        }

        sealed class UndefValue : Exception { public UndefValue(string m) : base(m) { } }

        uint Ev(Expr e, Dictionary<int, uint> env, uint[] args, uint frame, HashSet<int> undef)
        {
            switch (e)
            {
                case Const c: return c.V;
                case Var v: return env.TryGetValue(v.N, out var x) ? x : throw new UndefValue("변수 " + v.N);
                case Arg a: return a.K < args.Length ? args[a.K] : 0;
                case Sp p: return unchecked(frame + (uint)p.Off);
                case MemRead m: return Mem.Read(Ev(m.Addr, env, args, frame, undef), m.Size);
                case Sext s:
                    {
                        uint v = Ev(s.E, env, args, frame, undef) & (uint)((1L << s.Bits) - 1);
                        if ((v & (1u << (s.Bits - 1))) != 0) v |= ~(uint)((1L << s.Bits) - 1);
                        return v;
                    }
                case Bin b: return Op(b.Op, Ev(b.A, env, args, frame, undef), Ev(b.B, env, args, frame, undef));
                case Undef u: throw new UndefValue(u.Why);
            }
            throw new OrigUnmodeled("알 수 없는 식");
        }

        static uint Op(string op, uint a, uint b)
        {
            switch (op)
            {
                case "+": return unchecked(a + b);
                case "-": return unchecked(a - b);
                case "*": return unchecked(a * b);
                case "&": return a & b;
                case "|": return a | b;
                case "^": return a ^ b;
                case "<<": return b >= 32 ? 0 : a << (int)b;
                case ">>": return b >= 32 ? 0 : a >> (int)b;
                case ">>>": return (uint)((int)a >> (int)Math.Min(b, 31));
                case "/": return b == 0 ? 0 : a / b;
                case "%": return b == 0 ? a : a % b;
                case "/s": return b == 0 ? 0 : (uint)((int)a / (int)b);
                case "%s": return b == 0 ? a : (uint)((int)a % (int)b);
            }
            throw new OrigUnmodeled("알 수 없는 연산 " + op);
        }

        public static bool Cmp(string cc, uint a, uint b)
        {
            int sa = (int)a, sb = (int)b;
            switch (cc)
            {
                case "eq": return a == b;
                case "ne": return a != b;
                case "geu": return a >= b;
                case "ltu": return a < b;
                case "gtu": return a > b;
                case "leu": return a <= b;
                case "ge": return sa >= sb;
                case "lt": return sa < sb;
                case "gt": return sa > sb;
                case "le": return sa <= sb;
                case "mi": return unchecked((int)(a - b)) < 0;
                case "pl": return unchecked((int)(a - b)) >= 0;
            }
            throw new OrigUnmodeled("알 수 없는 비교 " + cc);
        }

        // ---------------- 트리 구조 ----------------
        abstract class Node { }
        sealed class Ret : Node { public Expr E; }
        sealed class If : Node { public string Cc; public Expr A, B; public Node Then, Else; }
        sealed class Let : Node { public int Var; public string Fn; public Expr[] Args; public Node Body; }
        sealed class Store : Node { public int Size; public Expr Addr, Val; public Node Body; }
        sealed class Fail : Node { public string Why; }
        sealed class If2 : Node { public string Cc; public Expr A, B; public Node Then, Else, After; }
        sealed class End : Node { public Pair[] Pairs; }
        sealed class Loop : Node { public int Tag; public Pair[] Init; public Node Body, After; }
        sealed class Jump : Node { public int Tag; public bool Again; public Pair[] Pairs; }
        sealed class Pair { public int Var; public Expr E; }
        abstract class Expr { }
        sealed class Const : Expr { public uint V; }
        sealed class Var : Expr { public int N; }
        sealed class Arg : Expr { public int K; }
        sealed class MemRead : Expr { public int Size; public Expr Addr; }
        sealed class Sext : Expr { public int Bits; public Expr E; }
        sealed class Bin : Expr { public string Op; public Expr A, B; }
        sealed class Undef : Expr { public string Why; }
        sealed class Sp : Expr { public int Off; }

        static Pair[] Pairs(object o)
        {
            var l = (List<object>)o; var r = new Pair[l.Count];
            for (int i = 0; i < r.Length; i++) { var p = (List<object>)l[i]; r[i] = new Pair { Var = I(p[0]), E = ParseExpr(p[1]) }; }
            return r;
        }

        static int I(object o) { return Convert.ToInt32(o); }

        static Node ParseNode(object o)
        {
            var l = (List<object>)o;
            switch ((string)l[0])
            {
                case "r": return new Ret { E = ParseExpr(l[1]) };
                case "i": return new If { Cc = (string)l[1], A = ParseExpr(l[2]), B = ParseExpr(l[3]), Then = ParseNode(l[4]), Else = ParseNode(l[5]) };
                case "l":
                    {
                        var al = (List<object>)l[3]; var args = new Expr[al.Count];
                        for (int i = 0; i < args.Length; i++) args[i] = ParseExpr(al[i]);
                        return new Let { Var = I(l[1]), Fn = (string)l[2], Args = args, Body = ParseNode(l[4]) };
                    }
                case "s": return new Store { Size = I(l[1]), Addr = ParseExpr(l[2]), Val = ParseExpr(l[3]), Body = ParseNode(l[4]) };
                case "f": return new Fail { Why = (string)l[1] };
                case "j": return new If2 { Cc = (string)l[1], A = ParseExpr(l[2]), B = ParseExpr(l[3]), Then = ParseNode(l[4]), Else = ParseNode(l[5]), After = ParseNode(l[6]) };
                case "e": return new End { Pairs = Pairs(l[1]) };
                case "o": return new Loop { Tag = I(l[1]), Init = Pairs(l[2]), Body = ParseNode(l[3]), After = ParseNode(l[4]) };
                case "c": return new Jump { Tag = I(l[1]), Again = true, Pairs = Pairs(l[2]) };
                case "b": return new Jump { Tag = I(l[1]), Again = false, Pairs = Pairs(l[2]) };
            }
            throw new FormatException("트리 노드 " + l[0]);
        }

        static Expr ParseExpr(object o)
        {
            if (o is long n) return new Const { V = unchecked((uint)n) };
            var l = (List<object>)o;
            var k = (string)l[0];
            switch (k)
            {
                case "v": return new Var { N = I(l[1]) };
                case "a": return new Arg { K = I(l[1]) };
                case "m": return new MemRead { Size = I(l[1]), Addr = ParseExpr(l[2]) };
                case "x": return new Sext { Bits = I(l[1]), E = ParseExpr(l[2]) };
                case "u": return new Undef { Why = (string)l[1] };
                case "p": return new Sp { Off = I(l[1]) };
            }
            return new Bin { Op = k, A = ParseExpr(l[1]), B = ParseExpr(l[2]) };
        }
    }
}
