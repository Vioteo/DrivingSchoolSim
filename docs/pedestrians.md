# Пешеходы: модели и анимации

Набор `DS_Pedestrian_*` — стилизованные люди с общим скелетом и зацикленными клипами «на месте». Навигации, ИИ и логики перехода здесь нет (это T17), набор даёт только внешний вид и анимации.

## Состав

| Ассет | Кто | Рост, м | Клипы |
|---|---|---|---|
| `DS_Pedestrian_A` | мужчина, бирюзовая куртка | 1.79 | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_B` | женщина, оранжевая куртка | 1.79 | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_C` | мужчина с рюкзаком | 1.79 | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_Child_A` | школьник с ранцем (~8 лет) | 1.26 | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_Child_B` | школьница с хвостиками (~8 лет) | 1.26 | Idle, Walk, Run, LookAround |
| `DS_Pedestrian_Police` | инспектор ДПС: форма, жилет со световозвращающими полосами, фуражка, жезл | 1.82 | Idle, Walk, LookAround, Signal_ArmsSide, Signal_RightArmForward, Signal_ArmUp |

Точные рост, треугольники, длительность клипов и «естественная» скорость каждого цикла — в `artifacts/reports/pedestrians-manifest.json`.

Стилизовано: форма, жилет и надпись «ДПС» не воспроизводят нормативный образец и служат только для узнаваемости.

## Скелет и клипы
- Скелет общий для всех (20 костей: `Root`, `Hips`, `Spine`, `Chest`, `Neck`, `Head`, `UpperLeg/LowerLeg/Foot_L/R`, `Shoulder/UpperArm/Forearm/Hand_L/R`). Имена костей — контракт с `PedestrianAssetBuilder` (скорость клипа считается по `Foot_L/R`).
- Дети строятся в координатах взрослого и деформируются: рост ×0.68, ширина ×1.07, голова дополнительно ×1.26 — у ребёнка крупнее голова относительно тела.
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
1. настраивает импорт (Generic, клипы по имени take, все зациклены), материалы URP/Lit в `Materials/Pedestrians/`;
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
& "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --python tools/build_pedestrians.py
& "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --python tools/verify_pedestrians.py
& $u -batchmode -projectPath $p -executeMethod DrivingSchool.Editor.PedestrianAssetBuilder.Build -quit -logFile "$p/artifacts/reports/pedestrians-unity.log"
```
Оба скрипта также запускаются обычным Python с модулем `bpy` 5.0 (`pip install bpy==5.0.0`, Python 3.11).
Рендеры Blender: `artifacts/visual-review/pedestrians/` (`*-front/rear/walk/run/look.png`, жесты полицейского, `lineup.png`), GLB для просмотра — там же в `models/`.

## Ограничения
- LOD не подготовлены (≈6–8 тыс. треугольников на человека).
- Жёсткая привязка деталей к костям: при поднятой руке плечо деформируется грубо (стиль «манекен»).
- Нет разворотов на месте, старта/остановки, бега у полицейского; повороты делает агент поворотом transform.
- Префаб — вариант FBX без обёртки `Model` (как было до этой задачи), `bakeAxisConversion` не включён — отдельная правка по `art-pipeline.md`, раздел 10.
