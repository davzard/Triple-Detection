# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Triple-Detection (Apache 2.0): WPF visual inspection system for packaging lines — OCR-verify batch number (BN) / production date (Mfg) / expiration date (EXP) against task parameters, archive images, record results to SQLite.

## Project Structure (single csproj, DDD folders)

- **TripleDetection.csproj** — the only project; `net8.0-windows`, **C# 12.0**, x64, WPF + WinForms
- `Domain/` — entities (`DetectionRecord`, `Task`, `Product`, `User`…), enums, query models
- `Application/` — services & detection engines: `VmServices/` (VmIntegrationService, MockDetectionEngine), `Services/` (LoggingService, user/task/product/detection-record services, settings services)
- `Infrastructure/` — persistence (SqliteRepository<T> is reflection-driven; DatabaseInitializer creates the schema via raw SQL), JsonHelper
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
- `Infrastructure/Persistence/DatabaseInitializer.cs` — schema initialization (raw SQL)

## Database Conventions

- Schema is managed by raw SQL in `DatabaseInitializer` (EF configurations exist for model consistency but migrations are not EF-migration-based)
- `SqliteRepository<T>` inserts/maps all public writable properties by name — new entity fields flow through automatically once the DB column exists
- All queries use `SELECT *`; adding columns is safe once the corresponding DB column exists

## VisionMaster SDK

- Installed at: `C:\Program Files\VisionMaster4.2.0`
- SDK DLLs: `C:\Program Files\VisionMaster4.2.0\Development\V4.x\Libraries\win64\C#`
- Default solution file: `D:\xcm\ApplicationDemo\OCRDemoCs\OCRDemoChinese.sol`
