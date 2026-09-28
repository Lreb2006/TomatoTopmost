@echo off
setlocal
if not exist "%~dp0dist\TomatoTopmost-1.0.0\TomatoTopmost.exe" (
  echo Please run build.ps1 first.
  pause
  exit /b 1
)
start "" "%~dp0dist\TomatoTopmost-1.0.0\TomatoTopmost.exe"
