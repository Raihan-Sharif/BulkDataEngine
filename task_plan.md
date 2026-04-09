# Task Plan â€” TXT to CSV/Excel Converter

## Goal
Build a high-performance, production-grade ASP.NET Core 8 MVC web application that converts large TXT files (100MB+) to CSV or Excel format with:
- Configurable column delimiter (default `~`)
- Header row support (first row treated as header)
- Accurate handling of embedded single/double quotes, newlines, and special chars
- Real-time progress via Server-Sent Events (SSE)
- All configuration in appsettings.json
- Modern, aesthetic UI

## Architecture Decisions
| Decision | Choice | Reason |
|---|---|---|
| Progress push | Server-Sent Events (SSE) | Lightweight, no SignalR overhead, works great for one-way serverâ†’browser push |
| Excel library | ClosedXML | Clean API, NuGet stable, handles large workbooks row-by-row |
| File upload | Streaming to temp dir | Never buffer entire 100MB file in memory |
| CSV algorithm | RFC 4180 compliant custom | Handle `~` delimiter + embedded quotes/newlines correctly |
| Job tracking | ConcurrentDictionary in-memory | Simple, no DB dependency for this utility app |

## Phases

### Phase 1 â€” Project Setup & Config [ ]
- [ ] Update appsettings.json with all config (max file size, temp path, defaults)
- [ ] Update BulkDataEngine.csproj â€” add ClosedXML NuGet
- [ ] Update Program.cs â€” register services, configure limits

### Phase 2 â€” Models & Services [ ]
- [ ] Models/ConverterSettings.cs â€” config binding model
- [ ] Models/ConversionJob.cs â€” job state (progress, status, output path)
- [ ] Models/ConversionRequest.cs â€” upload request parameters
- [ ] Services/IConversionService.cs â€” interface
- [ ] Services/ConversionService.cs â€” core streaming parse + write engine
- [ ] Services/ConversionJobManager.cs â€” job lifecycle management

### Phase 3 â€” Controller [ ]
- [ ] Controllers/HomeController.cs â€” Upload, Progress (SSE), Download, Cancel endpoints

### Phase 4 â€” UI [ ]
- [ ] Views/Home/Index.cshtml â€” full modern single-page UI
- [ ] wwwroot/css/site.css â€” glassmorphism + gradient modern aesthetic
- [ ] wwwroot/js/site.js â€” drag-drop upload, SSE progress, download trigger

## Key Algorithm: TXT â†’ CSV (RFC 4180)
```
For each line in TXT file (streamed, never fully loaded):
  Split by chosen delimiter (e.g. ~)
  For each field:
    Trim if configured
    If field contains: CSV delimiter, double-quote, \r, or \n:
      â†’ Wrap in double quotes
      â†’ Escape internal " as ""
    Write field to CSV output stream
  Write line terminator \r\n
Report progress every N lines or M bytes
```

## Config Keys (appsettings.json)
- `Converter:DefaultDelimiter` â€” default `~`
- `Converter:MaxFileSizeMB` â€” default `500`
- `Converter:TempDirectory` â€” default `temp`
- `Converter:ProgressReportEveryNLines` â€” default `1000`
- `Converter:JobExpiryMinutes` â€” default `30`
- `Converter:DefaultHasHeader` â€” default `true`
- `Converter:MaxExcelFileSizeMB` â€” default `50` (Excel has row limits; large files â†’ CSV recommended)
