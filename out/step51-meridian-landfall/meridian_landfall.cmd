@echo off
rem Meridian Landfall: meridian_landfall.cmd publish [-Message "..."]  or  meridian_landfall.cmd install [-BarData folder]  (see meridian_landfall.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0meridian_landfall.ps1" %*
