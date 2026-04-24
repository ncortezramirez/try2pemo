# Task Dashboard — MIS 321 (Full-stack)

Personal task and project manager: **ASP.NET Core Web API (.NET 8)** + **MySQL (direct SQL via MySqlConnector)**, and a **vanilla JavaScript + Bootstrap 5** dashboard.

---

## Prerequisites

- **[.NET 8 SDK](https://dotnet.microsoft.com/download)** (`dotnet --version` should show 8.x)
- **MySQL Server 8+** running locally (or update `ConnectionStrings:MySql`)
- A **static file server** for the frontend (recommended: [Live Server](https://marketplace.visualstudio.com/items?itemName=ritwickdey.LiveServer) in VS Code, or any `localhost` server)

> **Do not open `index.html` as a `file://` URL** — browsers block API calls from `file://`. Use `http://localhost` (e.g. Live Server).

---

## Quick start (for grading / local demo)

### 1. Start the API

From the **repository root**:

```bash
cd api/TaskDashboard.Api
dotnet run --launch-profile http
```

Leave this terminal open. The API listens at **http://localhost:5253** (HTTP profile).

- Health check: [http://localhost:5253/api/health](http://localhost:5253/api/health)
- OpenAPI (dev): [http://localhost:5253/openapi/v1.json](http://localhost:5253/openapi/v1.json) (if enabled)

> If you use the **https** profile instead, the URL is **https://localhost:7131**. Then update the frontend `config.js` (see below).

### 2. Database (automatic)

On startup, the API creates required **MySQL tables** (`Projects`, `Tasks`) if they do not exist and seeds sample rows.

- Configure connection in `api/TaskDashboard.Api/appsettings.json` under `ConnectionStrings:MySql`.

### 3. Run the frontend

1. Open the **`Client`** folder in VS Code (or your editor).
2. Start Live Server (or similar) so the site is served at something like **http://127.0.0.1:5500**.
3. Ensure **`Client/Resources/scripts/config.js`** matches your API URL:

```js
window.APP_CONFIG = {
  apiBaseUrl: "http://localhost:5253"   // use https://localhost:7131 if using HTTPS profile
};
```

4. Reload the page. You should see projects/tasks load without CORS errors.

**Development CORS:** In `Development`, the API allows any **localhost** / **127.0.0.1** origin (any port), so Live Server on 5500, 8080, etc. works without editing the backend.

---

## Project layout

| Path | Purpose |
|------|--------|
| `TaskDashboard.sln` | Visual Studio / `dotnet` solution |
| `api/TaskDashboard.Api/` | Web API, direct SQL data access |
| `Client/` | `index.html`, `Resources/styles/styles.css`, `Resources/scripts/app.js`, **`Resources/scripts/config.js`** (API URL) |
| `TESTING.md` | Manual validation checklist |
| `api/TaskDashboard.Api.Tests/` | Unit tests (`RequestValidators`) |

---

## Configuration

| Setting | Location | Notes |
|--------|-----------|--------|
| MySQL connection | `appsettings.json` → `ConnectionStrings:MySql` | Example: `server=localhost;port=3306;database=taskdashboard;user=root;password=changeme;` |
| OpenAI (optional) | `OpenAI:ApiKey` | Use [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) or env var `OpenAI__ApiKey` |

```bash
cd api/TaskDashboard.Api
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-key-here"
```

---

## Tests

From the repo root:

```bash
dotnet test TaskDashboard.sln
```

---

## Optional: deployment notes

### Backend (API)

- Publish: `dotnet publish api/TaskDashboard.Api -c Release -o ./publish`
- Host on **Azure App Service**, **IIS**, **Linux + Kestrel**, or any host that runs .NET 8.
- Set environment to **Production** and configure:
  - Connection strings / `Database:Provider`
  - **CORS**: Production currently allows specific origins in `Program.cs` — adjust `WithOrigins(...)` for your real frontend URL.
- For production, secure the MySQL credentials and permit backend network access to your DB server.

### Frontend (static site)

- Deploy **`Client/`** contents (HTML/CSS/JS + **`Resources/scripts/config.js`**) to **GitHub Pages**, **Azure Static Web Apps**, **Netlify**, **S3 + CloudFront**, etc.
- After deployment, set **`config.js`** → `apiBaseUrl` to your **public API URL** (must use **HTTPS** in production).
- Update backend **CORS** to include your static site origin.

---

## Troubleshooting

| Issue | What to try |
|-------|-------------|
| CORS error in browser | Serve frontend over `http://localhost` or `http://127.0.0.1`, not `file://`. |
| Connection refused | API not running, or wrong port in `config.js`. |
| DB init error on startup | Verify MySQL is running and `ConnectionStrings:MySql` is valid in `appsettings.json`. |
| HTTPS redirect confusion | Use `--launch-profile http` and `http://localhost:5253` in `config.js`. |

---

## Authors

Course / group project — MIS 321.

## Terminal commands to run app

cd api/TaskDashboard.Api
dotnet run --launch-profile http

cd Client
npx serve -1 5500