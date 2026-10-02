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
from PIL import Image  # noqa: E402

import art  # noqa: E402
import display_font  # noqa: E402
import eyecatch  # noqa: E402
import logo  # noqa: E402
import title_fog  # noqa: E402

# ---- layout constants (must match src/main.c) ----
TEXT_W = 224          # dialogue box text width in pixels
TEXT_LINES = 3        # lines per dialogue page
ANIM_MAX_Y = 100      # background animation stays above the name tag / text box
# dialogue: every sentence starts on a new line. "…" only ends a sentence after a sentence-final syllable
# ("그래요… 하지만"), not in the middle of one ("장미 한 송이와… 이 편지가").
SENTENCE_END = re.compile(r"(?<=[.!?』])\s+|(?<=[다요까지야어아네군죠걸데래니나고][…])\s+")
MENU_W = 196          # max option width
DESC_LINES = 4        # court record description lines
GOT_NAME_W = 134      # evidence / character name width in the "입수!" popup
HEADER_W = 224        # court record header (present question)
LIST_W = 150          # court record list width

OPS = dict(SAY=1, SCENE=2, GET=3, FX=4, CHAPTER=5, INVEST=6, RETURN=7, ASK=8, PRESENT=9,
           ACCUSE=10, GOTO=11, ENDING=12, LIVES=13, GAMEOVER=14, MEET=15, WAIT=16, SHOUT=17,
           PROFILE=18, MENU=19, SET=20, IF=21, PENALTY=22, BANNER=23, VIDEO=24, TIMER=25, MASH=26,
           TESTIMONY=27, PRESENT_CHOICE=28, PLACE=29, INTRO=30, INSET=31, EXAMINE=32, SHOW=33)
BLOCKS = ("investigate", "ask", "accuse", "choice", "menu", "video", "testimony", "examine", "show")
MAX_FLAGS = 1024
SPEAKER_RE = re.compile(r"^(.+?)(?:\[([^\]]+)\])?$")  # 이름 or 이름[표정]
FX = dict(flash=0, shock=1, shake=2, red=3, boom=4, dun=5, horror=6, crash=7)
NL = 0xFFFE
EMPH_ON, EMPH_OFF = 0xFFFD, 0xFFFC  # {강조} markup in the script
END = 0xFFFF
NONE = 0xFFFF

UI_STRINGS = [
    ("UI_TITLE_SERIES", "소년탐정 김전일"),
    ("UI_TITLE_MAIN", "마술열차 살인사건"),
    ("UI_TITLE_FAN", "비공식 팬 게임"),
    ("UI_NEW", "처음부터"),
    ("UI_CONTINUE", "이어하기"),
    ("UI_PRESS_START", "PRESS START"),
    ("UI_DISCLAIMER", "이 게임은 「소년탐정 김전일」의\n팬이 만든 비공식 2차 창작\n게임입니다. 원작의 모든 권리는\n원작자와 출판사에 있습니다.\n\n폰트: 갈무리, 나눔명조\n(SIL OFL 1.1)"),
    ("UI_TAB_EVIDENCE", "증거물"),
    ("UI_TAB_PROFILE", "인물"),
    ("UI_RECORD_HINT", "L/R 전환  B 닫기"),
    ("UI_RECORD_SAVE", "SELECT: 저장"),
    ("UI_SAVE_Q", "어느 칸에 저장할까요?"),
    ("UI_LOAD_Q", "어느 기록을 불러올까요?"),
    ("UI_SLOT_AUTO", "자동 저장"),
    ("UI_SLOT_1", "기록 1"),
    ("UI_SLOT_2", "기록 2"),
    ("UI_SLOT_3", "기록 3"),
    ("UI_SLOT_EMPTY", "비어 있음"),
    ("UI_SLOT_SEP", " · "),
    ("UI_PRESENT_HINT", "A: 제시하기"),
    ("UI_SHOW_HINT", "A: 보여 준다  B: 그만둔다"),
    ("UI_PRESENT_BACK_HINT", "A: 제시하기  B: 돌아간다"),
    ("UI_EMPTY", "아직 아무것도 없다."),
    ("UI_GOT", "증거물 입수!"),
    ("UI_MEET", "인물 파일 추가!"),
    ("UI_PROFILE_UPDATED", "인물 파일 갱신!"),
    ("UI_INVEST_Q", "어디를 조사할까?"),
    ("UI_INVEST_DONE", "조사를 마친다"),
    ("UI_BACK", "돌아간다"),
    ("UI_EXAMINE_HINT", "A 조사  B 돌아간다"),
    ("UI_EXAMINE_NOTHING", "특별히 눈에 띄는 건 없다."),
    ("UI_SLAM_INVEST", "조사"),
    ("UI_SLAM_DEDUCE", "추리"),
    ("UI_SLAM_CROSS", "추궁"),
    ("UI_SLAM_START", "개시!"),
    ("UI_SLAM_DONE", "성공!"),
    ("UI_VIDEO_HINT", "←→ 넘기기  A 여기다!  B 그만"),
    ("UI_TESTI_HINT", "◀▶ 넘기기  A 추궁  R 증거 제시"),
    ("UI_PRESS_SHOUT", "잠깐!"),
    ("UI_OBJECTION", "그건 모순이야!"),
    ("UI_TIMEOUT", "시간이 없어…!\n머뭇거리는 사이에 기회를 놓쳤다."),
    ("UI_TIMEOUT_BRANCH", "시간이 없어…! 아무것도 내밀지 못했다."),
    ("UI_DANGER", "더 이상 실수할 수 없다…!"),
    ("UI_MASH_HINT", "A 버튼을 연타해!"),
    ("UI_INVEST_NOTYET", "아직 알아내지 못한 게 있어.\n좀 더 조사해 보자."),
    ("UI_WRONG", "틀렸다...!"),
    ("UI_BAD_END", "BAD END"),
    ("UI_TRUE_END", "TRUE END"),
    ("UI_GOOD_END", "GOOD END"),
    ("UI_BEST_END", "BEST END"),
    ("UI_NORMAL_END", "NORMAL END"),
    ("UI_PRESS_A", "A 버튼을 누르세요"),
    ("UI_THANKS", "플레이해 주셔서 감사합니다!"),
    ("UI_SAVED", "저장했습니다"),
    ("UI_CHSAVE_Q", "저장하시겠습니까?"),
    ("UI_YES", "예"),
    ("UI_NO", "아니오"),
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
        return sum(self.adv(c) for c in s if c not in "{}")


# ---------------------------------------------------------------- text wrapping

def wrap(font, text, width):
    """Word-wrap on spaces (Korean eojeol), falling back to per-character breaks. '\\n' forces a break."""
    out = []
    for para in text.split("\n"):
        line = ""
        words = para.split(" ")
        for i in range(len(words) - 1, 0, -1):  # "2, 3분" stays together (no break inside a number range)
            if re.search(r"\d,$", words[i - 1]) and re.match(r"\d", words[i]):
                words[i - 1:i + 1] = [words[i - 1] + "\u00a0" + words[i]]
        for word in words:
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
    # {emphasis} may not cross a line: close it at the line end and reopen it on the next line
    res, open_ = [], False
    for ln in out:
        pre = "{" if open_ else ""
        for ch in ln:
            if ch in "{}":
                open_ = ch == "{"
        res.append(pre + ln + ("}" if open_ else ""))
    return [ln.replace("{}", "").replace("\u00a0", " ") for ln in res]


def wrap_balanced(font, text, width):
    """Like wrap(), but with the line count of a greedy wrap and lines as even as possible
    (no lone word left dangling on the last line)."""
    lines = wrap(font, text, width)
    if len(lines) < 2:
        return lines
    lo, hi = width // len(lines), width
    while lo < hi:  # narrowest width that still needs no more lines
        mid = (lo + hi) // 2
        if len(wrap(font, text, mid)) <= len(lines):
            hi = mid
        else:
            lo = mid + 1
    return wrap(font, text, lo)


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
        self.insets = {}          # ins_* key -> idx
        self.lineno = 0
        self.missing_expr = set()
        self.flag_names = {}       # @set / if= names -> flag index (from 0 up)
        self.display = {}          # text id -> display_font style (titles, banners, shouts)
        self.next_mark = MAX_FLAGS - 1  # "option already chosen" marks (from the top down)  # 이름[표정] used in the script without an image file

    def disp(self, tid, style):
        """Draw this text with the display font instead of Galmuri."""
        if self.display.get(tid, style) != style:
            self.err(f"text {self.texts[tid]!r} used with two display styles")
        self.display[tid] = style
        return tid

    def ui(self, key):
        return self.text(dict(UI_STRINGS)[key])

    def err(self, msg):
        raise CompileError(f"story.txt:{self.lineno}: {msg}")

    # -- tables
    def text(self, s, style=None):
        """Text id for s. style keeps a separate copy per display style (the name '김전일' is both a
        name tag and an intro caption)."""
        for ch in s:
            if re.match(r"[\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]", ch):
                self.err(f"한자/일본어는 쓰지 않습니다: {ch!r}")
            if ch not in "\n{}" and not self.font.has(ch):
                self.err(f"font has no glyph for {ch!r} (U+{ord(ch):04X})")
        depth = 0
        for ch in s:
            depth += {"{": 1, "}": -1}.get(ch, 0)
            if depth not in (0, 1):
                self.err(f"unbalanced {{강조}} braces: {s!r}")
        if depth:
            self.err(f"unbalanced {{강조}} braces: {s!r}")
        key = s if style is None else (style, s)
        if key not in self.text_index:
            self.text_index[key] = len(self.texts)
            self.texts.append(s)
        return self.text_index[key]

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
        if ch["pkey"] is None:
            return ch["idx"], ch["portrait"]
        base = ch["pkey"] + (f"_{ch['outfit']}" if ch.get("outfit") else "")
        if base not in art.PORTRAITS:
            base = ch["pkey"]
        if not expr:
            return ch["idx"], self.portrait(base)
        key = f"{base}_{expr.strip()}"
        if key not in art.PORTRAITS:
            self.missing_expr.add(key)
            return ch["idx"], self.portrait(base)
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
        first, _, rest = text.partition("\n")
        if re.fullmatch(r"-[^-]+-", first.strip()):
            # location caption: shown on its own band in the middle of the screen, not in the text box
            place = first.strip()[1:-1].strip()
            if self.font.width(place) > TEXT_W:
                self.err(f"location caption too long: {place}")
            self.emit(OPS["PLACE"], self.disp(self.text(place), "place"))
            text = rest.strip()
            if not text:
                return
        sentences = []
        for para in text.split("\n"):
            parts = SENTENCE_END.split(para)
            for i, part in enumerate(parts):
                # a tiny fragment ("설마.", "헤헤.") stays on the line of its neighbour when both fit
                short = len(re.sub(r"[^가-힣A-Za-z0-9]", "", part)) <= 6
                prev_short = i > 0 and len(re.sub(r"[^가-힣A-Za-z0-9]", "", parts[i - 1])) <= 6
                if i > 0 and (short or prev_short) and self.font.width(sentences[-1] + " " + part) <= TEXT_W:
                    sentences[-1] += " " + part
                else:
                    sentences.append(part)
        # each sentence wrapped with balanced line lengths; pages break between sentences when possible
        pages, cur = [], []
        for sent in sentences:
            block = wrap_balanced(self.font, sent, TEXT_W)
            if cur and len(cur) + len(block) > TEXT_LINES:
                pages.append(cur)
                cur = []
            while len(block) > TEXT_LINES:  # long sentence: split evenly (4 -> 2+2), no one-line leftover
                n_pages = -(-len(block) // TEXT_LINES)
                take = -(-len(block) // n_pages)
                pages.append(block[:take])
                block = block[take:]
            cur += block
        if cur:
            pages.append(cur)
        # a lone short line on the last page borrows the previous page's last line (3+1 -> 2+2)
        if len(pages) > 1 and len(pages[-1]) == 1 and len(pages[-2]) == TEXT_LINES \
                and len(re.sub(r"[^가-힣A-Za-z0-9]", "", pages[-1][0])) <= 12:
            pages[-1].insert(0, pages[-2].pop())
        for page in pages:
            self.emit(OPS["SAY"], spk, self.text("\n".join(page)), portrait)

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
        elif cmd == "outfit":  # @outfit NAME SUFFIX|-: portraits <key>_SUFFIX[_표정] from here on (file order)
            self.need_args(a, 2, "@outfit NAME SUFFIX|-")
            if a[0] not in self.chars:
                self.err(f"undeclared speaker {a[0]!r}")
            self.chars[a[0]]["outfit"] = None if a[1] == "-" else a[1]
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
            if len(self.evidence) >= 64:
                self.err("too many evidence items (max 64)")
            if self.font.width(a[2]) > min(LIST_W - 12, GOT_NAME_W):
                self.err(f"evidence name too wide: {a[2]}")
            self.evidence[a[0]] = dict(idx=len(self.evidence), icon=list(art.ICONS).index(a[1]),
                                       name=self.text(a[2]),
                                       desc=self.wrapped(a[3], TEXT_W, DESC_LINES, "evidence description"))
            self.ev_order.append(a[0])
        elif cmd == "scene":  # @scene KEY [quick]: quick swaps the picture without a fade
            self.need_args(a, 1, "@scene KEY [quick]")
            if len(a) > 1 and a[1] != "quick":
                self.err("@scene KEY [quick]")
            self.emit(OPS["SCENE"], self.scene(a[0]) | (0x4000 if len(a) > 1 else 0))
        elif cmd == "cut":  # anime capture at a key moment: white flash, no portraits over it
            self.need_args(a, 1, "@cut CUT_KEY")
            if not a[0].startswith("cut_"):
                self.err("@cut takes a cut_* image (assets/cuts)")
            self.emit(OPS["FX"], FX["flash"])
            self.emit(OPS["SCENE"], self.scene(a[0]) | 0x8000)
        elif cmd == "inset":  # small framed picture over the middle of the screen for the next line
            self.need_args(a, 1, "@inset ins_KEY | off")
            if a[0] == "off":
                self.emit(OPS["INSET"], NONE)
            elif not a[0].startswith("ins_"):
                self.err("@inset takes an ins_* image (assets/cuts) or off")
            else:
                self.emit(OPS["INSET"], self.insets.setdefault(a[0], len(self.insets)))
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
            self.need_args(a, 1, "@chapter \"title\" [CARD_SCENE]")
            self.emit(OPS["CHAPTER"], self.disp(self.text(a[0]), "chapter"),
                      self.scene(a[1]) if len(a) > 1 else NONE)
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
            spk, por = self.speaker_portrait(a[0])
            self.emit(OPS["SHOUT"], spk, self.disp(self.text(a[1]), "shout"), por)
        elif cmd == "ending":
            self.need_args(a, 2, "@ending bad|true|good|best|normal \"title\"")
            kinds = dict(bad=0, true=1, good=2, best=3, normal=4)
            if a[0] not in kinds:
                self.err(f"unknown ending kind {a[0]!r}")
            self.emit(OPS["ENDING"], kinds[a[0]], self.disp(self.text(a[1]), "end_title"))
        elif cmd == "present":
            self.need_args(a, 4, "@present SPEAKER \"question\" EVIDENCE WRONG_LABEL")
            if self.font.width(a[1]) > HEADER_W:
                self.err("present question must fit on one line in the court record header")
            self.emit(OPS["PRESENT"], self.speaker(a[0]), self.wrapped(a[1], TEXT_W, TEXT_LINES, "question"),
                      self.ev(a[2]))
            self.label_ref(a[3])
        elif cmd == "present_choice":
            self.need_args(a, 5, "@present_choice SPEAKER \"question\" EVIDENCE OK_LABEL MISS_LABEL")
            if self.font.width(a[1]) > HEADER_W:
                self.err("present question must fit on one line in the court record header")
            self.emit(OPS["PRESENT_CHOICE"], self.speaker(a[0]), self.wrapped(a[1], TEXT_W, TEXT_LINES, "question"),
                      self.ev(a[2]))
            self.label_ref(a[3])
            self.label_ref(a[4])
        elif cmd == "intro":  # character introduction card: epithet over a big name, like the anime captions
            self.need_args(a, 3, "@intro SPEAKER \"epithet\" \"name\"")
            spk, por = self.speaker_portrait(a[0])
            self.emit(OPS["INTRO"], spk, por, self.disp(self.text(a[1], "intro_epi"), "intro_epi"),
                      self.disp(self.text(a[2], "intro_name"), "intro_name"))
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
            kinds = ("invest", "deduce", "cross", "cross_done")
            if not a or a[0] not in kinds:
                self.err("@banner invest|deduce|cross|cross_done")
            self.emit(OPS["BANNER"], kinds.index(a[0]))
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
            if cmd == "stmt":  # @stmt "statement" PRESS_LABEL [CONTRADICTING_EVIDENCE] [face=표정]
                self.need_args(a, 2, "@stmt \"statement\" PRESS_LABEL [CONTRADICTING_EVIDENCE] [face=EXPR]")
                face = [x[5:] for x in a[2:] if x.startswith("face=")]
                rest = [x for x in a[2:] if not x.startswith("face=")]
                spk = head[0].split("[")[0]
                por = self.speaker_portrait(f"{spk}[{face[0]}]" if face else head[0])[1]
                items.append((self.wrapped(a[0], TEXT_W, 2, "testimony statement"), a[1],
                              self.ev(rest[0]) if rest else NONE, self.lineno, por))
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
            for tid, lbl, ev, lineno, por in items:
                self.emit(tid)
                saved, self.lineno = self.lineno, lineno
                self.label_ref(lbl)
                self.lineno = saved
                self.emit(ev, por)
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
        if kind == "show":  # @show SPEAKER "question" DEFAULT_LABEL / @ev EVIDENCE LABEL ... / @end
            if cmd == "ev":
                self.need_args(a, 2, "@ev EVIDENCE LABEL")
                items.append((self.ev(a[0]), a[1], self.lineno))
                return block
            if cmd != "end":
                self.err("only @ev / @end inside @show")
            self.need_args(head, 3, "@show SPEAKER \"question\" DEFAULT_LABEL")
            if self.font.width(head[1]) > HEADER_W:
                self.err("show question must fit on one line in the court record header")
            self.emit(OPS["SHOW"], self.speaker(head[0]), self.text(head[1]))
            self.label_ref(head[2])
            self.emit(len(items))
            for ev, lbl, lineno in items:
                self.emit(ev)
                saved, self.lineno = self.lineno, lineno
                self.label_ref(lbl)
                self.lineno = saved
            return None
        if kind == "examine":
            if cmd == "at":
                self.need_args(a, 6, "@at X Y W H \"name\" LABEL [if=[!]FLAG] [trap] [end]")
                x, y, w, h = (int(v) for v in a[:4])
                if not (0 <= x < x + w <= 240 and 0 <= y < y + h <= 160):
                    self.err("@at rectangle must lie on the 240x160 screen")
                if self.font.width(a[4]) > MENU_W:
                    self.err(f"spot name too wide: {a[4]}")
                cond, trap, end = NONE, False, False
                for extra in a[6:]:
                    if extra.startswith("if="):
                        cond = self.cond(extra[3:])
                    elif extra == "trap":
                        trap = True
                    elif extra == "end":
                        end = True
                    else:
                        self.err(f"unknown spot flag {extra!r}")
                items.append((self.text(a[4]), a[5], self.lineno, cond, (trap, end), (x, y, w, h)))
                return block
            if cmd != "end":
                self.err("only @at / @end inside @examine")
            self.need_args(head, 1, "@examine \"prompt\"")
            if not 1 <= len(items) <= 12:
                self.err("@examine needs 1-12 spots")
            self.emit(OPS["EXAMINE"], self.text(head[0]), len(items))
            for tid, lbl, lineno, cond, (trap, end), rect in items:
                self.emit(tid)
                saved, self.lineno = self.lineno, lineno
                self.label_ref(lbl)
                self.lineno = saved
                self.emit(cond, self.mark() | (0x8000 if trap else 0) | (0x4000 if end else 0), *rect)
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
            if not items or len(items) > 11:  # 11 + the exit fill the two-column menu
                self.err(f"@{kind} needs 1-11 options")
            if kind == "investigate":
                mask = 0
                for e in need:
                    mask |= 1 << e
                self.emit(OPS["INVEST"], len(items), *((mask >> (16 * w)) & 0xFFFF for w in range(4)))
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


def render_bdf(font, text, colour=(255, 255, 255), shadow=(8, 8, 16)):
    """Small pixel-font label (name tags): white glyphs with a 1px drop shadow, transparent elsewhere."""
    w = font.width(text) + 1
    rows = [r for c in text for _, r in font.pixels(c)]
    top, bottom = min(rows), max(rows)
    img = Image.new("RGBA", (w, bottom - top + 2), (0, 0, 0, 0))
    for layer, (dx, dy, col) in enumerate(((1, 1, shadow), (0, 0, colour))):
        x = 0
        for c in text:
            for px_, r in font.pixels(c):
                img.putpixel((x + px_ + dx, r - top + dy), col + (255,))
            x += font.adv(c)
    return img


def build(story_path, font_path, out_dir):
    font = Font(font_path)
    comp = Compiler(font)
    with open(story_path, encoding="utf-8") as f:
        comp.compile(f.read())
    ui_ids = [(k, comp.text(v)) for k, v in UI_STRINGS]
    ui_style = dict(UI_TITLE_MAIN="logo_img", UI_TITLE_SERIES="logo_sub", UI_PRESS_START="place", UI_SLAM_INVEST="slam",
                    UI_SLAM_DEDUCE="slam", UI_SLAM_CROSS="slam", UI_SLAM_START="slam", UI_SLAM_DONE="slam", UI_PRESS_SHOUT="shout", UI_OBJECTION="shout", UI_BAD_END="bad_label",
                    UI_TRUE_END="end_label", UI_GOOD_END="end_label", UI_BEST_END="end_label",
                    UI_NORMAL_END="end_label", UI_WRONG="banner",
                    # small key hints in Galmuri9
                    UI_RECORD_HINT="hint", UI_RECORD_SAVE="hint", UI_PRESENT_HINT="hint", UI_PRESENT_BACK_HINT="hint", UI_SHOW_HINT="hint", UI_VIDEO_HINT="hint",
                    UI_TESTI_HINT="hint", UI_PRESS_A="hint", UI_TITLE_FAN="hint", UI_SAVED="hint",
                    UI_EXAMINE_HINT="hint")
    for k, tid in ui_ids:
        if k in ui_style:
            comp.disp(tid, ui_style[k])
    for key in art.ALWAYS_SCENES:
        comp.scene(key)

    # glyph table from every character used
    chars = sorted({c for t in comp.texts for c in t if c not in "\n{}"})
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
        text_data.extend({"\n": NL, "{": EMPH_ON, "}": EMPH_OFF}.get(c) or gmap[c] for c in t)
        text_data.append(END)

    scene_keys = sorted(comp.scenes, key=comp.scenes.get)
    portrait_keys = sorted(comp.portraits, key=comp.portraits.get)
    os.makedirs(out_dir, exist_ok=True)

    h = ["/* generated by tools/build_assets.py - do not edit */", "#ifndef GEN_DATA_H", "#define GEN_DATA_H",
         '#include "platform.h"',
         f"#define GLYPH_H {gh}", f"#define GLYPH_COUNT {len(chars)}",
         f"#define CHAR_COUNT {len(comp.chars)}", f"#define EVIDENCE_COUNT {len(comp.evidence)}",
         f"#define GLYPH_DASH {gmap.get('-', 0xFFFF)}",  # "-장소-" lines are centred
         f"#define ANIM_FRAMES {art.ANIM_FRAMES}", f"#define ANIM_MAX_Y {ANIM_MAX_Y}",
         f"#define SCENE_COUNT {len(scene_keys)}", f"#define PORTRAIT_COUNT {len(portrait_keys)}",
         f"#define ICON_COUNT {len(art.ICONS)}",
         f"#define PORTRAIT_W {art.PORTRAIT_W}", f"#define PORTRAIT_H {art.PORTRAIT_H}",
         f"#define THUMB_W {art.THUMB_W}", f"#define THUMB_H {art.THUMB_H}",
         f"#define INSET_W {art.INSET_W}", f"#define INSET_H {art.INSET_H}",
         f"#define ICON_SIZE {art.ICON_SIZE}", "#define TRANSPARENT 0xFFFF", "#define NONE 0xFFFF",
         "#define TXT_NL 0xFFFE", "#define TXT_END 0xFFFF",
         "#define TXT_EMPH_ON 0xFFFD", "#define TXT_EMPH_OFF 0xFFFC"]
    for k, v in OPS.items():
        h.append(f"#define OP_{k} {v}")
    script_hash = 0x811C9DC5
    for w in comp.code:
        script_hash = ((script_hash ^ w) * 0x01000193) & 0xFFFFFFFF
    h.append(f"#define SCRIPT_HASH 0x{script_hash:08X}u")
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
          "extern const u16 anim_data[];", "extern const u32 anim_ofs[];", "extern const u8 anim_count[];",
          "extern const u16 *const scene_img[];", "extern const u16 *const portrait_img[];", "extern const u8 portrait_breathe[];", "extern const u8 blink_rect[];", "extern const u16 *const inset_img[];", "extern const u16 *const blink_img[];", "extern const u16 *const portrait_thumb[];",
          "extern const u16 icon_img[];",
          "extern const u16 char_name[];", "extern const u16 char_portrait[];", "extern const u16 char_color[];",
          "extern const u16 char_profile[];",
          "extern const u16 disp_of_text[];", "extern const u16 disp_data[];", "extern const u32 disp_ofs[];",
          "extern const u16 disp_w[];", "extern const u16 disp_h[];", "extern const u8 disp_cuts[];", "extern const u8 title_fog1[];", "extern const u8 title_fog2[];", "extern const u8 title_lights[];", "extern const u16 title_lut[];", "extern const u16 eyecatch_img[];",
          f"#define HAVE_EYECATCH {1 if eyecatch.available() else 0}",
          "extern const u16 ev_name[];", "extern const u16 ev_desc[];", "extern const u16 ev_icon[];",
          "#endif"]

    c = ['/* generated by tools/build_assets.py - do not edit */', '#include "gen_data.h"']
    c.append(c_array("script", "u16", comp.code))
    c.append(c_array("text_data", "u16", text_data))
    c.append(c_array("text_ofs", "u32", text_ofs))
    c.append(c_array("glyph_bits", "u16", glyph_bits, fmt="0x{:04X}"))
    c.append(c_array("glyph_adv", "u8", glyph_adv))
    # display-font images (titles, banners, shouts)
    disp_of, disp_data, disp_ofs, disp_w, disp_h, disp_cuts = [NONE] * len(comp.texts), [], [], [], [], []
    name_font = Font(os.path.join(os.path.dirname(font_path), "Galmuri9.bdf"))
    for n in comp.char_order:  # name tags use the small Galmuri9
        comp.disp(comp.text_index[n], "name")
    for k in ("UI_GOT", "UI_MEET", "UI_PROFILE_UPDATED"):
        comp.disp(dict(ui_ids)[k], "name_gold")
    for tid, style in sorted(comp.display.items()):
        if style == "name":
            im = render_bdf(name_font, comp.texts[tid])
        elif style == "name_gold":
            im = render_bdf(name_font, comp.texts[tid], colour=(248, 208, 96))
        elif style == "logo_img":
            im = logo.render()
        elif style == "hint":
            im = render_bdf(name_font, comp.texts[tid], colour=(176, 176, 196))
        else:
            im = display_font.render(comp.texts[tid], style)
        disp_of[tid] = len(disp_ofs)
        disp_ofs.append(len(disp_data))
        disp_w.append(im.width)
        disp_h.append(im.height)
        disp_data.extend(art.to15(im, dither=False))
        # shouts slam in word by word: the column where each word (but the last) ends
        cuts = []
        if style == "shout":
            words, size = comp.texts[tid].split(" "), display_font.last_size
            for k in range(1, len(words)):
                cuts.append(min(im.width, display_font.render(" ".join(words[:k]), style, size).width))
        disp_cuts.extend((cuts + [0, 0, 0])[:3])
    c.append(c_array("disp_of_text", "u16", disp_of))
    # chapter-change eyecatch (Kindaichi's glowing silhouette), if the source picture is present
    if eyecatch.available():
        c.append(c_array("eyecatch_img", "u16", art.to15(eyecatch.render(), dither=False), fmt="0x{:04X}"))
    else:
        c.append("const u16 eyecatch_img[1] = {0};")
    c.append(c_array("disp_data", "u16", disp_data or [0], fmt="0x{:04X}"))
    c.append(c_array("disp_ofs", "u32", disp_ofs or [0]))
    c.append(c_array("disp_w", "u16", disp_w or [0]))
    c.append(c_array("disp_h", "u16", disp_h or [0]))
    c.append(c_array("disp_cuts", "u8", disp_cuts or [0]))
    # title screen fog: two drifting fog layers and a layer of faint lights, composed live
    fog1, fog2 = title_fog.fog_layers()
    c.append(c_array("title_fog1", "u8", fog1.ravel().tolist()))
    c.append(c_array("title_fog2", "u8", fog2.ravel().tolist()))
    c.append(c_array("title_lights", "u8", title_fog.light_layer().ravel().tolist()))
    c.append(c_array("title_lut", "u16", title_fog.colour_table(), fmt="0x{:04X}"))
    for k in scene_keys:
        c.append(c_array(f"scene_{k}", "u16", art.render_scene(k), fmt="0x{:04X}"))
    c.append("const u16 *const scene_img[] = {" + ",".join(f"scene_{k}" for k in scene_keys) + "};")
    # looping background animation: per scene ANIM_FRAMES diffs [y0, y1, nruns, (ofs, len, px...)...]
    anim_data, anim_ofs, anim_n = [0], [], []
    for k in scene_keys:
        frames = art.scene_anim(k, ANIM_MAX_Y)
        anim_n.append(len(frames) if frames else 0)
        for y0, y1, runs in frames or []:
            anim_ofs.append(len(anim_data))
            anim_data += [y0, y1, len(runs)]
            for ofs, pxs in runs:
                anim_data += [ofs, len(pxs)] + pxs
        if not frames:
            anim_ofs += [0] * art.ANIM_FRAMES
    c.append(c_array("anim_data", "u16", anim_data, fmt="0x{:04X}"))
    c.append(c_array("anim_ofs", "u32", anim_ofs or [0]))
    c.append(c_array("anim_count", "u8", anim_n or [0]))
    blink_rect, blink_ptr = [], []
    for k in portrait_keys:
        pix = art.render_portrait(k)
        c.append(c_array(f"portrait_{k}", "u16", pix, fmt="0x{:04X}"))
        # idle blink: <key>_blink.png is the same picture with the eyes closed; keep the changed rectangle
        shut = art.render_portrait(k + "_blink") if k + "_blink" in art.USER_PORTRAITS else None
        diff = [i for i in range(len(pix)) if shut and shut[i] != pix[i]]
        if not diff:
            blink_rect += [0, 0, 0, 0]
            blink_ptr.append("0")
            continue
        W = art.PORTRAIT_W
        ys = [i // W for i in diff]
        xs = [i % W for i in diff]
        x0, y0, x1, y1 = min(xs), min(ys), max(xs) + 1, max(ys) + 1
        blink_rect += [x0, y0, x1 - x0, y1 - y0]
        c.append(c_array(f"blink_{k}", "u16", [shut[y * W + x] for y in range(y0, y1) for x in range(x0, x1)], fmt="0x{:04X}"))
        blink_ptr.append(f"blink_{k}")
    c.append(c_array("blink_rect", "u8", blink_rect or [0]))
    c.append("const u16 *const blink_img[] = {" + ",".join(blink_ptr or ["0"]) + "};")
    c.append("const u16 *const portrait_img[] = {" + ",".join(f"portrait_{k}" for k in portrait_keys) + ("" if portrait_keys else "0") + "};")
    # idle breathing: every character's bust rises and falls a little
    c.append(c_array("portrait_breathe", "u8", [1 for k in portrait_keys] or [0]))
    inset_keys = sorted(comp.insets, key=comp.insets.get)
    for k in inset_keys:
        c.append(c_array(f"inset_{k}", "u16", art.render_inset(k), fmt="0x{:04X}"))
    c.append("const u16 *const inset_img[] = {" + ",".join(f"inset_{k}" for k in inset_keys) + ("" if inset_keys else "0") + "};")
    missing = [k for k in inset_keys if k not in art.USER_INSETS]
    if missing:
        print("note: inset pictures not found, using plain cards: " + ", ".join(f"assets/cuts/{k}.png" for k in missing))
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
