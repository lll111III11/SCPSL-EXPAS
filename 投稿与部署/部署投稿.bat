@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ============================================
echo  EXPAS 投稿一键导入
echo ============================================
echo.

where node >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 Node.js, 请先安装 Node.js 后再运行。
    echo        下载: https://nodejs.org/
    echo.
    pause
    exit /b 1
)

rem 支持把"收件夹"直接拖到本 bat 上(可选); 不拖就用脚本里的默认收件夹
if not "%~1"=="" (
    set "EXPAS_SRC=%~1"
    echo 使用拖入的收件夹: %~1
    echo.
)

node "%~dp0部署投稿.js" %2

echo.
pause
