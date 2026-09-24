# MakeHuman — исходники пешеходов

Лицензия: **CC0 1.0** (`LICENSE.md` — копия `LICENSE.ASSETS.md` из репозитория MakeHuman). Ассеты MakeHuman переданы в общественное достояние в сентябре 2020 года; это указано в заголовке каждого файла.

Что используется и откуда (закреплено в `tools/fetch_makehuman.py`):

| Что | Источник | Закрепление |
|---|---|---|
| Базовый меш `hm08` (`3dobjs/base.obj`), морфы `targets/macrodetails/**`, скелет `rigs/default.mhskel`, веса `rigs/default_weights.mhw` | https://github.com/makehumancommunity/makehuman, папка `makehuman/data/` | коммит `a8bc2d54ff0ac92e78ff71431b1023eda42bf482` |
| Кожа, одежда, обувь, причёски, брови, ресницы, глаза | `makehuman_system_assets_cc0.zip` с files.makehumancommunity.org | SHA256 `b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107` |
| Сезонная одежда: футболки и свитер, брюки, обувь и сапоги, перчатки, кепка | паки сообщества `shirts01`, `pants01`, `shoes01`, `gloves01`, `hats01` (варианты `_cc0`, https://static.makehumancommunity.org/assets/assetpacks.html) | SHA256 каждого архива в `COMMUNITY` |

Используются только ассеты с лицензией CC0. Паки сообщества под CC-BY сознательно не подключены, потому что требуют указывать авторов.

Файлы скачиваются в `cache/` (в git не хранится, около 400 МБ): `python tools/fetch_makehuman.py`. `tools/build_pedestrians.py` вызывает загрузку сам.

В проект попадают только результаты: FBX, текстуры, уменьшенные до 1024 px, в `Assets/DrivingSchool/Art/Pedestrians/`, а также `.blend` в `ArtSource/Pedestrians/`.
