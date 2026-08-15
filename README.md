# DocIntel

Document intelligence API and UI. Upload a PDF or DOCX, wait until it is indexed, then ask questions grounded in that file — not the open web.

The UI is a static SPA in `DocIntelApi/wwwroot`, served by the same ASP.NET Core 10 process as the API.

**Live local URL:** http://localhost:5082  
**API docs:** http://localhost:5082/scalar  
**Repo:** https://github.com/Pavelfahmi/DocIntelApi

## Stack

| Piece | Role |
| --- | --- |
| ASP.NET Core 10 | API + UI (`net10.0`) |
| PostgreSQL 16 | Users, documents, chat history |
| Qdrant | Per-document vectors (`doc-{guid}`, gRPC **6334**) |
| Gemini | Embeddings (`gemini-embedding-001`) and answers (`gemini-3.5-flash`) |

## Prerequisites

- Docker Desktop
- A [Gemini API key](https://aistudio.google.com/apikey)
- .NET 10 SDK — only if you run the API on the host instead of in Docker

## Secrets

JWT and Gemini keys are **not** in git. Set them once with user-secrets (Windows/macOS/Linux):

```bash
cd DocIntelApi
dotnet user-secrets set "Jwt:Secret" "replace-with-at-least-32-characters"
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_KEY"
```

Docker Compose mounts that secrets folder into the API container on Windows via `%APPDATA%`. On Linux/macOS, pass the same values as environment variables instead:

```yaml
Jwt__Secret: "replace-with-at-least-32-characters"
Gemini__ApiKey: "YOUR_GEMINI_KEY"
```

## Run with Docker (recommended)

Named volumes are external so existing local data is reused. Create them once if they do not exist:

```bash
docker volume create docintel_pgdata
docker volume create qdrant_storage
docker compose up --build -d
```

Then open http://localhost:5082

| Container | Host port |
| --- | --- |
| `docintel-api` | 5082 |
| `docintel-postgres` | 5433 → 5432 |
| `qdrant` | 6333 (HTTP), 6334 (gRPC) |

Inside the Docker network the API talks to `postgres:5432` and `qdrant:6334`.

Stop:

```bash
docker compose down
```

## Run the API on the host

Keep Postgres and Qdrant in Docker, then:

```bash
docker compose up -d postgres qdrant
cd DocIntelApi
dotnet run --launch-profile http
```

Host `appsettings.json` expects Postgres on **localhost:5433** and Qdrant gRPC on **localhost:6334**.

## Development admin

In Development the seeder creates:

- Email: `admin@docintel.local`
- Password: `Admin123!`

Do not use this in production. Set `ASPNETCORE_ENVIRONMENT=Production` and create a real admin yourself.

## How it works

1. **Upload** extracts text (PDF / DOCX / plain text, max 10 MB), splits into 500-word chunks with 100-word overlap, saves status `Pending`, and enqueues indexing.
2. **Index** (background) embeds chunks with Gemini (`RETRIEVAL_DOCUMENT`), writes a Qdrant collection `doc-{id}`, then sets status `Ready`.
3. **Ask** embeds the question (`RETRIEVAL_QUERY`), searches top 5 passages, and asks Gemini to answer **only** from those passages.

Indexing is in-memory. Restarting the API while a document is `Pending` or `Processing` can leave it stuck until you re-upload.

## API

All document and admin routes need `Authorization: Bearer <token>`.

| Method | Path | Notes |
| --- | --- | --- |
| `POST` | `/api/v1/auth/register` | 201 / 409 |
| `POST` | `/api/v1/auth/login` | 200 / 401 |
| `POST` | `/api/v1/documents` | multipart, 202 Accepted |
| `GET` | `/api/v1/documents` | current user |
| `GET` | `/api/v1/documents/{id}` | |
| `DELETE` | `/api/v1/documents/{id}` | also drops the Qdrant collection |
| `POST` | `/api/v1/documents/{id}/ask` | `{ "question": "...", "topK": 5 }` |
| `GET` | `/api/v1/admin/usage` | admin token dashboard |

Postman collection: `DocIntelApi/postman/DocIntelApi.postman_collection.json`

## Supported files

PDF, DOCX, TXT, MD, CSV, JSON, XML, HTML, RTF, LOG. Image-only PDFs are not OCR'd. Legacy `.doc` is not supported (save as `.docx`).
