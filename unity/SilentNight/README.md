# Operation: Silent Night — Unity

Порт игры с three.js на Unity 6 под Steam/ПК. Код написан на Mac **без Unity**, поэтому первая сборка на ПК — это проверка компиляции. Ошибки ожидаемы, их немного, и они быстро чинятся (см. «Если не компилируется»).

## Что нужно на ПК
1. **Unity Hub** — unity.com/download, войти в аккаунт Unity (Personal бесплатно).
2. В Hub → Installs → Install Editor → **Unity 6 LTS** (последняя `6000.0.x LTS`).
   Модули: **Windows Build Support (IL2CPP)**, по желанию **Mac Build Support**. Visual Studio Community Hub предложит поставить сам — соглашайся (для C#).
3. Свободно ~15 ГБ.

## Первый запуск
1. Скопируй папку `unity/SilentNight` на ПК (или склонируй репозиторий).
2. Unity Hub → **Add** → **Add project from disk** → выбери папку `SilentNight`.
   Hub спросит версию — выбери установленную Unity 6 LTS. Первое открытие идёт долго (скачиваются пакеты из `Packages/manifest.json`).
3. Если Unity спросит «Enable the new Input System backends? (restart)» — **Yes**.
4. Меню **Silent Night ▸ Setup Project** — создаёт слои, теги, URP-рендер и настройки.
5. Меню **Silent Night ▸ Build Greybox Scenes** — собирает сцены `Boot`, `Hub`, `House` из кода и добавляет их в Build Settings.
6. Открой `Assets/_Project/Scenes/Boot.unity` и нажми **Play**.

Управление: WASD — ходьба, мышь — камера, Shift — бег, C — красться, Space — прыжок, Q — реактивный ранец (после покупки), E — взять/использовать, R — мешок, Tab — доска заказов, V — дальность камеры, колесо — зум, Esc — пауза. Геймпад тоже работает.

## Если не компилируется
Unity покажет ошибки в окне **Console** (красные строки). Скопируй их все (или скриншот) и пришли мне — я исправлю. В коде места, где я не был уверен в API, помечены комментарием `// VERIFY:` — ошибки скорее всего там.

Если ругается на версию пакета в `Packages/manifest.json` — открой **Window ▸ Package Manager** и обнови пакет до версии, которую он предлагает.

## Структура
```
Assets/_Project/
  Scripts/Core      — контракты: состояния, события, сервисы, профиль, GameFlow
  Scripts/Player    — эльф: движение, мешок, взаимодействие, камера (Cinemachine)
  Scripts/Stealth   — Дед (NavMesh AI, слух, зрение), кот, скрипучие зоны
  Scripts/Gifts     — подарки, конвейер, сани, ёлка, камин, настроение дома
  Scripts/UI        — интерфейс на UI Toolkit (меню, HUD, доска заказов, верстак)
  Scripts/Audio     — звук и музыка (клипы из Resources/Audio)
  Scripts/Editor    — Setup Project, сборщик сцен, импорт Mixamo-анимаций
  Art/              — сюда класть модели (.fbx/.glb) персонажей и пропсов
  Audio/            — сюда звуки (список имён в Audio/README.md)
```
Архитектура и правила модулей — в `ARCHITECTURE.md`.

## Графика: что дальше
Сейчас всё на заглушках из примитивов (greybox) — это нормально для проверки геймплея.
1. **Окружение**: бесплатные CC0-паки (Kenney Furniture Kit / Holiday Kit, Quaternius Ultimate Furniture и т.п.) — импорт в `Art/Environment`, затем расставляем в сценах вместо серых коробок.
2. **Персонажи и ключевые предметы**: генерация через ИИ по промптам из `../../docs/AI_ASSET_PROMPTS.md` (Meshy/Tripo + Mixamo).
   Кладёшь модель в `Art/Characters/Elf/` и анимации Mixamo в `Art/Characters/Elf/Animations/`, затем меню **Silent Night ▸ Build Animator Controllers**. В компоненте `ElfVisual` / `NPCVisual` / `GiftVisual` указываешь Model Prefab — заглушка заменится на модель.
