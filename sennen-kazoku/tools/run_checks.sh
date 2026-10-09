#!/usr/bin/env bash
# 코어 테스트 + 샘플 팩 검사. 사전 조건: dotnet SDK 8
set -e
cd "$(dirname "$0")/.."
dotnet run --project tests/Core.Tests -v q
dotnet run --project tools/PackLintCli -v q -- unity/Assets/Resources/BundledPacks tests/Core.Tests/Fixtures/nova.pack001.json --preview nova.picnic.001
# 앱 스크립트(Unity) 컴파일 검사 — UnityEngine 빈 껍데기로 형식만 확인한다(실행 확인 아님)
dotnet build tools/UnityStubCheck -v q
