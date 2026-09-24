# Автодром из схемы → данные для Unity

Скрипты переводят схему `docs/autodrome-schema-100m.png` (100 × 100 м, 19,17 px/м)
в `Assets/DrivingSchool/Data/Training/autodrome-schema.json` и текстуры знаков
`Assets/DrivingSchool/Art/TrainingKit/SignFaces/*.png`.

Запуск (Python 3 с numpy, scipy, opencv-python, scikit-image, networkx, matplotlib, Pillow):

```
cd tools/autodrome_from_schema
cp ../../docs/autodrome-schema-100m.png schema_hi.png
python masks.py && python fix.py && python geom.py && python posts.py && python postfilter.py \
  && python marks3.py && python gen_data.py && python signs_tex.py && python preview.py
cp out/autodrome-schema.json ../../Assets/DrivingSchool/Data/Training/
cp out/SignFaces/*.png ../../Assets/DrivingSchool/Art/TrainingKit/SignFaces/
```

Затем в Unity: **Driving School / Training Ground / Build scene and models**.

| Скрипт | Что делает |
|---|---|
| masks.py | маска проезжей части; прямоугольники `remove` убирают дубли упражнений (второй У4, У5, У6, У7) |
| fix.py | ручные правки маски: эстакада У3, навес, переезд, ровные края прямых участков, выемки от знаков |
| geom.py | контуры и триангуляция покрытия |
| posts.py, postfilter.py | стойки (белые точки на схеме) → конусы |
| marks3.py | линии края, осевые, стоп-линии, зебры |
| gen_data.py | меш разметки, стрелки 1.18 по шаблону, **таблица знаков и светофоров** (правится вручную) |
| signs_tex.py | лицевые стороны знаков, которых нет среди моделей: 3.24 «20», 4.6, 3.18.2, 4.1.4, 1.12, 1.13, 1.14, табличка 8.13 |
| preview.py | `plan_preview.png` — контроль результата сверху |

Координаты: север = +Z, центр участка (0, 0), X/Z от −50 до +50 м.
Знак в таблице задаётся позицией стойки и курсом движения, к которому он относится
(0 = на север, 90 = на восток); лицевая сторона разворачивается навстречу этому потоку.
