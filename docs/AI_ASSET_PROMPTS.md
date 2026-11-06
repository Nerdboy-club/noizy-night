# Генерация 3D-ассетов через ИИ

Пайплайн для уникальных персонажей и ключевых предметов. Окружение (мебель, стены, декор) берём из CC0-паков (Quaternius, Kenney), не генерируем.

## Пайплайн

1. **Концепт-арт (2D).** Генерируем картинку персонажа в позе A-pose или T-pose, вид спереди, на белом фоне. Подойдёт Midjourney, ChatGPT/DALL·E, Leonardo или Ideogram. Лучше сразу сделать лист «спереди / сбоку / сзади», если сервис умеет.
2. **Картинка → 3D.** Загружаем концепт в Meshy, Tripo или Rodin (режим Image to 3D).
   - Выбираем стилизованный / мультяшный стиль, а не реализм.
   - Целевая полигональность: персонажи 10–30k треугольников, предметы 2–10k.
   - Текстуры: PBR, 2K.
3. **Риг и анимации.**
   - Гуманоиды (эльф, Дед): авториг в Meshy/Tripo или загрузка в Mixamo (mixamo.com, бесплатно с аккаунтом Adobe). Скачиваем FBX, «for Unity», без скина для анимаций.
   - Кот: Mixamo не умеет четвероногих. Берём авториг Meshy/Tripo для животных или готового кота из CC0-пака Quaternius (Ultimate Animated Animals).
4. **Экспорт.** FBX или GLB, кладём в `Assets/Art/Characters/<имя>/` Unity-проекта. Дальше подключаю я.

## Общий стиль (добавлять в каждый промпт)

> stylized 3D game character, cozy Christmas, soft hand-crafted toy look, felt and knitted wool textures, chunky rounded proportions, warm colors, clean readable silhouette, Pixar-like but low-poly friendly, game-ready, neutral A-pose, front view, plain white background, full body, no shadows

## Персонажи

### Эльф (игрок) — 4 цветовые версии
> small heist elf, big head (1/3 of body height), pointy ears, rosy cheeks, mischievous smile, knitted red beanie with white pom-pom and folded brim, chunky knitted green sweater with candy-cane stripe, thick striped scarf, mittens, felt boots with curled toes and tiny bells, small brown belt with gold buckle, sneaky thief vibe + [общий стиль]

Варианты цветов (как в игре сейчас):
1. Красная шапка, зелёный свитер, красный шарф — «Красношап (Лидер)»
2. Оранжевая шапка, синий свитер, зелёный шарф — «Синий Свитер»
3. Зелёная шапка, фиолетовый свитер, жёлтый шарф — «Фиолетовый Вязаный»
4. Синяя шапка, рыжий свитер, красный шарф — «Рыжий Шарф»

Совет: сгенерировать одного эльфа, а цвета менять в Unity через материалы. Так все 4 будут одинаковыми по форме.

Анимации из Mixamo: Idle, Walking, Running, Sneak Walk (crouch), Jump, Carrying (walk with box), Pick Up, Falling, Getting Up.

### Ворчливый Дед (NPC)
> grumpy old grandpa in striped flannel pajamas, long nightcap with pom-pom, round glasses, huge bushy white moustache, bald head with grey side hair, slight belly, fluffy slippers, holding a smartphone, sleepy annoyed expression + [общий стиль]

Анимации из Mixamo: Sleeping (лёжа), Sitting Idle, Walking (old man / drunk walk медленный), Look Around, Angry, Searching.

### Толстый кот (NPC)
> very fat lazy orange tabby cat, round body, small head, tiny paws, fluffy tail, half-closed sleepy eyes, red collar with golden bell + [общий стиль, без «A-pose»: «standing on four legs, side view»]

Анимации: Idle, Sleep (свернулся), Walk, Hiss/Alert. Если генерация не ригается — брать кота из Quaternius.

## Ключевые предметы

### Велосипед в рождественской обёртке (огромный подарок)
> kid's bicycle clumsily wrapped in shiny Christmas wrapping paper with red and green pattern, big knitted bow on handlebars, wheels partially visible through the paper, tape pieces, funny oversized gift + [общий стиль, «game prop» вместо «game character»]

### Мешок за спиной
> burlap Santa sack, tied with red rope, slightly lumpy with gifts inside, patched + [общий стиль, prop]

### Подарки (3–4 шт.)
> small Christmas gift box wrapped in [red with white dots / green with gold stars / blue with snowflakes] paper, satin ribbon cross, fluffy bow on top + [общий стиль, prop]

### Шумный подарок
> Christmas gift box with a big wind-up key sticking out of the side, slightly shaking, red striped paper + [общий стиль, prop]

### Реактивный ранец эльфа
> tiny steampunk jetpack made of two copper thermos tanks, leather straps, brass nozzles, candy-cane striped details + [общий стиль, prop]

## Проверка перед импортом

- Масштаб: эльф ~1.1 м, Дед ~1.75 м, кот ~0.4 м в высоту.
- Pivot (точка опоры) внизу у ног, лицо по оси +Z.
- Нет лишних кусков фона, дыр в сетке, текстуры не «поплыли».
- Если не уверен — кидай файл мне, проверю перед импортом.
