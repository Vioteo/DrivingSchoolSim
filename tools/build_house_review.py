"""Build the static house review page from measured asset reports."""
from pathlib import Path
import json
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/visual-review/houses'
manifest=json.loads((ROOT/'artifacts/reports/houses/manifest.json').read_text(encoding='utf8'))
template=Path('C:/Users/AVSok/.agents/skills/visual-demo-page/template.html').read_text(encoding='utf8')
values={
    'taskTitle':'Четыре дома для Driving School Sim',
    'whatChanged':'Оригинальные модели окружения: коттедж, таунхаус, кирпичная пятиэтажка и современный восьмиэтажный дом. Blender 5 · FBX / GLB · метры · 3 LOD на модель.',
    'beforeImage':'brick-before.png','afterImage':'DS_House_Brick5-front.png',
    'requirementClass':'pass','requirementResult':'PASS','requirementNote':'четыре разных внешних облика; двери, окна на четырёх фасадах, крыши и балконы',
    'layoutClass':'pass','layoutResult':'PASS','layoutNote':'проверены восемь Blender-ракурсов; балконы выровнены по дверям после самопроверки',
    'consistencyClass':'pass','consistencyResult':'PASS','consistencyNote':'общая палитра материалов, единый масштаб и пропорции этажей',
    'minimalityClass':'pass','minimalityResult':'PASS','minimalityNote':'галерея моделей и одна пара до/после ремонта балконов; обратные ракурсы доступны по ссылкам',
    'finalVerdict':'PASS — внешний вид и проверка обменных форматов',
    'residualRisk':'Модели предназначены для окружения снаружи, без интерьеров. Коллайдеры ограничены основным объёмом здания; крыши, балконы и козырьки не имеют отдельных коллайдеров. Производительность в игровом квартале не измерялась.'
}
for key,value in values.items(): template=template.replace('{{'+key+'}}',value)
cards=[]
for a in manifest['assets']:
    name=a['id']; counts=' / '.join(str(l['triangles']) for l in a['lods'])
    cards.append(f'<article class="card"><h2>{a["title"]}</h2><a href="{name}-front.png"><img class="shot" src="{name}-front.png" alt="{a["title"]}, Blender, houses-v1"></a><p>{a["w"]:g} × {a["d"]:g} м по стенам · {a["floors"]} этажа/этажей</p><p>LOD 0 / 1 / 2: {counts} треугольников</p><p><a href="{name}-rear.png">Обратная сторона</a> · <a href="{name}.glb" download>Модель GLB ↓</a></p></article>')
gallery='<section class="grid">'+''.join(cards)+'</section><h2>Исправление балконов · Blender</h2><p>До: смещение относительно окон. После: точное совмещение с балконными дверями. Камера и освещение сохранены.</p>'
template=template.replace('<section class="grid">',gallery+'<section class="grid">',1)
unity=ROOT/'artifacts/reports/houses/unity-import.txt'
if unity.exists() and 'PASS: 4 prefabs' in unity.read_text(encoding='utf8'):
    unity_card='<section class="qa"><h2>Импорт в Unity 6000.3 · URP</h2><img class="shot" src="unity-gallery.png" alt="Четыре импортированных дома в Unity"><p>Созданы 4 префаба и отдельная сцена Houses. Проверены 3 LOD на модель, материалы URP, габариты с допуском 5 мм, точка привязки на земле и коллайдер основного объёма.</p></section>'
    template=template.replace('</main>',unity_card+'</main>')
template=template.replace('</style>','a{color:#a9dcf4} .shot{height:auto!important;object-fit:contain!important} h2{font-size:21px} p{line-height:1.6}</style>')
template=template.replace('Целостность layout','Расположение деталей')
assert '{{' not in template
(OUT/'index.html').write_text(template,encoding='utf8')
print(OUT/'index.html')
