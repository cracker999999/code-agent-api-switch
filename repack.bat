@echo off
setlocal

cd /d "%~dp0"

echo [1/5] Stopping running APISwitch.exe ...
taskkill /F /T /IM APISwitch.exe >nul 2>&1

echo [2/5] Checking dotnet ...
dotnet --version >nul 2>&1
if errorlevel 1 (
  echo [ERROR] dotnet is not installed or not in PATH.
  goto :error
)

echo [3/5] Restoring packages ...
dotnet restore "src\WPF\WPF.csproj" -r win-x64
if errorlevel 1 (
  echo [ERROR] dotnet restore failed.
  goto :error
)

echo [4/5] Publishing single-file executable to root Release ...
if exist "%~dp0Release" rmdir /s /q "%~dp0Release"
dotnet publish "src\WPF\WPF.csproj" -c Release -r win-x64 --self-contained false -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0Release"
if errorlevel 1 (
  echo [ERROR] dotnet publish failed.
  goto :error
)

echo [5/5] Cleaning build artifacts ...
if exist "%~dp0src\WPF\bin" rmdir /s /q "%~dp0src\WPF\bin"
if exist "%~dp0src\WPF\obj" rmdir /s /q "%~dp0src\WPF\obj"

echo.
echo [OK] Repack complete.
echo Output: "%~dp0Release\APISwitch.exe"
exit /b 0

:error
echo.
echo Press any key to close this window...
pause >nul
exit /b 1
