@echo off
rem ScreenKit UI text helper: add or update one string in ScreenKit/lang/*.toml.
rem Usage: scripts\langtext.cmd set app.demo.title "Demo title" -l all
rem        scripts\langtext.cmd -h        (help is printed in Chinese)
rem NOTE: keep this file ASCII-only. cmd parses .cmd with the local ANSI code page,
rem       so UTF-8 Chinese comments here would break the script.
setlocal
set "OLDCP="
for /f "tokens=2 delims=:" %%a in ('chcp') do set "OLDCP=%%a"
chcp 65001 >nul
python -X utf8 "%~dp0langtext.py" %*
set "RC=%errorlevel%"
if defined OLDCP chcp%OLDCP% >nul
exit /b %RC%
