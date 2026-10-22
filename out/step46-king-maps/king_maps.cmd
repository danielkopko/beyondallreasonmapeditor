@echo off
rem The king maps: king_maps.cmd publish <star_fort|edge_keeps|corner_fortresses> [-Message "..."]  or  king_maps.cmd install <map> [-BarData folder]  (see king_maps.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0king_maps.ps1" %*
