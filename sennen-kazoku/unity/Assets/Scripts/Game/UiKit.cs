using System;
using UnityEngine;
using UnityEngine.UI;
using SennenKazoku.Core;

namespace SennenKazoku.Game
{
    /// <summary>코드로 UGUI 요소를 만드는 도우미. 좌표는 화면 왼쪽 위 기준 픽셀.</summary>
    public static class UiKit
    {
        static Font font;
        public static Font DefaultFont
        {
            get
            {
                if (font == null)
                {
                    // 임시: OS 한글 폰트. 출시 전 라이선스가 확인된 한글 폰트(예: Noto Sans KR, OFL)를 번들할 것.
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans CJK KR", "NotoSansCJK-Regular", "Noto Sans KR", "Malgun Gothic", "Apple SD Gothic Neo", "sans-serif" }, 32);
                }
                return font;
            }
        }

        public static readonly Color Bg = new Color32(0xFB, 0xF3, 0xE0, 255);
        public static readonly Color Panel = new Color32(0xFF, 0xFC, 0xF2, 255);
        public static readonly Color Ink = new Color32(0x3A, 0x2E, 0x22, 255);
        public static readonly Color Accent = new Color32(0xD9, 0x7B, 0x3C, 255);
        public static readonly Color AccentDark = new Color32(0x9C, 0x52, 0x20, 255);
        public static readonly Color Soft = new Color32(0xEA, 0xDC, 0xBE, 255);

        static Sprite circle;
        public static Sprite Circle
        {
            get
            {
                if (circle == null)
                {
                    const int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f; float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(n / 2f - d); t.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                    t.Apply(); t.filterMode = FilterMode.Bilinear;
                    circle = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
                }
                return circle;
            }
        }

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            return rt;
        }

        public static void SetPx(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
        }
        public static void SetPx(RectTransform rt, RectPx r) { SetPx(rt, r.X, r.Y, r.W, r.H); }

        public static Image Box(Transform parent, string name, Color c, Sprite sprite = null)
        {
            var rt = Node(parent, name); var im = rt.gameObject.AddComponent<Image>();
            im.color = c; if (sprite != null) im.sprite = sprite; im.raycastTarget = false; return im;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Node(parent, name); var t = rt.gameObject.AddComponent<Text>();
            t.font = DefaultFont; t.text = text; t.fontSize = size; t.color = color; t.alignment = anchor; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.raycastTarget = false;
            t.resizeTextForBestFit = false; return t;
        }

        public static Button Btn(Transform parent, string name, string text, int size, Color bg, Color fg, Action onClick)
        {
            var im = Box(parent, name, bg); im.raycastTarget = true;
            var b = im.gameObject.AddComponent<Button>(); b.targetGraphic = im;
            var cb = b.colors; cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cb.highlightedColor = Color.white; b.colors = cb;
            var l = Label(im.transform, "label", text, size, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            var lr = l.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(8, 4); lr.offsetMax = new Vector2(-8, -4);
            lr.pivot = new Vector2(0.5f, 0.5f);
            b.onClick.AddListener(() => onClick());
            return b;
        }

        /// <summary>한 줄 글자 입력칸 (모바일 화면 키보드 사용).</summary>
        public static InputField Input(Transform parent, string name, string text, int size, int maxChars, Action<string> onChange)
        {
            var im = Box(parent, name, Color.white); im.raycastTarget = true;
            var tl = Label(im.transform, "text", "", size, Ink, TextAnchor.MiddleLeft); tl.supportRichText = false;
            Stretch(tl.rectTransform, 10, 2, 10, 2);
            var ph = Label(im.transform, "placeholder", "이름 입력", size, new Color(0.6f, 0.6f, 0.6f), TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 10, 2, 10, 2);
            var f = im.gameObject.AddComponent<InputField>();
            f.textComponent = tl; f.placeholder = ph; f.characterLimit = maxChars; f.targetGraphic = im;
            f.lineType = InputField.LineType.SingleLine;   // 모바일: 기기 키보드(한글 IME)로 입력, 줄바꿈 없이 완료 버튼으로 끝
            f.text = text ?? "";
            f.onValueChanged.AddListener(v => onChange(v));
            return f;
        }

        public static void SetBtnText(Button b, string s) { b.GetComponentInChildren<Text>().text = s; }

        public static void Stretch(RectTransform rt, float l, float t, float r, float b)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }
    }
}
