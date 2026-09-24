# Шрифты интерфейса

Решение: `docs/ui-settings.md` §6 (24.09.2026). Лицензия — SIL Open Font License 1.1, текст лежит рядом со шрифтом (`OFL.txt`) и должен попадать в сборку.

| Папка | Файлы | Источник | Коммит |
|---|---|---|---|
| `GolosText/` | GolosText-Regular/Medium/Bold.ttf | https://github.com/googlefonts/golos-text `fonts/ttf/` | cf2e27222937d97c2d858fff0499bcc667a64e9d (тот же, что в google/fonts `ofl/golostext`) |
| `RobotoMono/` | RobotoMono-Medium/Bold.ttf | https://github.com/googlefonts/robotomono `fonts/ttf/` | 895ec691990d041dd727c7b5afa3ce56525d98e6 |

Покрытие проверено fontTools: ASCII, А–я, Ё/ё, № « » — – … ° × есть. **Нет** ◀ ▶ ✓ (и стрелок в Roboto Mono) — такие значки в UI рисуются спрайтами, а не текстом.

TMP-ассеты (`*_SDF.asset`) создаёт `UIBuilder` → меню «Driving School/Build UI Fonts»; руками не править.
