# 그래픽 에셋 목록 (직접 그리실 파일)

파일을 아래 폴더에 **정확한 이름**으로 넣고 `make` 하면 바로 게임에 들어갑니다.
`assets/` 폴더는 git에 올라가지 않으니 원본은 따로 백업해 두세요.

- 기본값(`KMT_PIXEL=off`)은 **그린 그림을 그대로** 씁니다. 크기가 다르면 자동으로 맞추고, 색은 GBA 15비트 색으로 변환됩니다.
- PNG 권장. **인물·아이콘은 투명 배경 PNG**로 그리면 배경 위에 역전재판처럼 올라갑니다.
- 파일이 없는 항목은 임시 도트 그림이 대신 나옵니다.
- 미리보기: `make preview` → `build/preview/`

## 1. 인물 — `assets/portraits/` (128×144, 투명 PNG)

화면 위쪽 가운데에 그려지고, 아래 40px 정도는 반투명 대사창에 덮입니다. **얼굴은 위쪽 2/3 안에** 두세요.
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
| `clown.png` | 지옥의 광대 | |
| `reiko.png` *(선택)* | 치카미야 레이코 (회상 장면) | 넣으려면 story.txt의 `@char 레이코 -`를 `@char 레이코 reiko`로 |

표정을 더 늘리려면 `<파일이름>_<표정>.png`로 그리고 대사에 `김전일[angry]: …`처럼 쓰면 됩니다.

## 2. 배경 — `assets/scenes/` (240×160 PNG)

아래 56px는 대사창에 반쯤 덮이니, 중요한 것은 위쪽에 두세요.

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

## 3. 증거물 아이콘 — `assets/icons/` (64×64, 투명 PNG)

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
