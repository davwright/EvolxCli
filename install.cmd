@echo off
REM Build Evolx.Cli, install a self-contained ev.exe to %LOCALAPPDATA%\Programs\Evolx\,
REM and add that folder to the User PATH.
REM
REM Idempotent - safe to re-run after every code change.
REM The published exe is self-contained: no .NET runtime needed at run time.
REM
REM Usage:
REM   install.cmd          build + install + register on User PATH
REM
REM After first run, open a NEW terminal so the User PATH change takes effect.
REM Subsequent runs just rebuild and update the exe in-place.

setlocal

set "PROJ=%~dp0src\Evolx.Cli\Evolx.Cli.csproj"
set "DEST=%LOCALAPPDATA%\Programs\Evolx"
set "EXE=%DEST%\ev.exe"

echo === Building Evolx.Cli (Release, self-contained, win-x64) ===
dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%DEST%"
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

if exist "%DEST%\Evolx.Cli.exe" (
    if exist "%EXE%" erase /q "%EXE%"
    move /y "%DEST%\Evolx.Cli.exe" "%EXE%" >nul
)

if not exist "%EXE%" (
    echo ERROR: %EXE% not found after publish.
    exit /b 1
)

echo.
echo === Installed: %EXE% ===

powershell -NoProfile -Command "$dest = $env:LOCALAPPDATA + '\Programs\Evolx'; $cur = [Environment]::GetEnvironmentVariable('Path','User'); if ($cur -split ';' -notcontains $dest) { [Environment]::SetEnvironmentVariable('Path', $cur.TrimEnd(';') + ';' + $dest, 'User'); Write-Host ('Added ' + $dest + ' to User PATH (open a new terminal to pick it up).') } else { Write-Host ($dest + ' already on User PATH.') }"

echo.
echo Try it:
echo     ev --help
echo     ev ado wi list --type Issue --top 5
echo.
echo If 'ev' is not found, open a NEW terminal - User PATH only updates for new shells.

endlocal
