@echo off
rem Five Rivers: five_rivers.cmd join [-Install] ^| split ^| push [-Message "..."]   (see five_rivers.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0five_rivers.ps1" %*
