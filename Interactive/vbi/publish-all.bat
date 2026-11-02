cd /d "%~dp0"
dotnet publish -r win-x64 -f net10.0-windows
dotnet publish -f net10.0
dotnet pack -o bin\Release
pause