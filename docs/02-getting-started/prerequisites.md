# Prerequisites and Installation

This guide lists the required tools for running and contributing to the EShop backend repository.

---

## System Requirements

### Minimum

| Component | Requirement |
|-----------|-------------|
| OS | Windows 10/11, macOS 12+, Linux (Ubuntu 22.04+) |
| RAM | 8 GB |
| Disk | 20 GB free space |
| CPU | 2+ cores |

### Recommended

| Component | Requirement |
|-----------|-------------|
| RAM | 16 GB+ |
| Disk | SSD with 30+ GB free space |
| CPU | 4+ cores |

---

## Required Software

### 1. .NET 10 SDK

#### Verify

```bash
dotnet --version
# Expected: 10.x
```

#### Install

**Windows**

```powershell
winget install Microsoft.DotNet.SDK.10
```

If the WinGet package ID has changed, run `winget search dotnet sdk` to find the current name, or download the SDK directly from https://dotnet.microsoft.com/download/dotnet/10.0.

**macOS**

```bash
brew install --cask dotnet-sdk
```

**Linux (Ubuntu)**

```bash
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

#### Post-install checks

```bash
dotnet --list-sdks
dotnet --info
```

---

### 2. Docker Desktop (or Docker Engine + Compose plugin)

#### Verify

```bash
docker --version
docker compose version
```

#### Install

- Windows/macOS: Docker Desktop
- Linux: Docker Engine + Docker Compose plugin

#### Recommended local Docker resources

- CPUs: 4+
- Memory: 8 GB+
- Disk: 20 GB+

---

### 3. Git

#### Verify

```bash
git --version
```

#### Basic setup

```bash
git config --global user.name "Your Name"
git config --global user.email "your.email@example.com"
git config --global init.defaultBranch main
```

---

### 4. IDE

Choose one:

- Visual Studio 2026+ with .NET and ASP.NET workloads
- JetBrains Rider (latest)
- Visual Studio Code with C# Dev Kit

---

## Optional Tools

### Node.js (only if you work with external UI clients)

Node.js is **not required** to build backend services in this repository.

```bash
node --version
npm --version
```

### Database and Cache Clients

- pgAdmin / psql
- Redis Insight / redis-cli

### API Tools

- Postman
- VS Code REST Client
- `curl`

---

## Environment Files

Copy and adjust environment files before Docker runs:

```bash
cp .env.example .env
```

For local development, using convenient password values in `.env` is acceptable. Replace them with secure secret management for non-local environments.

---

## Quick Validation Checklist

- `dotnet --version` returns `10.x`
- `docker compose version` works
- `git --version` works
- You can open the solution in your IDE

---

## Related Documents

- [Local Setup](local-setup.md)
- [Docker Setup](docker-setup.md)
- [Team Agreement](team-agreement.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
