@echo off
chcp 65001 > nul
echo ============================================================
echo   Silent Night - Автоматическая сборка Windows (64-bit EXE)
echo ============================================================
echo.

set UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe
set PROJECT_PATH=%~dp0
set LOG_FILE=%PROJECT_PATH%build.log

tasklist /FI "IMAGENAME eq Unity.exe" 2>NUL | find /I /N "Unity.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo [!] Unity Editor сейчас открыт на этом компьютере.
    echo.
    echo Чтобы собрать EXE, у вас есть 2 мгновенных варианта:
    echo.
    echo  ВАРИАНТ 1 (НЕ ЗАКРЫВАЯ UNITY):
    echo    Перейдите в открытое окно Unity и в верхнем меню нажмите:
    echo    Silent Night -> 📦 Собрать игру (Windows EXE)
    echo.
    echo  ВАРИАНТ 2 (ИЗ ЭТОГО ОКНА):
    echo    Сохраните сцену в Unity, закройте окно Unity Editor,
    echo    и нажмите любую клавишу здесь для авто-сборки.
    echo.
    pause
)

tasklist /FI "IMAGENAME eq Unity.exe" 2>NUL | find /I /N "Unity.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo Unity Editor всё еще запущен. Пожалуйста, соберите через верхнее меню внутри Unity!
    pause
    exit /b
)

echo Запуск Unity в режиме сборщика (подождите 30-60 сек)...
"%UNITY_EXE%" -batchmode -quit -projectPath "%PROJECT_PATH%" -executeMethod SilentNight.Editor.BuildScript.PerformBuildCommandLine -logFile "%LOG_FILE%"

if exist "%PROJECT_PATH%Builds\Windows\SilentNight.exe" (
    echo.
    echo ============================================================
    echo   [OK] СБОРКА УСПЕШНО ЗАВЕРШЕНА!
    echo   EXE: %PROJECT_PATH%Builds\Windows\SilentNight.exe
    echo ============================================================
    explorer "%PROJECT_PATH%Builds\Windows"
) else (
    echo.
    echo [ERR] Ошибка сборки. Лог:
    if exist "%LOG_FILE%" (
        powershell -Command "Get-Content '%LOG_FILE%' -Tail 30"
    )
)
pause
