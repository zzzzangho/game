#!/usr/bin/env bash
# 안드로이드 테스트 APK 빌드 (Docker + GameCI Unity 이미지). 원작 자료(LocalArt·LocalOrig·LocalPacks)는 이 컴퓨터에 있는 것을 그대로 넣는다 —
# 결과 APK 는 본인 테스트용이다. 공개·배포·저장소 업로드 금지.
#
#   UNITY_EMAIL=... UNITY_PASSWORD=... tools/build_android.sh [출력 apk]
#
# 라이선스: Unity Personal 계정(UNITY_EMAIL · UNITY_PASSWORD, 2단계 인증 끔)으로 컨테이너 안에서 활성화하고 끝나면 반납한다.
#   UNITY_LICENSE(Unity_lic.ulf 내용)가 있으면 그 파일을 쓰고, UNITY_SERIAL 이 있으면 Pro/Plus 시리얼로 활성화한다.
# 이미지: unityci/editor:ubuntu-<ProjectVersion>-android-3 (약 8GB). Docker 데몬이 꺼져 있으면 dockerd 를 띄운다.
set -euo pipefail
HERE="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$HERE/build/sennen-kazoku-test.apk}"
VER="$(sed -n 's/^m_EditorVersion: //p' "$HERE/unity/ProjectSettings/ProjectVersion.txt")"
IMAGE="unityci/editor:ubuntu-${VER}-android-3"

if [ -z "${UNITY_EMAIL:-}" ] || [ -z "${UNITY_PASSWORD:-}" ]; then
  echo "UNITY_EMAIL · UNITY_PASSWORD 환경 변수가 필요합니다." >&2; exit 2
fi
if ! docker info >/dev/null 2>&1; then
  (dockerd >/tmp/dockerd.log 2>&1 &)
  for _ in $(seq 1 30); do docker info >/dev/null 2>&1 && break; sleep 1; done
fi
docker image inspect "$IMAGE" >/dev/null 2>&1 || docker pull "$IMAGE"

for d in LocalArt LocalOrig; do
  [ -d "$HERE/unity/Assets/Resources/$d" ] || echo "주의: unity/Assets/Resources/$d 가 없습니다 — 원작 그림/규칙 없이 빌드됩니다." >&2
done
mkdir -p "$(dirname "$OUT")"
OUT_ABS="$(cd "$(dirname "$OUT")" && pwd)/$(basename "$OUT")"

docker run --rm \
  -e UNITY_EMAIL -e UNITY_PASSWORD -e UNITY_LICENSE -e UNITY_SERIAL \
  -v "$HERE/unity:/project" -v "$(dirname "$OUT_ABS"):/out" \
  "$IMAGE" bash -c '
set -uo pipefail
LOG=/out/unity_build.log; : > "$LOG"
cred=(-username "$UNITY_EMAIL" -password "$UNITY_PASSWORD")
if [ -n "${UNITY_LICENSE:-}" ]; then
  mkdir -p ~/.local/share/unity3d/Unity && printf "%s" "$UNITY_LICENSE" > ~/.local/share/unity3d/Unity/Unity_lic.ulf
elif [ -n "${UNITY_SERIAL:-}" ]; then
  unity-editor -batchmode -nographics -quit -logFile - -serial "$UNITY_SERIAL" "${cred[@]}" >> "$LOG" 2>&1
else
  unity-editor -batchmode -nographics -quit -logFile - "${cred[@]}" >> "$LOG" 2>&1
fi
unity-editor -batchmode -nographics -quit -logFile - -projectPath /project -buildTarget Android \
  -executeMethod SennenKazoku.EditorTools.BuildScript.BuildAndroid -output /out/'"$(basename "$OUT_ABS")"' >> "$LOG" 2>&1
code=$?
[ -z "${UNITY_LICENSE:-}" ] && unity-editor -batchmode -nographics -quit -logFile - -returnlicense "${cred[@]}" >> "$LOG" 2>&1
grep -E "빌드 결과|error CS|Build Failed|LICENSE SYSTEM|No valid Unity Editor license" "$LOG" | tail -20
exit $code
'
echo "APK: $OUT_ABS"
