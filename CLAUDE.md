# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Triple-Detection (Apache 2.0): WPF visual inspection system for packaging lines — OCR-verify batch number (BN) / production date (Mfg) / expiration date (EXP) against task parameters, archive images, record results to SQLite.

## Project Structure (single csproj, DDD folders)

- **TripleDetection.csproj** — the only project; `net8.0-windows`, **C# 12.0**, x64, WPF + WinForms
- `Domain/` — entities (`DetectionRecord`, `Task`, `Product`, `User`…), enums (`NgReason`…), query models
- `Application/` — services & detection engines: `VmServices/` (VmIntegrationService, MockDetectionEngine, ImageArchiver, result parsers), `Services/` (LoggingService, user/task/product/detection-record services, settings services)
- `Infrastructure/` — persistence (SqliteRepository<T> is reflection-driven; DatabaseInitializer with versioned migrations), JsonHelper
- `Presentation/` — WPF UI (Prism 9 modules/views/viewmodels), `App.xaml.cs` holds all DI registrations

## Tech Stack

- .NET 8 (net8.0-windows), C# 12
- Prism 9.0.537 (DryIoc) — DI + navigation; registrations in `Presentation/App.xaml.cs`
- EF Core 8.0.11 + SQLite (Microsoft.Data.Sqlite) — DB at `Data/tripledetection.db`
- Newtonsoft.Json 13.0.3
- VisionMaster SDK — **conditional**: `VM_SDK` compile macro is defined only when `Infrastructure/libs/VM.Core.dll` and the installed VMControls WPF DLL both exist; otherwise `MockDetectionEngine` is used

## Build Commands

```
dotnet build TripleDetection.sln -c Debug
```

`build.bat` does the same from the repo root. (Older docs mention MSBuild/.NET Framework 4.8/C# 8 — that is stale; this project is .NET 8 single-project.)

## Key Files

- `Presentation/App.xaml.cs` — DI registrations (add new services here)
- `Presentation/Views/App/DetectionView.xaml(.cs)` — detection workflow UI
- `Presentation/Views/App/DashboardView.xaml(.cs)` — dashboard statistics
- `Application/VmServices/VmIntegrationService.cs` — VisionMaster SDK wrapper (VM_SDK only)
- `Application/VmServices/MockDetectionEngine.cs` — no-hardware simulation of the full detection chain
- `Application/VmServices/ResultParserRegistry.cs` — result protocol routing: v1 CSV / v2 JSON (see design doc §3.3)
- `Application/VmServices/ImageArchiver.cs` — image archive: `Images/{yyyyMMdd}/{OK|NG}/{时间}_{工位}_{任务}_{批号}_{序号}_{OK|NG}.png`
- `Infrastructure/Persistence/DatabaseInitializer.cs` — versioned schema migrations (SchemaMigrations table); new migrations are appended, never edited
- `Domain/Enums/Enums.cs` — `NgReason` NG defect classification

## Database Conventions

- Schema is managed by raw SQL in `DatabaseInitializer` (EF configurations exist for model consistency but migrations are not EF-migration-based)
- `SqliteRepository<T>` inserts/maps all public writable properties by name — new entity fields flow through automatically once the DB column exists
- All queries use `SELECT *`; adding columns is safe, but always add a versioned migration for the column

## Production Readiness (ongoing)

`docs/specs/2026-11-01-production-readiness-design.md` is the governing design doc for the factory-production retrofit (PLC/IO, line state machine, MES, RBAC, SPC, deployment). Phase 0 (foundation) is implemented; follow its conventions:

- NG records must carry `NgReason` + `NgDetail`; OK records use `NgReason.None`
- No swallowed exceptions in detection paths — log with level (`LoggingService.Log(msg, "ERROR")`); only the logger itself may fail silently
- Detection path must never block on I/O (images, DB, MES)

## VisionMaster SDK

- Installed at: `C:\Program Files\VisionMaster4.2.0`
- SDK DLLs: `C:\Program Files\VisionMaster4.2.0\Development\V4.x\Libraries\win64\C#`
- Default solution file: `D:\xcm\ApplicationDemo\OCRDemoCs\OCRDemoChinese.sol`