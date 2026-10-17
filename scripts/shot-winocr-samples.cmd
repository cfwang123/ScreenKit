@echo off
setlocal
cd /d "%~dp0.."
python -X utf8 "%~dp0..\fixtures\winocr-samples\gen_html.py" %*
exit /b %ERRORLEVEL%
