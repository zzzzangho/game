using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SennenKazoku.EditorTools
{
    /// <summary>
    /// 명령줄 빌드: Unity -batchmode -quit -projectPath unity -executeMethod SennenKazoku.EditorTools.BuildScript.BuildAndroid -output build/sennen.apk
    /// 선행 조건: Unity 6000.3 LTS + Android Build Support(SDK/NDK/OpenJDK) 설치, 유효한 Unity 라이선스.
    /// </summary>
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("천년가족/프로젝트 설정 적용 (세로 고정)")]
        public static void ApplySettings()
        {
            PlayerSettings.companyName = "SennenKazokuFanProject";
            PlayerSettings.productName = "천년가족 (프로토타입)";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.example.sennenkazoku.proto");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.renderOutsideSafeArea = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EnsureScene();
            Debug.Log("프로젝트 설정을 적용했습니다.");
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        public static void BuildAndroid()
        {
            ApplySettings();
            string output = "build/sennen-kazoku-proto.apk";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-output") output = args[i + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            EditorUserBuildSettings.buildAppBundle = false;
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output, target = BuildTarget.Android, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("빌드 결과: " + report.summary.result + " (" + report.summary.totalSize + " bytes) → " + output);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
