@echo off
setlocal EnableExtensions
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0kashflow-mcp.ps1" %*
exit /b %ERRORLEVEL%
