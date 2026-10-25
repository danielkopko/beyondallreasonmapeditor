@echo off
rem Marram Line: marram_line.cmd publish [-Message "..."]  or  marram_line.cmd install [-BarData folder]  (see marram_line.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0marram_line.ps1" %*
