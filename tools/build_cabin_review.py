"""Build a review page from verified Blender captures and measured reports."""
import json, hashlib, html
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/visual-review/cabin-v3'
fit=json.loads((ROOT/'artifacts/reports/cabin-v3-fit.json').read_text())
export=json.loads((ROOT/'artifacts/reports/cabin-v3-export-check.json').read_text())
unity_file=ROOT/'artifacts/reports/cabin-v3-unity-import.txt'
unity=unity_file.exists() and unity_file.read_text().startswith('PASS')
assert fit['passed'] and export['pass']
source=ROOT/'ArtSource/DS_Sedan_A_cabin_v3.blend'
digest=hashlib.sha256(source.read_bytes()).hexdigest()
assert fit['sha256']==export['sourceSha256']==digest
template=Path('C:/Users/AVSok/.agents/skills/visual-demo-page/template.html').read_text(encoding='utf-8')
values={
 'taskTitle':'DS Sedan A — законченный салон',
 'whatChanged':'Передние кресла с салазками и боковой поддержкой, полноценный задний диван с тремя подголовниками, пространство для ног и отделка дверей. Руль опущен, приборы открыты для обзора. Исходный закрытый кузов сохранён.',
 'beforeImage':'before-frontseat-cutaway.png','afterImage':'after-frontseat-cutaway.png',
 'requirementClass':'ok','requirementResult':'PASS','requirementNote':'проверены сиденья, крепления, задний ряд, приборы и педали',
 'layoutClass':'ok','layoutResult':'PASS','layoutNote':'между рядами 372 мм; от задних подголовников/спинки до плоскости стекла минимум 32 мм',
 'consistencyClass':'ok','consistencyResult':'PASS','consistencyNote':'единая обивка, согласованные вставки и швы; кузов проверен спереди и сзади',
 'minimalityClass':'ok','minimalityResult':'PASS','minimalityNote':'фиксированные камеры и освещение до/после; разрез салона явно исключает крышу и ближнюю дверь',
 'finalVerdict':'PASS — статическая модель, посадка салона и обменные форматы',
 'residualRisk':'Unity: '+('импорт, материалы и prefab проверены.' if unity else 'проверка ещё не завершена.')+' Полный ход подвески, физика столкновений, отражения зеркал и VR не проверялись в этой доработке.'}
for key,value in values.items():template=template.replace('{{'+key+'}}',value)
extra=''
for view,title in [('driver-eye','Обзор с места водителя'),('rearseat-cutaway','Задний ряд и крепления'),('pedals','Педальный узел')]:
 extra+=f'<h2>{title}</h2><section class="grid">'
 for stage,label in [('before','До'),('after','После')]:
  path=OUT/f'{stage}-{view}.png';assert path.exists() and path.stat().st_size>10000
  extra+=f'<article class="card"><p class="label">{label} · Blender</p><a href="{path.name}"><img class="shot" src="{path.name}" alt="{title}: {label.lower()}" loading="lazy"></a></article>'
 extra+='</section>'
extra+='<h2>Внешний вид после доработки</h2><section class="grid">'
for view in ('exterior3quarter','rear-exterior'):
 extra+=f'<article class="card"><img class="shot" src="after-{view}.png" alt="Внешний вид автомобиля"></article>'
extra+='</section><section class="qa"><h2>Файлы и проверка</h2><p><a href="../viewer.html">Открыть автомобиль в 3D →</a> · <a href="../models/DS_Sedan_A_cabin_v3.glb" download>Скачать GLB</a></p>'
extra+=f'<p>FBX и GLB повторно импортированы: {export["sourceMeasurement"]["evaluatedTriangles"]:,} треугольников в каждом формате. Имена привязок сохранены.</p>'
extra+='<p>Все три педали проверены на зазор до пола в 11 положениях (0–20°). Минимальный зазор — 79 мм. Это проверка до пола, не полный анализ пересечений механизма.</p>'
extra+=f'<p>Ревизия: cabin_v3 · SHA-256 исходника: <code style="overflow-wrap:anywhere">{digest}</code></p></section>'
template=template.replace('</main>',extra+'</main>').replace('</style>','a{color:#a9d7ff}h2{margin-top:32px}.shot{width:100%;height:auto}</style>')
assert '{{' not in template
(OUT/'index.html').write_text(template,encoding='utf-8')
report='''# Доработка автомобиля: cabin_v3

Завершена статическая доработка салона на основе DS_Sedan_A_closed_shell.blend.
Исходный закрытый кузов сохранён. Предыдущие основные файлы — в
artifacts/visual-review/history/before-cabin-v3/.

- Передние кресла: наклон спинки, прилегающие вставки, боковая поддержка, швы, салазки и опоры.
- Задний ряд: цельный диван, три подголовника, замки ремней, коврики, полка и динамики.
- Добавлены дверные элементы, пороги, потолочный плафон, крепления козырьков и перегородка багажника.
- Руль и колонка опущены; приборный блок поднят для обзора с Socket_DriverEye.

Визуальная самопроверка: PASS в указанном объёме. Просмотрены реальные Blender-рендеры
до/после: водительский вид, передние кресла и задний ряд в разрезе, педали;
дополнительно внешний вид спереди и сзади. После самопроверки уменьшена ширина
дивана у колёсных ниш и дополнительно опущен руль. Камера педалей была внутри
руля; контрольный кадр переснят с одинаковой исправленной камерой до и после.

Измерения: 372 мм между обивкой рядов; минимум 32 мм до плоскости заднего стекла;
79 мм до пола у педалей на 11 шагах. Числа получены из evaluated mesh.
Отчёты: cabin-v3-fit.json и cabin-v3-export-check.json.

Не проверены: полный ход колёс/подвески, все взаимные пересечения деталей,
Unity collision/contact, зеркала в движении, VR, LOD и производительность.
Это доработка статической модели, а не завершение физики симулятора.
'''
report+='\nUnity import: '+('PASS — prefab/Showroom обновлены, необходимые детали и материалы найдены.\n' if unity else 'PENDING.\n')
(ROOT/'artifacts/reports/cabin-v3-review.md').write_text(report,encoding='utf-8')
print('Review page built; Unity import:',unity)
