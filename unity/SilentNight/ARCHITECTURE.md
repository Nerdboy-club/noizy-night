# Silent Night — архитектура Unity-проекта

**Стек (по `Packages/manifest.json`):** Unity 6000.6.8f1, URP 17.6.0, Input System 1.20.1 (бинды строятся в коде, `.inputactions` нет), Cinemachine 6.6.0, AI Navigation 2.0.16 (NavMesh), UI Toolkit/uGUI-примитивы, Test Framework 1.8.0, Unity-MCP (`com.ivanmurzak.unity.mcp` 1.7.1, OpenUPM). Multiplayer Center 2.1.0 стоит, сетевого кода нет.

Asmdef у рантайма нет: скрипты идут в Assembly-CSharp, `Editor/` — в editor-сборку. Исключение — `Tests/PlayMode/SilentNight.PlayModeTests.asmdef`.
Namespace на папку: `SilentNight.Core`, `.Player`, `.Stealth`, `.Gifts`, `.UI`, `.Audio`, `.FX`, `.Rooftop`, `.EditorTools`.

## Правила модулей
1. Модули общаются **только через `SilentNight.Core`** (GameEvents, GameServices, интерфейсы). Не ссылаться на конкретные классы чужого модуля, не делать `FindObjectOfType` чужого класса. Исключения: Editor-сборщик сцен, тесты и мелкие статические реестры внутри модуля (`CatnipPouch.Active`, `ElfInput.All`).
2. Core трогать нельзя — **правило сознательно ослаблено**. Для кооператива и крыш в Core добавили: `GameState.Rooftop`, `GiftKind.Fragile` и `GiftKind.RcCar`, `GameServices.Players` (список эльфов; `LocalPlayer` остался), `GameEvents.RequestEnterHouse`. Новые правки Core — только осознанные и с записью в отчёте.
3. Каждый визуал работает на **примитивах-заглушках** и позволяет подставить модель: `[SerializeField] GameObject modelPrefab`, null = заглушка. Аниматоры необязательны (null-safe).
4. Все настройки — `[SerializeField]` с дефолтами из three.js-прототипа, сборщик сцен просто делает `AddComponent<>()`.
5. Отписка от событий в `OnDisable/OnDestroy`. Без аллокаций в `Update` (стелс-проверки идут через `StealthUtil`).
6. Курсор: GameFlow лочит его в Hub/Rooftop/House; UI-панели во время игры разблокируют и возвращают лок.
7. Игровой текст на русском, код и комментарии на английском.

## Сцены и поток
`Boot` (GameFlow, AudioManager, UIRoot; живут всегда) -> `Hub` (мастерская: конвейер, сани, доска заказов, верстак) -> `Rooftop` (крыши с трубами, паркур) -> `House` (комнаты: Дед, кот, ёлка) -> результаты -> `Hub`.
Поток: сани загружены -> `RequestStartDelivery` -> Rooftop; [E] у правильной трубы (номер из адреса заказа, выбирает `RooftopLevelController`) -> `RequestEnterHouse` -> House; доставка под ёлку или поимка -> Results/GameOver -> `RequestReturnToHub`.
Сцены генерирует `GreyboxSceneBuilder` (Silent Night > Build Greybox Scenes) и руками не правятся: правки вносятся в сборщик, сцены пересобираются.

## Модули
| Папка | Ключевые классы |
|---|---|
| `Core` | `GameEnums` (GameState, HouseMood, GiftKind, NoiseSource, SurfaceType, UpgradeType, AlertLevel), `NoiseEvent` (громкость с затуханием), `Interfaces` (IInteractable, IInteractor, ICarryable, ICarrier, IPlayerStatus), `Order` (+генератор, включает Fragile/RcCar), `PlayerProfile` (чаевые, апгрейды, мята, JSON-сейв), `GameEvents` (шина: Noise, StateChanged, GrandpaAlertChanged, Gift*, Objective, Toast, PlayerCaught, DeliveryCompleted, Request*), `GameServices`, `GameFlow` (стейт-машина + загрузка сцен), `GameConstants`, `SurfaceTag` |
| `Player` | `ElfController` (CharacterController: ходьба/спринт/присед/прыжок, стамина, ранец, поверхности, шум шагов, удар о стену `Bumped`, мантл на края 0.35–1.0 м, толкание лёгких тел, проверка габарита «Huge»-велосипеда), `ElfInput` (ввод в коде; индекс игрока и фильтр устройств: игрок 1 = клавиатура/мышь, игроки 2–4 = только свой геймпад; `DebugMoveOverride` для тестов), `ElfCarry` (руки + мешок, Huge замедляет), `ElfInteractor`, `ElfAnimator`, `ElfVisual`, `ElfRagdoll`, `ThirdPersonCameraRig` (Cinemachine, в коопе кадрирует группу), `PlayerSetup`/`PlayerGate`, `FallRespawn`, `TripHazard` (игрушки-ловушки), `CatnipThrower` (мята, [F]/LB). **Кооп:** `LocalCoopJoin` (Start/A на свободном геймпаде = новый эльф; состав переживает смену сцен; отключение геймпада убирает эльфа), `ElfBonk` (лбами при сближении >= 3 м/с, медленнее — толкаются), `ElfHelpUp` ([E] над упавшим), `CoCarry` + `ElfCoCarryHandle` (вдвоём за велосипед, верёвочное ограничение, застревание в проёмах), `ElfNameTag` («Эльф N», «Помогите! [E]», «несём вместе») |
| `Stealth` | `Grandpa` (NavMesh AI: сон, слух, поиск с осмотром, погоня, поимка; в коопе выкидывает пойманного в камин), `VisionCone`, `FatCat` (спит/бродит, реагирует на шум, орёт при наступании на хвост, идёт на мяту), `NoiseZone` (скрипучий пол), `HidingSpot` (укрытие под мебелью, нужен присед), `CreakyDoor` (физическая дверь: тихо / скрип ~30 / БАХ 65), `NoisyTV` + `TvRemote` (упавший пульт включает ТВ, шум 55, выключить [E]), `GrandpaSnore` (3D-храп), `GrandpaAlertIndicator` (Zzz/…/?/??/! и реплика, что услышал), `StealthUtil` (ближайший/любой эльф без аллокаций), `NPCFactory`, `NPCVisual`, `NavMover` |
| `Gifts` | `Gift` (ICarryable; Fragile — снежный шар бьётся от удара >= 3.4 м/с; музыкальная шкатулка заводится ~35 с и звенит, в руках ключ подкручивается), `RcCarDriver` (машинка сама уезжает и шумит ~7 с), `GiftFactory`, `GiftVisual`, `Conveyor`, `SleighLoadZone`, `TreeDropZone`, `ChimneyExit`, `OrderBoardStation`, `WorkbenchStation`, `HouseGiftSpawner`, `HouseMoodController`, `InteractableBase` |
| `Rooftop` | `RooftopLevelController` (выбирает трубу по адресу, нумерует 15 труб, ставит цель), `RooftopChimney` ([E] нырнуть; правильная грузит House, неправильная даёт подсказку; сажа), `SnowCushion` (сугроб гасит падение), `IceSlope` (ледяной скат стаскивает вниз), `Billboard` (подпись к камере) |
| `FX` | `FxUtil` (общие материалы, шейдер частиц ищется по имени), `SootPuff`, `ChimneySmoke`, `LandingDust` |
| `Audio` | `AudioManager` (музыка, 2D/3D SFX; клипы из `Resources/Audio` приоритетнее синтеза), `ProceduralSfx` (весь звук синтезируется кодом: шаги по поверхностям, удары, стекло, шкатулка, кот, джингл, музыка-колокольчики) |
| `UI` | `UIRoot`, `UIView`, `UIShapes`, `MainMenuView`, `HudView` (плашка «в руках», счётчик мяты), `OrderBoardView`, `WorkbenchView`, `ResultsView` (звание и смешные строки), `GameOverView`, `PauseView`, `RunStats` (статистика рейса: громкий звук, пробуждения Деда, падения, время) |
| `Editor` | `ProjectBootstrap`, `GreyboxSceneBuilder`, `AnimatorControllerBuilder`, `MixamoImportPostprocessor`, `EnvironmentMaterialFixer` |

## Активный рэгдолл (эльф)
- **`ElfController` — единственный авторитет геймплея** (позиция, скорость, стамина, шум, поверхности). Корень — CharacterController.
- **`ElfRagdoll` — кукла-«puppet»**: 11 частей на Rigidbody + ConfigurableJoint со slerp-приводами, таз удерживается пружиной на высоте, походка процедурная. Кукла следует за контроллером и на него не влияет (тест: отставание 0.16 м при ходьбе).
- **Привязка модели:** `modelPrefab` — скинненый эльф (Tripo FBX, кости с именами Mixamo), кости линкуются к частям куклы. В редакторе дефолтный `Elf_Tripo` подгружается через AssetDatabase; кооп-эльфы в билде берут модель у эльфа сцены.
- **Limp/get-up:** `GoLimp()` / `GoLimp(push)` (жёсткое приземление, удар о стену, игрушка, столкновение, [G]) -> эльф обмякает, роняет подарок -> встаёт сам (~1.4 с соло, 4 с в коопе, сразу после `ElfHelpUp`). Повторные вызовы в одном кадре схлопываются.

## Арт-пайплайн
- `art/blender/*.py` (запуск `blender -b -P script.py -- args`): `prep_prop.py` (Tripo/Meshy GLB -> децимация, масштаб по высоте, пивот в центре пола, FBX + `<Name>_Textures` в `Art/Environment/<Room>/`), `rig_tripo_elf.py` (риг с именами Mixamo + процедурные клипы), `rig_cat.py`, `pose_cat.py`, `build_elf.py`, `build_props.py`, `prep_ornaments.py`, `make_textures.py` (бесшовные текстуры в `Art/Environment/Textures`), `preview*.py`, `pose_check.py`.
- Цепочка: Tripo GLB -> `prep_prop`/`rig_*` -> FBX в `Art/` -> Silent Night > **Fix Environment Materials** (`EnvironmentMaterialFixer`: URP-материалы, текстуры) -> **Build Animator Controllers** -> **Build Greybox Scenes** (сборщик ставит модели, иначе заглушки).
- Паки CC0: `Art/Environment/Packs/KenneyFurniture`, `KenneyHoliday`. Персонажи: `Art/Characters/{Elf,Cat,Grandpa}` (у Деда пока только контроллер, без модели).
- Промпты для ИИ: `docs/AI_ASSET_PROMPTS.md`, `docs/AI_ENV_PROMPTS.md`.

## Тесты
`Tests/PlayMode` (NUnit, asmdef `SilentNight.PlayModeTests`): `SceneSmokeTests`, `GiftMechanicsTests`, `AudioTests`, `CatnipTests`, `CoopFoundationTests` (виртуальный геймпад), `RunStatsTests`. По ночному отчёту проходили 38 тестов. `ScreenshotCapture` помечен `[Explicit("Visual review only")]`: в обычный прогон не входит, пишет PNG в `SN_SHOT_DIR` или `<project>/Screenshots`.
Batchmode (проект закрыт в редакторе):
```
Unity.exe -batchmode -projectPath unity/SilentNight -runTests -testPlatform PlayMode -testResults results.xml -logFile test.log
Unity.exe -batchmode -projectPath unity/SilentNight -runTests -testPlatform PlayMode -testFilter SilentNight.Tests.ScreenshotCapture
Unity.exe -batchmode -quit -projectPath unity/SilentNight -executeMethod SilentNight.EditorTools.GreyboxSceneBuilder.BuildAll
```
В batchmode сборщик сцен пропускает запечку освещения. Скриншот-тест запускать без `-nographics`.

## Известные ограничения / TODO
- Сетевого кооператива нет (только локальный до 4 игроков, план в `docs/COOP_PLAN.md`, шаги 7+); нет HUD на каждого игрока.
- Нет моделей Деда, саней, конвейера, доски заказов, подарков — серые заглушки.
- Звук синтезирован, на слух не проверен; шейдер частиц нужно добавить в Always Included Shaders для билда.
- Панель верстака и экран результатов могут не влезать на 1080p.
- Руками не проверены: ощущение управления, эффекты, поведение кота (NavMesh), проход велосипеда через двери, совместная переноска.
- Батут-навес не сделан; укрытие спасает от погони лишь частично; нет Крампуса и «притвориться игрушкой».
