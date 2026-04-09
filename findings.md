# Findings & Research

## Project State
- ASP.NET Core 8 MVC scaffold, no existing business logic
- Has Bootstrap 5 in wwwroot/lib
- No NuGet packages beyond default MVC scaffold

## Large File Handling Strategy
- ASP.NET Core default request body limit: 30MB → must override to 500MB+
- Use `[DisableRequestSizeLimit]` or configure `KestrelServerOptions.Limits.MaxRequestBodySize`
- Use `IFormFile.OpenReadStream()` to stream directly to temp file — never `IFormFile.CopyToAsync(MemoryStream)`
- Set `MultipartBodyLengthLimit` in `[RequestFormLimits]`

## RFC 4180 CSV Escaping Rules
- Fields containing `,` (CSV delimiter), `"`, `\r`, or `\n` MUST be wrapped in double quotes
- Internal `"` characters MUST be doubled: `"` → `""`
- Single quotes `'` do NOT need escaping in CSV
- Line terminator MUST be `\r\n` (CRLF) per spec

## TXT File Parsing
- Input delimiter is `~` by default (user configurable)
- Input may contain embedded `'` or `"` — these are literal field values
- Split strategy: simple `string.Split(delimiter)` per line is sufficient since `~` rarely appears in data
- If delimiter appears inside a quoted field in TXT — handle with state machine parser

## Excel Considerations
- ClosedXML: simple API, holds workbook in memory → limit to ~50MB input
- Excel max rows: 1,048,576 → warn user if line count exceeds this
- For large files → recommend CSV output

## Progress Reporting via SSE
- Endpoint returns `text/event-stream` content type
- Each event: `data: {json}\n\n`
- Browser uses `EventSource` API
- No CORS issues since same-origin
- Connection closes when job completes/fails

## UI Design Direction
- Dark glassmorphism aesthetic (frosted glass cards)
- Gradient background (deep navy → purple)
- Smooth animations (file drop zone pulse, progress bar shimmer)
- Single-page: no navigation to other pages needed
- Responsive — works on desktop and tablet
