@echo off
title Trust DAVE_HR Code-Signing Certificate
echo ========================================================
echo   Trusting DAVE_HR Code-Signing Certificate on this PC   
echo ========================================================
echo.
echo Installing certificate into CurrentUser Root and TrustedPublisher stores...
echo (When Windows displays the Security Warning prompt, click [Yes] to confirm)
echo.
certutil -user -addstore "Root" "%~dp0DAVE_HR.cer"
certutil -user -addstore "TrustedPublisher" "%~dp0DAVE_HR.cer"
echo.
echo ========================================================
echo   Done! Windows now treats DAVE_HR as a Verified Publisher.
echo   VortexSetup.exe will now launch with trusted status.
echo ========================================================
pause
