# Operation: Silent Night (noizy-night)

Кооперативная физическая стелс-комедия про эльфов Санты. Вы работаете в мастерской, берёте подарки, ныряете в дымоход и тащите их через спящий дом мимо Ворчливого Деда и Толстого Кота, ничего не разбудив (и разбив как можно меньше). Принцип: **fun first**. Сначала смешная неуклюжая физика (упал, врезался, споткнулся об игрушку, уронил подарок), потом всё остальное. Вид: пластилиновый, «Tactile Christmas Chaos».

Цикл: База Санты (Хаб) → доска заказов → погрузка подарков → крыши и дымоход → дом (стелс, шум, укрытия) → подарки под ёлку → возврат в камин → прокачка гаджетов на верстаке. Кооператив 1–4 игрока.

## Что в репозитории

| Часть | Где | Что это |
|---|---|---|
| **Unity 6 порт** | [`unity/SilentNight`](./unity/SilentNight) | **Основной продукт.** C#, URP, Input System, Cinemachine, NavMesh. |
| Веб-прототип | [`/src`](./src), [`/playground`](./playground) | Оригинальный прототип на Three.js + Rapier. Референс, не развивается. |
| Арт-пайплайн | [`art/blender`](./art/blender) | Скрипты Blender: подготовка пропов и риг моделей Tripo, экспорт FBX в Unity. |

## Как запустить

**Unity (основной):** Unity 6000.6.8f1, открыть папку `unity/SilentNight` через Unity Hub, меню `Silent Night ▸ Setup Project`, затем `Silent Night ▸ Build Greybox Scenes`, открыть `Assets/_Project/Scenes/Boot.unity` и нажать Play. Подробнее: [`unity/SilentNight/README.md`](./unity/SilentNight/README.md). Управление: WASD, мышь, Shift (бег), C (красться), Space (прыжок), E (взять/использовать), R (мешок), Tab (заказы); геймпад работает. Вторые и следующие игроки подключаются кнопкой Start/A на свободном геймпаде.

**Веб-прототип:** `npm install`, затем `npm run dev` (Vite). Игра на `http://localhost:3000/`, полигон физики на `/playground/`. Сборка: `npm run build`.

**Арт-скрипты** (нужен Blender в PATH), из корня репозитория:
```
blender -b -P art/blender/prep_prop.py -- <in.glb> <Name> <Room> <height_m> [tris=25000] [tex=1024]
blender -b -P art/blender/rig_tripo_elf.py -- <input.glb> [target_tris=30000]
blender -b -P art/blender/rig_cat.py -- <cat.glb> [target_tris=20000]
```
Подробный пайплайн (Gemini → Tripo → Blender → Unity): [`docs/AI_ASSET_PROMPTS.md`](./docs/AI_ASSET_PROMPTS.md).

## Документация

- [`unity/SilentNight/ARCHITECTURE.md`](./unity/SilentNight/ARCHITECTURE.md) — архитектура Unity-проекта.
- [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) — архитектура веб-прототипа (устарела как описание продукта).
- [`docs/COOP_PLAN.md`](./docs/COOP_PLAN.md) — план кооператива: локальный готов, сеть впереди.
- [`docs/AI_ASSET_PROMPTS.md`](./docs/AI_ASSET_PROMPTS.md), [`docs/AI_ENV_PROMPTS.md`](./docs/AI_ENV_PROMPTS.md) — промпты и пайплайн 3D-ассетов.
- [`DEV_DIARY.md`](./DEV_DIARY.md) — решения и дорожная карта.
- [`NIGHTLY_REPORT.md`](./NIGHTLY_REPORT.md) — отчёт ночной сессии.

## Текущий статус

Unity-версия играбельна и обставлена 3D-мебелью и декором: Хаб, крыши с дымоходами и 6-комнатный дом с Дедом, котом, подарками-механиками, процедурным звуком, локальным кооперативом до 4 игроков (join, «поднять друга», совместный велосипед, столкновения, ники) и подключённым **Unity-MCP** (`com.ivanmurzak.unity.mcp` `1.7.1`). Готовы модели эльфа, кота, верстака, камина, ёлки, мешка и игрушек (Tripo + Blender), а также расставлены **238 CC0-моделей из паков Kenney Furniture Kit и Kenney Holiday Kit** (диваны, кровати, кухонный гарнитур с техникой, шкафы с книгами и подарками, гирлянды, щелкунчики, снеговик, игрушечная железная дорога). Дед, сани, конвейер, доска заказов и подарки геймплейного заказа пока процедурные/блокаут. Всё проверено компиляцией и 38 PlayMode-автотестами + рендером скриншотов камер (`ScreenshotCapture`). Сетевого кооператива пока нет.

## Ветки

- `main` — основная ветка.
- `unity-rooftop-and-models` — рабочая ветка: Unity-порт, этап крыши, модели Tripo.
- `claude/overnight-dev` — ночная сессия (физика, NPC, звук, локальный кооператив); отчёт в `NIGHTLY_REPORT.md`.
