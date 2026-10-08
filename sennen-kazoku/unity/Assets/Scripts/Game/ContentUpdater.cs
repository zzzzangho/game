using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using SennenKazoku.Core;

namespace SennenKazoku.Game
{
    /// <summary>
    /// 원격 콘텐츠 인덱스에서 새 팩을 받아 PackStore 에 설치한다 (검증 실패 시 기존 유지).
    /// 인덱스 형식: {"packs":[{"packId":"..","version":2,"url":"https://..","sha256":".."}]}
    /// 서버 주소는 설정 전까지 비활성(오프라인 전용).
    /// </summary>
    public sealed class ContentUpdater
    {
        public string IndexUrl = "";    // 예: https://example.com/sennen/index.json (미설정 = 비활성)
        public readonly List<string> Report = new List<string>();

        public IEnumerator CheckAndInstall(PackStore store, List<Pack> bundled, Action<int> onDone)
        {
            Report.Clear(); int applied = 0;
            if (string.IsNullOrEmpty(IndexUrl)) { Report.Add("업데이트 서버가 설정되지 않음 (오프라인 모드)"); onDone(0); yield break; }
            string indexText = null;
            using (var req = UnityWebRequest.Get(IndexUrl))
            {
                req.timeout = 15; yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Report.Add("인덱스 받기 실패: " + req.error); onDone(0); yield break; }
                indexText = req.downloadHandler.text;
            }
            Dictionary<string, object> idx;
            try { idx = J.Obj(MiniJson.Parse(indexText)); } catch (Exception e) { Report.Add("인덱스 형식 오류: " + e.Message); onDone(0); yield break; }
            var active = store.ReadActive();
            foreach (var o in J.List(idx, "packs"))
            {
                var d = J.Obj(o); string id = J.Str(d, "packId"); int ver = J.Int(d, "version"); string url = J.Str(d, "url");
                int cur; active.TryGetValue(id, out cur);
                foreach (var b in bundled) if (b.PackId == id) cur = Math.Max(cur, b.Version);
                if (ver <= cur || string.IsNullOrEmpty(url)) continue;
                using (var req = UnityWebRequest.Get(url))
                {
                    req.timeout = 30; yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { Report.Add(id + " 받기 실패: " + req.error); continue; }
                    var r = store.Install(Encoding.UTF8.GetString(req.downloadHandler.data), J.Str(d, "sha256"), bundled);
                    Report.Add(id + " v" + ver + ": " + (r.Ok ? "적용" : "거부 — " + r.Message + " " + string.Join("; ", r.Errors)));
                    if (r.Ok) applied++;
                }
            }
            onDone(applied);
        }
    }
}
