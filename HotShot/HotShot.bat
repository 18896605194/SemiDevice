@echo off
setlocal
set PROJECT=%~dp0
set PY=%MIMO_PYTHON%
if "%PY%"=="" set PY=C:\Program Files\Xiaomi MiMo\resources\runtimes\win32-x64\python\python.exe
set PYW=%~dp0..\..\Xiaomi MiMo\resources\runtimes\win32-x64\python\pythonw.exe
for %%I in ("%PY%") do set PYDIR=%%~dpI
set PYW=%PYDIR%pythonw.exe
if not exist "%PYW%" set PYW=%PY%
start "" "%PYW%" "%PROJECT%main.py"
endlocal
