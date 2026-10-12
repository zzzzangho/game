using UnityEditor;
using UnityEngine;

namespace SennenKazoku.EditorTools
{
    /// <summary>
    /// Resources/LocalAudio (tools/gba_capture/audio_rip.py 가 만든 원작 소리, git 제외) 가져오기 설정:
    /// 배경음악(bgm_*)은 길어서 압축한 채 메모리에 두고(Vorbis), 효과음(se_*)은 짧아서 풀어 둔다(지연 없이 바로 낸다).
    /// 반복 이음매가 맞도록 둘 다 표본 수를 바꾸지 않는다(원래 표본율 유지).
    /// </summary>
    public sealed class LocalAudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/LocalAudio/")) return;
            var ai = (AudioImporter)assetImporter;
            bool bgm = System.IO.Path.GetFileName(assetPath).StartsWith("bgm_");
            var s = ai.defaultSampleSettings;
            s.loadType = bgm ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = bgm ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            s.quality = 0.5f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
            ai.forceToMono = false;
            ai.loadInBackground = false;
        }
    }
}
