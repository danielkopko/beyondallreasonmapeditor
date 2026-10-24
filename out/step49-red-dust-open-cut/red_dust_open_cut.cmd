@echo off
rem Red Dust Open Cut: red_dust_open_cut.cmd publish [-Message "..."]  or  red_dust_open_cut.cmd install [-BarData folder]  (see red_dust_open_cut.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0red_dust_open_cut.ps1" %*
