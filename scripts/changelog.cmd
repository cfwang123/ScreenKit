@echo off
rem Add one bilingual entry to CHANGELOG.md (file lock + compare before write).
rem Usage: scripts\changelog.cmd added "English text" "Chinese text"
rem        scripts\changelog.cmd fixed --en "English text" --zh "Chinese text"
rem        scripts\changelog.cmd -h
rem NOTE: keep this file ASCII-only. cmd parses .cmd with the local ANSI code page,
rem       so UTF-8 Chinese comments here would break the script.
setlocal
set "OLDCP="
for /f "tokens=2 delims=:" %%a in ('chcp') do set "OLDCP=%%a"
chcp 65001 >nul
python -X utf8 "%~dp0changelog.py" %*
set "RC=%errorlevel%"
if defined OLDCP chcp%OLDCP% >nul
exit /b %RC%
