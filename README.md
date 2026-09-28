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
- **수첩**: 모은 증거물(아이콘+설명)과 만난 인물(초상화+프로필)을 볼 수 있습니다.
- **저장**: 각 장(챕터)이 시작될 때 자동 저장됩니다. 타이틀에서 `이어하기`로 그 장의 처음부터 다시 할 수 있습니다(배드 엔딩 후 재도전에도 사용).

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
그냥 문장                 ← 나레이션
*라벨                     ← 점프 목적지

@char 이름 초상화키 #색     인물 선언 (초상화키: kin miyuki kenmochi yamagami reika kuroki okada izumi takato puppet, 없으면 -)
@profile 이름 "설명"        수첩의 인물 프로필
@evidence ID 아이콘 "이름" "설명"   증거물 정의
@meet 이름                  인물 파일 추가 (팝업)
@get ID                     증거물 입수 (팝업)

@chapter "제목\n부제"       챕터 카드 + 자동 저장
@scene 배경키               배경 전환 (black title platform corridor stage cabin cabin_crime dining baggage snowfield)
@fx flash|shock|shake|red   화면 효과
@shout 이름 "대사"          큰 글씨 외침 연출 (예: 수수께끼는 모두 풀렸어!)
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
