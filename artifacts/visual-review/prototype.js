const screens = ['home', 'lessons', 'vehicle', 'calibration', 'drive', 'pause', 'theory', 'result', 'editor'];

const lessons=[
  { id: 1, title: 'Начало движения', category: 'Автодром', desc: 'Плавное трогание с места, баланс сцепления' },
  { id: 2, title: 'Змейка', category: 'Автодром', desc: 'Маневрирование между конусами с интервалом 11.25м' },
  { id: 3, title: 'Параллельная парковка', category: 'Автодром', desc: 'Заезд задним ходом в парковочный карман' },
  { id: 4, title: 'Въезд в бокс задним ходом', category: 'Автодром', desc: 'Упражнение 90 градусов с контролем габаритов' },
  { id: 5, title: 'Въезд в бокс', category: 'Автодром', desc: 'Упражнение гараж задним ходом' },
  { id: 6, title: 'Горка и эстакада', category: 'Автодром', desc: 'Остановка и трогание на подъеме 10% без отката' },
  { id: 7, title: 'Разворот в ограниченном пространстве', category: 'Автодром', desc: 'Разворот в три приема' },
  { id: 8, title: 'Регулируемый перекресток', category: 'Город', desc: 'Проезд перекрестков по сигналам светофора' }
];

let selectedLesson = 5;
let currentWeather = 'Ясно';
let currentTime = 'День';
let virtualCalActive = false;
let selectedAnswer = null;
let editorMarkers = [];

function showToast(msg) {
  let t = document.getElementById('toast');
  if (!t) {
    t = document.createElement('div');
    t.id = 'toast';
    t.className = 'toast';
    document.body.appendChild(t);
  }
  t.textContent = msg;
  t.style.display = 'block';
  setTimeout(() => { t.style.display = 'none'; }, 2500);
}

function renderScreen(id) {
  const app = document.getElementById('app');
  if (!app) return;
  
  if (id === 'home') {
    app.innerHTML = "<div id='screen-home' class='screen'><h1>DRIVINGSCHOOLSIM</h1><div class='subtitle'>Профессиональный симулятор автошколы</div><div class='progress-box'><div class='progress-label'>Общий прогресс курса: 68%</div><div class='progress-bar'><div class='progress-fill' style='width: 68%'></div></div></div><div class='actions'><button class='btn primary' onclick=\"location.hash='#drive'\">Продолжить занятие</button><button class='btn' onclick=\"location.hash='#lessons'\">Выбрать занятие</button></div></div>";
  } else if (id === 'lessons') {
    let items = lessons.map(l => "<div class='card lesson-card " + (l.id === selectedLesson ? 'active' : '') + "' data-lesson='" + l.id + "' onclick='selectedLesson=" + l.id + "; renderScreen(\"lessons\")'><h3>" + l.title + "</h3><div class='tag'>" + l.category + "</div><p>" + l.desc + "</p><button class='btn small' onclick=\"location.hash='#vehicle'\">Выбрать</button></div>").join('');
    app.innerHTML = "<div id='screen-lessons' class='screen'><h2>Каталог учебных занятий: " + (lessons.find(l=>l.id===selectedLesson)?.title || 'Въезд в бокс') + "</h2><div class='grid'>" + items + "</div><div class='actions-bar'><button class='btn primary' onclick=\"location.hash='#vehicle'\">Условия поездки</button></div></div>";
  } else if (id === 'vehicle') {
    app.innerHTML = "<div id='screen-vehicle' class='screen'><h2>Настройка учебного автомобиля и условий</h2><div class='setting-group'><label>Трансмиссия:</label><div class='toggle-buttons'><button class='btn small active'>МКПП · 5</button><button class='btn small'>АКПП</button></div></div><div class='setting-group'><label>Погодные условия:</label><div class='toggle-buttons'><button class='btn small' onclick='currentWeather=\"Ясно\"'>Ясно</button><button class='btn small' onclick='currentWeather=\"Дождь\"'>Дождь</button></div></div><div class='setting-group'><label>Время суток:</label><div class='toggle-buttons'><button class='btn small' onclick='currentTime=\"День\"'>День</button><button class='btn small' onclick='currentTime=\"Ночь\"'>Ночь</button></div></div><button class='btn primary' onclick=\"location.hash='#drive'\">К занятию →</button></div>";
  } else if (id === 'calibration') {
    app.innerHTML = "<div id='screen-calibration' class='screen'><h2>Калибровка оборудования Logitech G27</h2><div id='calstatus' class='status-alert'>" + (virtualCalActive ? 'Профиль откалиброван и сохранён' : 'Внимание: руль Logitech G27 не обнаружен') + "</div><div class='cal-panel'><div>Угол поворота руля: 900°</div><input id='steer' type='range' min='-450' max='450' value='0' /><div>Педали: Сцепление, Тормоз, Газ</div><input data-axis='0' type='range' min='0' max='100' value='0' /></div><div class='actions'><button class='btn' onclick='virtualCalActive=true; document.getElementById(\"calstatus\").textContent=\"Виртуальная калибровка активна\";'>Включить виртуальную калибровку</button><button class='btn primary' onclick='if(!virtualCalActive){ document.getElementById(\"calstatus\").textContent=\"Ошибка: сначала включите виртуальную калибровку\"; showToast(\"Ошибка: сначала включите виртуальную калибровку\"); } else { document.getElementById(\"calstatus\").textContent=\"Профиль сохранён\"; showToast(\"Профиль сохранён\"); }'>Сохранить демо-профиль</button></div></div>";
  } else if (id === 'drive') {
    app.innerHTML = "<div id='screen-drive' class='screen'><div class='hud-stage'><img src='renders/sedan-side.png' alt='view' /><div class='hud-overlay'><div class='hud-speed'>48 км/ч</div><div class='hud-gear'>Передача 3</div><div class='hud-coach'>Подготовьтесь к манёвру на перекрестке</div><div class='note'>Статичный рендер кабины седана</div><button class='btn small' onclick=\"location.hash='#pause'\">Ⅱ Пауза</button></div></div></div>";
  } else if (id === 'pause') {
    app.innerHTML = '<div id="screen-pause" class="screen"><h2>Поездка приостановлена</h2><div class="setting-row"><label>Угол обзора (FOV): <span id="fovvalue">75°</span></label><input id="fov" type="range" min="50" max="100" value="75" oninput="document.getElementById(\'fovvalue\').textContent=this.value+\'°\'" /></div><div class="actions"><button class="btn" onclick="showToast(\'Настройки применены\'); location.hash=\'#drive\';">Применить в прототипе</button><button class="btn" onclick="location.hash=\'#drive\'">Продолжить</button><button class="btn primary" onclick="location.hash=\'#result\'">Завершить и посмотреть разбор</button></div></div>';
  } else if (id === 'theory') {
    app.innerHTML = "<div id='screen-theory' class='screen'><h2>Экзамен по теории ПДД</h2><div class='question'>Перед поворотом направо водитель обязан заблаговременно включить световой указатель правого поворота. Кому необходимо уступить дорогу?</div><div class='answers'><button class='btn' data-answer='0' onclick='selectedAnswer=0; this.classList.add(\"active\");'>Уступить дорогу пешеходу на переходе</button><button class='btn' data-answer='1' onclick='selectedAnswer=1; this.classList.add(\"active\");'>Проехать первым без остановки</button></div><div id='explanation'></div><div class='actions'><button class='btn primary' onclick='if (selectedAnswer === null) { showToast(\"Выберите один из ответов\"); } else if (selectedAnswer === 1) { document.getElementById(\"explanation\").textContent = \"Неверно. Попробуйте еще раз.\"; } else { document.getElementById(\"explanation\").textContent = \"Верно! Водитель обязан уступить пешеходам.\"; }'>Проверить ответ</button><button class='btn' onclick='selectedAnswer=null; renderScreen(\"theory\");'>Начать заново</button></div></div>";
  } else if (id === 'result') {
    app.innerHTML = "<div id='screen-result' class='screen'><h2>Итоги учебной поездки: 86 / 100</h2><div class='stats'>Время поездки: 12:34 | Штрафных баллов: 1</div><div class='timeline'><div class='item pass'>00:15 - Ремень безопасности пристегнут</div><div class='item pass'>02:40 - Зеркала проверены перед началом маневра</div><div class='item warn'>08:12 - Незначительное отклонение от оси полосы</div></div><button class='btn primary' onclick=\"location.hash='#home'\">Вернуться в меню</button></div>";
  } else if (id === 'editor') {
    app.innerHTML = "<div id='screen-editor' class='screen'><h2>Встроенный редактор дорог и объектов</h2><div id='mapstatus'>Редактор готов</div><div class='toolbar'><button class='btn small' data-tool='Дорога'>Дорога</button><button class='btn small' data-tool='Здание'>Здание</button><button class='btn small' data-tool='Знак'>Знак</button><button class='btn small' data-action='undo' onclick='if (editorMarkers.length === 0) { showToast(\"Нет добавленных объектов\"); } else { editorMarkers.pop(); document.getElementById(\"mapstatus\").textContent = \"Объект отменен\"; }'>↶ Отменить (undo)</button><button class='btn small primary' onclick='const blob = new Blob([JSON.stringify({ demonstration: true, uiMarkers: editorMarkers.length ? editorMarkers : [1], masterplan: true })], {type: \"application/json\"}); const a = document.createElement(\"a\"); a.href = URL.createObjectURL(blob); a.download = \"exported-map-demo.json\"; a.click();'>Экспорт JSON ↓</button></div><svg id='mapSvg' width='600' height='400' style='background: #111318; border: 1px solid rgba(255,255,255,0.1);' onclick='editorMarkers.push(1); document.getElementById(\"mapstatus\").textContent = \"Есть несохранённые изменения\";'><g transform='translate(10,10)'><rect width='20' height='20' fill='#F0A500'/></g></svg><div class='data-note'>Шаблон trajectory-map-demo.json доступен для сборки дорожной сети.</div></div>";
  } else if (id === 'vr') {
    app.innerHTML = "<div id='screen-vr' class='screen'><h2>VR Пространственный обзор</h2><button class='btn primary' data-vrlesson='4' onclick=\"location.hash='#lessons'\">Открыть урок в VR</button></div>";
  }
}

function updateRoute() {
  const hash = (location.hash || '#home').replace('#', '');
  renderScreen(hash);
}

window.addEventListener('DOMContentLoaded', () => {
  window.addEventListener('hashchange', updateRoute);
  updateRoute();
});
