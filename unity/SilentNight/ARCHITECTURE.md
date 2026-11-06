# Silent Night — Unity architecture (contracts)

Unity 6 LTS (6000.0.x), URP, Input System (new), Cinemachine 3, AI Navigation (NavMesh), UI Toolkit.
No assembly definitions: runtime scripts compile into Assembly-CSharp, `Editor/` folders into the editor assembly.
Namespace per folder: `SilentNight.Core`, `SilentNight.Player`, `SilentNight.Stealth`, `SilentNight.Gifts`, `SilentNight.UI`, `SilentNight.Audio`, `SilentNight.EditorTools`.

## Rules
1. Modules talk ONLY via `SilentNight.Core` (GameEvents, GameServices, interfaces). Never reference another module's concrete class, never `FindObjectOfType` another module's class. (Exception: Editor scene builder may reference everything.)
2. Do not edit files in `Scripts/Core/`. If a contract is missing, write a note in your report; work around locally.
3. Art is not available yet. Every visual must work with **placeholder primitives** created in code AND allow a real model later: expose a `[SerializeField] GameObject modelPrefab`; if null, build a placeholder. Animators are optional (null-safe).
4. All tunables are `[SerializeField]` with sensible defaults (taken from the three.js prototype in `/src`), so the scene builder can just `AddComponent<>()`.
5. Unsubscribe from events in `OnDisable`/`OnDestroy`. No `Update` allocations.
6. Cursor: GameFlow locks the cursor in Hub/Delivery. UI that opens a panel during gameplay must unlock it and re-lock on close.
7. Russian player-facing text (the game is in Russian), English code/comments.

## Core (done)
- `GameEnums.cs` — GameState, HouseMood, GiftKind, NoiseSource, SurfaceType, UpgradeType, AlertLevel
- `NoiseEvent.cs` — struct with `LoudnessAt(listener)` falloff
- `Interfaces.cs` — IInteractable, IInteractor, ICarryable, ICarrier, IPlayerStatus
- `Order.cs` — order data + random generator
- `PlayerProfile.cs` — tips, upgrade levels, costs, TryBuy, JSON save
- `GameEvents.cs` — event bus (Noise, StateChanged, GrandpaAlertChanged, GiftDelivered, GiftLoaded, GiftDamaged, Objective, Toast, PlayerCaught, DeliveryCompleted, RequestStartDelivery, RequestReturnToHub, RequestHubPanel)
- `GameServices.cs` — Profile, LocalPlayer, Flow, ActiveOrder, AvailableOrders, LoadedGifts
- `GameFlow.cs` — state machine + scene loading (Boot → Hub(MainMenu) → Hub → House → Results/GameOver → Hub)
- `GameConstants.cs` — scene names, layers, tags, noise numbers
- `SurfaceTag.cs` — floor surface marker

## Scenes (built by Editor/GreyboxSceneBuilder)
- `Boot` — GameFlow, AudioManager, UIRoot (persist)
- `Hub` — workshop greybox, PlayerSpawn, conveyor, sleigh, order board, workbench
- `House` — 5 rooms greybox, PlayerSpawn, tree drop zone, chimney exit, grandpa, cat, surfaces, NavMesh

## Module ownership
| Folder | Owner | Key classes |
|---|---|---|
| Scripts/Core | lead | (above) |
| Scripts/Player | Player agent | ElfInput, ElfController (IPlayerStatus), ElfCarry (ICarrier), ElfInteractor (IInteractor), ElfAnimator, ElfVisual, ThirdPersonCameraRig |
| Scripts/Stealth | Stealth agent | Grandpa (NavMesh AI), VisionCone, FatCat, NoiseZone (creaky floor) |
| Scripts/Gifts | Gifts agent | Gift (ICarryable), GiftFactory, Conveyor, SleighLoadZone, TreeDropZone, ChimneyExit, OrderBoardStation, WorkbenchStation, HouseGiftSpawner, HouseMoodController |
| Scripts/UI + UI/ | UI agent | UIRoot, MainMenuView, HudView, OrderBoardView, WorkbenchView, ResultsView, GameOverView, PauseView (+ UXML/USS) |
| Scripts/Audio | UI agent | AudioManager |
| Scripts/Editor | Editor agent | ProjectBootstrap, GreyboxSceneBuilder, MixamoImportPostprocessor, AnimatorControllerBuilder |
