@echo off
rem Five Rivers: five_rivers.cmd publish [-Message "..."]  or  five_rivers.cmd install [-BarData folder]  (see five_rivers.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0five_rivers.ps1" %*
