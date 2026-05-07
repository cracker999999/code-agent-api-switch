@echo off
setlocal

cd /d "%~dp0"

echo [1/5] Stopping running APISwitch.exe ...
taskkill /F /T /IM APISwitch.exe >nul 2>&1

echo [2/5] Checking dotnet ...
dotnet --version >nul 2>&1
if errorlevel 1 (
  echo [ERROR] dotnet is not installed or not in PATH.
  exit /b 1
)

echo [3/5] Restoring packages for UI ...
dotnet restore "src\UI\UI.csproj" -r win-x64
if errorlevel 1 (
  echo [ERROR] dotnet restore failed.
  exit /b 1
)

echo [4/5] Publishing new UI single-file executable ...
if exist "%~dp0Release-UI" rmdir /s /q "%~dp0Release-UI"
dotnet publish "src\UI\UI.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%~dp0Release-UI"
if errorlevel 1 (
  echo [ERROR] dotnet publish failed.
  exit /b 1
)

echo [5/5] Cleaning build artifacts ...
if exist "%~dp0src\UI\bin" rmdir /s /q "%~dp0src\UI\bin"
if exist "%~dp0src\UI\obj" rmdir /s /q "%~dp0src\UI\obj"

echo.
echo [OK] Repack complete.
echo Output: "%~dp0Release-UI\APISwitch.exe"
exit /b 0
