# Installation

## NinjaTrader 8

1. Close NinjaTrader if you are replacing an existing file.
2. Copy `RSQSignalMultiFrame.cs` to:

`Documents\NinjaTrader 8\bin\Custom\Indicators`

3. Open NinjaTrader 8.
4. Go to `Tools -> Edit NinjaScript -> Compile`.
5. Open a chart and add `RSQSignalMultiFrame`.

## Indicator Fields

- `ApiKey`: your RSQ access key
- `ApiBaseUrl`: API endpoint
- `Symbol`: example `BTCUSDT`
- `Min Strength`: minimum signal threshold
- `Refresh Seconds`: visual refresh interval
- `Show Full HUD`: show full layout
- `Show All Timeframes`: show `1s`, `1m`, and `5m`

## API Base URL Examples

- Local API: `http://127.0.0.1:8000`
- Hosted API: `https://api.rsq.digital`

## Safe Distribution Notes

If you share this package with other users, do not include:

- personal API keys
- `.env` files
- backend source code
- local research CSV files
- deployment scripts from the private RSQ project
