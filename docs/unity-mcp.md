# Unity MCP — мост редактора к Claude

Инструмент разработчика, не часть игры. Даёт Claude (и другим MCP-клиентам) доступ к открытому Unity Editor:
консоль, сцены и GameObject, ассеты, запуск тестов, Play Mode, скриншоты, выполнение C# в редакторе.

Используем официальный путь Unity: **Unity CLI** (`unity`) + пакет **`com.unity.pipeline`** в проекте.
Сторонние варианты (CoplayDev MCP for Unity, самодельный HTTP-сервер на порту 8081) пробовали 24–25.09 и убрали:
официальный сервер работает с Claude через `unity mcp configure` без ручных мостов и паролей в коде.

- Пакет: `com.unity.pipeline` (экспериментальный, версия закреплена в `Packages/manifest.json` и в `tools/setup_project.py`).
  Обновление версии — отдельный коммит `chore:` в обоих файлах.

## Установка на машине разработчика (один раз)
```powershell
# 1. Unity CLI (потом открыть новое окно PowerShell)
$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex
# 2. Пакет уже в manifest; если нет — из корня проекта:
unity pipeline install
# 3. Зарегистрировать MCP-сервер в клиенте
unity mcp configure claude --project-path "<путь к проекту>"        # приложение Claude
unity mcp configure claude-code --project-path "<путь к проекту>"   # Claude Code
```
Затем открыть проект в Unity и полностью перезапустить клиент (в приложении Claude — Quit из трея).
Проверка: `unity status` показывает редактор в состоянии `ready`.

## Правила использования
- Правила `CLAUDE.md` действуют и через MCP: сгенерированные сцены (`Autodrome_Training.unity` и др.) и `course-v2.json`
  руками не правим, генераторы запускаем только по явной просьбе и на чистом дереве.
- Статус проверок — PASS/FAIL/NOT_RUN/BLOCKED + как запускали + где лежит результат. Тесты через MCP (`run_tests`)
  засчитываются, если XML скопирован в `artifacts/reports/`. Пакетный эталон — `tools\check.ps1` (требует закрытый Editor).
- Если проект не компилируется, Unity открывается в Safe Mode и MCP не подключается — сначала чинить ошибки компиляции.
- Git из редактора запускать полным путём (`C:\Program Files\Git\cmd\git.exe`): Unity не видит git в PATH.
