"""Create the focused evidence page after renders, inspection and export checks."""
import json, sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
web=root/'artifacts/visual-review'
out=web/'shell-repair'
visual_pass='--visual-pass' in sys.argv
coverage=json.loads((root/'artifacts/reports/shell-coverage-check.json').read_text(encoding='utf-8'))
export=json.loads((root/'artifacts/reports/shell-export-check.json').read_text(encoding='utf-8'))
verdict='PASS' if visual_pass and coverage['pass'] and export['pass'] else 'НЕ ПРИНЯТО'
template=Path(r'C:\Users\AVSok\.agents\skills\visual-demo-page\template.html').read_text(encoding='utf-8')
replacements={
'taskTitle':'Седан · передняя часть и днище',
'whatChanged':'Точечный ремонт исходника DS_Sedan_A_reviewed.blend скриптом Blender. Сопоставлены реальные рендеры с одинаковыми камерами и светом. Исходная модель сохранена без перезаписи.',
'beforeImage':'before-front-low.png','afterImage':'after-front-low.png',
'requirementClass':'pass' if visual_pass else 'warn','requirementResult':'PASS' if visual_pass else 'Ожидает осмотра',
'requirementNote':'Передняя щель закрыта отдельной геометрией; проверены нижний и косой виды.',
'layoutClass':'pass' if coverage['pass'] else 'warn','layoutResult':'PASS' if coverage['pass'] else 'FAIL',
'layoutNote':'17 ограниченных лучей проверяют наличие поверхностей в местах прежних просветов. Это выборочная проверка.',
'consistencyClass':'pass' if export['pass'] else 'warn','consistencyResult':'PASS' if export['pass'] else 'FAIL',
'consistencyNote':'FBX и GLB повторно импортированы в Blender; проверены габариты, привязки колёс, руля, педалей и зеркал.',
'minimalityClass':'pass','minimalityResult':'Проверяемые файлы',
'minimalityNote':'Пары ДО/ПОСЛЕ показывают передок и днище. Дополнительные кадры — общий вид и место для ног.',
'finalVerdict':verdict+' · только ремонт указанных просветов',
'residualRisk':'Модель остаётся черновиком. Качество остальных стыков, работа подвески и поворота колёс, физика, зеркала и рендер Unity требуют отдельной приёмки. Подробная механика подвески и двигателя не моделировалась.'}
for key,value in replacements.items():template=template.replace('{{'+key+'}}',value)
template=template.replace('Целостность layout:','Покрытие просветов:').replace('Визуальная консистентность:','Обменные форматы:').replace('Минимальность доказательств:','Материалы проверки:')
extra='''<section class="grid"><article class="card"><p class="label">Днище · до</p><img class="shot" src="before-underside.png" alt="Просветы по краям пола до ремонта"></article><article class="card"><p class="label">Днище · после</p><img class="shot" src="after-underside.png" alt="Закрытые края днища после ремонта"></article></section>
<section class="grid"><article class="card"><p class="label">Косой вид · до</p><img class="shot" src="before-front3quarter.png" alt="Исходный передний ракурс"></article><article class="card"><p class="label">Косой вид · после</p><img class="shot" src="after-front3quarter.png" alt="Новый передний ракурс"></article></section>
<section class="grid"><article class="card"><p class="label">Педали и пол · после</p><img class="shot" src="after-cockpit-floor.png" alt="Педали остаются доступными над полом"></article><article class="card"><h2>Файлы для проверки</h2><ul>
<li><a href="../../../ArtSource/DS_Sedan_A_closed_shell.blend">Новая модель Blender</a></li><li><a href="viewer.html">Вращать новую модель в 3D</a></li><li><a href="../models/DS_Sedan_A_closed_shell.glb">GLB</a> · <a href="../../../Assets/DrivingSchool/Art/DS_Sedan_A_closed_shell.fbx">FBX</a></li><li><a href="../../reports/shell-repair.md">Отчёт исполнителя</a></li><li><a href="../../reports/shell-export-check.json">Проверка экспорта</a> · <a href="../../reports/shell-coverage-check.json">Проверка просветов</a></li><li><a href="../../../docs/model-iteration.md">Порядок следующих доработок</a></li></ul><p>Внизу показан упрощённый закрывающий кузовной узел. Это не детальная модель агрегатов автомобиля.</p></article></section>'''
template=template.replace('      <section class="qa">',extra+'      <section class="qa">').replace('</style>','a{color:#a8d5ff}li{margin:8px 0}.card p{line-height:1.6;color:#b6c0e3}</style>')
(out/'index.html').write_text(template,encoding='utf-8')
viewer=(web/'viewer.html').read_text(encoding='utf-8').replace('./vendor/','../vendor/').replace('models/DS_Sedan_A.glb','../models/DS_Sedan_A_closed_shell.glb').replace('DS Sedan A — осмотр 3D','DS Sedan A — закрытие кузова').replace('DS SEDAN A / GLB','SHELL REPAIR / GLB').replace('← Визуальная лаборатория','← Ремонт кузова: до и после')
(out/'viewer.html').write_text(viewer,encoding='utf-8')
index=web/'index.html'
main=index.read_text(encoding='utf-8')
if 'shell-repair/index.html' not in main:
    main=main.replace('<main>','<main><section class="section"><div class="banner"><strong>19.09 · Новая версия кузова</strong><p>Передняя часть и днище: рендеры до/после, проверка экспорта и отдельная модель.</p><a class="button primary" href="shell-repair/index.html">Открыть результат ремонта →</a><p>Остальные изображения ниже показывают предыдущую версию v2.</p></div></section>',1)
    index.write_text(main,encoding='utf-8')
print('SHELL_REVIEW_PAGE',verdict)
