using System.Collections.Generic;
using UnityEngine;
using SennenKazoku.Core;

namespace SennenKazoku.Game
{
    /// <summary>
    /// 원작 소리. Resources/LocalAudio (사용자 ROM 에서 tools/gba_capture/audio_rip.py 로 녹음, git 제외)의 audio_index.json 이 곡 번호(원작 곡 표 번호)마다
    /// 반복 곡이면 앞부분(intro, 없을 수 있음) + 반복 한 바퀴(loop), 한 번 곡이면 once 를 알려 준다. 없으면 아무 소리도 내지 않는다.
    /// 배경음악은 원작 재생기 0·1 처럼 한 번에 하나: 앞부분 끝 표본에 맞춰 반복 바퀴를 예약 재생(PlayScheduled)해 이음매 없이 돈다.
    /// </summary>
    public sealed class SoundKit : MonoBehaviour
    {
        sealed class Entry { public string Intro, Loop, Once; public int Group; }
        sealed class Voice
        {
            public AudioSource A, B; public int Id = -1; public float Vol, Target, Speed;
            public void Stop() { A.Stop(); B.Stop(); A.clip = null; B.clip = null; Id = -1; Vol = Target = 0; }
            public void Apply(float master) { A.volume = B.volume = Vol * master; }
        }

        readonly Dictionary<int, Entry> songs = new Dictionary<int, Entry>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        Voice[] voices; int cur; AudioSource se;
        public bool Available { get; private set; }
        public int Count { get { return songs.Count; } }
        public int CurrentBgm { get { return voices != null && voices[cur].Target > 0 ? voices[cur].Id : -1; } }

        const string BgmKey = "sk.vol.bgm", SeKey = "sk.vol.se";
        public float BgmVolume { get { return PlayerPrefs.GetFloat(BgmKey, 0.8f); } set { PlayerPrefs.SetFloat(BgmKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); } }
        public float SeVolume { get { return PlayerPrefs.GetFloat(SeKey, 0.8f); } set { PlayerPrefs.SetFloat(SeKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); } }

        public static SoundKit Create(Transform parent)
        {
            var go = new GameObject("Sound"); go.transform.SetParent(parent, false);
            var k = go.AddComponent<SoundKit>(); k.Init();
            return k;
        }

        void Init()
        {
            // 씬 없이 만든 카메라에는 귀가 없다 — 없으면 하나 둔다
            if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            voices = new Voice[2];
            for (int i = 0; i < 2; i++) voices[i] = new Voice { A = Src(), B = Src() };
            se = Src();
            var t = Resources.Load<TextAsset>("LocalAudio/audio_index");
            if (t == null) return;
            var root = J.Obj(MiniJson.Parse(t.text));
            var map = J.Child(root, "songs");
            if (map == null) return;
            foreach (var kv in map)
            {
                int id; var o = kv.Value as Dictionary<string, object>;
                if (o == null || !int.TryParse(kv.Key, out id)) continue;
                songs[id] = new Entry { Intro = J.Str(o, "intro"), Loop = J.Str(o, "loop"), Once = J.Str(o, "once"), Group = J.Int(o, "group", 0) };
            }
            Available = songs.Count > 0;
        }

        AudioSource Src()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.spatialBlend = 0f;
            return s;
        }

        AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            AudioClip c;
            if (!clips.TryGetValue(name, out c)) clips[name] = c = Resources.Load<AudioClip>("LocalAudio/" + name);
            return c;
        }

        public bool Has(int id) { return songs.ContainsKey(id); }
        /// <summary>반복 곡인가 (반복 바퀴가 있다). 모르는 곡은 false.</summary>
        public bool Loops(int id) { Entry e; return songs.TryGetValue(id, out e) && !string.IsNullOrEmpty(e.Loop); }

        /// <summary>
        /// 배경음악을 바꾼다. 같은 곡이 이미 나오고 있으면 그대로 둔다. 앞 곡은 fadeOut 초 동안 줄이고, 새 곡은 fadeIn 초 동안 키운다
        /// (원작 0x08248E44 의 두 번째 인자 = 프레임 수 페이드). startAt: 곡 처음부터 몇 초 뒤에서 시작할지(오프닝 데모처럼 중간부터 볼 때).
        /// id &lt; 0 이면 멈춘다. 없는 곡이면 앞 곡만 멈춘다. 한 번 곡이 끝난 뒤 같은 번호를 다시 부르면 again 일 때만 처음부터 다시 낸다.
        /// </summary>
        public void PlayBgm(int id, float fadeOut = 0.5f, float fadeIn = 0f, float startAt = 0f, bool again = false)
        {
            if (voices == null) return;
            var v = voices[cur];
            if (id >= 0 && v.Id == id && v.Target > 0 && (!again || v.A.isPlaying || v.B.isPlaying)) return;
            Fade(v, fadeOut);
            Entry e;
            if (id < 0 || !songs.TryGetValue(id, out e)) return;
            cur ^= 1; v = voices[cur]; v.Stop();
            double now = AudioSettings.dspTime + 0.05;
            if (!string.IsNullOrEmpty(e.Once))
            {
                var c = Clip(e.Once); if (c == null) return;
                v.A.clip = c; v.A.loop = false; v.A.time = Mathf.Min(startAt, Mathf.Max(0f, c.length - 0.01f)); v.A.PlayScheduled(now);
            }
            else
            {
                var intro = Clip(e.Intro); var loop = Clip(e.Loop); if (loop == null) return;
                float il = intro != null ? intro.length : 0f;
                if (intro != null && startAt < il)
                {
                    v.A.clip = intro; v.A.loop = false; v.A.timeSamples = Mathf.Clamp((int)(startAt * intro.frequency), 0, intro.samples - 1); v.A.PlayScheduled(now);
                    v.B.clip = loop; v.B.loop = true; v.B.timeSamples = 0;
                    v.B.PlayScheduled(now + (double)(intro.samples - v.A.timeSamples) / intro.frequency);
                }
                else
                {
                    v.B.clip = loop; v.B.loop = true; v.B.timeSamples = Mathf.Clamp((int)(Mathf.Repeat(startAt - il, loop.length) * loop.frequency), 0, loop.samples - 1);
                    v.B.PlayScheduled(now);
                }
            }
            v.Id = id; v.Target = 1f;
            if (fadeIn > 0) { v.Vol = 0f; v.Speed = 1f / fadeIn; } else v.Vol = 1f;
            v.Apply(BgmVolume);
        }

        public void StopBgm(float fadeOut = 0.5f) { if (voices != null) Fade(voices[cur], fadeOut); }

        void Fade(Voice v, float t)
        {
            if (v.Id < 0) return;
            v.Target = 0f;
            if (t <= 0f) v.Stop(); else v.Speed = 1f / t;
        }

        /// <summary>효과음 한 번 (원작 효과음 재생기 — 앱은 겹쳐 낸다). 없는 번호면 조용히 넘어간다.</summary>
        public void PlaySe(int id)
        {
            Entry e;
            if (se == null || !songs.TryGetValue(id, out e)) return;
            var c = Clip(!string.IsNullOrEmpty(e.Once) ? e.Once : !string.IsNullOrEmpty(e.Intro) ? e.Intro : e.Loop);
            if (c != null) se.PlayOneShot(c, SeVolume);
        }

        /// <summary>음량 설정이 바뀌면 지금 나오는 곡에 바로 적용</summary>
        public void ApplyVolume() { if (voices != null) foreach (var v in voices) v.Apply(BgmVolume); }

        void Update()
        {
            if (voices == null) return;
            float m = BgmVolume;
            foreach (var v in voices)
            {
                if (v.Id < 0) continue;
                if (v.Vol != v.Target) v.Vol = Mathf.MoveTowards(v.Vol, v.Target, v.Speed * Time.unscaledDeltaTime);
                if (v.Target <= 0f && v.Vol <= 0f) { v.Stop(); continue; }
                v.Apply(m);
            }
        }
    }
}
