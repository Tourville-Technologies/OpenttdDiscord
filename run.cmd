@echo off
setlocal

where docker >nul 2>&1
if errorlevel 1 (
    echo Docker was not found. Install and start Docker Desktop, then try again.
    exit /b 1
)

if not defined discord_token (
    echo The discord_token environment variable is not set.
    echo.
    echo In PowerShell, run:
    echo   $env:discord_token = "YOUR_DISCORD_BOT_TOKEN"
    echo   .\run.cmd
    exit /b 1
)

docker compose version >nul 2>&1
if errorlevel 1 (
    echo Docker Compose is unavailable. Make sure Docker Desktop is running.
    exit /b 1
)

echo Building the OpenTTD Discord bot...
docker compose build
if errorlevel 1 exit /b 1

echo Starting the bot and database...
docker compose up --no-build -d --force-recreate
if errorlevel 1 exit /b 1

echo.
echo OpenTTD Discord is running.
echo View logs with: docker compose logs -f bot
exit /b 0
