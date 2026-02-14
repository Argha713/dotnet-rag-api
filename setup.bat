@echo off
REM =============================================================================
REM dotnet-rag-api Setup Script for Windows
REM Run this script to set up everything needed to run the RAG API
REM =============================================================================

echo ==============================================
echo   dotnet-rag-api Environment Setup
echo ==============================================
echo.

echo Step 1: Checking Prerequisites...
echo -----------------------------------

REM Check .NET SDK
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [X] .NET SDK is NOT installed
    echo     Please install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
echo [OK] .NET SDK is installed
dotnet --version

REM Check Docker
where docker >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [X] Docker is NOT installed
    echo     Please install Docker Desktop: https://www.docker.com/get-started
    pause
    exit /b 1
)
echo [OK] Docker is installed

docker info >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [X] Docker is not running. Please start Docker Desktop.
    pause
    exit /b 1
)
echo [OK] Docker daemon is running

echo.
echo Step 2: Starting Docker Services...
echo ------------------------------------

if not exist "docker-compose.yml" (
    echo [X] docker-compose.yml not found. Are you in the project root?
    pause
    exit /b 1
)

echo Starting Qdrant and Ollama containers...
docker-compose up -d

echo Waiting for services to start...
timeout /t 5 /nobreak >nul

echo.
echo Step 3: Pulling Ollama Models...
echo ---------------------------------

echo Pulling nomic-embed-text (embedding model)...
docker exec -it rag-ollama ollama pull nomic-embed-text

echo Pulling llama3.2 (chat model)...
docker exec -it rag-ollama ollama pull llama3.2

echo.
echo Step 4: Restoring .NET Dependencies...
echo ---------------------------------------

cd src\RagApi.Api
dotnet restore
cd ..\..

echo.
echo ==============================================
echo   Setup Complete!
echo ==============================================
echo.
echo To run the API:
echo   cd src\RagApi.Api
echo   dotnet run
echo.
echo Then open: http://localhost:5000
echo.
pause
