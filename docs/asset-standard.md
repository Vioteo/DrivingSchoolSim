# Стандарт ассетов и доказательств

Все числовые бюджеты ниже **целевые**, до импорта/профилирования не являются измеренными. Доработкой модели занимается отдельный арт-исполнитель. Документ не разрешает кодовому исполнителю перезаписывать .blend ради прохождения теста.

## Источник и экспорт

Исходник: `ArtSource/DS_Sedan_A.blend`, аналогично district/autodrome. Целевой обменный каталог: `Art/Exports/<asset>/<revision>/` с FBX для Unity и GLB для обозрения. **Текущий pipeline** импортирует FBX непосредственно из `Assets/DrivingSchool/Art/DS_Sedan_A.fbx`, `DS_District.fbx`, `DS_Autodrome.fbx`; GLB находится в `artifacts/visual-review/models`. До отдельной миграции T02 эти пути не переименовывать.

На ревизию: .blend, exports, manifest с SHA256/UTC/exporter/version/units/axis/triangles/material slots/textures, measurement report, screenshots. Помещать review evidence в `artifacts/visual-review`, машинные отчёты — `artifacts/reports`. Каждый скриншот помечает Blender/Web/Unity и revision. Generated reports не подменяют source.

Unity prefab root: identity transform, scale1, SI metres; X right, Y up, Z forward. Blender исходник может быть Z-up; правильность conversion устанавливается импортом осей, габаритов и pivots, а не только опциями FBX. Не полагаться на догадку ориентации по Roof/Cabin_Floor как долгосрочный importer contract.

## Совместимость имён текущего каркаса

Сохранить уникальные `Wheel_FL`, `Wheel_FR`, `Wheel_RL`, `Wheel_RR`, `SteeringWheel_Pivot`, `Pedal_Throttle`, `Pedal_Brake`, `Pedal_Clutch`, `Socket_DriverEye`, `Socket_CentreOfMass`, `MirrorSurface_L`, `MirrorSurface_R`, `MirrorSurface_Centre`, `Transmission_Manual`, `Transmission_Automatic`, `Roof`, `Cabin_Floor`. `Needle_*` и `Wiper_Pivot*` используются prefix matching. Leaf decorative objects не должны случайно совпадать с animation prefix. У wheels нужны отдельные steer/spin/suspension pivots в будущем; существующий name adapter мигрируется явно.

Socket_DriverEye — точка вида, не камера, привязка к автомобилю в local space. CentreOfMass — marker предполагаемой настройки, не доказанное измерение. Manual variant: clutch/brake/throttle; automatic variant: brake/throttle с управляемой видимостью ручного варианта. Все педали отдельные, а не один animated mesh.

## Геометрическая приёмка автомобиля

- **G01 размеры/оси:** world-space bounds кузова, зеркал, wheelbase, track и радиуса шины из evaluated mesh и Unity import. Сравнить с vehicle target, записать отклонение и решение; 1unit=1m, ground contact и направление forward видимы на orthographic views.
- **G02 поверхность:** exterior/front/rear/sides/top/bottom; отсутствие случайных щелей, inverted normals, z-fighting, самопересечений и floating parts. Намеренные панели/щели маркируются. Wireframe и close-up обязательны для спорных мест; silhouette screenshot недостаточен для intersection claim.
- **G03 коллизии:** отдельная упрощённая collision geometry корпуса, колёса и опорные поверхности. В Unity collision debug overlay и contact test с полом/бордюром/стеной. Не ставить concave dynamic mesh collider на каждую декоративную деталь; отсутствие коллайдера на визуально закрытой двери — fail. Упрощение принимается по gameplay gap/penetration, не числу render triangles.
- **G04 салон:** вид от Socket_DriverEye и обзор головы в допустимом диапазоне; руль/приборы/сиденья/пол/туннель/стёкла/крепления/стойки не проникают друг в друга; приборы читаются на целевой камере. Проверка сечениями нужна для мест, закрытых trim. Салон полный для заднего обзора/центрального зеркала.
- **G05 три зеркала:** физически отдельные surfaces и housing, исправные normals/UV; слева/по центру/справа есть обзоры с места водителя. Отдельно проверить правильную отражённую сцену маркерами L/R и движущимся объектом в Unity. Блестящий material или статическая картинка не является отражением.
- **G06 педали:** throttle/brake/clutch в released/mid/full travel, зазор до пола, соседних педалей и ноговой зоны. Pivot и направление проверены. Sweep evaluated meshes с минимум 11 шагами; снимки endpoints и минимальный измеренный gap. Текущий shared slider не доказывает независимый ход.
- **G07 колёса в нейтрали:** все четыре контакта/центры, clearance до подкрылков/кузова/подвески, симметрия и покрытие кузовом. Радиальный scalar gap проверяет только принятую аппроксимацию.
- **G08 полный ход колёс:** steering −max…+max и suspension rebound…bump для передних, suspension для задних; все углы комбинаций и промежуточная сетка минимум 9 steering × 9 travel × 4 колеса плюс экстремальные позы. Радиус и реальная ширина шины включены. Проверить tire vs body/liner/strut/brake, contact metrics и screenshots минимум full-left/full-right/full-bump/full-rebound. Если suspension limits ещё не утверждены — статус BLOCKED/PENDING, не PASS. Положительный зазор по одной окружности не доказывает mesh sweep.

G01–G08 выполняются повторно для финального экспортированного asset hash. Проверки с заведомо уменьшающим зазор fixture должны обнаружить пересечение. Анимация в Blender не подтверждает imported pivot; итоговые endpoints проверяются также Unity.

## Окружение и целевые бюджеты

Сетка привязки roads: метры, модуль дороги разделён на surface/curb/markings/collision, без наложенной независимой логической карты. Каталожные объекты имеют stable catalogId, pivot на осмысленной точке размещения, bounds, collision recipe и LOD metadata. District mesh не заменяет 256-м chunk model.

Стартовые бюджеты для согласования после profiler:

- Player exterior LOD0 ≤80k triangles; cockpit отдельный ≤60k; общий видимый player ≤140k. LOD1 exterior ≤35k, LOD2 ≤12k, LOD3 ≤3k. Interior сохраняется при cockpit view; дальний AI не получает полноценный салон.
- Traffic vehicle LOD0 ≤35k, LOD1 ≤15k, LOD2 ≤5k, LOD3 ≤1.5k triangles. Не обязательно использовать player mesh как traffic source.
- Player ≤12 material slots, exterior textures преимущественно2K, cockpit до4K по читаемости; background props1K/2K. Это цели, не соответствие текущему procedural material count.
- Near-world on-screen budget стартово ≤2M triangles и ≤1500 batches в desktop stress route; allocations после warm-up стремятся к0B/frame для tick logic. Итоговые пределы корректируются по CPU/GPU, особенно трём зеркалам.
- Mirror RT start512×256 mono совпадает с прототипом; бюджет обновления, разрешение и stereo profile утверждаются T23–T25. Не делать три полных world renders на каждый eye без измерений.

Triangle report должен считать evaluated/exported/imported meshes и каждую LOD группу, distinct materials, memory estimate и actual renderer stats. `trianglesBase` не заменяет эти измерения. Отсутствующие LODGroup/LOD exports маркировать «не подготовлено».
