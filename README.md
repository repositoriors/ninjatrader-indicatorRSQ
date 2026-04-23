# RSQ NinjaTrader Indicator

Public distribution package for the `RSQSignalMultiFrame` NinjaTrader 8 indicator.

This package is intentionally limited to the client-side indicator only.

## Included

- `RSQSignalMultiFrame.cs`
- `INSTALL.md`

## Not Included

- RSQ backend/API source code
- `.env` files
- internal logs, CSVs, audits, or research artifacts
- experimental indicators and diagnostic scripts

## Recommended Publishing Model

Use this repository as the public client package and keep the signal engine private.

- Public: indicator source and installation docs
- Private: backend logic, thresholds, scoring, training data, audits, and deployment files

## Basic Usage

1. Copy `RSQSignalMultiFrame.cs` to `Documents\NinjaTrader 8\bin\Custom\Indicators`.
2. In NinjaTrader 8, open `Tools -> Edit NinjaScript -> Compile`.
3. Add `RSQSignalMultiFrame` to a chart.
4. Fill in:
   - `ApiKey`
   - `ApiBaseUrl`

Common `ApiBaseUrl` values:

- Local: `http://127.0.0.1:8000`
- Production: `https://api.rsq.digital`

## Exposure Boundary

This package exposes the indicator UI/client integration only. It should not be used as a public repository for the full RSQ backend project.
