# Инструкции для ИИ-агентов (Codex, Cursor, Gemini и др.)

Все правила проекта — в [CLAUDE.md](CLAUDE.md). Прочитай его целиком перед любой работой и следуй ему так же, как Claude.

Самое важное:
- Чистые модули (`Contracts`, `Simulation`, `Rules`, `Learning`) не ссылаются на UnityEngine.
- Не править сгенерированные сцены и `course-v2.json` руками и не запускать генераторы без просьбы.
- Не создавать копии файлов (`*_backup`, `*_v2`) — история в git. Работать в отдельной ветке; правила git — раздел «Git» в CLAUDE.md.
- «Готово» = `powershell -ExecutionPolicy Bypass -File tools\check.ps1` вернул PASS (а для чистых модулей ещё `dotnet test tools/purecheck/Tests/PureTests.csproj`). Иначе пиши FAIL / NOT_RUN / BLOCKED.
- Не удалять и не ослаблять тесты, чтобы они прошли.
- Известные проблемы кода и план — `docs/code-review-2026-09-24.md`.
