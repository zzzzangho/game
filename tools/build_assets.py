#!/usr/bin/env python3
"""Compile story/story.txt + the Galmuri BDF font + generated art into C source for the GBA engine.

Output: <out>/gen_data.c and <out>/gen_data.h
See README.md for the script syntax.
"""
import argparse
import os
import re
import shlex
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import art  # noqa: E402

# ---- layout constants (must match src/main.c) ----
TEXT_W = 224          # dialogue box text width in pixels
TEXT_LINES = 3        # lines per dialogue page
MENU_W = 196          # max option width
DESC_LINES = 4        # court record description lines
HEADER_W = 224        # court record header (present question)
LIST_W = 150          # court record list width

OPS = dict(SAY=1, SCENE=2, GET=3, FX=4, CHAPTER=5, INVEST=6, RETURN=7, ASK=8, PRESENT=9,
           ACCUSE=10, GOTO=11, ENDING=12, LIVES=13, GAMEOVER=14, MEET=15, WAIT=16, SHOUT=17,
           PROFILE=18, MENU=19, SET=20, IF=21, PENALTY=22, BANNER=23, VIDEO=24, TIMER=25, MASH=26,
           TESTIMONY=27)
BLOCKS = ("investigate", "ask", "accuse", "menu", "video", "testimony")
MAX_FLAGS = 1024
SPEAKER_RE = re.compile(r"^(.+?)(?:\[([^\]]+)\])?$")  # 이름 or 이름[표정]
FX = dict(flash=0, shock=1, shake=2, red=3)
NL = 0xFFFE
END = 0xFFFF
NONE = 0xFFFF

UI_STRINGS = [
    ("UI_TITLE_SERIES", "소년탐정 김전일"),
    ("UI_TITLE_MAIN", "마술열차 살인사건"),
    ("UI_TITLE_FAN", "비공식 팬 게임"),
    ("UI_NEW", "처음부터"),
    ("UI_CONTINUE", "이어하기"),
    ("UI_DISCLAIMER", "이 게임은 「소년탐정 김전일」의 팬이 만든\n비공식 2차 창작 게임입니다.\n원작의 모든 권리는 원작자와 출판사에 있습니다.\n\n폰트: 갈무리11 Condensed (SIL OFL 1.1)"),
    ("UI_TAB_EVIDENCE", "증거물"),
    ("UI_TAB_PROFILE", "인물"),
    ("UI_RECORD_HINT", "L/R: 전환  B: 닫기"),
    ("UI_PRESENT_HINT", "A: 제시하기"),
    ("UI_EMPTY", "아직 아무것도 없다."),
    ("UI_GOT", "증거물 입수!"),
    ("UI_MEET", "인물 파일 추가!"),
    ("UI_PROFILE_UPDATED", "인물 파일 갱신!"),
    ("UI_INVEST_Q", "어디를 조사할까?"),
    ("UI_INVEST_DONE", "조사를 마친다"),
    ("UI_BACK", "돌아간다"),
    ("UI_BANNER_INVEST", "조사 개시!"),
    ("UI_BANNER_DEDUCE", "추리 개시!"),
    ("UI_VIDEO_HINT", "←→ 넘기기  A 여기다!  B 그만"),
    ("UI_TESTI_HINT", "◀▶ 넘기기  A 추궁  R 증거 제시"),
    ("UI_PRESS_SHOUT", "잠깐!"),
    ("UI_OBJECTION", "그건 모순이야!"),
    ("UI_TIMEOUT", "시간이 없어…! 머뭇거리는 사이에 기회를 놓쳤다."),
    ("UI_DANGER", "더 이상 실수할 수 없다…!"),
    ("UI_MASH_HINT", "A 버튼을 연타해!"),
    ("UI_INVEST_NOTYET", "아직 알아내지 못한 게 있어.\n좀 더 조사해 보자."),
    ("UI_WRONG", "틀렸다...!"),
    ("UI_BAD_END", "BAD END"),
    ("UI_TRUE_END", "TRUE END"),
    ("UI_PRESS_A", "A 버튼을 누르세요"),
    ("UI_THANKS", "플레이해 주셔서 감사합니다!"),
    ("UI_SAVED", "저장했습니다"),
    ("UI_RECORD_KEY", "START: 수첩"),
    ("UI_TO_TITLE", "타이틀로 돌아갑니다"),
]


class CompileError(Exception):
    pass


# ---------------------------------------------------------------- font

class Font:
    def __init__(self, path):
        self.glyphs = {}
        self.ascent = 14
        with open(path, encoding="utf-8") as f:
            lines = f.read().split("\n")
        i = 0
        while i < len(lines):
            ln = lines[i]
            if ln.startswith("FONT_ASCENT"):
                self.ascent = int(ln.split()[1])
            if ln.startswith("STARTCHAR"):
                enc = dw = None
                bbx = None
                rows = []
                i += 1
                while not lines[i].startswith("ENDCHAR"):
                    p = lines[i].split()
                    if p[0] == "ENCODING":
                        enc = int(p[1])
                    elif p[0] == "DWIDTH":
                        dw = int(p[1])
                    elif p[0] == "BBX":
                        bbx = tuple(int(x) for x in p[1:5])
                    elif p[0] == "BITMAP":
                        i += 1
                        while not lines[i].startswith("ENDCHAR"):
                            rows.append(lines[i].strip())
                            i += 1
                        break
                    i += 1
                if enc is not None and enc >= 0:
                    self.glyphs[enc] = (dw, bbx, rows)
            i += 1

    def has(self, ch):
        return ord(ch) in self.glyphs

    def adv(self, ch):
        if ord(ch) not in self.glyphs:
            raise CompileError(f"font has no glyph for {ch!r} (U+{ord(ch):04X})")
        return self.glyphs[ord(ch)][0]

    def pixels(self, ch):
        """Yield (x, row) pixels relative to the line top (row 0 = ascent line top)."""
        dw, (w, h, xo, yo), rows = self.glyphs[ord(ch)]
        top = self.ascent - (yo + h)
        for r, hexrow in enumerate(rows):
            if not hexrow:
                continue
            bits = int(hexrow, 16)
            nbits = len(hexrow) * 4
            for c in range(w):
                if bits & (1 << (nbits - 1 - c)):
                    yield xo + c, top + r

    def width(self, s):
        return sum(self.adv(c) for c in s)


# ---------------------------------------------------------------- text wrapping

def wrap(font, text, width):
    """Word-wrap on spaces (Korean eojeol), falling back to per-character breaks. '\\n' forces a break."""
    out = []
    for para in text.split("\n"):
        line = ""
        for word in para.split(" "):
            cand = word if not line else line + " " + word
            if font.width(cand) <= width:
                line = cand
                continue
            if line:
                out.append(line)
                line = ""
            while font.width(word) > width:
                cut = len(word)
                while font.width(word[:cut]) > width:
                    cut -= 1
                out.append(word[:cut])
                word = word[cut:]
            line = word
        out.append(line)
    return out


# ---------------------------------------------------------------- compiler

class Compiler:
    def __init__(self, font):
        self.font = font
        self.code = []
        self.labels = {}
        self.fixups = []          # (code index, label, line no)
        self.texts = []           # list of strings (already containing \n)
        self.text_index = {}
        self.chars = {}           # name -> dict(idx, portrait, color, profile)
        self.char_order = []
        self.evidence = {}        # id -> dict(idx, icon, name, desc)
        self.ev_order = []
        self.scenes = {}          # key -> idx
        self.portraits = {}       # key -> idx
        self.lineno = 0
        self.missing_expr = set()
        self.flag_names = {}       # @set / if= names -> flag index (from 0 up)
        self.next_mark = MAX_FLAGS - 1  # "option already chosen" marks (from the top down)  # 이름[표정] used in the script without an image file

    def ui(self, key):
        return self.text(dict(UI_STRINGS)[key])

    def err(self, msg):
        raise CompileError(f"story.txt:{self.lineno}: {msg}")

    # -- tables
    def text(self, s):
        for ch in s:
            if re.match(r"[\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]", ch):
                self.err(f"한자/일본어는 쓰지 않습니다: {ch!r}")
            if ch != "\n" and not self.font.has(ch):
                self.err(f"font has no glyph for {ch!r} (U+{ord(ch):04X})")
        if s not in self.text_index:
            self.text_index[s] = len(self.texts)
            self.texts.append(s)
        return self.text_index[s]

    def wrapped(self, s, width, max_lines, what):
        lines = wrap(self.font, s, width)
        if len(lines) > max_lines:
            self.err(f"{what} is too long ({len(lines)} lines, max {max_lines}): {s}")
        return self.text("\n".join(lines))

    def scene(self, key):
        if key not in art.SCENES:
            self.err(f"unknown scene {key!r}; known: {', '.join(art.SCENES)}")
        return self.scenes.setdefault(key, len(self.scenes))

    def portrait(self, key):
        if key not in art.PORTRAITS:
            self.err(f"unknown portrait {key!r}; known: {', '.join(art.PORTRAITS)}")
        return self.portraits.setdefault(key, len(self.portraits))

    def speaker(self, name):
        return self.speaker_portrait(name)[0]

    def speaker_portrait(self, token):
        """'이름' or '이름[표정]' -> (char index, portrait index). Expression = file <portrait>_<표정>."""
        if token in ("-", "narr", "나레이션"):
            return NONE, NONE
        m = SPEAKER_RE.match(token.strip())
        name, expr = m.group(1).strip(), m.group(2)
        if name not in self.chars:
            self.err(f"undeclared speaker {name!r} (use @char)")
        ch = self.chars[name]
        if not expr or ch["pkey"] is None:
            return ch["idx"], ch["portrait"]
        key = f"{ch['pkey']}_{expr.strip()}"
        if key not in art.PORTRAITS:
            self.missing_expr.add(key)
            return ch["idx"], ch["portrait"]
        return ch["idx"], self.portrait(key)

    def ev(self, eid):
        if eid not in self.evidence:
            self.err(f"unknown evidence {eid!r}")
        return self.evidence[eid]["idx"]

    def emit(self, *words):
        self.code.extend(words)

    def label_ref(self, name):
        self.fixups.append((len(self.code), name, self.lineno))
        self.code.append(0)

    # -- statements
    def say(self, spk, text, portrait=NONE):
        text = text.replace("\\n", "\n")  # "-장소-\n본문" style line breaks in dialogue lines
        lines = wrap(self.font, text, TEXT_W)
        for i in range(0, len(lines), TEXT_LINES):
            self.emit(OPS["SAY"], spk, self.text("\n".join(lines[i:i + TEXT_LINES])), portrait)

    def compile(self, src):
        lines = src.split("\n")
        block = None  # (kind, header data, items list)
        for self.lineno, raw in enumerate(lines, 1):
            try:
                block = self.compile_line(raw, block)
            except CompileError as e:
                if str(e).startswith("story.txt:"):
                    raise
                self.err(str(e))
        if block is not None:
            self.err("unterminated block at end of file")
        self.resolve()

    def compile_line(self, raw, block):
        ln = raw.strip()
        if not ln or ln.startswith("#"):
            return block
        if ln.startswith("*"):
            name = ln[1:].strip()
            if name in self.labels:
                self.err(f"duplicate label {name}")
            self.labels[name] = len(self.code)
            return block
        if ln.startswith("@"):
            try:
                args = shlex.split(ln[1:])
            except ValueError as e:
                self.err(str(e))
            args = [a.replace("\\n", "\n") for a in args]
            cmd, args = args[0], args[1:]
            if block is not None:
                return self.block_cmd(block, cmd, args)
            self.command(cmd, args)
            if cmd in BLOCKS:
                return (cmd, args, [], [])
            return block
        if block is not None:
            self.err("text inside a block; close it with @end")
        name, sep, rest = ln.partition(":")
        name = name.strip().strip('"')  # names with spaces may be quoted: "트네 마리오": 대사
        m = SPEAKER_RE.match(name) if sep else None
        if m and m.group(1).strip() in self.chars:
            spk, por = self.speaker_portrait(name)
            self.say(spk, rest.strip(), por)
        else:
            self.say(NONE, ln)
        return block

    def resolve(self):
        for pos, name, lineno in self.fixups:
            if name not in self.labels:
                self.lineno = lineno
                self.err(f"unknown label {name!r}")
            self.code[pos] = self.labels[name]
        if len(self.code) >= 0xFFFE:
            raise CompileError("script too large")

    def need_args(self, args, n, usage):
        if len(args) < n:
            self.err(f"usage: {usage}")

    def command(self, cmd, a):
        if cmd == "char":
            self.need_args(a, 3, "@char NAME PORTRAIT|- #RRGGBB")
            name = a[0]
            if len(self.chars) >= 32:
                self.err("too many characters (max 32)")
            self.text(name)
            self.chars[name] = dict(idx=len(self.chars), portrait=NONE if a[1] == "-" else self.portrait(a[1]),
                                    pkey=None if a[1] == "-" else a[1], color=art.rgb15(a[2]), profile=None)
            self.char_order.append(name)
        elif cmd == "profile":
            self.need_args(a, 2, "@profile NAME \"description\"")
            if a[0] not in self.chars:
                self.err(f"undeclared char {a[0]}")
            self.chars[a[0]]["profile"] = self.wrapped(a[1], TEXT_W, DESC_LINES, "profile")
        elif cmd == "profile_update":
            self.need_args(a, 2, "@profile_update NAME \"new description\"")
            if a[0] not in self.chars:
                self.err(f"undeclared char {a[0]}")
            self.emit(OPS["PROFILE"], self.chars[a[0]]["idx"], self.wrapped(a[1], TEXT_W, DESC_LINES, "profile"))
        elif cmd == "evidence":
            self.need_args(a, 4, "@evidence ID ICON \"name\" \"description\"")
            if a[1] not in art.ICONS:
                self.err(f"unknown icon {a[1]!r}; known: {', '.join(art.ICONS)}")
            if len(self.evidence) >= 32:
                self.err("too many evidence items (max 32)")
            if self.font.width(a[2]) > LIST_W - 12:
                self.err(f"evidence name too wide: {a[2]}")
            self.evidence[a[0]] = dict(idx=len(self.evidence), icon=list(art.ICONS).index(a[1]),
                                       name=self.text(a[2]),
                                       desc=self.wrapped(a[3], TEXT_W, DESC_LINES, "evidence description"))
            self.ev_order.append(a[0])
        elif cmd == "scene":
            self.need_args(a, 1, "@scene KEY")
            self.emit(OPS["SCENE"], self.scene(a[0]))
        elif cmd == "get":
            self.emit(OPS["GET"], self.ev(a[0]))
        elif cmd == "meet":
            self.emit(OPS["MEET"], self.speaker(a[0]))
            if self.chars[a[0]]["profile"] is None:
                self.err(f"@meet {a[0]} before its @profile")
        elif cmd == "fx":
            if not a or a[0] not in FX:
                self.err(f"@fx needs one of {', '.join(FX)}")
            self.emit(OPS["FX"], FX[a[0]])
        elif cmd == "chapter":
            self.emit(OPS["CHAPTER"], self.wrapped(a[0], TEXT_W, 3, "chapter title"))
        elif cmd == "lives":
            self.emit(OPS["LIVES"], int(a[0]))
        elif cmd == "gameover":
            self.emit(OPS["GAMEOVER"])
            self.label_ref(a[0])
        elif cmd == "goto":
            self.emit(OPS["GOTO"])
            self.label_ref(a[0])
        elif cmd == "return":
            self.emit(OPS["RETURN"])
        elif cmd == "wait":
            self.emit(OPS["WAIT"], int(a[0]))
        elif cmd == "shout":
            self.need_args(a, 2, "@shout SPEAKER \"text\"")
            if self.font.width(a[1]) * 2 > 232:
                self.err("shout text too wide (drawn at 2x)")
            spk, por = self.speaker_portrait(a[0])
            self.emit(OPS["SHOUT"], spk, self.text(a[1]), por)
        elif cmd == "ending":
            self.need_args(a, 2, "@ending bad|true \"title\"")
            self.emit(OPS["ENDING"], 1 if a[0] == "true" else 0, self.wrapped(a[1], TEXT_W, 2, "ending title"))
        elif cmd == "present":
            self.need_args(a, 4, "@present SPEAKER \"question\" EVIDENCE WRONG_LABEL")
            if self.font.width(a[1]) > HEADER_W:
                self.err("present question must fit on one line in the court record header")
            self.emit(OPS["PRESENT"], self.speaker(a[0]), self.wrapped(a[1], TEXT_W, TEXT_LINES, "question"),
                      self.ev(a[2]))
            self.label_ref(a[3])
        elif cmd == "set":
            self.need_args(a, 1, "@set FLAG")
            self.emit(OPS["SET"], self.flag(a[0]))
        elif cmd == "if":
            self.need_args(a, 2, "@if [!]FLAG_OR_EVIDENCE LABEL")
            self.emit(OPS["IF"], self.cond(a[0]))
            self.label_ref(a[1])
        elif cmd == "penalty":
            self.emit(OPS["PENALTY"])
        elif cmd == "timer":
            self.need_args(a, 1, "@timer SECONDS   (applies to the next @ask / @present)")
            self.emit(OPS["TIMER"], int(a[0]))
        elif cmd == "mash":
            self.need_args(a, 3, "@mash \"prompt\" SECONDS FAIL_LABEL")
            self.emit(OPS["MASH"], self.wrapped(a[0], TEXT_W, TEXT_LINES, "mash prompt"), int(a[1]))
            self.label_ref(a[2])
        elif cmd == "banner":
            if not a or a[0] not in ("invest", "deduce"):
                self.err("@banner invest|deduce")
            self.emit(OPS["BANNER"], 0 if a[0] == "invest" else 1)
        elif cmd in BLOCKS:
            pass  # handled as a block
        else:
            self.err(f"unknown command @{cmd}")

    def flag(self, name):
        if name in self.evidence:
            self.err(f"{name!r} is an evidence id; flags need their own names")
        if name not in self.flag_names:
            self.flag_names[name] = len(self.flag_names)
            if len(self.flag_names) >= self.next_mark:
                self.err("too many flags")
        return self.flag_names[name]

    def cond(self, text):
        neg = text.startswith("!")
        name = text[1:] if neg else text
        c = (0x4000 | self.evidence[name]["idx"]) if name in self.evidence else self.flag(name)
        return c | (0x8000 if neg else 0)

    def mark(self):
        m = self.next_mark
        self.next_mark -= 1
        if m <= len(self.flag_names):
            self.err("too many menu options (flag space full)")
        return m

    def block_cmd(self, block, cmd, a):
        kind, head, items, need = block
        if kind == "testimony":
            if cmd == "stmt":
                self.need_args(a, 2, "@stmt \"statement\" PRESS_LABEL [CONTRADICTING_EVIDENCE]")
                items.append((self.wrapped(a[0], TEXT_W, 2, "testimony statement"), a[1],
                              self.ev(a[2]) if len(a) > 2 else NONE, self.lineno))
                return block
            if cmd != "end":
                self.err("only @stmt / @end inside @testimony")
            self.need_args(head, 3, "@testimony SPEAKER \"title\" WRONG_LABEL")
            if not 2 <= len(items) <= 8 or not any(it[2] != NONE for it in items):
                self.err("@testimony needs 2-8 statements, at least one with contradicting evidence")
            if self.font.width(head[1]) > HEADER_W:
                self.err("testimony title must fit on one line")
            self.emit(OPS["TESTIMONY"], self.speaker(head[0]), self.text(head[1]), len(items))
            self.label_ref(head[2])
            for tid, lbl, ev, lineno in items:
                self.emit(tid)
                saved, self.lineno = self.lineno, lineno
                self.label_ref(lbl)
                self.lineno = saved
                self.emit(ev)
            return None
        if kind == "video":
            if cmd == "frame":
                self.need_args(a, 3, "@frame SCENE \"timecode\" \"caption\" [correct]")
                items.append((self.scene(a[0]), self.text(a[1]),
                              self.wrapped(a[2], TEXT_W, 2, "video caption"), "correct" in a[3:]))
                return block
            if cmd != "end":
                self.err("only @frame / @end inside @video")
            self.need_args(head, 3, "@video \"title\" OK_LABEL WRONG_LABEL")
            correct = [i for i, it in enumerate(items) if it[3]]
            if len(correct) != 1 or not 2 <= len(items) <= 32:
                self.err("@video needs 2-32 frames with exactly one marked correct")
            self.emit(OPS["VIDEO"], self.text(head[0]), len(items), correct[0])
            self.label_ref(head[1])
            self.label_ref(head[2])
            for sc, tc, cap, _ in items:
                self.emit(sc, tc, cap)
            return None
        if cmd in ("spot", "opt"):
            self.need_args(a, 2, f"@{cmd} \"text\" LABEL [if=[!]FLAG] [trap]")
            if self.font.width(a[0]) > MENU_W:
                self.err(f"option text too wide: {a[0]}")
            cond, trap = NONE, False
            for extra in a[2:]:
                if extra.startswith("if="):
                    cond = self.cond(extra[3:])
                elif extra == "trap":
                    trap = True
                else:
                    self.err(f"unknown option flag {extra!r}")
            if cond != NONE and kind not in ("investigate", "menu"):
                self.err("if= only works in @investigate / @menu")
            items.append((self.text(a[0]), a[1], self.lineno, cond, trap))
            return block
        if cmd == "need" and kind == "investigate":
            need.extend(self.ev(x) for x in a)
            return block
        if cmd != "end":
            self.err(f"@{cmd} not allowed inside @{kind}")
        if kind in ("investigate", "menu"):
            if not items or len(items) > 12 or sum(1 for it in items if it[3] == NONE) > 6:
                self.err(f"@{kind} needs 1-12 options, at most 6 without if=")
            if kind == "investigate":
                mask = 0
                for e in need:
                    mask |= 1 << e
                self.emit(OPS["INVEST"], len(items), mask & 0xFFFF, mask >> 16)
            else:
                self.need_args(head, 2, "@menu SPEAKER \"prompt\"")
                self.emit(OPS["MENU"], self.speaker(head[0]),
                          self.wrapped(head[1], TEXT_W, TEXT_LINES, "menu prompt"), self.ui("UI_BACK"), len(items))
            for tid, lbl, lineno, cond, trap in items:
                self.emit(tid)
                saved, self.lineno = self.lineno, lineno
                self.label_ref(lbl)
                self.lineno = saved
                self.emit(cond, self.mark() | (0x8000 if trap else 0))
            return None
        if not items or len(items) > 6:
            self.err(f"@{kind} needs 1-6 options")
        if True:
            self.need_args(head, 2, f"@{kind} SPEAKER \"question\"")
            op = OPS["ASK"] if kind == "ask" else OPS["ACCUSE"]
            self.emit(op, self.speaker(head[0]), self.wrapped(head[1], TEXT_W, TEXT_LINES, "question"), len(items))
        if kind == "ask" and sum(1 for it in items if it[1] == "ok") != 1:
            self.err("@ask needs exactly one option marked ok")
        for tid, lbl, lineno, _, _ in items:
            self.emit(tid)
            if lbl == "ok":
                self.emit(NONE)
            else:
                saved, self.lineno = self.lineno, lineno
                self.label_ref(lbl)
                self.lineno = saved
        return None


# ---------------------------------------------------------------- output

def c_array(name, ctype, values, per_line=16, fmt="{}"):
    out = [f"const {ctype} {name}[{len(values)}] __attribute__((aligned(4))) = {{"]
    for i in range(0, len(values), per_line):
        out.append("  " + ",".join(fmt.format(v) for v in values[i:i + per_line]) + ",")
    out.append("};")
    return "\n".join(out)


def build(story_path, font_path, out_dir):
    font = Font(font_path)
    comp = Compiler(font)
    with open(story_path, encoding="utf-8") as f:
        comp.compile(f.read())
    ui_ids = [(k, comp.text(v)) for k, v in UI_STRINGS]
    for key in art.ALWAYS_SCENES:
        comp.scene(key)

    # glyph table from every character used
    chars = sorted({c for t in comp.texts for c in t if c != "\n"})
    gmap = {c: i for i, c in enumerate(chars)}
    rows_used = [r for c in chars for _, r in font.pixels(c)]
    top, bottom = min(rows_used), max(rows_used)
    gh = bottom - top + 1
    glyph_bits, glyph_adv = [], []
    for c in chars:
        rows = [0] * gh
        for x, r in font.pixels(c):
            if 0 <= x < 16:
                rows[r - top] |= 0x8000 >> x
        glyph_bits.extend(rows)
        glyph_adv.append(font.adv(c))

    text_data, text_ofs = [], []
    for t in comp.texts:
        text_ofs.append(len(text_data))
        text_data.extend(NL if c == "\n" else gmap[c] for c in t)
        text_data.append(END)

    scene_keys = sorted(comp.scenes, key=comp.scenes.get)
    portrait_keys = sorted(comp.portraits, key=comp.portraits.get)
    os.makedirs(out_dir, exist_ok=True)

    h = ["/* generated by tools/build_assets.py - do not edit */", "#ifndef GEN_DATA_H", "#define GEN_DATA_H",
         '#include "platform.h"',
         f"#define GLYPH_H {gh}", f"#define GLYPH_COUNT {len(chars)}",
         f"#define CHAR_COUNT {len(comp.chars)}", f"#define EVIDENCE_COUNT {len(comp.evidence)}",
         f"#define SCENE_COUNT {len(scene_keys)}", f"#define PORTRAIT_COUNT {len(portrait_keys)}",
         f"#define ICON_COUNT {len(art.ICONS)}",
         f"#define PORTRAIT_W {art.PORTRAIT_W}", f"#define PORTRAIT_H {art.PORTRAIT_H}",
         f"#define THUMB_W {art.THUMB_W}", f"#define THUMB_H {art.THUMB_H}",
         f"#define ICON_SIZE {art.ICON_SIZE}", "#define TRANSPARENT 0xFFFF", "#define NONE 0xFFFF",
         "#define TXT_NL 0xFFFE", "#define TXT_END 0xFFFF"]
    for k, v in OPS.items():
        h.append(f"#define OP_{k} {v}")
    for k, v in FX.items():
        h.append(f"#define FX_{k.upper()} {v}")
    for k, v in ui_ids:
        h.append(f"#define {k} {v}")
    for k in scene_keys:
        h.append(f"#define SCENE_{k.upper()} {comp.scenes[k]}")
    for eid in comp.ev_order:
        h.append(f"#define EV_{eid.upper()} {comp.evidence[eid]['idx']}")
    h += ["extern const u16 script[];", "extern const u16 text_data[];", "extern const u32 text_ofs[];",
          "extern const u16 glyph_bits[];", "extern const u8 glyph_adv[];",
          "extern const u16 *const scene_img[];", "extern const u16 *const portrait_img[];", "extern const u16 *const portrait_thumb[];",
          "extern const u16 icon_img[];",
          "extern const u16 char_name[];", "extern const u16 char_portrait[];", "extern const u16 char_color[];",
          "extern const u16 char_profile[];",
          "extern const u16 ev_name[];", "extern const u16 ev_desc[];", "extern const u16 ev_icon[];",
          "#endif"]

    c = ['/* generated by tools/build_assets.py - do not edit */', '#include "gen_data.h"']
    c.append(c_array("script", "u16", comp.code))
    c.append(c_array("text_data", "u16", text_data))
    c.append(c_array("text_ofs", "u32", text_ofs))
    c.append(c_array("glyph_bits", "u16", glyph_bits, fmt="0x{:04X}"))
    c.append(c_array("glyph_adv", "u8", glyph_adv))
    for k in scene_keys:
        c.append(c_array(f"scene_{k}", "u16", art.render_scene(k), fmt="0x{:04X}"))
    c.append("const u16 *const scene_img[] = {" + ",".join(f"scene_{k}" for k in scene_keys) + "};")
    for k in portrait_keys:
        c.append(c_array(f"portrait_{k}", "u16", art.render_portrait(k), fmt="0x{:04X}"))
    c.append("const u16 *const portrait_img[] = {" + ",".join(f"portrait_{k}" for k in portrait_keys) + ("" if portrait_keys else "0") + "};")
    for k in portrait_keys:
        c.append(c_array(f"thumb_{k}", "u16", art.render_thumb(k), fmt="0x{:04X}"))
    c.append("const u16 *const portrait_thumb[] = {" + ",".join(f"thumb_{k}" for k in portrait_keys) + ("" if portrait_keys else "0") + "};")
    icons = []
    for k in art.ICONS:
        icons.extend(art.render_icon(k))
    c.append(c_array("icon_img", "u16", icons, fmt="0x{:04X}"))
    ch = [comp.chars[n] for n in comp.char_order]
    c.append(c_array("char_name", "u16", [comp.text_index[n] for n in comp.char_order] or [0]))
    c.append(c_array("char_portrait", "u16", [x["portrait"] for x in ch] or [0]))
    c.append(c_array("char_color", "u16", [x["color"] for x in ch] or [0]))
    c.append(c_array("char_profile", "u16", [NONE if x["profile"] is None else x["profile"] for x in ch] or [0]))
    evs = [comp.evidence[e] for e in comp.ev_order]
    c.append(c_array("ev_name", "u16", [x["name"] for x in evs] or [0]))
    c.append(c_array("ev_desc", "u16", [x["desc"] for x in evs] or [0]))
    c.append(c_array("ev_icon", "u16", [x["icon"] for x in evs] or [0]))

    with open(os.path.join(out_dir, "gen_data.h"), "w", encoding="utf-8") as f:
        f.write("\n".join(h) + "\n")
    with open(os.path.join(out_dir, "gen_data.c"), "w", encoding="utf-8") as f:
        f.write("\n".join(c) + "\n")
    if comp.missing_expr:
        print("note: expression images not found, using base portraits: "
              + ", ".join(f"assets/portraits/{k}.png" for k in sorted(comp.missing_expr)))
    print(f"script: {len(comp.code)} words, {len(comp.texts)} strings, {len(chars)} glyphs (h={gh}), "
          f"{len(comp.evidence)} evidence, {len(comp.chars)} chars, {len(scene_keys)} scenes")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--story", required=True)
    ap.add_argument("--font", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview", help="write PNG previews of all art into this directory")
    args = ap.parse_args()
    if args.preview:
        art.write_previews(args.preview)
    try:
        build(args.story, args.font, args.out)
    except CompileError as e:
        print(f"error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
