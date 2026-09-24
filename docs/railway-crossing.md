# Железнодорожный переезд (У9), набор моделей v2

Генератор: `tools/build_railway_assets.py` (Blender 5).
Исходник: `ArtSource/DS_RailwayKit.blend`. Отчёт: `artifacts/reports/railway-manifest.json`.
Снимки для проверки: `artifacts/visual-review/railway/*.png`.

```
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --python tools/build_railway_assets.py
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --python tools/build_railway_assets.py -- --no-render
```

Затем в Unity: **Driving School / Training Ground / Build scene and models**.
Для префабов знаков 1.1–1.4 дополнительно: **Driving School / Traffic / Build prefabs and showroom**.

## Модели

| Модель | Папка | Что это |
|---|---|---|
| `TK_RailwayCrossing_Tracks` | Art/TrainingKit | Однопутный участок 1520 мм, 11 м: балласт, ж/б шпалы, рельсы Р65, резинокордовый настил 3,6 × 7,2 м вровень с головками рельсов, бетонные борта, асфальтовые въезды по 3 м (уклон ≈ 7 %). |
| `TK_RailwayBarrier` | Art/TrainingKit | Автоматический шлагбаум: тумба, стрела 4,5 м с красно-белыми секциями по 0,5 м, противовес, три красных фонаря. |
| `TK_RailwaySignal` | Art/TrainingKit | Переездный светофор: две красные головки и бело-лунная сверху на чёрном экране с белой каймой, открытые козырьки, звонок, знак 1.3.1 на мачте. |
| `DS_Sign_RailwayBarrier` / `NoBarrier` | Art/Traffic | Знаки 1.1 и 1.2, треугольник 866 мм. |
| `DS_Sign_SingleTrack` / `MultiTrack` | Art/Traffic | Знаки 1.3.1 и 1.3.2. |
| `DS_Sign_RailwayDist{1,2,3}_{R,L}` | Art/Traffic | Знаки 1.4.1–1.4.6, 350 × 700 мм (типоразмер II). |

Высоты настила: верх головки рельса 0,240 м, настил 0,235 м, конец въезда 0,018 м.

## Соглашения об именах

- `DriveSurface_*` — проезжая поверхность (MeshCollider, слой 9).
- `Detail_*` — рельсы, шпалы, балласт, швы настила; без коллайдера, машину не останавливают.
- Остальные меши набора получают BoxCollider на слое препятствий (10).
- `Boom_Pivot` / `Boom_Tip` — шарнир и конец стрелы. Стрела смоделирована опущенной;
  поднимать поворотом `Boom_Pivot` вокруг оси, перпендикулярной (`Boom_Tip` − `Boom_Pivot`)
  и вертикали; положительный угол поднимает конец стрелы.
- `Lamp_*` — светящиеся диски перед тёмными `Lens_*`: `Lamp_Red_L`, `Lamp_Red_R`, `Lamp_White`
  на светофоре и `Lamp_Boom_*` на стреле. Для мигания включать/выключать их Renderer.
- Материалы `RW_*` описаны в `Art/TrainingKit/RW_Materials.json`; Unity берёт цвета оттуда,
  а не из FBX. Знаки используют общие материалы `Traffic_*`.
- Знаки: `Socket_Front` / `Socket_Up`, `Post`, `Plate_Back` — как у остальных дорожных знаков.

## Расстановка на автодроме

Западная сторона кольца односторонняя (движение на север), поэтому всё обращено к югу:
светофоры справа (−39,3; 20,4) и слева (−48,7; 20,4), знак «Стоп» справа (−39,3; 18,6),
стоп-линия z = 19, пути по центру (−44; 24). Шлагбаумы на сцену не ставятся: без логики
открытия опущенная стрела перегородила бы урок.
