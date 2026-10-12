@echo off
rem ScreenKit 界面文案小工具：新增或修改一条文案。
rem 用法: scripts\langtext.cmd set app.demo.title "演示标题" -l zh|all|zh,en
rem       scripts\langtext.cmd -h   查看全部命令
chcp 65001 >nul
python -X utf8 "%~dp0langtext.py" %*
exit /b %errorlevel%
