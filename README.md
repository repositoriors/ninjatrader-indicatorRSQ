# NinjaTrader Indicator Package

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

## Basic Usage

1. Copy `RSQSignalMultiFrame.cs` to `Documents\NinjaTrader 8\bin\Custom\Indicators`.
2. In NinjaTrader 8, open `Tools -> Edit NinjaScript -> Compile`.
3. Add `RSQSignalMultiFrame` to a chart.
4. Fill in:
   - `ApiKey`
   - `ApiBaseUrl`

`ApiBaseUrl` should be provided by the operator for the intended environment.

## Exposure Boundary

This package is limited to the client-side indicator only.
