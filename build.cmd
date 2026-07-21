@echo off
setlocal
rem Runs the NUKE build (build/Build.cs) — the same entry point used locally and in CI.
rem Usage: build.cmd <Target> [<Target>...] [--<parameter> <value>]
cd /d "%~dp0"
dotnet run --project build\_build.csproj --configuration Release -- %*
endlocal
