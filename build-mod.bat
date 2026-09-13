@echo off
rem ============================================================
rem  PCL 幻星修改版 一键构建脚本
rem  前置条件：
rem    1. .NET SDK 9 位于 D:\dotnet（zip 免安装版即可）
rem    2. VS 2022 Build Tools 位于 D:\VSBuildTools\2022\BuildTools
rem       （需要 VB.NET / .NET Framework 4.8 目标能力）
rem    3. net48 引用程序集已解压至 D:\build-tools\ref48pkg\build
rem       （来自 NuGet 包 Microsoft.NETFramework.ReferenceAssemblies.net48）
rem  用法：直接双击运行，或在其目录下执行 build-mod.bat
rem ============================================================
setlocal
set ROOT=%~dp0
set DOTNET_ROOT=D:\dotnet
set MSBuildSDKsPath=D:\dotnet\sdk\9.0.318\Sdks
set MSBUILD=D:\VSBuildTools\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe
set REFROOT=D:\build-tools\ref48pkg\build

echo [1/2] 编译 C# 支持库（MeloongCore / MeloongCore.Wpf / PCLCS）...
"%DOTNET_ROOT%\dotnet.exe" build "%ROOT%MeloongCore\Shared\MeloongCore.csproj" -c Debug --nologo || goto :fail
"%DOTNET_ROOT%\dotnet.exe" build "%ROOT%MeloongCore\Wpf\MeloongCore.Wpf.csproj" -c Debug --nologo || goto :fail
"%DOTNET_ROOT%\dotnet.exe" build "%ROOT%PCLCS\PCLCS.csproj" -c Debug --nologo || goto :fail

echo [2/2] 编译主程序（VB.NET / WPF）...
"%MSBUILD%" "%ROOT%Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" ^
  -p:Configuration=Debug -p:BuildProjectReferences=false ^
  -p:TargetFrameworkRootPath="%REFROOT%" -nologo -v:m || goto :fail

echo.
echo 构建完成：%ROOT%Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe
exit /b 0

:fail
echo.
echo 构建失败，请检查上方错误信息。
exit /b 1
