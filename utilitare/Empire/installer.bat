@echo off
setlocal
 
set EMPIRE_SRC=%~dp0
set EMPIRE_BIN=C:\Windows\empire.cmd
 
if "%1"=="install" goto install
if "%1"=="uninstall" goto uninstall
echo -- Usage: install.bat [install^|uninstall]
exit /b 1
 
:install
echo @python "%EMPIRE_SRC%empire.py" %%* > "%EMPIRE_BIN%"
echo -- Linked  %EMPIRE_BIN% -^> %EMPIRE_SRC%empire.py
echo -- Done. try: empire --help
exit /b 0
 
:uninstall
del "%EMPIRE_BIN%" 2>nul
echo -- Removed %EMPIRE_BIN%
echo -- Done
exit /b 0

