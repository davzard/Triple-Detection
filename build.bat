@echo off
rem 在项目根目录执行（自动定位脚本所在目录），输出到 build_output.txt
cd /d "%~dp0"
dotnet build TripleDetection.sln -c Debug > build_output.txt 2>&1