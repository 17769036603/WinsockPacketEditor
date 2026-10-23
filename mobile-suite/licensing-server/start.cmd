@echo off
node "%~dp0start-local.mjs"
if errorlevel 1 (
  echo.
  echo Licensing service did not start. See the message above.
  pause
)
