# MCP for Unity — мост редактора к Claude

Инструмент разработчика, не часть игры. Даёт Claude (и другим MCP-клиентам) доступ к открытому Unity Editor:
консоль, сцены и GameObject, ассеты, запуск тестов, Play Mode, скриншоты.

- Пакет: `com.coplaydev.unity-mcp` (CoplayDev/unity-mcp, MIT), закреплён тегом `#v10.2.0` в `Packages/manifest.json`
  и в `tools/setup_project.py`. Обновление версии — отдельный коммит `chore:` с новым тегом в обоих файлах.
- Документация: https://coplaydev.github.io/unity-mcp/getting-started/install

## Установка на машине разработчика (один раз)
1. Нужны `git` в PATH (Unity качает пакет по git URL) и `uv`: `winget install --id=astral-sh.uv -e`.
2. Открыть проект в Unity — пакет подтянется сам, `Packages/packages-lock.json` обновится (закоммитить вместе с этой веткой).
3. `Window → MCP for Unity`: мастер проверит Python/uv. Для Claude desktop нужен транспорт **stdio**.
4. Конфиг Claude desktop (`%APPDATA%\Claude\claude_desktop_config.json`), затем полностью перезапустить Claude:
   ```json
   {
     "mcpServers": {
       "unityMCP": {
         "command": "C:/Users/<USER>/AppData/Local/Microsoft/WinGet/Links/uvx.exe",
         "args": ["--from", "mcpforunityserver", "mcp-for-unity", "--transport", "stdio"]
       }
     }
   }
   ```
   Можно вместо ручной правки нажать в окне MCP for Unity «Configure» у Claude Desktop.

## Правила использования
- Правила `CLAUDE.md` действуют и через MCP: сгенерированные сцены (`Autodrome_Training.unity` и др.) и `course-v2.json`
  руками не правим, генераторы запускаем только по явной просьбе и на чистом дереве.
- Статус проверок по-прежнему PASS/FAIL/NOT_RUN/BLOCKED + команда + лог. Тесты, запущенные через MCP, засчитываются,
  если результат сохранён в `artifacts/reports/`; эталон приёмки — `tools\check.ps1`.
- Редактор с активным MCP нельзя одновременно использовать для `tools\check.ps1` (скрипт требует закрытый Editor).
- AI-генерация ассетов в пакете идёт через облако Coplay — для ассетов проекта не используем, у нас `docs/art-pipeline.md`.
