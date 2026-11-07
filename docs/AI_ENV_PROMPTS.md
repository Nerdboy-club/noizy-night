# Промпты для Gemini: комнаты, окружение, предметы

## Проверенный пайплайн (Unity-порт)

**Gemini (концепт) → Tripo Image-to-3D (GLB) → Blender-скрипт из `art/blender/` → FBX в Unity.**

1. Gemini: концепт одного предмета/персонажа на белом фоне.
2. Tripo Image-to-3D: скачать `.glb` (высокополигональный, с текстурой).
3. Blender (headless), из корня репозитория:
   - **Проп** (мебель, подарки, декор):
     `blender -b -P art/blender/prep_prop.py -- <in.glb> <Name> <Room> <height_m> [tris=25000] [tex=1024]`
     Результат: `unity/SilentNight/Assets/_Project/Art/Environment/<Room>/<Name>.fbx` (+ `<Name>_Textures`), pivot в центре пола, децимация, масштаб до заданной высоты.
   - **Эльф**: `blender --background --python art/blender/rig_tripo_elf.py -- <input.glb> [target_tris=30000]` (риг с Mixamo-именами костей + процедурные клипы; `Elf_Tripo.fbx`, `Animations/Elf_Clips.fbx`).
   - **Кот**: `blender -b -P art/blender/rig_cat.py -- <cat.glb> [target_tris=20000]` (Generic rig, клипы Idle/Walk/Run/Sleep/Alert; `Art/Characters/Cat/`).
   - **Три ёлочных шарика в одном GLB**: `prep_ornaments.py -- <glb>` режет на `Ornament_Red/Blue/Green.fbx` (по 0,16 м) в `Art/Environment/Decor`.
4. Unity: меню `Silent Night ▸ Build Animator Controllers` и пересборка сцен сборщиком.

**Стиль, который понравился:** `3D game prop ..., cozy miniature stop-motion plasticine style, isolated on white background` (пластилиновый «Tactile Christmas Chaos»). Один предмет на картинку, без фона и теней.

**Идея кастомизации ёлки:** генерировать голую ёлку отдельно, а игрушки — отдельными пропами (`prep_ornaments.py`), чтобы игрок мог украшать её сам.

### Статус ассетов

| Ассет | Статус |
|---|---|
| Эльф | ГОТОВО (Tripo, `rig_tripo_elf.py`) |
| Кот | ГОТОВО (`rig_cat.py`) |
| Верстак | ГОТОВО |
| Камин | ГОТОВО |
| Ёлка | ГОТОВО (голая ёлка) |
| Мешок | ГОТОВО |
| Ёлочные игрушки (Red/Blue/Green) | ГОТОВО |
| Дед | НУЖНО |
| Сани | НУЖНО |
| Конвейер | НУЖНО |
| Доска заказов | НУЖНО |
| Подарки (коробки, велосипед, шар, шкатулка, машинка) | НУЖНО (пока серые коробки) |
| Детали крыш | НУЖНО |

Часть мебели берётся из CC0-паков Kenney (не генерируется).

---

Дополняет `AI_ASSET_PROMPTS.md` (там персонажи и подарки). Здесь окружение: Хаб Санты и 5 комнат дома.

## Как работаем (пайплайн)

1. **Gemini (Nano Banana / Imagen)**: генерируем *концепт* по промпту ниже. Один общий стиль на всё.
2. **Комнату не делаем одной моделью.** Из концепта выбираем отдельные предметы и генерируем каждый как 3D-проп (Meshy / Tripo / Rodin, Image to 3D, 2–10k треугольников).
3. **Стены, пол, потолок** делаем в Unity из простых плоскостей и тайлящихся текстур (промпты «Текстуры» ниже).
4. Кидаешь результаты мне в `unity/SilentNight/Assets/_Project/Art/Environment/<Комната>/`, я расставляю по сцене и подключаю в сборщик сцен.

Совет: сначала сделать по 1–2 предмета на комнату, проверить стиль в игре, потом дорабатывать остальное.

## Общий стиль (добавлять в КАЖДЫЙ промпт)

> stylized 3D game environment art, cozy Christmas night, soft hand-crafted toy look, felt and knitted wool textures, chunky rounded shapes, warm lamp light with deep blue night shadows, clean readable silhouettes, Pixar-like but low-poly friendly, game-ready

Для **предметов** добавлять: `single object, centered, three-quarter view, plain white background, no shadows, no text`.
Для **концептов комнат** добавлять: `isometric cutaway view of the room, 45 degree angle, no people`.

---

## Концепты комнат (изометрия, для ориентира)

### Хаб: Мастерская Санты
> Santa's workshop base: wooden floor, a long gift conveyor belt along the wall, a big order board with paper notes pinned on it, a workbench with tools and upgrade parts, a red sleigh parked by a wide open garage door with snowy night outside, shelves with wrapped gifts, string lights, + [общий стиль] + isometric cutaway

### 1. Гостиная (цель: ёлка + камин-выход)
> cozy living room at night: big decorated Christmas tree with presents space under it, brick fireplace with a chimney (the elf escape route), armchair, rug, TV off, bookshelf, window with snow, warm string lights, + [общий стиль] + isometric cutaway

### 2. Прихожая (скрипучие полы, часы)
> narrow hallway of a house at night: old grandfather clock, coat rack with winter coats, shoe rack, wooden floorboards (some visibly worn and creaky), small side table with a lamp, door to other rooms, + [общий стиль] + isometric cutaway

### 3. Кухня (скользкая плитка, кот)
> small kitchen at night: glossy checkered tile floor, kitchen counter and sink, old fridge, table with two chairs, cat bed with a fat orange cat bowl nearby, hanging pots, + [общий стиль] + isometric cutaway

### 4. Кабинет Деда
> grumpy grandpa's study: big wooden desk with old computer and lamp, leather armchair, bookshelves full of books, a bed or couch with a blanket where grandpa sleeps, framed photos, slippers by the bed, + [общий стиль] + isometric cutaway

### 5. Гараж (лёд)
> cluttered garage at night: workbench with tools, old car covered with a tarp, shelves with boxes, a frozen puddle of ice on the concrete floor, ladder, snow tires stack, side door to the snowy yard, + [общий стиль] + isometric cutaway

### Жуткий дом (редкий заказ)
> same cozy house but gloomy variant: cold green-blue light, dim flickering lamps, long shadows, cobwebs on corners, tilted frames on walls, + [общий стиль] + isometric cutaway

---

## Предметы (каждый отдельно → Image to 3D)

Формат: `<описание> + single object, centered, three-quarter view, plain white background, no shadows + [общий стиль]`. Размеры в метрах для масштаба в Unity.

### Хаб (мастерская)
| Предмет | Описание для промпта | Высота |
|---|---|---|
| Конвейер подарков | `wooden toy-style conveyor belt section with red rubber belt and candy-cane striped sides` | 0.9 |
| Доска заказов | `big cork order board with pinned paper notes, red pushpins, small star on top, wooden frame` | 2.0 |
| Верстак | `chunky wooden workbench with hand tools, jars of bolts, a little jetpack blueprint pinned above` | 1.0 |
| Сани Санты | `red Santa sleigh with golden runners and a big empty sack space, cartoon style` | 1.3 |
| Полка с подарками | `wooden shelf with stacked wrapped gifts` | 2.0 |

### Гостиная
| Предмет | Описание | Высота |
|---|---|---|
| Ёлка | `tall decorated Christmas tree with ornaments, tinsel, big star on top, round base` | 2.2 |
| Камин | `brick fireplace with wide mantel, stockings hanging, small glowing fire, chimney opening` | 1.6 |
| Кресло | `fat comfy armchair with knitted blanket` | 0.9 |
| Ковёр | `round knitted rug with snowflake pattern, top-down flat view` | плоский |
| Книжный шкаф | `wooden bookshelf full of books and small decorations` | 2.0 |
| Телевизор на тумбе | `old CRT TV on a wooden stand` | 1.0 |

### Прихожая
| Предмет | Описание | Высота |
|---|---|---|
| Напольные часы | `tall grandfather clock with pendulum, wood and brass` | 2.0 |
| Вешалка с пальто | `coat rack with winter coats and a scarf` | 1.8 |
| Обувница | `wooden shoe rack with boots and slippers` | 0.6 |
| Тумба с лампой | `small side table with a warm table lamp` | 0.9 |

### Кухня
| Предмет | Описание | Высота |
|---|---|---|
| Холодильник | `chunky retro fridge with magnets and drawings` | 1.7 |
| Столешница с раковиной | `kitchen counter with sink and cutting board` | 0.95 |
| Стол со стульями | `small round kitchen table with two chairs` | 0.75 |
| Миска и лежанка кота | `cat bed with a bowl, fish-shaped pillow` | 0.3 |
| Подвесные кастрюли | `hanging rail with pots and ladles` | 0.5 |

### Кабинет
| Предмет | Описание | Высота |
|---|---|---|
| Письменный стол | `big wooden desk with an old computer, lamp and papers` | 0.8 |
| Кресло деда | `leather armchair with worn cushion` | 1.0 |
| Кровать деда | `old bed with a patchwork quilt and a pillow` | 0.6 |
| Тапки-олени | `pair of fluffy reindeer slippers` | 0.15 |

### Гараж
| Предмет | Описание | Высота |
|---|---|---|
| Машина под брезентом | `old car covered with a dusty tarp` | 1.5 |
| Верстак с инструментами | `messy garage workbench with tools` | 1.0 |
| Стеллаж с коробками | `metal shelving with cardboard boxes` | 2.0 |
| Лёд на полу | `frozen ice puddle, top-down flat, glossy blue-white` | плоский |

### Декор (общий)
> `string of warm Christmas lights`, `wreath with red bow`, `hanging stockings`, `candle in glass jar`, `framed family photo`, `round wall clock`, `snowman figurine`, `window with frost and curtains` (каждый отдельным промптом)

---

## Текстуры (бесшовные, для стен и пола)

Формат: `seamless tileable texture, top-down flat, 2048x2048, no lighting, no shadows` + описание:

- Деревянный пол: `warm wooden plank floor, worn, painted-style` (Гостиная, Прихожая, Кабинет)
- Скрипучие доски: `wooden floorboards with darker worn patches` (Прихожая)
- Плитка: `black and white checkered glossy kitchen tiles` (Кухня)
- Бетон: `grey concrete garage floor with small cracks` (Гараж)
- Обои: `cozy wallpaper with tiny snowflakes, muted green/red/cream` (по одному цвету на комнату)
- Снег снаружи: `soft snow ground, stylized`

## Проверка перед присылкой

- Один предмет на картинке, фон белый, без текста и теней.
- Масштаб: двери 2.0 м, потолок 2.5 м. Если модель вышла другого размера, ничего страшного, я подгоню в Unity.
- Предметы, с которыми игрок взаимодействует (ёлка, камин, верстак, сани, доска заказов), присылай **в первую очередь**.
