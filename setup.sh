#!/bin/bash

# =============================================================================
# dotnet-rag-api Setup Script
# Run this script to set up everything needed to run the RAG API
# =============================================================================

set -e

echo "=============================================="
echo "  dotnet-rag-api Environment Setup"
echo "=============================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to check if command exists
check_command() {
    if command -v $1 &> /dev/null; then
        echo -e "${GREEN}✓${NC} $1 is installed"
        return 0
    else
        echo -e "${RED}✗${NC} $1 is NOT installed"
        return 1
    fi
}

# Function to check if docker container is running
check_container() {
    if docker ps --format '{{.Names}}' | grep -q "^$1$"; then
        echo -e "${GREEN}✓${NC} Container '$1' is running"
        return 0
    else
        echo -e "${YELLOW}○${NC} Container '$1' is not running"
        return 1
    fi
}

echo "Step 1: Checking Prerequisites..."
echo "-----------------------------------"

# Check .NET SDK
if check_command dotnet; then
    DOTNET_VERSION=$(dotnet --version)
    echo "  Version: $DOTNET_VERSION"
    if [[ ! "$DOTNET_VERSION" =~ ^8\. ]]; then
        echo -e "${YELLOW}  Warning: .NET 8.x recommended, you have $DOTNET_VERSION${NC}"
    fi
else
    echo -e "${RED}  Please install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0${NC}"
    exit 1
fi

# Check Docker
if check_command docker; then
    if docker info &> /dev/null; then
        echo -e "${GREEN}✓${NC} Docker daemon is running"
    else
        echo -e "${RED}✗${NC} Docker is installed but not running. Please start Docker Desktop."
        exit 1
    fi
else
    echo -e "${RED}  Please install Docker: https://www.docker.com/get-started${NC}"
    exit 1
fi

echo ""
echo "Step 2: Starting Docker Services..."
echo "------------------------------------"

# Start docker-compose services
if [ -f "docker-compose.yml" ]; then
    echo "Starting Qdrant and Ollama containers..."
    docker-compose up -d
    
    # Wait for services to be ready
    echo "Waiting for services to start..."
    sleep 5
else
    echo -e "${RED}docker-compose.yml not found. Are you in the project root?${NC}"
    exit 1
fi

# Verify containers
echo ""
check_container "rag-qdrant"
check_container "rag-ollama"

echo ""
echo "Step 3: Pulling Ollama Models..."
echo "---------------------------------"

# Pull embedding model
echo "Pulling nomic-embed-text (embedding model)..."
docker exec -it rag-ollama ollama pull nomic-embed-text

# Pull chat model
echo "Pulling llama3.2 (chat model)..."
docker exec -it rag-ollama ollama pull llama3.2

echo ""
echo "Step 4: Verifying Services..."
echo "------------------------------"

# Check Qdrant
if curl -s http://localhost:6333/collections > /dev/null 2>&1; then
    echo -e "${GREEN}✓${NC} Qdrant is responding on http://localhost:6333"
else
    echo -e "${RED}✗${NC} Qdrant is not responding"
fi

# Check Ollama
if curl -s http://localhost:11434/api/tags > /dev/null 2>&1; then
    echo -e "${GREEN}✓${NC} Ollama is responding on http://localhost:11434"
else
    echo -e "${RED}✗${NC} Ollama is not responding"
fi

echo ""
echo "Step 5: Restoring .NET Dependencies..."
echo "---------------------------------------"

cd src/RagApi.Api
dotnet restore
cd ../..

echo ""
echo "=============================================="
echo -e "${GREEN}  Setup Complete!${NC}"
echo "=============================================="
echo ""
echo "To run the API:"
echo "  cd src/RagApi.Api"
echo "  dotnet run"
echo ""
echo "Then open: http://localhost:5000"
echo ""
