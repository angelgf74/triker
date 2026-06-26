@echo off
REM ============================================================
REM  Triker - compila el APK debug y lo instala por USB.
REM  Usa "adb install" directo porque gradlew installDebug
REM  no registra el paquete de forma fiable en HyperOS (Xiaomi).
REM ============================================================
setlocal

cd /d "%~dp0"

REM --- Localiza adb ---
set "ADB=adb"
if defined ANDROID_HOME if exist "%ANDROID_HOME%\platform-tools\adb.exe" set "ADB=%ANDROID_HOME%\platform-tools\adb.exe"
if defined ANDROID_SDK_ROOT if exist "%ANDROID_SDK_ROOT%\platform-tools\adb.exe" set "ADB=%ANDROID_SDK_ROOT%\platform-tools\adb.exe"

set "APK=app\build\outputs\apk\debug\app-debug.apk"
set "PKG=com.angelgf.triker"

echo.
echo [1/4] Comprobando dispositivo...
"%ADB%" get-state 1>nul 2>nul
if errorlevel 1 (
    echo   ERROR: ningun dispositivo conectado/autorizado.
    echo   Conecta el telefono con depuracion USB activada y acepta el dialogo.
    "%ADB%" devices
    exit /b 1
)
"%ADB%" devices

echo.
echo [2/4] Compilando APK debug...
call "%~dp0gradlew.bat" :app:assembleDebug
if errorlevel 1 (
    echo   ERROR: fallo la compilacion.
    exit /b 1
)

echo.
echo [3/4] Instalando %APK% ...
"%ADB%" install -r -t "%APK%"
if errorlevel 1 (
    echo   ERROR: fallo la instalacion.
    exit /b 1
)

echo.
echo [4/4] Lanzando la app...
"%ADB%" shell am start -n %PKG%/.MainActivity

echo.
echo Hecho.
endlocal
