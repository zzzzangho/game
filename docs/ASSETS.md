# 그래픽 에셋 목록 (직접 그리실 파일)

파일을 아래 폴더에 **정확한 이름**으로 넣고 `make` 하면 바로 게임에 들어갑니다.
`assets/` 폴더는 git에 올라가지 않으니 원본은 따로 백업해 두세요.

- 기본값(`KMT_PIXEL=off`)은 **그린 그림을 그대로** 씁니다. 크기가 다르면 자동으로 맞추고, 색은 GBA 15비트 색으로 변환됩니다.
- PNG 권장. **인물·아이콘은 투명 배경 PNG**로 그리면 배경 위에 역전재판처럼 올라갑니다.
- 파일이 없는 항목은 임시 도트 그림이 대신 나옵니다.
- 미리보기: `make preview` → `build/preview/`

## 1. 인물 — `assets/portraits/` (128×144, 투명 PNG)

화면 위쪽 가운데에 그려지고, 아래 48px 정도(y 96~144)는 반투명 대사창에 덮입니다. **얼굴은 위쪽 2/3 안에** 두세요.
수첩(인물 파일)용 얼굴 사진(64×80)은 윗부분을 잘라 자동으로 만듭니다.

| 파일 | 인물 | 표정 차분 (같은 구도로) |
|---|---|---|
| `kin.png` | 김전일 | `kin_serious.png` 진지, `kin_surprised.png` 놀람 |
| `miyuki.png` | 미유키 | `miyuki_surprised.png` 놀람 |
| `saki.png` | 사키 류지 | |
| `kenmochi.png` | 켄모치 경부 | |
| `akechi.png` | 아케치 경시 | |
| `yamagami.png` | 젠틀 야마가미 | |
| `yumi.png` | 머메이드 유미 | |
| `sakonji.png` | 피에로 사콘지 | |
| `yurama.png` | 노블 유라마 | `yurama_angry.png` 화남 |
| `sakuraba.png` | 채널러 사쿠라바 | |
| `takato.png` | 타카토 (매니저, 안경) | `takato_surprised.png` 놀람, `takato_smile.png` 미소, `takato_shock.png` 정체가 드러난 순간(안경 벗음), `takato_cold.png` 차가운 미소(안경 벗음) |
| `satomi.png` | 잔마 사토미 (+로버트 인형) | |
| `nagasaki.png` | 나가사키 지배인 | |
| `mario.png` | 트네 마리오 (가면, 중절모) | |
| `clown.png` *(선택)* | 지옥의 광대 | 지금은 정체를 숨기려고 얼굴 없이 이름표만 나옵니다. 넣으려면 story.txt의 `@char ??? -`를 `@char ??? clown`으로 |
| `reiko.png` *(선택)* | 치카미야 레이코 (회상 장면) | 넣으려면 story.txt의 `@char 레이코 -`를 `@char 레이코 reiko`로 |

표정을 더 늘리려면 `<파일이름>_<표정>.png`로 그리고 대사에 `김전일[angry]: …`처럼 쓰면 됩니다.

## 2. 배경 — `assets/scenes/` (240×160 PNG, 선택)

모든 장소에 내장 일러스트(`tools/art_hd.py`)가 있습니다. 같은 이름의 그림을 넣으면 그 그림이 우선합니다.

아래 64px(y 96~160)는 대사창에 반쯤 덮이니, 중요한 것은 위쪽에 두세요.

| 파일 | 장소 |
|---|---|
| `title.png` | 타이틀 화면 (은유성호 야경) |
| `police.png` | 경시청 사무실 |
| `platform.png` | 우에노역 승강장 |
| `corridor.png` | 은유성호 객실 복도 |
| `stage.png` | 은유성호 이벤트 차량 (마술쇼) |
| `dining.png` | 은유성호 식당차 |
| `snowfield.png` | 눈밭에 멈춰 선 열차 (폭파 소동) |
| `cabin_roses.png` | 장미로 가득한 객실 + 쓰러진 단장 |
| `cabin_roses_hand.png` | 사키 비디오 정답 컷: 풍선에 묶여 뜬 왼손 |
| `cabin_smoke.png` | 사키 비디오: 연기로 하얘진 화면 |
| `cabin_empty.png` | 시체가 사라지고 장미만 남은 객실 |
| `hotel.png` | 시츠겐 호텔 로비 |
| `hotel_room.png` | 호텔 객실 (창밖 나무, 비취 장식) |
| `theater.png` | 스테이션 극장 무대 (마리오네트 의자, 천장 로프) |
| `swamp.png` | 안개 낀 밤의 습지 |
| `card_prologue.png` | 챕터 카드: 프롤로그 (밤의 경시청, 마리오네트 상자) |
| `card_ch1.png` | 챕터 카드: 제1장 (눈 내리는 밤, 고가교를 달리는 은유성호) |
| `card_ch2.png` | 챕터 카드: 제2장 (안개 낀 습지의 호텔과 극장) |
| `card_ch3.png` | 챕터 카드: 제3장 (눈보라, 불 켜진 창과 로프가 걸린 나무) |
| `card_final.png` | 챕터 카드: 최종장 (스포트라이트 속 마리오네트 의자) |

챕터 카드는 아래쪽 약 40px(y 118~160)에 제목 띠가 깔리니, 중요한 것은 위쪽에 두세요.
그림이 없는 배경과 증거물 아이콘은 내장 일러스트(`tools/art_hd.py`)가 대신 나옵니다.

## 2-1. 명장면 컷 — `assets/cuts/` (240×160 PNG, 애니 캡처)

평소 배경은 내장 일러스트를 쓰고, 이야기의 중요한 순간에만 애니 캡처가 번쩍 하며 나옵니다(`@cut`).
컷이 떠 있는 동안에는 인물 초상화를 그리지 않아 캡처가 가려지지 않습니다.
`python3 tools/import_screenshots.py`가 캡처 캐시에서 아래 파일을 잘라 만듭니다. 파일이 없으면 비슷한 내장 그림이 대신 나옵니다.

| 파일 | 장면 |
|---|---|
| `cut_parcel.png` | 프롤로그: 경시청에서 연 소포 |
| `cut_roses.png` | 제1장: 장미 속에 쓰러진 단장 (사키 비디오 화면에도 사용) |
| `cut_balloons.png` | 제1장: 사키 비디오 정답 컷, 풍선에 묶인 손 |
| `cut_mario.png` | 제2장: 체크아웃하는 가면의 손님 |
| `cut_body.png` | 제2장: 창고 짐 속의 시체 |
| `cut_marionette.png` | 제2장: 춤추는 살아있는 마리오네트 |
| `cut_yurama.png` | 제2장: 마리오네트 의자의 유라마 |
| `cut_fog.png` | 제2장: 안개 속을 걷는 사람 |
| `cut_sinking.png` | 제2장: 늪에 빠진 김전일 |
| `cut_jade.png` | 최종장: 묵직한 비취 원석 |
| `cut_bag.png` | 최종장: 이중 바닥 가방 |
| `cut_takato.png` | 최종장: 안경을 고쳐 쓰는 타카토 |
| `cut_reiko.png` | 최종장: 런던 공원의 레이코 |
| `cut_rock.png`, `cut_fire.png` | 엔딩: 떠오르는 바위, 불길 |

 — `assets/icons/` (64×64, 투명 PNG)

| 파일 | 쓰이는 증거물 |
|---|---|
| `puppet.png` | 뒤틀린 마리오네트, 마리오네트가 된 시체 |
| `letter.png` | 예고장, 유라마 앞 협박 편지 |
| `paper.png` | 마술쇼 진행표, 무대 뒤 통로, 침대 밑의 조커 |
| `train.png` | 폭파 예고, 연기 발생 장치 |
| `rose.png` | 장미의 객실 |
| `balloon.png` | 고무 조각 |
| `window.png` | 조금만 열리는 창문 |
| `chain.png` | 화장실 창틀의 자국 |
| `camera.png` | 사키의 비디오 |
| `bag.png` | 가방 검사 |
| `mask.png` | 트네 마리오 |
| `clipboard.png` | 숙박부의 이름, 지배인의 경력 |
| `clock.png` | 유라마의 시체 |
| `rope.png` | 천장의 로프, 창밖의 나무 |
| `scale.png` | 단원들의 몸무게 |
| `nail.png` | 5년 전의 못 |
| `jade.png` | 객실의 비취, 아래층 방의 비취 |
| `talk.png` | 증언류 (유미·사콘지·사토미·타카토의 증언, 직원의 증언) |

증거물마다 다른 그림을 쓰고 싶으면 story.txt의 `@evidence` 줄에서 아이콘 이름을 새 이름으로 바꾸고 그 이름의 PNG를 넣으면 됩니다.

## 직접 그린 캐릭터 원화

흰 배경의 상반신 원화는 `python3 tools/import_charart.py <폴더>`로 가져옵니다.
파일 이름은 `김전일.png`, `미유키.png`, `사쿠라바_무대분장.png`처럼 인물 이름으로 짓습니다.
흰 배경만 투명하게 오려서 `assets/portraits/<키>.png`로 저장합니다(git에는 올라가지 않음).
표정 파일(`kin_serious.png` 등)이 없으면 기본 원화가 대신 쓰입니다.

눈 깜빡임: 같은 그림에서 눈만 감은 `<키>_blink.png`(예: `kin_serious_blink.png`)가 있으면, 대사 중에 2~4초마다 약 0.1초씩 눈을 감습니다.
`python3 tools/import_blink.py <폴더>`는 `pairs/<표정>/open.png`, `closed.png` 짝에서 이 파일들을 만듭니다(현재 김전일만 해당).
ROM에는 바뀐 눈 부분만 작은 사각형으로 들어갑니다.

## 배경 그림 교체

`assets/scenes/<키>.png`(240×160 비율, 아무 크기)를 넣으면 그 배경이 그려 둔 그림 대신 쓰입니다.

| 키 | 장면 |
|---|---|
| darkroom | 프롤로그, 마리오네트를 비트는 어두운 방 |
| platform | 우에노역 승강장(밤) |
| berth | 은유성호 침대칸 |
| police | 경시청 사무실 |
| dining | 식당차 |
| freight_yard | 긴급 정차한 화물역(아침, 맞은편에 우편 열차) |
| corridor | 열차 복도 |
| cabin_roses / cabin_empty | 객실 5호(장미·시체 / 사라진 뒤) |
| train_toilet | 객실 옆 화장실 |
| country_station | 시코츠가하라역(해 질 녘) |
| hotel_exterior | 늪 한가운데 호텔 외관 |
| hotel / lobby_night | 호텔 로비(낮 / 밤) |
| mario_room | 트네 마리오의 방 |
| drawbridge | 연못 위 공연장과 올라간 도개교 |
| theater / catwalk / dressing_room | 공연장 무대 / 천장 발판 / 분장실 |
| swamp | 호텔 뒤 습지(밤, 안개) |
| hotel_room / sickroom | 호텔 방(밤 / 아침) |
| yumi_room / room_below | 유미의 방(창문이 열린 밤) / 아래층 빈 방 |
| airport | 5년 전 공항 로비(회상) |
| london_park | 런던 공원(회상) |
| prison | 아사히카와 구치소 독방 |
| magic_hall | 도쿄 공연장(사콘지 단독 공연) |
