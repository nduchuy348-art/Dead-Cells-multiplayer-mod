@echo off
title Dead Cells Multiplayer - Windows Firewall Configurator
echo ========================================================
echo   Dead Cells Multiplayer - Windows Firewall Configurator
echo ========================================================
echo Configuring Windows Firewall inbound rules for Port 1234...

net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

netsh advfirewall firewall delete rule name="DeadCells-Port1234" >nul 2>&1
netsh advfirewall firewall delete rule name="DeadCells-Port1234-UDP" >nul 2>&1
netsh advfirewall firewall delete rule name="DeadCells-Game" >nul 2>&1
netsh advfirewall firewall delete rule name="DeadCells-GameGL" >nul 2>&1
netsh advfirewall firewall delete rule name="DeadCells-Modding" >nul 2>&1

netsh advfirewall firewall add rule name="DeadCells-Port1234" dir=in action=allow protocol=TCP localport=1234 profile=any
netsh advfirewall firewall add rule name="DeadCells-Port1234-UDP" dir=in action=allow protocol=UDP localport=1234 profile=any
netsh advfirewall firewall add rule name="DeadCells-Game" dir=in action=allow program="%~dp0deadcells.exe" enable=yes profile=any
netsh advfirewall firewall add rule name="DeadCells-GameGL" dir=in action=allow program="%~dp0deadcells_gl.exe" enable=yes profile=any
netsh advfirewall firewall add rule name="DeadCells-Modding" dir=in action=allow program="%~dp0coremod\core\host\startup\DeadCellsModding.exe" enable=yes profile=any

echo [OK] Successfully opened Windows Firewall rules for Dead Cells on Port 1234 and Tailscale!
echo You can now host multiplayer co-op lobbies with your friends.
pause
