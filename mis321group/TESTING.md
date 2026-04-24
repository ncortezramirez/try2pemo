# Validation & edge cases (manual checklist)

Run the API (`dotnet run --launch-profile http` in `api/TaskDashboard.Api`) and open the **`Client`** folder with a static server (e.g. Live Server). Match **`Client/Resources/scripts/config.js`** to your API URL (`http://localhost:5253` by default).

## Backend (HTTP)

| Case | How to test | Expected |
|------|-------------|----------|
| No tasks | `GET /api/tasks/all` | `200`, `[]` |
| Many tasks | Add many tasks; `GET /api/tasks/all` | `200`, ordered list |
| Missing due date | `POST /api/tasks` with `"dueDate": null` | `201`; task has no due; no Calendar until due set |
| Empty body | `POST /api/tasks` with empty body | `400`, `{ "error": "Request body is required." }` |
| Empty title | `POST /api/tasks` with `"title": " "` | `400`, title error |
| Invalid `projectId` | `POST` with `"projectId": 0` or `-1` | `400` |
| Unknown project | `POST` with non-existent `projectId` | `400`, project not found message |
| Invalid query | `GET /api/tasks/all?projectId=0` | `400` |
| Calendar, no due | `GET /api/tasks/{id}/calendar` for task without due | `400` |
| Calendar, missing task | `GET /api/tasks/99999/calendar` | `404` |

## Frontend

| Case | Expected |
|------|----------|
| No projects | Quick add disabled; clear error if API unreachable |
| No tasks | Empty hint; count “No tasks” |
| Many tasks | List renders; filters still work |
| Missing due dates | “—” in date chip; no Calendar button |
| Submit empty title | Inline error; no request |
| Title too long (>500) | Client-side error before request |
| API down / network | Red error toast; network message |
| Initial load | “Loading…” then content or error |

## Automated

```bash
dotnet test TaskDashboard.sln
```

Covers shared validation rules in `RequestValidators`.
