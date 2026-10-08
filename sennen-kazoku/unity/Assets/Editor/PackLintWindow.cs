using System.Collections.Generic;
using System.IO;
using SennenKazoku.Core;
using UnityEditor;
using UnityEngine;

namespace SennenKazoku.EditorTools
{
    /// <summary>이벤트 팩 JSON 검사·미리보기 창: 메뉴 천년가족 > 이벤트 팩 검사.</summary>
    public sealed class PackLintWindow : EditorWindow
    {
        string path = ""; string eventId = ""; string report = ""; Vector2 scroll;

        [MenuItem("천년가족/이벤트 팩 검사·미리보기")]
        static void Open() { GetWindow<PackLintWindow>("이벤트 팩 검사"); }

        void OnGUI()
        {
            EditorGUILayout.LabelField("팩 JSON 파일", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            path = EditorGUILayout.TextField(path);
            if (GUILayout.Button("선택", GUILayout.Width(50))) path = EditorUtility.OpenFilePanel("팩 JSON", Application.dataPath, "json");
            EditorGUILayout.EndHorizontal();
            eventId = EditorGUILayout.TextField("미리보기 이벤트 id", eventId);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("검사")) Run(false);
            if (GUILayout.Button("검사 + 미리보기")) Run(true);
            EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void Run(bool preview)
        {
            if (!File.Exists(path)) { report = "파일이 없습니다."; return; }
            var baseline = new List<Pack>();
            foreach (var ta in Resources.LoadAll<TextAsset>("BundledPacks"))
            { var e = new List<string>(); var p = Pack.Load(ta.text, e); if (p != null) baseline.Add(p); }
            string json = File.ReadAllText(path);
            var rep = PackLint.Check(json, baseline);
            report = rep.Count == 0 ? "검사 통과" : string.Join("\n", rep) + (PackLint.HasErrors(rep) ? "\n\n검사 실패" : "\n\n검사 통과 (경고 있음)");
            if (preview && !PackLint.HasErrors(rep) && eventId.Length > 0) report += "\n\n" + PackLint.Preview(json, baseline, eventId);
        }
    }
}
