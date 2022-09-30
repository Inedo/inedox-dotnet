@echo off

dotnet new tool-manifest --force
dotnet tool install inedo.extensionpackager

cd DotNet\InedoExtension
dotnet inedoxpack pack . C:\LocalDev\BuildMaster\Extensions\DotNet.upack --build=Debug -o
cd ..\..