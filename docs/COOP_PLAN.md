# Operation: Silent Night: план перехода на кооператив (1–4 игрока)

> ## Статус на 2026-11-07 (ветка `claude/overnight-dev`)
>
> | Шаг | Что | Статус |
> |---|---|---|
> | 0 | Реестр игроков `GameServices.Players`, `StealthUtil` | ГОТОВО (ит. 7) |
> | 1 | Локальный join (Start/A на геймпаде), камера на всех, отключение геймпада | ГОТОВО (ит. 11) |
> | 6 | «Поднять друга» [E] | ГОТОВО (ит. 12) |
> | 6 | Совместная переноска велосипеда (+ застревание в проёмах) | ГОТОВО (ит. 13, 14) |
> | 6 | Столкновения эльфов («бонк») | ГОТОВО (ит. 16) |
> | 3 | Состав команды переживает смену сцен | ГОТОВО (ит. 17) |
> | 5 | Подписи-ники над эльфами («Эльф N», «Помогите! [E]») | ГОТОВО (ит. 20) |
> | 4 | Поимка одного эльфа = выкинут в камин, проигрыш при поимке последнего | ГОТОВО (ит. 15) |
> | 5 | Отдельный HUD для каждого игрока | TODO |
> | 2 | Опция split-screen | TODO |
> | 7+ | Сеть (Netcode, LAN, Steam) | TODO |
>
> Проверено только автотестами и компиляцией, не руками. Строки таблицы ниже с пометкой **РЕШЕНО** / **ЧАСТИЧНО** уже закрыты кодом, остальное актуально.

Проект: `unity/SilentNight` (Unity **6000.6.8f1**, URP 17.6, Input System 1.20.1, Cinemachine 6.6.0, AI Navigation 2.0.16, Multiplayer Center 2.1.0 уже стоит).
Всё ниже основано на чтении текущего кода. Код этим документом не менялся.

> Важно: `ARCHITECTURE.md`, правило 2, запрещает трогать `Scripts/Core/`. Без изменения контрактов Core кооператив не сделать, поэтому шаг 0 надо согласовать с лидом.

---

## 1. Что сейчас завязано на одного игрока

| Файл | Член / место | Проблема |
|---|---|---|
| `Core/GameServices.cs` | `LocalPlayer`, `RegisterLocalPlayer`, `UnregisterLocalPlayer` | Один слот. Второй `ElfController.OnEnable` перезаписывает первого, а `OnDisable` любого из них обнуляет слот. Списка игроков нет. **[РЕШЕНО (ит. 7): `GameServices.Players`, `LocalPlayer` работает как раньше]** |
| `Core/GameServices.cs` | `UIModalOpen` (bool) | Глобальный флаг: если один игрок открыл доску заказов, управление теряют все. |
| `Core/Interfaces.cs` | `IPlayerStatus` | Нет `PlayerIndex`, `IsDowned`, владельца или клиента. `ICarrier` рассчитан на один предмет в руках одного эльфа. |
| `Core/GameEvents.cs` | `PlayerCaught(string)` | Не говорит, *кого* поймали. Сразу ведёт к GameOver. **[ЧАСТИЧНО (ит. 15): проигрыш только при поимке последнего эльфа]** |
| `Core/GameFlow.cs` | `OnPlayerCaught` → `SetState(GameOver)`; `TogglePause` → `Time.timeScale = 0`; `LoadScene` → `SceneManager.LoadSceneAsync`; `SetState` → `Cursor.lockState` | Одна поимка заканчивает забег. Пауза останавливает время для всех (онлайн так нельзя). Сцены грузятся локально, без сети. **[ЧАСТИЧНО (ит. 15): `GameOver` только при поимке последнего]** |
| `Core/GameServices.cs` | `ActiveOrder`, `AvailableOrders`, `LoadedGifts`, `Profile` | Статическое локальное состояние. В сети нужен один источник правды (хост). |
| `Player/ElfInput.cs` | `CreateActions()`: биндинги `<Keyboard>`, `<Mouse>`, `<Gamepad>` без привязки к устройству; `static DebugMoveOverride` | Каждый эльф читает **все** устройства: геймпад двигает обоих. Static-override действует на всех. **[РЕШЕНО (ит. 11): ввод по устройствам, join по Start/A на свободном геймпаде]** |
| `Player/ElfController.cs` | `OnEnable/OnDisable` → `RegisterLocalPlayer`; `FindSpawn()` → `FindWithTag(TagSpawn)`; `GetCameraYaw()` → фолбэк на `Camera.main`; `handlePauseInput` | Один спавн на сцену, все появляются в одной точке. Камера берётся глобальная. **[РЕШЕНО (ит. 11, 17): друзья появляются рядом с первым игроком в каждой сцене]** |
| `Player/ThirdPersonCameraRig.cs` | `EnsureBrain()` → `Camera.main` + один `CinemachineBrain`; `HandleInput` → `Mouse.current` | Все виртуальные камеры идут в одну реальную камеру. Нет каналов Cinemachine и viewport rect. **[ЧАСТИЧНО (ит. 11): камера первого игрока берёт в кадр всех; split-screen не сделан]** |
| `Player/PlayerGate.cs` | `CanControl`, `CanMouseLook` (static) | Глобальное разрешение на управление, без разбивки по игрокам. Мышь всегда у всех. |
| `Player/ElfRagdoll.cs` | `Update` → `Keyboard.current[ragdollDebugKey]`; `OnGUI` → `Rect(10,10,…)`; `BuildModel` → `AssetDatabase.LoadAssetAtPath` под `#if UNITY_EDITOR` | Клавиша G роняет всех эльфов. Отладочные надписи накладываются друг на друга. Эльф, созданный в рантайме **в билде**, остаётся без модели: AssetDatabase в билде недоступен. |
| `Player/ElfCarry.cs` | `Held` (один), `IsCarryingHuge` и `SpeedMultiplier` (Huge = ×0.42 для одного эльфа) | Велосипед несёт один эльф. Совместного хвата нет. **[РЕШЕНО (ит. 13, 14): совместная переноска велосипеда [E], застревание в проёмах]** |
| `Player/PlayerSetup.cs` | `CreatePlayer(pos, themeIndex)` | Нет индекса игрока и списка устройств. Темы 0..3 уже есть: это хорошо. |
| `Player/FallRespawn.cs` | `FindWithTag(TagSpawn)` | Один спавн. |
| `Gifts/Gift.cs` | `OnPickedUp` → `transform.SetParent(holdPoint)`; `OnCollisionEnter` (урон и шум локально); `Update` → `Destroy` ниже `killY` | В NGO `NetworkObject` нельзя сделать дочерним к Transform без `NetworkObject`, а `HoldPoint` его не имеет. Урон, шум и уничтожение должны считаться только на хосте. |
| `Gifts/Conveyor.cs` | `Update` → `TrySpawnNext` → `GiftFactory.Create`; `OnTriggerStay` двигает rigidbody | В сети спавнить и толкать должен только сервер. |
| `Stealth/StealthUtil.cs` | `TryGetPlayer()` (только `LocalPlayer`); `IsPlayerCollider()` (фолбэк на `LocalPlayer`) | Дед, кот, конус зрения и скрипучий пол видят одного эльфа. **[РЕШЕНО (ит. 7): `StealthUtil` ищет ближайшего/любого эльфа без аллокаций]** |
| `Stealth/Grandpa.cs` | `EnterChase`, `TickChasing`, `Catch` → `RaisePlayerCaught` | Гонится за `LocalPlayer`. Ловит «игрока вообще», а не конкретного эльфа. **[ЧАСТИЧНО (ит. 15): поимка одного эльфа выкидывает его в камин, проигрыш только когда пойман последний]** |
| `Stealth/FatCat.cs`, `VisionCone.cs`, `NoiseZone.cs` | `TryGetPlayer` | То же самое. **[РЕШЕНО (ит. 7): через `StealthUtil`]** |
| `Audio/AudioManager.cs` | `LocalSurface()` → `LocalPlayer` | Поверхность для звука шагов берётся у одного игрока. |
| `UI/HudView.cs` (~313) | `GameServices.LocalPlayer` | Один HUD: стамина, шум, подсказки. |
| `UI/UIRoot.cs` (~330) | `Keyboard.current` | UI слушает глобальную клавиатуру. |
| `Rooftop/Billboard.cs` | `Camera.main` | При разделённом экране смотрит только в одну камеру. |
| `Editor/GreyboxSceneBuilder.cs` | `CreateSpawnAndPlayer()` | Эльф и один `PlayerSpawn` запекаются прямо в сцену. |

---

## 2. Рекомендуемый стек

**Сеть: Netcode for GameObjects (NGO), режим «клиент-хост» (host = игрок-сервер).**
- Пакет `com.unity.netcode.gameobjects` 2.x. Взять последнюю стабильную, которую Package Manager предлагает для 6000.6, ориентир 2.4+ (**проверить**).
- `com.unity.transport` 2.x ставится зависимостью (**проверить версию**).
- `com.unity.multiplayer.playmode` (Multiplayer Play Mode): несколько игроков в одном редакторе (**проверить версию**).
- `com.unity.multiplayer.tools`: Network Profiler и Network Simulator для лага и потерь (**проверить версию**).
- Почему NGO: официальный пакет, Multiplayer Center уже в манифесте, 1–4 игрока в режиме хоста для него типичная задача. Компоненты `NetworkTransform`, `NetworkRigidbody`, `NetworkVariable`, `[Rpc]` и `NetworkSceneManager` закрывают всё, что нужно. В NGO нет встроенного предсказания, но кооперативу без PvP оно и не нужно: owner-authoritative движение даёт мгновенный отклик своему эльфу.
- Не берём Distributed Authority (NGO 2.x): ему нужен облачный сервис Unity, и физика на одном хосте с ним проще не становится.

**Steam: Steamworks.NET + транспорт поверх Steam Networking Sockets.**
- Steamworks.NET (rlabrecque, ставится git-URL в UPM, версия 20.x / 2024.x, **проверить**). Поддерживается активнее, чем Facepunch.Steamworks.
- Транспорт NGO для Steam берём из `Unity-Technologies/multiplayer-community-contributions`: SteamNetworkingSockets transport для Steamworks.NET или Facepunch transport для Facepunch.Steamworks. **Проверить совместимость с NGO 2.x.** Если не собирается, написать свой `NetworkTransport` поверх `SteamNetworkingSockets`/`SteamNetworkingMessages`. Это примерно 300–500 строк.
- Steam Lobbies: создать лобби, пригласить через оверлей, `GameLobbyJoinRequested_t`, rich presence. Для тестов App ID 480 (Spacewar), `steam_appid.txt` рядом с exe.
- План Б: Unity Relay (`com.unity.services.multiplayer`, **проверить**) через UnityTransport, а Steam-лобби используются только для приглашений, в данных лобби лежит join code.

**Авторитет:**
- Эльф (`CharacterController`): **owner-authoritative**. `NetworkTransform` в режиме Owner authority (в NGO 2.x это `AuthorityMode`, **проверить имя поля**). Владелец сразу видит своё движение, это и есть «клиентское предсказание» для кооператива. Хост только проверяет грубые нарушения (телепорт больше N м).
- Подарки, велосипед, конвейер, дед, кот, заказы, состояние игры: **host-authoritative**. PhysX не детерминирован, поэтому физику подарков считает только хост. У клиентов `NetworkRigidbody` переводит тела в kinematic и интерполирует.
- Ragdoll-марионетка (11 rigidbody): **не синхронизируется**. Каждый клиент симулирует её локально по корню эльфа. По сети идут только состояние `Active/Limp/GettingUp/Downed` и импульс падения.

**Альтернативы:**
- FishNet + FishySteamworks: бесплатно, есть client-side prediction. Хорошо, если понадобится предсказание.
- Mirror + FizzySteamworks: зрелый, но API старее.
- Photon Fusion 2: лучшая сетевая физика, но платный CCU и привязка к облаку Photon.

---

## 3. Миграция мелкими шагами

Каждый шаг даёт играбельную сборку. Шаги 1–6 работают **без сети**.

### Шаг 0. Контракты Core (согласовать с лидом)
- `Core/GameServices.cs`: `IReadOnlyList<IPlayerStatus> Players`, `RegisterPlayer` / `UnregisterPlayer`. `LocalPlayer` оставить для совместимости: это «первый локальный» игрок. `UIModalOpen` заменить на `UIModalOwner` (индекс игрока или -1).
- `Core/Interfaces.cs`: в `IPlayerStatus` добавить `int PlayerIndex`, `bool IsDowned`. Новый `ICoCarryable` (слоты хвата, `TryGrab(slot, carrier)`, `Release`).
- `Core/GameEvents.cs`: `PlayerJoined`, `PlayerLeft`, `ElfCaught(IPlayerStatus)`, `ElfDowned`, `ElfRevived`. `PlayerCaught` оставить на случай «поймали всех».
- `Core/GameConstants.cs`: `MaxPlayers = 4`, тег спавна `PlayerSpawn` с индексом.

### Шаг 1. Локальный кооп: ввод по устройствам (клавиатура+мышь и геймпад)
- `Player/ElfInput.cs`: собрать actions в `InputActionMap`, добавить `AssignDevices(params InputDevice[])` (через `map.devices = …`). `DebugMoveOverride` сделать полем экземпляра. Добавить action `RagdollDebug`.
- Новый `Player/LocalCoopManager.cs`: P1 получает Keyboard+Mouse, P2 входит нажатием South на свободном геймпаде (`InputSystem.onAnyButtonPress`). Хранит ростер `PlayerSlot {index, devices, theme}` между сценами (DontDestroyOnLoad).
- `Player/PlayerSetup.cs`: `CreatePlayer(pos, themeIndex, playerIndex, devices)`.
- `Player/PlayerGate.cs`: `CanControl(playerIndex)`. `CanMouseLook` только у игрока, у которого есть мышь.
- `Player/ElfRagdoll.cs`: клавишу G читать через `ElfInput`. `OnGUI` смещать по индексу или убрать. Модель брать из сериализованного поля или из `Resources`, а не из `AssetDatabase`.
- `Player/ElfController.cs`: `RegisterPlayer` вместо `RegisterLocalPlayer`. Пауза с любого устройства.

### Шаг 2. Камеры (по умолчанию разделённый экран, общая камера как опция)
- `Player/ThirdPersonCameraRig.cs`: свой `Camera` и `CinemachineBrain` на каждого игрока, `Camera.rect` (вертикальный сплит 50/50). Каналы Cinemachine 3: `CinemachineCamera.OutputChannel` + `CinemachineBrain.ChannelMask` (**проверить имена в CM 3.x / 6.6**). Убрать `Camera.main`. `AudioListener` один, на камере P1.
- `Player/ElfController.cs`: `GetCameraYaw()` без фолбэка на `Camera.main`.
- `Rooftop/Billboard.cs`: поворачиваться к ближайшей камере (или в `OnWillRenderObject` к текущей).
- Опция «общая камера» для переноски велосипеда: `CinemachineTargetGroup` + `CinemachineGroupFraming`, фиксированный угол, мышиный обзор выключен. Включается, пока игроки рядом, или настройкой.

### Шаг 3. Спавны и сцены
- `Editor/GreyboxSceneBuilder.cs`: не запекать эльфа, ставить `PlayerSpawn_0..3`.
- Новый `Player/PlayerSpawner.cs`: на старте сцены создаёт эльфов по ростеру.
- `Player/ElfController.cs` (`FindSpawn`) и `Player/FallRespawn.cs`: спавн по `PlayerIndex`.
- `Tests/PlayMode/SceneSmokeTests.cs`: тест «два эльфа, два спавна, ввод не пересекается».

### Шаг 4. Стелс видит всех
- `Stealth/StealthUtil.cs`: `TryGetNearestPlayer(from, out IPlayerStatus)` и перебор `GameServices.Players`. `IsPlayerCollider` через `GetComponentInParent<IPlayerStatus>()`.
- `Stealth/Grandpa.cs`, `FatCat.cs`, `VisionCone.cs`, `NoiseZone.cs`: цель = ближайший видимый эльф, который не лежит. `Catch` поднимает `ElfCaught(player)`.
- `Core/GameFlow.cs`: GameOver только когда поймали или уронили **всех**. Пойманный эльф получает Downed, его можно поднять.
- `Audio/AudioManager.cs` (`LocalSurface`): поверхность брать у `IPlayerStatus` того, кто издал шум.

### Шаг 5. HUD и UI
- `UI/HudView.cs`: панель на каждого игрока (в своей половине экрана или в углах).
- `UI/UIRoot.cs`, `OrderBoardView.cs`, `WorkbenchView.cs`: модальное окно принадлежит открывшему (`UIModalOwner`), остальные продолжают играть. Ввод UI от устройств владельца.
- Пауза локального коопа: `Time.timeScale = 0` можно оставить.

### Шаг 6. Кооп-механики (главная проверка геймплея)
- **«Поднять упавшего друга»**: `Player/ElfRagdoll.cs` получает режим `Downed` (после поимки или сильного падения лежит, пока не поднимут; таймер опционален). Новый `Player/ElfReviveTarget.cs` (IInteractable на эльфе, подсказка «Поднять друга», удерживать E/X 1.5 с). `Player/ElfInteractor.cs` учится находить эльфов.
- **Совместная переноска велосипеда**: новый `Gifts/CoCarry.cs` на Huge-подарке, правки в `Gifts/Gift.cs`, `Player/ElfCarry.cs`, `Player/ElfController.cs` (см. раздел 4).
- Плейтест, тюнинг, только после этого сеть.

### Шаг 7. Сетевой фундамент (LAN, прямой IP)
- `Packages/manifest.json`: NGO, Multiplayer Play Mode, Multiplayer Tools.
- Сцена Boot: `NetworkManager` + `UnityTransport`. Prefab эльфа = результат `PlayerSetup` (`NetworkObject`, owner `NetworkTransform`), зарегистрирован в `NetworkPrefabsList`. Спавнит его сервер (`PlayerSpawner` → `SpawnAsPlayerObject`).
- `ElfInput`, `ThirdPersonCameraRig`, `ElfInteractor`, HUD включаются только при `IsOwner`. `ElfController` у чужих эльфов не двигает CC.
- Подход «один сетевой игрок на машину, локальный кооп отдельно». Сочетать локальный и онлайн (2+2) отложить.

### Шаг 8. Поток игры по сети
- Новый `Core/NetGameState.cs` (NetworkBehaviour, в Core по согласованию). `GameState`, `ActiveOrder` (Order → `INetworkSerializable`), `AvailableOrders` и `LoadedGifts` хранятся в `NetworkVariable` / `NetworkList`.
- `Core/GameFlow.cs`: переходы только на сервере, сцены через `NetworkManager.SceneManager.LoadScene`. Клиенты шлют `Request*` RPC-запросом на сервер. Онлайн-пауза = только меню, без `timeScale`.
- `Core/PlayerProfile.cs`: профиль хоста определяет апгрейды, чаевые пишутся каждому локально. Это решение по дизайну, **зафиксировать**.

### Шаг 9. Подарки по сети
- `Gifts/Gift.cs`: `NetworkObject` + `NetworkRigidbody` + `NetworkTransform` (server authority). `Integrity`, `IsHeld`, `IsInSack`, `IsDelivered`, `HolderClientId` хранятся в `NetworkVariable`. Урон, шум и `killY` считаются только на сервере. `SetParent(holdPoint)` заменить на следование за `HoldPoint` в `LateUpdate` или на `NetworkObject.TrySetParent(elfRoot)` с локальным офсетом.
- `Gifts/GiftFactory.cs`: `Spawn()` только на сервере, prefab'ы по видам.
- `Gifts/Conveyor.cs`, `SleighLoadZone.cs`, `TreeDropZone.cs`, `HouseGiftSpawner.cs`, `ChimneyExit.cs`, `RooftopChimney.cs`: логика под `IsServer`. Взаимодействие от клиента идёт RPC-запросом «взять / положить / в мешок», сервер его проверяет.
- `Player/ElfCarry.cs`: предсказанный показ «в руках» сразу, откат, если сервер отказал.

### Шаг 10. Стелс на сервере
- `Stealth/Grandpa.cs`, `FatCat.cs`, `NavMover.cs`, `VisionCone.cs`, `NoiseZone.cs`: думают только на хосте, NavMeshAgent только на хосте. Трансформы идут через `NetworkTransform`, `AlertLevel` через `NetworkVariable`, на клиенте из него поднимается `GrandpaAlertChanged`.
- `Player/ElfController.cs` (`RaiseNoise`): на клиенте шум отправляется RPC-запросом на сервер с ограничением частоты (шаги не чаще 4 Гц). На хосте он попадает в `GameEvents.Noise`.

### Шаг 11. Ragdoll и велосипед по сети
- `Player/ElfRagdoll.cs`: режим и импульс падения в `NetworkVariable` + RPC, марионетка локальная. Downed и revive решает сервер.
- `Gifts/CoCarry.cs`: см. раздел 4.

### Шаг 12. Steam
- Steamworks.NET + Steam-транспорт, новый `Net/SteamLobby.cs`: создать / пригласить / войти, rich presence. Главное меню: «Играть с друзьями».
- Хост вышел = сессия завершена (миграции хоста нет). Входить можно только в хабе, в доме нельзя.

### Шаг 13. Надёжность
- Разрывы и реконнект, тесты в Network Simulator (150 мс, 2% потерь), лимиты RPC, сборки Steam-билда (депо, `steam_appid.txt`).

---

## 4. Совместная переноска велосипеда

**Геометрия.** Коробка велосипеда около 1.64 × 1.04 × 0.68 м (`GiftVisual.BuildHuge`), эльф ростом около 0.6 м. Две ручки, дочерние Transform: `Grip_Front` и `Grip_Rear` на концах по локальной X, на высоте рук.

**Компонент `Gifts/CoCarry.cs` (координатор, на Huge-подарке):**
- Слоты `Front` и `Rear`, у каждого есть `carrier` (ICarrier / IPlayerStatus) или пусто.
- Состояния:
  - **0 эльфов**: обычный dynamic rigidbody, как сейчас.
  - **1 эльф = «волоком»**: поднят один конец. Тело dynamic, к руке эльфа его тянет `ConfigurableJoint` с мягкой линейной пружиной. Скорость эльфа ×0.25, прыжка нет, при движении раз в 0.5 с скребущий шум (`NoiseSource.Object`, около 30). Подсказка «Нужен второй эльф».
  - **2 эльфа = «несём»**: велосипед поднят.
- Почему гибрид, а не два жёстких сустава: `CharacterController` не реагирует на силы, поэтому суставы между CC и rigidbody не дают обратной связи. Вместо этого:
  1. Координатор каждый `FixedUpdate` собирает от обоих эльфов `wishVelocity` (их ввод в мировых координатах).
  2. Общая скорость груза: `v = (wishA + wishB) / 2 × carrySpeed`, где `carrySpeed` = walk × 0.55, а с Grip-апгрейдом до ×0.8. Спринт работает только если спринтуют оба.
  3. Поворот: компоненты желаний, перпендикулярные оси велосипеда, дают крутящий момент: `yawRate = (perpA − perpB) / length`. Ограничение 70°/с, «тяжёлый» поворот вокруг центра.
  4. Если желания противоположны (`dot(wishA, wishB) < −0.3`), груз стоит и комично раскачивается (тянут в разные стороны). Можно добавить строку-тост «Тяни-толкай!».
  5. Велосипед остаётся **dynamic** (ContinuousDynamic), а не kinematic. Координатор ведёт его PD-регулятором к целевой позе (velocity tracking), поэтому дверные косяки и мебель останавливают его настоящей физикой.
  6. Эльфы привязаны к ручкам «поводком»: их CC каждый кадр подтягивается к позиции слота (`ElfController` получает внешний `CarryConstraint`, который подменяет `wishDir` и добавляет коррекцию). Их собственная скорость = `v` × `(1 − lag01)`, где `lag` = отставание велосипеда от целевой позы. Если велосипед упёрся, эльфы тоже встают.
- **Застревание в дверях**: дверь около 0.8–0.9 м, длина велосипеда 1.64 м, нести можно только вдоль. `Rigidbody.SweepTest` по направлению движения: при касании скорость проецируется на плоскость препятствия (скольжение). Если `lag > 0.35 м` дольше 0.4 с, эльфы получают «тугой ход». Если `lag > 0.7 м` или расстояние между эльфами больше `length + 0.5 м` дольше 1 с, **хват срывается**: `Release` у обоих, велосипед падает с их скоростью (`Gift.OnDropped`), получает урон и громко лязгает (уже есть `hugeNoiseMultiplier`). Опционально кнопка «поднять над головой» (Jump у обоих в пределах 0.25 с) приподнимает велосипед на 0.3 м, чтобы пронести над низкой мебелью.
- **Выход из хвата**: Interact = отпустить свою ручку, и велосипед переходит в режим «волоком». Если эльф упал (ragdoll limp / downed) или его поймал дед, его слот автоматически освобождается.
- **Стамина**: оба тратят `heavyCarryDrainPerSecond`. При исчерпании у одного скорость ×0.6, у обоих срывается хват.
- **Анимация**: `ElfRagdoll.AnimateJoints` при `carrying` руки уже поднимает. Добавить IK-подобную цель: руки тянутся к ручке.
- **Изменения**: `Gift.cs` получает флаг Huge → `CoCarry`, `OnPickedUp` для Huge не вызывается. `ElfCarry.TryPickUp` для Huge идёт в `ICoCarryable.TryGrab(nearestSlot)`. `ElfController` получает `ICarryConstraint`. `ElfInteractor` показывает подсказку «Взяться спереди / сзади».

**Сеть:**
- Велосипед и `CoCarry` полностью на хосте. `NetworkVariable`: `FrontClientId`, `RearClientId`, `State`. Поза через `NetworkRigidbody` / `NetworkTransform`, у клиентов интерполяция.
- Хват: клиент шлёт RPC `RequestGrab(slot)`, сервер проверяет дистанцию и что слот свободен, затем выставляет NetworkVariable.
- Пока эльф держит велосипед, его ввод (`wish`, sprint, jump) каждый тик (30–60 Гц) уходит RPC-запросом на сервер (unreliable/sequenced). Хост считает общую скорость.
- Позиция эльфа-носильщика у клиента: эльф «пристёгнут» и ставится в позицию своего слота по **интерполированной** позе велосипеда, плюс небольшое локальное смещение по своему вводу (до 0.15 м, чтобы ввод ощущался сразу). Получается задержка около RTT/2 + интерполяция, но велосипед и так тяжёлый и инерционный, поэтому лаг маскируется. Owner-authority эльфа на время хвата **приостанавливается**: позицию задаёт формула от позы велосипеда, и хост проверяет `lag` и срыв хвата по своим данным.
- Срыв хвата, урон и шум (сервер → `GameEvents.Noise` для деда) только на хосте. Клиенты получают событие RPC-вызовом с сервера (тост, звук).

---

## 5. Риски

1. **Физика и сеть.** PhysX не детерминирован, а подарки на конвейере и броски будут дёргаться у клиентов. Нужен server authority + интерполяция, клиенты увидят толчки с задержкой. Ragdoll синхронизировать нельзя (11 тел × 4 игрока), только локальная симуляция, поэтому позы у игроков будут расходиться (для комедии это нормально).
2. **Шаг 0 противоречит правилу «не трогать Core».** Без согласования с лидом план встаёт.
3. **Steam-транспорт для NGO 2.x.** Community-транспорты могут отставать от NGO, возможно придётся писать свой (+1–2 недели). План Б: Relay.
4. **Ввод и Input System.** Ручные `InputAction` без `PlayerInput`/`InputUser`. Сопряжение устройств, отключение геймпада и Steam Input (Steam может переназначать геймпады) нужно тестировать (**проверить** настройки Steam Input для игры).
5. **Разделённый экран.** Две камеры URP и два Cinemachine Brain удваивают рендер. Нужно проверить FPS (тени, пост-обработка), возможно снизить качество в сплите.
6. **Модель эльфа в билде.** `ElfRagdoll.BuildModel` грузит FBX через `AssetDatabase`, поэтому эльф, созданный в рантайме, в билде останется примитивным. Нужна ссылка в prefab'е.
7. **Совместный вес велосипеда.** Связка CharacterController и dynamic rigidbody может давать дрожание и пролёт через стены при поводке. Нужны лимиты коррекции и ContinuousDynamic. Тюнинг займёт время, поэтому шаг 6 делать **до** сети.
8. **Баланс стелса на 4 игрока.** Шум от четырёх эльфов складывается, деда будит заметно чаще. Нужно масштабировать пороги или `GameConstants` по числу игроков.
9. **Экономика и профиль.** Чьи апгрейды и чаевые (хост или каждый)? Без решения сломается `PlayerProfile.TryBuy` и сохранения.
10. **Объём работ.** Почти каждый MonoBehaviour в Gifts и Stealth надо разделить на серверную и клиентскую части (`IsServer`). Это самая большая часть плана (шаги 9–10), лучше делать по одной сцене (Hub → Rooftop → House).
