@echo off
rem Doble clic: abre el visor del Ojeador con los analisis de FakeDatos (Start-Visor.ps1).
rem Esta ventana se cierra sola al cerrar la del visor.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Visor.ps1" -Pause %*
