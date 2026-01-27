# 🔧 Передумови та встановлення

## Системні вимоги

### Мінімальні вимоги
| Компонент | Вимога |
|-----------|--------|
| **OS** | Windows 10/11, macOS 12+, Linux (Ubuntu 20.04+) |
| **RAM** | 8 GB (16 GB рекомендовано) |
| **Disk** | 20 GB вільного місця |
| **CPU** | 2+ cores |

### Рекомендовані вимоги
| Компонент | Вимога |
|-----------|--------|
| **RAM** | 16 GB+ |
| **Disk** | SSD з 30+ GB |
| **CPU** | 4+ cores |

---

## Необхідне ПЗ

### 1. .NET SDK 9.0

#### Перевірка встановлення
```bash
dotnet --version
# Має показати: 9.0.x
```

#### Встановлення

**Windows**:
```powershell
# Через winget
winget install Microsoft.DotNet.SDK.9

# АБО завантажити з офіційного сайту
# https://dotnet.microsoft.com/download/dotnet/9.0
```

**macOS**:
```bash
# Через Homebrew
brew install dotnet@9

# АБО завантажити .pkg файл
# https://dotnet.microsoft.com/download/dotnet/9.0
```

**Linux (Ubuntu/Debian)**:
```bash
# Додати Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Встановити SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0
```

#### Перевірка після встановлення
```bash
dotnet --list-sdks
# Має показати 9.0.x у списку

dotnet --info
# Має показати Runtime, SDK versions
```

---

### 2. Docker Desktop

#### Перевірка встановлення
```bash
docker --version
# Має показати: Docker version 24.x.x або новіше

docker compose version
# Має показати: Docker Compose version 2.x.x
```

#### Встановлення

**Windows**:
```powershell
# Завантажити Docker Desktop для Windows
# https://www.docker.com/products/docker-desktop/

# Після встановлення:
# 1. Увімкнути WSL 2 (якщо ще не увімкнено)
# 2. Перезавантажити систему
# 3. Запустити Docker Desktop
```

**macOS**:
```bash
# Через Homebrew
brew install --cask docker

# АБО завантажити з офіційного сайту
# https://www.docker.com/products/docker-desktop/

# Запустити Docker.app
```

**Linux (Ubuntu)**:
```bash
# Видалити старі версії
sudo apt-get remove docker docker-engine docker.io containerd runc

# Встановити залежності
sudo apt-get update
sudo apt-get install ca-certificates curl gnupg lsb-release

# Додати Docker GPG key
sudo mkdir -p /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg

# Додати repository
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Встановити Docker Engine
sudo apt-get update
sudo apt-get install docker-ce docker-ce-cli containerd.io docker-compose-plugin

# Додати свого користувача до групи docker (щоб не використовувати sudo)
sudo usermod -aG docker $USER
newgrp docker
```

#### Перевірка Docker після встановлення
```bash
# Перевірка Docker Engine
docker run hello-world

# Перевірка Docker Compose
docker compose version

# Перевірка доступних ресурсів
docker system info
```

#### Налаштування Docker Desktop (Рекомендовано)

**Resources Settings**:
- **CPUs**: 4+ cores
- **Memory**: 8 GB+ (для всього stack)
- **Disk**: 20 GB+

**Advanced Settings** (Windows):
- ✅ Enable WSL 2 based engine
- ✅ Use Docker Compose V2

---

### 3. Git

#### Перевірка встановлення
```bash
git --version
# Має показати: git version 2.x.x
```

#### Встановлення

**Windows**:
```powershell
# Через winget
winget install Git.Git

# АБО завантажити з
# https://git-scm.com/download/win
```

**macOS**:
```bash
# Вже встановлений з Xcode Command Line Tools
# АБО через Homebrew
brew install git
```

**Linux**:
```bash
sudo apt-get update
sudo apt-get install git
```

#### Початкова конфігурація Git
```bash
git config --global user.name "Your Name"
git config --global user.email "your.email@example.com"
git config --global init.defaultBranch main
git config --global core.autocrlf input  # Linux/Mac
# git config --global core.autocrlf true  # Windows
```

---

### 4. IDE / Code Editor

#### Рекомендовані варіанти

##### Option 1: Visual Studio 2022 (Windows)
- **Версія**: Community (безкоштовна) або Professional/Enterprise
- **Завантажити**: https://visualstudio.microsoft.com/downloads/
- **Workloads**:
  - ✅ ASP.NET and web development
  - ✅ .NET Desktop development
  - ✅ Azure development (опційно)

##### Option 2: JetBrains Rider (Cross-platform) ⭐ Рекомендовано
- **Завантажити**: https://www.jetbrains.com/rider/download/
- **Ліцензія**: Платна (є безкоштовна для студентів)
- **Переваги**: Найкращий C# IDE, вбудований Docker support

##### Option 3: Visual Studio Code (Cross-platform)
```bash
# Windows
winget install Microsoft.VisualStudioCode

# macOS
brew install --cask visual-studio-code

# Linux
sudo snap install code --classic
```

**Необхідні розширення для VS Code**:
```bash
# Встановити через командний рядок
code --install-extension ms-dotnettools.csharp
code --install-extension ms-dotnettools.csdevkit
code --install-extension ms-azuretools.vscode-docker
code --install-extension ms-vscode-remote.remote-containers
code --install-extension eamodio.gitlens
```

---

### 5. Node.js (для Frontend)

#### Перевірка встановлення
```bash
node --version
# Має показати: v20.x.x або новіше

npm --version
# Має показати: 10.x.x або новіше
```

#### Встановлення

**Windows**:
```powershell
# Через winget
winget install OpenJS.NodeJS.LTS

# АБО завантажити LTS версію
# https://nodejs.org/
```

**macOS**:
```bash
# Через Homebrew
brew install node@20

# Додати до PATH (якщо потрібно)
echo 'export PATH="/opt/homebrew/opt/node@20/bin:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

**Linux**:
```bash
# Через NodeSource repository (LTS)
curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
sudo apt-get install -y nodejs

# Перевірка
node --version
npm --version
```

#### Альтернатива: nvm (Node Version Manager) ⭐ Рекомендовано
```bash
# Windows: завантажити nvm-windows
# https://github.com/coreybutler/nvm-windows/releases

# macOS/Linux
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.5/install.sh | bash

# Після встановлення nvm
nvm install --lts
nvm use --lts
```

---

## Опційні інструменти

### 1. PostgreSQL Client (для ручних запитів)

#### pgAdmin (GUI)
```bash
# Windows
winget install PostgreSQL.pgAdmin

# macOS
brew install --cask pgadmin4

# Linux
sudo apt-get install pgadmin4
```

#### psql (CLI)
```bash
# macOS
brew install postgresql

# Linux
sudo apt-get install postgresql-client

# Підключення до Docker PostgreSQL
psql -h localhost -U eshop -d catalog
```

---

### 2. Redis Client (для debug кешу)

#### Redis Insight (GUI) ⭐ Рекомендовано
```bash
# Завантажити
# https://redis.com/redis-enterprise/redis-insight/

# АБО через Docker
docker run -d --name redis-insight -p 5540:5540 redis/redisinsight:latest
```

#### redis-cli (CLI)
```bash
# Вже включений у Docker image RabbitMQ
docker exec -it redis redis-cli

# Приклад команд
> PING
> KEYS *
> GET basket:user123
```

---

### 3. API Testing Tools

#### Postman (GUI)
```bash
# Windows
winget install Postman.Postman

# macOS
brew install --cask postman

# АБО використовувати web версію
# https://www.postman.com/
```

#### HTTP Client у VS Code
```bash
# Встановити REST Client extension
code --install-extension humao.rest-client
```

**Приклад .http файлу**:
```http
### Get Products
GET http://localhost:5000/api/v1/products HTTP/1.1

### Login
POST http://localhost:5000/api/v1/auth/login HTTP/1.1
Content-Type: application/json

{
  "email": "test@test.com",
  "password": "Test123!"
}
```

---

### 4. Database Migration Tool (опційно)

#### EF Core Tools (Global Tool)
```bash
# Встановити глобально
dotnet tool install --global dotnet-ef

# Перевірка
dotnet ef --version

# Використання
dotnet ef migrations add InitialCreate -p Infrastructure -s API
dotnet ef database update -p Infrastructure -s API
```

---

## Перевірка всього стеку

### Чеклист перед початком

Виконайте всі команди нижче і переконайтеся що немає помилок:

```bash
# 1. .NET SDK
dotnet --version
dotnet --list-sdks

# 2. Docker
docker --version
docker compose version
docker run hello-world

# 3. Git
git --version
git config --list

# 4. Node.js
node --version
npm --version

# 5. IDE/Editor
code --version  # VS Code
# АБО відкрийте Visual Studio / Rider

# Якщо всі команди виконались успішно - ви готові! ✅
```

---

## Troubleshooting (Типові проблеми)

### Проблема: Docker не запускається

**Windows**:
```powershell
# 1. Переконайтеся що WSL 2 встановлений
wsl --list --verbose

# Якщо немає WSL 2:
wsl --install

# 2. Оновіть WSL kernel
wsl --update

# 3. Перезапустіть Docker Desktop
```

**macOS**:
```bash
# Переконайтеся що Docker.app запущений
open -a Docker

# Якщо не допомагає - переінсталюйте
brew reinstall --cask docker
```

---

### Проблема: .NET SDK не знайдено

```bash
# Перевірте PATH
echo $PATH  # Linux/macOS
echo %PATH% # Windows

# Перезапустіть термінал після встановлення

# Якщо не допомагає - переінсталюйте SDK
```

---

### Проблема: Docker out of memory

**Рішення**:
1. Відкрийте Docker Desktop Settings
2. Resources → Memory → збільште до 8 GB
3. Apply & Restart

---

### Проблема: Port already in use

```bash
# Знайти процес що використовує порт (наприклад 5432)

# Windows
netstat -ano | findstr :5432
taskkill /PID <PID> /F

# macOS/Linux
lsof -i :5432
kill -9 <PID>

# АБО зупиніть Docker контейнери
docker compose down
```

---

## Наступні кроки

Після встановлення всього ПЗ:

1. ✅ Перейдіть до [Local Setup](local-setup.md) для клонування проекту
2. ✅ Налаштуйте [Docker](docker-setup.md) для запуску інфраструктури
3. ✅ Ознайомтеся з [Team Agreement](team-agreement.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
