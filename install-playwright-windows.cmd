@echo off
dotnet build src\LiveTotalsHelper.Tools\LiveTotalsHelper.Tools.csproj
powershell -ExecutionPolicy Bypass -File src\LiveTotalsHelper.Tools\bin\Debug\net8.0\playwright.ps1 install chromium
