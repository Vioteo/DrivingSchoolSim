# G29: аппаратная проверка 28.09.2026

- **BLOCKED** — `Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match 'VID_046D&PID_C24F' }`, exit 0, совпадений нет. Реальное устройство G29 не обнаружено Windows в этой сессии; оси, педали, кнопки 6+R и вращение руля в Play Mode не измерены.
- **NOT_RUN** — аппаратный FFB. В проекте пока только `LogitechWheelFeedbackMock`; нативный бэкенд и DLL не поставляются.
- **PASS** — `powershell -ExecutionPolicy Bypass -File tools\check.ps1 -Filter DrivingSchool.Tests.VehicleDynamicsTests`, exit 0, 20/20. Лог: `artifacts/reports/check-20260928-131424.log`.
- **PASS** — `powershell -ExecutionPolicy Bypass -File tools\check.ps1 -Filter DrivingSchool.Tests.KeyboardSteeringTests`, exit 0, 7/7. Лог: `artifacts/reports/check-20260928-132240.log`.
- **PASS** — `powershell -ExecutionPolicy Bypass -File tools\check.ps1 -Filter DrivingSchool.Tests.SettingsTests`, exit 0, 19/19. Лог: `artifacts/reports/check-20260928-131454.log`.
- **FAIL** — `powershell -ExecutionPolicy Bypass -File tools\check.ps1`, exit 1 (Unity exit 2), 379/385. Шесть ошибок в CityDistrictRulesTests, GuidedLessonPlayTests и TrainingSceneTests. Лог: `artifacts/reports/check-20260928-132757.log`.
- **BLOCKED** — `dotnet test tools/purecheck/Tests/PureTests.csproj --logger "trx;LogFileName=purecheck.trx" --results-directory artifacts/reports`, exit -2147450735, SDK .NET не найден в этом окружении (команда `dotnet` присутствует только как runtime). Лог: `artifacts/reports/purecheck-blocked.log`.

После появления устройства в Windows: в настройках игры назначить четыре оси и кнопки R/1–6, сохранить профиль, затем на полигоне при 5–10 км/ч сравнить полный угол, yaw и радиус обоих направлений с геометрией в `docs/g29-setup.md`. Номера контролов до этого не считаются подтверждёнными.
