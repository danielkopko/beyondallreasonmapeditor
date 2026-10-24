@echo off
rem Starship Meridian: starship_meridian.cmd publish [-Message "..."]  or  starship_meridian.cmd install [-BarData folder]  (see starship_meridian.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0starship_meridian.ps1" %*
