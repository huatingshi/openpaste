@echo off
powershell -STA -NoProfile -ExecutionPolicy Bypass -File "%~dp0openpaste.ps1" %*
