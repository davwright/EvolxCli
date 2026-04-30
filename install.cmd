@echo off
REM Build Evolx.Cli and install a self-contained ev.exe.
REM
REM Install location, in priority order:
REM   1. %USERPROFILE%\.local\bin    if it's already on User PATH (preferred — generic per-user bin)
REM   2. %LOCALAPPDATA%\Programs\Evolx\   fallback (registers itself on User PATH if not already)
REM
REM Idempotent — safe to re-run after every code change.
REM The published exe is self-contained: no .NET runtime needed at run time.
REM
REM Usage:
REM   install.cmd          build + install + register on User PATH if needed
REM
REM After first run, open a NEW terminal so a User PATH change takes effect.

setlocal

set "PROJ=%~dp0src\Evolx.Cli\Evolx.Cli.csproj"
set "PUBLISH_DIR=%~dp0src\Evolx.Cli\bin\Release\net9.0\win-x64\publish"

REM Decide install location.
REM Use a small PowerShell snippet to inspect the User PATH and emit the chosen DEST.
for /f "usebackq delims=" %%D in (`powershell -NoProfile -Command "$local = $env:USERPROFILE + '\.local\bin'; $userPath = [Environment]::GetEnvironmentVariable('Path','User'); if ((Test-Path $local) -and ($userPath -split ';' -contains $local)) { Write-Output $local } else { Write-Output ($env:LOCALAPPDATA + '\Programs\Evolx') }"`) do set "DEST=%%D"

set "EXE=%DEST%\ev.exe"

echo === Building Evolx.Cli (Release, self-contained, win-x64) ===
dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

if not exist "%PUBLISH_DIR%\Evolx.Cli.exe" (
    echo ERROR: published exe not found at %PUBLISH_DIR%\Evolx.Cli.exe
    exit /b 1
)

if not exist "%DEST%" mkdir "%DEST%"

if exist "%EXE%" erase /q "%EXE%"
copy /y "%PUBLISH_DIR%\Evolx.Cli.exe" "%EXE%" >nul

if not exist "%EXE%" (
    echo ERROR: %EXE% not found after copy.
    exit /b 1
)

echo.
echo === Installed: %EXE% ===

REM Make sure DEST is on User PATH. Skip if already there (the .local\bin case usually is).
powershell -NoProfile -Command "$dest = '%DEST%'; $cur = [Environment]::GetEnvironmentVariable('Path','User'); if ($cur -split ';' -notcontains $dest) { [Environment]::SetEnvironmentVariable('Path', $cur.TrimEnd(';') + ';' + $dest, 'User'); Write-Host ('Added ' + $dest + ' to User PATH (open a new terminal to pick it up).') } else { Write-Host ($dest + ' already on User PATH.') }"

echo.
echo Try it (new terminal):
echo     ev --help
echo.
echo Or refresh PATH in this terminal:
echo     $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')

endlocal
