# 소년탐정 김전일 — 마술열차 살인사건 (비공식 GBA 팬 게임)

「소년탐정 김전일」의 **마술열차 살인사건**을 모티브로 한 게임보이 어드밴스(GBA)용 추리 어드벤처입니다.
대사 중심으로 스토리를 진행하고, 증거를 모은 뒤, 중간 추리와 최종 추리로 범인을 밝혀냅니다.

> 비공식 2차 창작물입니다. 원작의 모든 권리는 원작자와 출판사에 있습니다.
> 시나리오는 원작의 설정(마술열차, 마술단, 지옥의 꼭두각시)을 바탕으로 새로 쓴 팬 창작이며, 조연 인물 일부와 트릭은 오리지널입니다.

## 플레이 방법

`kindaichi_magic_train.gba`를 mGBA 같은 에뮬레이터나 GBA 플래시 카트리지로 실행하세요.

| 버튼 | 기능 |
|---|---|
| A | 대사 넘기기 / 선택 / 증거 제시 |
| B | 수첩 닫기, 대사 빨리 표시 |
| START | **수첩**(역전재판식 법정기록) 열기 — 대화 중이나 선택지에서 언제든 |
| L / R (또는 ←/→) | 수첩에서 `증거물` ↔ `인물` 탭 전환 |
| ↑ / ↓ | 커서 이동 |

### 게임 흐름

```
프롤로그 (인물 소개)
  → 제1장: 사건 발생 → 증거 수집 → 중간 추리 ①
  → 제2장: 두 번째 사건 → 증거 수집 → 중간 추리 ②
  → 최종장: 범인 지목 → 증거 제시로 범인 몰아붙이기 → TRUE END
```

- **목숨(♥ 5개)**: 추리 선택지나 증거 제시를 틀릴 때마다 하나씩 줄어듭니다. 0이 되면 `BAD END: 추리 실패`.
- **범인 지목**: 잘못된 사람을 지목하면 인물별 배드 엔딩(4종)으로 갑니다.
- **증거 수집**: 조사 장소를 골라 살펴보고, 필요한 증거를 모두 찾아야 "조사를 마친다"가 가능합니다.
- **수첩**: 모은 증거물(아이콘+설명)과 만난 인물(얼굴+프로필)을 볼 수 있습니다.
  인물 파일은 그 인물과 **처음 대화한 직후** 자동으로 추가되고, 사건이 진행되면 프로필이 갱신됩니다.
- **저장**: 각 장(챕터)이 시작될 때 자동 저장됩니다. 타이틀에서 `이어하기`로 그 장의 처음부터 다시 할 수 있습니다(배드 엔딩 후 재도전에도 사용).

## 그림 바꾸기 (실제 인물 이미지 넣기)

기본으로 들어 있는 도트 그림은 임시 그림입니다. 아래 폴더에 이미지 파일을 넣고 `make` 하면 자동으로 교체됩니다.
크기는 상관없고(PNG/JPG/WEBP), 빌드할 때 GBA에 맞게 줄이고 색을 변환합니다.

| 폴더 | 파일 이름 | 변환 결과 |
|---|---|---|
| `assets/portraits/` | `kin.png`, `miyuki.png`, `kenmochi.png`, `yamagami.png`, `reika.png`, `kuroki.png`, `okada.png`, `izumi.png`, `takato.png`, `puppet.png` | 128×144 상반신. 화면 가운데, 대사창 뒤에 표시. 수첩용 얼굴(64×80)은 윗부분을 잘라 자동 생성 |
| `assets/portraits/` | `kin_surprised.png` 같은 `<초상화>_<표정>.png` | 표정 차분 (아래 참고) |
| `assets/scenes/` | `platform.png`, `corridor.png`, `stage.png`, `cabin.png`, `cabin_crime.png`, `dining.png`, `baggage.png`, `snowfield.png`, `title.png` | 240×160으로 꽉 차게 잘라서 사용. 새 이름으로 넣으면 `@scene 새이름`으로 쓸 수 있는 새 배경 |
| `assets/icons/` | `letter.png`, `puppet.png`, `chain.png`, `watch.png` 등 (story.txt의 `@evidence` 아이콘 이름) | 64×64 증거물 아이콘 |

- **배경이 투명한 PNG를 권장합니다.** 투명하지 않은 이미지는 테두리의 단색 배경을 자동으로 지웁니다. 배경이 복잡하면 미리 지워 주세요.
- **표정**: 스크립트에서 `이름[표정]: 대사`라고 쓰면 `<초상화>_<표정>.png`를 사용합니다. 파일이 없으면 기본 초상화를 쓰고, 빌드할 때 어떤 파일이 비어 있는지 알려 줍니다.
  지금 스크립트에서 쓰는 표정: `kin_surprised`, `kin_serious`, `miyuki_surprised`, `kenmochi_surprised`, `kenmochi_angry`, `yamagami_angry`, `reika_sad`, `kuroki_scared`, `izumi_angry`, `takato_smile`, `takato_shock`, `takato_cold`
- `make preview` → `build/preview/`에서 변환 결과를 미리 볼 수 있습니다.
- 용량: GBA 롬은 하드웨어상 최대 **32MB**입니다. 상반신 1장이 약 37KB, 배경 1장이 약 77KB라서 표정을 많이 넣어도 충분합니다.
- 원작 그림을 넣을 경우, 저작권 때문에 저장소를 공개하거나 롬을 배포하는 데 주의하세요.

## 빌드

필요한 것: `arm-none-eabi-gcc`(Ubuntu: `gcc-arm-none-eabi`), Python 3 + Pillow, 호스트 C 컴파일러.
devkitPro 없이 빌드됩니다.

```sh
make            # → kindaichi_magic_train.gba
make test       # PC에서 엔진을 돌려 모든 엔딩/게임오버/저장-이어하기를 자동 검증
make preview    # 배경·초상화·아이콘 미리보기 PNG → build/preview/
```

## 구조

```
story/story.txt         스토리 스크립트 (대사, 증거, 인물, 추리, 엔딩 전부 여기)
tools/build_assets.py   스크립트 컴파일러: 스크립트 + 폰트 + 그림 → build/gen_data.c
tools/art.py            배경/초상화/증거 아이콘 도트 그림 (코드로 생성)
src/main.c              게임 엔진 (텍스트, 수첩, 선택지, 추리, 저장)
src/plat_gba.c          GBA 하드웨어 계층 (Mode 3 화면, 입력, 효과음, SRAM)
src/crt0.s, src/gba.ld  시작 코드 / 링커 스크립트
test/                   PC용 테스트 하네스와 자동 플레이 테스트
assets/fonts/           갈무리11 Condensed (BDF)
```

## 스토리 스크립트 문법 (`story/story.txt`)

대사를 고치거나 새 장면을 추가하려면 이 파일만 수정하고 `make` 하면 됩니다.
텍스트는 자동 줄바꿈되고, 한 페이지(3줄)를 넘으면 자동으로 다음 페이지로 나뉩니다.
폰트에 없는 글자나 너무 긴 선택지는 빌드할 때 줄 번호와 함께 에러로 알려 줍니다.

```
# 주석
김전일: 대사              ← @char로 선언한 이름이면 이름표+초상화와 함께 대사
김전일[surprised]: 대사   ← 표정 차분 (assets/portraits/kin_surprised.png)
그냥 문장                 ← 나레이션
*라벨                     ← 점프 목적지

@char 이름 초상화키 #색     인물 선언 (초상화키: kin miyuki kenmochi yamagami reika kuroki okada izumi takato puppet, 없으면 -)
@profile 이름 "설명"        수첩의 인물 프로필 (처음 대사를 한 뒤 자동으로 수첩에 추가)
@profile_update 이름 "설명" 프로필 갱신 (팝업)
@evidence ID 아이콘 "이름" "설명"   증거물 정의
@meet 이름                  대사 없이 인물 파일만 추가하고 싶을 때
@get ID                     증거물 입수 (팝업)

@chapter "제목\n부제"       챕터 카드 + 자동 저장
@scene 배경키               배경 전환 (black title platform corridor stage cabin cabin_crime dining baggage snowfield)
@fx flash|shock|shake|red   화면 효과
@shout 이름[표정] "대사"    큰 글씨 외침 연출 (예: 수수께끼는 모두 풀렸어!)
@lives 5                    목숨 설정
@gameover 라벨              목숨이 0이 되면 갈 곳
@goto 라벨 / @return        점프 / 조사·오답 서브루틴에서 돌아가기
@wait 프레임수

@investigate                조사 파트
@spot "장소 이름" 라벨      (라벨의 내용을 실행하고 @return으로 복귀)
@need 증거ID ...            조사 종료에 필요한 증거
@end

@ask 이름 "질문"            객관식 추리 (틀리면 목숨 -1, 해설 라벨 실행 후 재도전)
@opt "선택지" ok            정답
@opt "선택지" 오답라벨
@end

@present 이름 "질문" 증거ID 오답라벨    수첩에서 증거 제시
@accuse 이름 "질문"         범인 지목 (선택지마다 다른 라벨로 분기)
@opt "인물" 라벨
@end
@ending bad|true "엔딩 제목"
```

## 크레딧 / 라이선스

- 폰트: [갈무리(Galmuri)](https://github.com/quiple/galmuri) 11 Condensed — © Lee Minseo, SIL Open Font License 1.1 (`assets/fonts/`)
- `tools/gbafix.c`: devkitPro GBA ROM fixer (LGPL)
- 원작: 「소년탐정 김전일」 — 이 게임은 비영리 팬 창작물입니다.
