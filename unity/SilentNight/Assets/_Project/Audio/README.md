# Silent Night — audio clips

`AudioManager` (Scripts/Audio) loads clips with `Resources.Load<AudioClip>("Audio/<name>")`.
Put files in **`Assets/_Project/Audio/Resources/Audio/`** (any format Unity imports: .wav, .ogg, .mp3).
Missing clips are skipped silently (logged once). Random variations: add `<name>_1`, `<name>_2`, … `<name>_8`
next to (or instead of) `<name>` and one is picked at random per play.

## Expected clips

| Name | When | Notes |
|---|---|---|
| `footstep_wood` | Noise: Footstep (wood/tile/default), Landing | short, dry; 4 variants recommended |
| `footstep_carpet` | Noise: Footstep on carpet | soft muffled thump |
| `creak` | Noise: CreakyFloor | wooden floorboard creak |
| `gift_thud` | Noise: GiftImpact | cardboard box hitting floor |
| `toy_tick` | Noise: NoisyGift | wind-up toy / clock tick |
| `jetpack` | Noise: Jetpack | short burst/whoosh (played repeatedly, ~0.2 s cooldown) |
| `meow` | Noise: Cat | cat meow / hiss |
| `knock` | Noise: Object | knocked-over prop |
| `music_menu` | MainMenu | loop |
| `music_hub` | Hub (workshop) | cheerful loop |
| `music_house_cozy` | Delivery, cozy house | quiet sneaky loop |
| `music_house_spooky` | Delivery, spooky house | dark ambient loop |
| `stinger_alert` | Grandpa reaches Chasing | short alarm hit |
| `stinger_caught` | PlayerCaught | short fail sting |
| `jingle_delivered` | DeliveryCompleted | short celebratory jingle |
| `gift_loaded` | Gift loaded into sleigh (Hub) | optional, bell "ding" |
| `gift_delivered` | Gift placed under the tree | optional, sparkle |
| `ui_open` | Hub panel opened | optional, paper/wood click |

Set music clips' import **Load Type = Streaming**, short SFX **Decompress On Load**.

## Free CC0 sources (download manually; check each file's licence)
- Kenney audio packs (CC0): https://kenney.nl/assets?q=audio — *Impact Sounds* (thuds, knocks, footsteps),
  *RPG Audio* (footsteps, creaks), *Interface Sounds* / *UI Audio* (ui_open), *Music Jingles* (jingle, stingers).
- Freesound with the *Creative Commons 0* licence filter: https://freesound.org/search/?f=license:%22Creative+Commons+0%22
  (search "floor creak", "cat meow", "wind up toy", "jetpack", "footstep carpet").
- OpenGameArt CC0 section: https://opengameart.org/art-search-advanced?field_art_licenses_tid[]=4 (music loops, christmas music).
- Sonniss GDC bundles (royalty-free, not CC0 — read the licence) for higher-quality ambience.
