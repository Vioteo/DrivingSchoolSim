# Пешеходы: модели и анимации

Набор `DS_Pedestrian_*` — реалистичные люди на базе **MakeHuman (CC0)**: анатомический меш с морфами пола, возраста и телосложения, текстуры кожи, подогнанная одежда, обувь, причёски, брови и ресницы. У всех общий игровой скелет и зацикленные клипы «на месте». Навигации, ИИ и логики перехода здесь нет (это T17): набор даёт только внешний вид и анимации.

Источники, закреплённые версии и лицензия — `ArtSource/Pedestrians/MakeHuman/README.md`.

## Состав

| Ассет | Кто | Одежда и причёска (MakeHuman) | Клипы |
|---|---|---|---|
| `DS_Pedestrian_A` | мужчина ~27 лет | куртка и джинсы `male_casualsuit05`, `shoes01`, `short02` | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_B` | женщина ~32 лет | блузка и юбка `female_elegantsuit01`, сапоги `shoes03`, `ponytail01` | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_C` | мужчина ~35 лет с рюкзаком | комбинезон `male_worksuit01`, `shoes02`, `short01` | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_Child_A` | школьник ~8 лет с ранцем | футболка и джинсы `male_casualsuit06`, кеды `shoes06`, `short03` | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_Child_B` | школьница ~8 лет | `female_casualsuit01`, `shoes05`, коса `braid01` | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_Police` | инспектор ДПС | костюм `male_elegantsuit01`, перекрашенный в тёмно-синий, `shoes03`, `short01`; жилет, фуражка и жезл строятся скриптом | Idle, Walk, LookAround, Signal_ArmsSide, Signal_RightArmForward, Signal_ArmUp |

Рост, треугольники, число материалов, длительность клипов и «естественная» скорость каждого цикла — в `artifacts/reports/pedestrians-manifest.json`.

Стилизовано: форма, жилет и надпись «ДПС» не воспроизводят нормативный образец и служат только для узнаваемости.

## Сборка модели (`tools/pedestrian_mh.py`)
1. Макро-морфы MakeHuman (пол, возраст в годах, мускулатура, вес, рост, пропорции, этничность) смешиваются по тем же весам, что в MakeHuman 1.x, для всего базового меша вместе со вспомогательной геометрией.
2. Одежда, обувь, волосы, брови, ресницы, глаза подгоняются по `.mhclo`: каждая вершина — взвешенная сумма трёх вершин тела плюс масштабированное смещение. Грани тела под одеждой (`delete_verts`) удаляются.
3. Веса скиннинга: стандартные веса 163 костей MakeHuman суммируются на 20 наших костей. Одежда наследует их через опорные вершины. Суставы скелета берутся из меток суставов MakeHuman.
4. MakeHuman смоделирован в A-позе: руки опускаются до 10° от вертикали, ноги сводятся примерно на ширину таза. Эта поза становится позой покоя, и клипы на неё рассчитаны.
5. Полицейский: жилет — гладкая оболочка тела MakeHuman (`helper-tights`) поверх пиджака, без рукавов, с ровными краями и двумя световозвращающими полосами, надпись «ДПС» на спине. Фуражка подгоняется по голове, жезл — в правой руке.
6. Текстуры уменьшаются до 1024 px (нормали до 512) и пишутся в `Art/Pedestrians/Textures/` (`MH_<источник>_d.jpg|png`, `_n.png`). Рядом с каждым FBX лежит `<имя>.materials.json`: текстуры, альфа-отсечение и двусторонность каждого материала для Unity.

## Скелет и клипы
- Скелет общий для всех (20 костей: `Root`, `Hips`, `Spine`, `Chest`, `Neck`, `Head`, `UpperLeg/LowerLeg/Foot_L/R`, `Shoulder/UpperArm/Forearm/Hand_L/R`). Имена костей — контракт с `PedestrianAssetBuilder` (скорость клипа считается по `Foot_L/R`).
- Все клипы зациклены (последний кадр = первый), корень на месте, самая низкая вершина прижата к полу на каждом кадре (у `Run` — кроме фазы полёта, подъём до 4.5 см). 30 кадров/с.
- `Walk` (взрослый: 1.0 с на цикл, ≈1.3 м/с; ребёнок: 0.8 с, ≈1.2 м/с), `Run` (0.73 с, ≈3.7 м/с; ребёнок 0.6 с, ≈3.1 м/с). Скорость, при которой ноги не скользят, — медиана скорости опорной стопы в фазе контакта; генератор и Builder считают её независимо, точные значения — в manifest и `pedestrians-unity.txt`.
- `LookAround` — проверка дороги перед переходом при правостороннем движении: налево, направо, снова налево.
- `Signal_*` — позы регулировщика (удерживаются, с дыханием), жезл в правой руке:
  - `Signal_ArmsSide` — руки вытянуты в стороны;
  - `Signal_RightArmForward` — правая рука вытянута вперёд;
  - `Signal_ArmUp` — рука поднята вверх.

  Позы взяты из перечня сигналов регулировщика, п. 6.10 ПДД РФ (утв. Постановлением Правительства РФ от 23.10.1993 № 1090). Редакция при создании не сверялась. **Значение сигнала для участников движения (кому и куда разрешено) — не часть этого набора**: правила регулировщика реализуются в `Rules` отдельной задачей со ссылкой на конкретную редакцию.

## Unity
`Driving School → Pedestrians → Import models and create showroom` (`PedestrianAssetBuilder.Build`) для каждого ассета:
1. настраивает импорт (Generic, клипы по имени take, все зациклены); материалы URP/Lit в `Materials/Pedestrians/` собирает по `<имя>.materials.json`: текстура цвета, карта нормалей, альфа-отсечение и двусторонний рендер для волос, бровей и ресниц;
2. измеряет естественную скорость `Walk`/`Run` по стопам и проверяет её правдоподобие;
3. проверяет рост и контакт с полом на 31 кадре **каждого** клипа;
4. пересоздаёт `Prefabs/Pedestrians/<имя>.controller`: blend tree `Locomotion` (Idle 0 → Walk → Run по параметру `Speed`, пороги = измеренные скорости), состояние `LookAround` (bool, только стоя), у полицейского — `Signal_*` по int `Signal` из Any State, `Signal = 0` возвращает в `Locomotion`;
5. собирает префаб: Animator, `PedestrianAnimator`, CapsuleCollider;
6. сохраняет `Scenes/PedestrianShowroom.unity` и рендер `artifacts/visual-review/pedestrians/unity-lineup.png`.

`PedestrianAnimator` (`Code/Presentation`) — связь с тем, кто двигает пешехода:
- `Speed` берётся из перемещения transform по XZ (или `SpeedOverride`), скачок быстрее 12 м/с считается телепортом и игнорируется;
- быстрее самого быстрого клипа — клип проигрывается быстрее, а не скользит;
- `LookAround = true` — оглядеться у бордюра; `Signal = RegulatorSignal.*` — жест полицейского.

## Как пересобрать
```powershell
python tools/fetch_makehuman.py      # один раз: исходники MakeHuman в ArtSource/Pedestrians/MakeHuman/cache
& "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --python tools/build_pedestrians.py
& "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --python tools/verify_pedestrians.py
& $u -batchmode -projectPath $p -executeMethod DrivingSchool.Editor.PedestrianAssetBuilder.Build -quit -logFile "$p/artifacts/reports/pedestrians-unity.log"
```
Оба скрипта также запускаются обычным Python с модулем `bpy` 5.0 (`pip install bpy==5.0.0`, Python 3.11).
Рендеры Blender: `artifacts/visual-review/pedestrians/` (`*-front/rear/walk/run/look.png`, жесты полицейского, `lineup.png`), GLB для просмотра — там же в `models/`.

## Ограничения
- LOD не подготовлены: ≈29–31 тыс. треугольников на человека, у полицейского больше (точные числа в manifest). Для толпы нужны LOD1/LOD2 — отдельная задача.
- Пальцы не анимируются: в игровом скелете одна кость кисти, кисть в позе покоя MakeHuman (полураскрытая).
- Одежда без симуляции ткани, юбка следует за ногами через веса.
- Футболки `female_casualsuit01` и `male_casualsuit06` несут принт с логотипом MakeHuman из исходной текстуры.
- Нет разворотов на месте, старта/остановки, бега у полицейского; повороты делает агент поворотом transform.
- Префаб — вариант FBX без обёртки `Model` (как было до этой задачи), `bakeAxisConversion` не включён — отдельная правка по `art-pipeline.md`, раздел 10.
