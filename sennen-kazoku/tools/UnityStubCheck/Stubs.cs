// Unity 없이 앱 스크립트 컴파일만 확인하기 위한 빈 껍데기 (실행 동작 없음). 쓰이는 API 만 컴파일러 오류를 보며 채웠다.
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static void Destroy(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static T Instantiate<T>(T o) where T : Object { return o; }
        public static T FindFirstObjectByType<T>() where T : Object { return null; }
        public static implicit operator bool(Object o) { return o != null; }
    }
    public class Component : Object
    {
        public GameObject gameObject; public Transform transform;
        public T GetComponent<T>() { return default(T); }
        public T GetComponentInChildren<T>() { return default(T); }
        public T AddComponent<T>() where T : Component { return default(T); }
    }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator e) { return null; }
        public void StopAllCoroutines() { }
    }
    public class Coroutine { }
    public class YieldInstruction { }
    public class WaitForSeconds : YieldInstruction { public WaitForSeconds(float s) { } }
    public class GameObject : Object
    {
        public Transform transform; public bool activeSelf; public string tag;
        public GameObject() { } public GameObject(string n) { } public GameObject(string n, params Type[] t) { }
        public T AddComponent<T>() where T : Component { return default(T); }
        public T GetComponent<T>() { return default(T); }
        public void SetActive(bool b) { }
    }
    public class Transform : Component, IEnumerable
    {
        public Vector3 position, localPosition, localScale, eulerAngles, localEulerAngles; public Quaternion rotation, localRotation;
        public Transform parent; public int childCount;
        public void SetParent(Transform t, bool w) { } public void SetParent(Transform t) { }
        public Transform GetChild(int i) { return null; } public void SetAsLastSibling() { } public void SetAsFirstSibling() { } public void SetSiblingIndex(int i) { }
        public Transform Find(string n) { return null; }
        public IEnumerator GetEnumerator() { yield break; }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition, offsetMin, offsetMax; public Rect rect;
        public void SetSizeWithCurrentAnchors(RectTransform.Axis a, float s) { }
        public enum Axis { Horizontal, Vertical }
    }
    public struct Vector2
    {
        public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static readonly Vector2 zero = default, one = default, up = default, down = default, left = default, right = default;
        public float magnitude { get { return 0; } } public Vector2 normalized { get { return this; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return a; } public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator *(Vector2 a, float b) { return a; } public static Vector2 operator *(float b, Vector2 a) { return a; } public static Vector2 operator /(Vector2 a, float b) { return a; }
        public static Vector2 operator -(Vector2 a) { return a; }
        public static bool operator ==(Vector2 a, Vector2 b) { return true; } public static bool operator !=(Vector2 a, Vector2 b) { return false; }
        public override bool Equals(object o) { return true; } public override int GetHashCode() { return 0; }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(); } public static implicit operator Vector2(Vector3 v) { return new Vector2(); }
        public static float Distance(Vector2 a, Vector2 b) { return 0; } public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { return a; }
    }
    public struct Vector3
    {
        public float x, y, z; public Vector3(float x, float y, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero, one, up;
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; } public static Vector3 operator -(Vector3 a, Vector3 b) { return a; } public static Vector3 operator *(Vector3 a, float b) { return a; }
    }
    public struct Quaternion { public static Quaternion identity; public static Quaternion Euler(float x, float y, float z) { return identity; } }
    public struct Rect
    {
        public float x, y, width, height, xMin, xMax, yMin, yMax; public Vector2 size, center, position;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; xMin = x; yMin = y; xMax = x + w; yMax = y + h; size = default; center = default; position = default; }
        public static bool operator ==(Rect a, Rect b) { return true; } public static bool operator !=(Rect a, Rect b) { return false; }
        public override bool Equals(object o) { return true; } public override int GetHashCode() { return 0; }
        public bool Contains(Vector2 p) { return false; }
    }
    public struct Color
    {
        public float r, g, b, a; public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white, black, red, green, blue, gray, grey, clear, yellow, cyan, magenta;
        public static implicit operator Color(Color32 c) { return new Color(); } public static Color Lerp(Color a, Color b, float t) { return a; }
        public static Color operator *(Color a, float b) { return a; }
    }
    public struct Color32
    {
        public byte r, g, b, a; public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) { return new Color32(); }
    }
    public class Texture : Object { public int width, height; public FilterMode filterMode; public TextureWrapMode wrapMode; }
    public class Texture2D : Texture
    {
        public Texture2D(int w, int h) { } public Texture2D(int w, int h, TextureFormat f, bool m) { }
        public void SetPixels32(Color32[] c) { } public Color32[] GetPixels32() { return null; } public void SetPixel(int x, int y, Color c) { } public Color GetPixel(int x, int y) { return default; }
        public void SetPixels(Color[] c) { } public Color[] GetPixels() { return null; }
        public void Apply() { } public void Apply(bool a, bool b) { } public bool LoadImage(byte[] d) { return true; } public byte[] EncodeToPNG() { return null; }
    }
    public static class ImageConversion { public static bool LoadImage(Texture2D t, byte[] d) { return true; } }
    public enum TextureFormat { RGBA32, ARGB32, RGB24 }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp }
    public class Sprite : Object
    {
        public Texture2D texture; public Rect rect; public Vector2 pivot; public float pixelsPerUnit;
        public static Sprite Create(Texture2D t, Rect r, Vector2 p) { return null; } public static Sprite Create(Texture2D t, Rect r, Vector2 p, float ppu) { return null; }
        public static Sprite Create(Texture2D t, Rect r, Vector2 p, float ppu, uint ex, SpriteMeshType m) { return null; }
        public static Sprite Create(Texture2D t, Rect r, Vector2 p, float ppu, uint ex, SpriteMeshType m, Vector4 b) { return null; }
    }
    public enum SpriteMeshType { FullRect, Tight }
    public struct Vector4 { public Vector4(float a, float b, float c, float d) { } public static Vector4 zero; }
    public class Font : Object { public static Font CreateDynamicFontFromOSFont(string n, int s) { return null; } public static Font CreateDynamicFontFromOSFont(string[] n, int s) { return null; } public static string[] GetOSInstalledFontNames() { return new string[0]; } }
    public class TextAsset : Object { public string text; public byte[] bytes; }
    public static class Resources
    {
        public static T Load<T>(string p) where T : Object { return null; } public static Object Load(string p) { return null; }
        public static T[] LoadAll<T>(string p) where T : Object { return new T[0]; } public static T GetBuiltinResource<T>(string p) where T : Object { return null; }
    }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public enum ScreenOrientation { Portrait, PortraitUpsideDown, LandscapeLeft, LandscapeRight, AutoRotation }
    public enum CameraClearFlags { Skybox, SolidColor, Depth, Nothing }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
    public enum RuntimePlatform { Android, IPhonePlayer, WindowsEditor, OSXEditor, LinuxEditor }
    public enum KeyCode { Escape, Space, Return, LeftArrow, RightArrow, UpArrow, DownArrow, R, S, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5 }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute() { } public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
    public class Canvas : Behaviour { public RenderMode renderMode; public int sortingOrder; public float scaleFactor; }
    public class Camera : Behaviour { public static Camera main; public CameraClearFlags clearFlags; public Color backgroundColor; }
    public static class Screen
    {
        public static int width, height; public static float dpi; public static Rect safeArea; public static ScreenOrientation orientation;
        public static bool autorotateToPortrait, autorotateToPortraitUpsideDown, autorotateToLandscapeLeft, autorotateToLandscapeRight; public static int sleepTimeout;
    }
    public static class SleepTimeout { public const int NeverSleep = -1; }
    public static class Application
    {
        public static int targetFrameRate; public static string persistentDataPath = "", dataPath = "", version = ""; public static RuntimePlatform platform; public static bool isEditor;
        public static void OpenURL(string u) { } public static void Quit() { }
    }
    public static class Time { public static float deltaTime, time, unscaledDeltaTime, unscaledTime, realtimeSinceStartup; }
    public static class Input
    {
        public static bool GetKey(KeyCode k) { return false; } public static bool GetKeyDown(KeyCode k) { return false; } public static bool GetMouseButton(int b) { return false; }
        public static bool GetMouseButtonDown(int b) { return false; } public static Vector3 mousePosition; public static int touchCount; public static bool anyKeyDown;
    }
    public static class Debug { public static void Log(object o) { } public static void LogWarning(object o) { } public static void LogError(object o) { } public static void LogException(Exception e) { } }
    public static class Mathf
    {
        public const float PI = 3.14159f, Infinity = float.PositiveInfinity;
        public static int RoundToInt(float f) { return 0; } public static int FloorToInt(float f) { return 0; } public static int CeilToInt(float f) { return 0; }
        public static float Clamp(float v, float a, float b) { return v; } public static int Clamp(int v, int a, int b) { return v; } public static float Clamp01(float v) { return v; }
        public static float Min(float a, float b) { return a; } public static float Max(float a, float b) { return a; } public static int Min(int a, int b) { return a; } public static int Max(int a, int b) { return a; }
        public static float Min(params float[] a) { return 0; } public static float Max(params float[] a) { return 0; }
        public static float Abs(float a) { return a; } public static int Abs(int a) { return a; } public static float Lerp(float a, float b, float t) { return a; } public static float MoveTowards(float a, float b, float d) { return a; }
        public static float Sin(float a) { return 0; } public static float Cos(float a) { return 0; } public static float Floor(float a) { return a; } public static float Round(float a) { return a; } public static float Repeat(float t, float l) { return t; }
        public static float Sqrt(float a) { return a; } public static float Pow(float a, float b) { return a; } public static float Sign(float a) { return a; } public static float PingPong(float t, float l) { return t; } public static float SmoothStep(float a, float b, float t) { return a; }
        public static bool Approximately(float a, float b) { return true; } public static float Log(float a) { return a; }
    }
    public static class Random { public static float Range(float a, float b) { return a; } public static int Range(int a, int b) { return a; } public static float value; }
    public static class PlayerPrefs
    {
        public static int GetInt(string k, int d = 0) { return d; } public static void SetInt(string k, int v) { } public static string GetString(string k, string d = "") { return d; }
        public static void SetString(string k, string v) { } public static float GetFloat(string k, float d = 0) { return d; } public static void SetFloat(string k, float v) { } public static void Save() { } public static bool HasKey(string k) { return false; }
    }
    public static class JsonUtility { public static string ToJson(object o) { return ""; } public static T FromJson<T>(string s) { return default; } }
}

namespace UnityEngine.EventSystems
{
    public class BaseEventData { }
    public class PointerEventData : BaseEventData { public Vector2 position, delta, pressPosition; public int pointerId; public bool dragging; }
    public interface IEventSystemHandler { }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData e); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData e); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData e); }
    public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData e); }
    public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData e); }
    public interface IBeginDragHandler : IEventSystemHandler { void OnBeginDrag(PointerEventData e); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData e); }
    public interface IEndDragHandler : IEventSystemHandler { void OnEndDrag(PointerEventData e); }
    public class EventSystem : MonoBehaviour { public static EventSystem current; }
    public class StandaloneInputModule : MonoBehaviour { }
    public class UIBehaviour : MonoBehaviour { }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction(); public delegate void UnityAction<T>(T a);
    public class UnityEvent { public void AddListener(UnityAction a) { } public void RemoveAllListeners() { } public void Invoke() { } }
    public class UnityEvent<T> { public void AddListener(UnityAction<T> a) { } public void RemoveAllListeners() { } }
}

namespace UnityEngine.UI
{
    using UnityEngine.Events;
    public class Graphic : EventSystems.UIBehaviour { public Color color; public bool raycastTarget; public RectTransform rectTransform; public Material material; }
    public class Material : Object { }
    public class MaskableGraphic : Graphic { public bool maskable; }
    public class Image : MaskableGraphic
    {
        public Sprite sprite; public bool preserveAspect; public Type type; public float fillAmount; public FillMethod fillMethod; public int fillOrigin;
        public enum Type { Simple, Sliced, Tiled, Filled } public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public enum OriginHorizontal { Left, Right }
    }
    public class RawImage : MaskableGraphic { public Texture texture; public Rect uvRect; }
    public class Text : MaskableGraphic
    {
        public string text; public Font font; public int fontSize; public TextAnchor alignment; public FontStyle fontStyle; public bool supportRichText, resizeTextForBestFit, alignByGeometry;
        public HorizontalWrapMode horizontalOverflow; public VerticalWrapMode verticalOverflow; public float lineSpacing, preferredHeight, preferredWidth; public int resizeTextMinSize, resizeTextMaxSize;
    }
    public class Selectable : EventSystems.UIBehaviour { public bool interactable; public Graphic targetGraphic; public Transition transition; public ColorBlock colors; public enum Transition { None, ColorTint, SpriteSwap, Animation } }
    public struct ColorBlock { public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor; public float colorMultiplier, fadeDuration; public static ColorBlock defaultColorBlock; }
    public class Button : Selectable { public ButtonClickedEvent onClick = new ButtonClickedEvent(); public class ButtonClickedEvent : UnityEvent { } }
    public class InputField : Selectable
    {
        public string text; public Text textComponent; public Graphic placeholder; public int characterLimit; public ContentType contentType; public LineType lineType;
        public SubmitEvent onEndEdit = new SubmitEvent(); public OnChangeEvent onValueChanged = new OnChangeEvent();
        public class SubmitEvent : UnityEvent<string> { } public class OnChangeEvent : UnityEvent<string> { }
        public enum ContentType { Standard, IntegerNumber, Name } public enum LineType { SingleLine, MultiLineNewline }
        public void ActivateInputField() { }
    }
    public class Outline : Shadow { }
    public class Shadow : EventSystems.UIBehaviour { public Color effectColor; public Vector2 effectDistance; }
    public class GraphicRaycaster : EventSystems.UIBehaviour { }
    public class CanvasScaler : EventSystems.UIBehaviour { public ScaleMode uiScaleMode; public Vector2 referenceResolution; public float matchWidthOrHeight; public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize } }
    public class Mask : EventSystems.UIBehaviour { public bool showMaskGraphic; }
    public class RectMask2D : EventSystems.UIBehaviour { }
    public class ScrollRect : EventSystems.UIBehaviour
    {
        public RectTransform content, viewport; public bool horizontal, vertical; public float verticalNormalizedPosition, horizontalNormalizedPosition, scrollSensitivity; public MovementType movementType;
        public enum MovementType { Unrestricted, Elastic, Clamped }
    }
    public class LayoutElement : EventSystems.UIBehaviour { public float preferredHeight, preferredWidth, minHeight, flexibleHeight; }
    public class ContentSizeFitter : EventSystems.UIBehaviour { public FitMode verticalFit, horizontalFit; public enum FitMode { Unconstrained, MinSize, PreferredSize } }
    public class VerticalLayoutGroup : EventSystems.UIBehaviour { public float spacing; public bool childControlHeight, childControlWidth, childForceExpandHeight, childForceExpandWidth; }
}

namespace UnityEngine.Networking
{
    public class UnityWebRequest : IDisposable
    {
        public static UnityWebRequest Get(string u) { return null; } public DownloadHandler downloadHandler; public Result result; public string error; public long responseCode; public int timeout;
        public UnityWebRequestAsyncOperation SendWebRequest() { return null; } public void Dispose() { } public void SetRequestHeader(string a, string b) { }
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
    }
    public class DownloadHandler { public string text; public byte[] data; }
    public class UnityWebRequestAsyncOperation : AsyncOperation { }
}

namespace UnityEngine
{
    public class AsyncOperation : YieldInstruction { public bool isDone; public float progress; }
}
