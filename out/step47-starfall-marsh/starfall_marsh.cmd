@echo off
rem Starfall Marsh: starfall_marsh.cmd publish [-Message "..."]  or  starfall_marsh.cmd install [-BarData folder]  (see starfall_marsh.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0starfall_marsh.ps1" %*
