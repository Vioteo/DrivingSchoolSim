# purecheck — проверка чистых модулей без Unity

Компилирует `Contracts`, `Simulation`, `Rules`, `Learning` как `netstandard2.1` / C# 9 (как их видит Unity) и прогоняет NUnit-тесты, которые не зависят от UnityEngine: `Tests/City/**` и перечисленные в `PureTests.csproj`.

```bash
dotnet test tools/purecheck/Tests/PureTests.csproj --logger "trx;LogFileName=purecheck.trx" --results-directory artifacts/reports
```

Это **не замена** Unity EditMode (`-runTests`, см. `docs/acceptance.md`): статус отсюда пишется как «purecheck PASS», а Unity-прогон — отдельно.
