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

echo [3/5] Restoring packages ...
dotnet restore "src\APISwitch\APISwitch.csproj" -r win-x64
if errorlevel 1 (
  echo [ERROR] dotnet restore failed.
  exit /b 1
)

echo [4/5] Publishing single-file executable to root Release ...
if exist "%~dp0Release" rmdir /s /q "%~dp0Release"
dotnet publish "src\APISwitch\APISwitch.csproj" -c Release -r win-x64 --self-contained false -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0Release"
if errorlevel 1 (
  echo [ERROR] dotnet publish failed.
  exit /b 1
)

echo [5/5] Cleaning build artifacts ...
if exist "%~dp0src\APISwitch\bin" rmdir /s /q "%~dp0src\APISwitch\bin"
if exist "%~dp0src\APISwitch\obj" rmdir /s /q "%~dp0src\APISwitch\obj"

echo.
echo [OK] Repack complete.
echo Output: "%~dp0Release\APISwitch.exe"
exit /b 0
