# Где искать ассеты

Актуальные игровые файлы находятся в `Assets/DrivingSchool`, исходники — `ArtSource`, генераторы — `tools` и `Code/Editor`. Бэкапы не являются источником истины: старое состояние восстанавливается из Git.

- **Автомобили:** `Art/DS_Sedan_A.fbx`, `Art/Vehicles`, `Prefabs/Vehicles`; каталог `Data/Vehicles/vehicles.json`. Генераторы `tools/build_sedan_*.py`, `tools/vehicle_kit` и `VehicleAssembler.cs`. Осторожно: текущий build_vehicles.py не поддерживает описанный в T50 аргумент ID:lo и может перезаписать канонический седан.
- **Дороги:** `Art/RoadKit`, `Art/RoadKitV2`, соответствующие Prefabs и Materials. Скрипты `build_road_kit.py`, `build_road_kit_v2.py`, `RoadKitBuilder.cs`. Оба набора действующие, V2 дополняет первый.
- **Автодром:** `Art/TrainingKit`, `Prefabs/TrainingKit`, `Data/Training`; `TrainingGroundBuilder.cs` и `TrainingGroundLayout.cs`. Актуальные процедурные меши имеют префикс TG_. Старые именованные куски дорог без зависимостей — кандидаты на удаление после подтверждения.
- **Знаки и светофоры:** `Art/Traffic`, `Prefabs/Traffic`, `Materials/Traffic`; `build_traffic_assets.py`, `TrafficAssetBuilder.cs`. Не все варианты стоят в игровых сценах; это библиотека для генераторов и TrafficShowroom.
- **Дома:** `Art/Houses`, `Prefabs/Houses`, `ArtSource/Houses`; `HouseKitBuilder.cs`. Суффикс _v1 в старых исходниках не доказывает ненужность.
- **Общественный транспорт:** `Art/Transit`, `Prefabs/Transit`, `Materials/Transit`; `tools/transit_kit`, `TransitKitBuilder.cs`, сцена Transit_Demo. Это действующий набор для демонстрации, хотя сцена не включена в обычную сборку.
- **Пешеходы:** `Art/Pedestrians`, `Prefabs/Pedestrians`, `Materials/Pedestrians`; `build_pedestrians.py`, `PedestrianAssetBuilder.cs`. Источники и лицензии MakeHuman сохранять.
- **Железная дорога и препятствия:** `ArtSource/DS_RailwayKit.blend`, `Art/TrainingKit/TK_Railway*`, `Art/SpeedBumps`, `Art/Props`; генераторы `build_railway_assets.py`, `build_speed_bumps.py` и `build_traffic_cone.py`. Неиспользуемый резиновый порог 3,5 м — вариант набора, не дубль 7 м.
- **UI:** `Prefabs/UI`, `Art/Fonts`, `Data/UI`, `Code/Presentation/UI`, `UIBuilder*.cs`. LessonCatalog становится рабочим экраном заданий в T53. ConditionsSetup/G27Calibration/TheoryExam — старые макеты, не готовые функции.
- **Схемы и референсы:** пока в корне (`schema.jpg`, `quad_*.png`); запланирован перенос в `docs/references`; GLB и рендеры — `artifacts/visual-review`; результаты тестов — `artifacts/reports`.

## Аудит и очистка

`tools/AuditAssets.cs` выполняется установленным Unity CLI:

```powershell
& 'C:/Program Files/Unity Hub/resources/cli/unity.exe' command run_script --file tools/AuditAssets.cs --entry AuditAssets.Main
```

Пишет `artifacts/reports/asset-dependencies.json`: входящие зависимости и достижимость из сцен Build Settings. Последняя не учитывает Resources, ссылки в ProjectSettings и программную загрузку; это сигнал для проверки, не автоматический список удаления.

`AuditAssets.PruneLegacyTrainingMeshes` удаляет только старые Mesh в корне Art/TrainingKit, не TG_*, с нулём входящих ссылок; проверяет также текстовые GUID-ссылки Assets/ProjectSettings и несохранённые сцены. Запускать на чистом дереве; изменения ассетов сохранять отдельным `gen:` коммитом с командой. Остальные модели не удаляются этим инструментом.

Аудит 28.09: проверено 669 моделей/префабов/материалов/изображений/ассетов/сцен. Найдено 72 устаревших процедурных меша автодрома (369 442 байта без meta). Демо-сцены и нерасставленные варианты наборов сохранены. Отдельные старые варианты седана пока связаны с историческими скриптами проверки и требуют согласованного вывода из конвейера.

Обычный `rg` исключает архив `.agents` и результаты `artifacts` через `.rgignore`. Для истории и доказательств используйте `rg --no-ignore <текст> artifacts`. `.agents` не изменялся.

Исходное состояние текущей работы сохранено в ветке `chore/T50-T51-checkpoint` (166be2e, f00e1c6, 1b37dd6); несохранённые ранее материалы — в локальном stash `9ea5ed8a76069938b7497622ca442ba85868c4a2`. Stash не отправляется обычным git push. Не применять его целиком поверх новых изменений; извлекать нужные файлы по отдельности.

Часть Blender-генераторов пока продолжает создавать ArtSource_Backup. Это отдельный известный дефект; папки исключены из Git/поиска, повторное появление не делает их каноническими исходниками.
