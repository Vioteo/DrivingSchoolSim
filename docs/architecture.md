# Архитектура

Этот документ описывает целевой дизайн поверх малого каркаса; существующие сигнатуры перечислены в [data-formats](data-formats.md), статус — в [current-state](current-state.md). Новые имена адаптеров ниже являются предложениями, не доступными API.

## Границы и зависимости

`DS.Contracts` не зависит от Unity и содержит сериализуемые DTO и порты. `DS.Simulation`, `DS.Rules`, `DS.Learning` — pure C# с noEngineReferences; каждая зависит от Contracts. `DS.World` сейчас использует Unity JsonUtility и Contracts. `DS.Input` зависит от Contracts и Unity.InputSystem. `DS.Presentation` связывает перечисленные модули, Unity сцены и отображение. `DS.Editor` только Editor; `DS.Tests` сейчас Editor-only.

Ввести отдельную Unity-адаптерную сборку для Rigidbody/контактов/покрытия. Solver должен принимать числовые снимки и возвращать силы/моменты; адаптер применяет их в FixedUpdate. Установка физики/контактов в pure-модуль или использование ModelDemonstrator как controller автомобиля нарушает границу. Не вводить новую сборку до конкретной задачи, требующей её.

Предлагаемые сервисы трафика/пешеходов потребляют immutable roadgraph snapshot; визуальные экземпляры живут в Unity adapter. Runtime editor вызывает world command service и transaction validator. UI передаёт намерения через application coordinator, не правит VehicleState и не читает Rigidbody напрямую. UI не должен управлять физическим tick.

## Один simulation tick

1. Unity coordinator получает IInputSource.Read(tick), проверяет DriverCommand.Validate, применяет выбранный профиль ассистов. Disconnect даёт явный безопасный переход, а не последний газ.
2. Solver обновляет engine/clutch/transmission, затем числовые команды колёсам. Unity adapter применяет силы/торможение, получает контакты и публикует VehicleState для следующего этапа. Порядок закрепляется тестом; не обещать bitwise deterministic PhysX между машинами.
3. Rule evaluation читает положение/полосу/сигнал/покрытие и создаёт RuleEvent только при переходе/новом нарушении. Learning обновляет критерии занятия и terminal result. TimeSource при паузе не увеличивает simulationSeconds.
4. Presentation интерполирует состояние, обновляет приборы/руль/педали и камеры. FFB вычисляется из физических величин через отдельный ограничитель и IForceFeedbackOutput. Render FPS не должен умножать RuleEvent или изменять время упражнения.

Текущие .01 s из Prepare — начальная настройка 100 Hz; устойчивость, стоимость и настройки substep ещё измерить. Команда и snapshot получают tick; sequence не приравнивать автоматически к tick без решения по replay/network. Сетевой режим не входит в текущий scope.

## Координаты, чанки и мир

Unity X вправо, Y вверх, Z вперёд, SI. Канонические позиции мира — double, начало мира фиксированно. `localPosition = worldPosition - origin` с преобразованием в float только на Unity-границе. Перенос origin атомарен на fixed boundary: двигаются активные Rigidbody, камеры, particles и streaming anchors; не меняются canonical graph, сохранения и evidence. Скорости/импульсы не обнуляются. В VR origin shift не заменяет пользовательскую recenter операцию.

256-м cell индексируется floor(x/256), floor(z/256), включая отрицательные координаты. 10 000 м не кратно 256: граничные чанки содержат clipping/coverage, размер не округляется молча до 10 240 м. Graph edges могут пересекать несколько чанков; chunk descriptor хранит references, не создаёт дубликат road identity. Граница чанка не является перекрёстком.

Стартовый проектный радиус: 3 × 3 активных collision cells, 5 × 5 подготовленных/видимых cells; это гипотеза для профилирования, не фиксированное ограничение. Предзагрузка учитывает скорость и маршрут. Graph/collision readiness — барьер перед въездом; на дефицит загрузки реагировать контролируемой паузой/экраном, не падением сквозь дорогу. Visual LOD, AI simulation distance и collision radius независимы.

## Единственный источник дорожной истины

Авторство меняет world document. Validator → immutable compiled graph revision → mesh/colliders, route queries, lane-following, rule zones. Curve tessellation для изображения не становится другим маршрутом AI. Stop lines, markings, signs, signals и crossings имеют IDs и ссылки на lane/segment. Генерация derived meshes кешируется по content hash и параметрам, не сериализуется в качестве канонического графа.

Изменение кривой через runtime editor проходит validate/commit; при ошибке остаётся прежняя ревизия. Undo восстанавливает данные, затем пересобирает только затронутые чанки. Save блокирует неподдерживаемую версию и не теряет поля через JsonUtility silently. Импорт внешней карты — будущая отдельная задача.

## Ввод, FFB, VR и UI

Keyboard и wheel adapter выдают один DriverCommand. Raw G29 bindings не предполагать по имени устройства: калибровка показывает live axes, инверсию, min/centre/max, dead zones, clutch semantics и H pattern. Combined pedals не считать тремя независимыми осями. Backend FFB для Windows — отдельная dependency за интерфейсом, с обоснованием лицензии и доставки DLL; наличие InputSystem не означает поддержку FFB.

Существующий IMGUI — технический стенд. Девять экранов HTML-прототипа служат визуальной спецификацией; перенос выполняется отдельно в uGUI/TMP с InputSystemUIInputModule, focus/navigation, Russian font fallback и сохранением настроек. Отключённые будущие функции помечаются явно; кнопка не изображает рабочую поездку без solver.

PCVR использует OpenXR и отдельный rig/profile. Desktop input не отключается автоматически при включении XR. Три зеркала должны иметь проверенное stereo/per-eye решение; текущий mono PlanarMirror — демонстрационный baseline. UI VR требует world-space размещения/масштаба и читаемости внутри HMD.

## Обучение и контент

Learning получает snapshots/evidence, а не контролирует автомобиль. Упражнение состоит из состояния, целей/зон и evaluator type с version. Каждая финальная запись содержит lesson revision, world revision, vehicle calibration, assists и rule revision. Список полей будущий; текущий SessionResult этих версий не содержит.

Теория из внешнего контента проходит подтверждение источника, даты/редакции, права использования и роли официальности. DTO bool isOfficial сам по себе ничего не подтверждает. Авторский учебный пакет остаётся доступным с заметной подписью. Не смешивать игровые пороги с правовыми нормами.
