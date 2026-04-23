//======================================================================
// RSQ Signal Multi-Frame - NinjaTrader 8 indicator
//
// All displayed metrics come from the configured remote API.
//
// No local chart data (Volume, Close, DOM) is mixed with RSQ data.
// No broker-specific delta is shown.
//
// INSTALLATION:
//   1. Copy to Documents\NinjaTrader 8\bin\Custom\Indicators\
//   2. Tools -> Edit NinjaScript -> Compile
//   3. Add to chart and enter your API settings
//   4. Provide your API settings
//======================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using D2DBrush = SharpDX.Direct2D1.SolidColorBrush;
using DWTextFormat = SharpDX.DirectWrite.TextFormat;
using DWFontWeight = SharpDX.DirectWrite.FontWeight;
using DWFontStyle = SharpDX.DirectWrite.FontStyle;
using DWTextAlignment = SharpDX.DirectWrite.TextAlignment;
using DWParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RSQSignalMultiFrame : Indicator
    {
        //------------------------------------------------------------------
        // ESTADO
        //------------------------------------------------------------------
        private readonly object signalLock = new object();
        private WebClient webClient;
        private DateTime lastFetchAttemptAt = DateTime.MinValue;
        private DateTime lastSuccessfulFetchAt = DateTime.MinValue;
        private bool disposed;
        private bool fetchInProgress;
        private bool renderResourceErrorLogged;

        private System.Timers.Timer motionTimer;
        private DateTime lastVisualRefresh = DateTime.MinValue;
        private DateTime lastSemanticChange = DateTime.MinValue;
        private string lastSemanticKey = "";
        private string lastTemporalPhase = "";
        private DateTime temporalPhaseStartedAt = DateTime.MinValue;

        private string lastBias        = "neutral";
        private string lastMarketState = "quiet";
        private string lastAsOf        = "";
        private string lastFetchError  = "";
        private double lastStrength    = 0.0;
        private int    lastHttpStatus  = 0;
        private int    consecutiveFetchFailures = 0;
        private int    fetchCount      = 0;
        private string lastArrowBar    = "";

        private readonly List<SignalPoint> history = new List<SignalPoint>();
        private const int HistorySize = 24;

        // AnimaÃƒÂ§ÃƒÂ£o suave das pressure bars
        private bool pressureInitialized;
        private DateTime pressureAnimStart = DateTime.MinValue;
        private double pressureFromBuy = 0.5;
        private double pressureFromSell = 0.5;
        private double pressureTargetBuy = 0.5;
        private double pressureTargetSell = 0.5;
        private double pressureDisplayBuy = 0.5;
        private double pressureDisplaySell = 0.5;

        // Recursos SharpDX
        private D2DBrush brushPanel, brushPanelAlt, brushBorder;
        private D2DBrush brushText, brushMuted;
        private D2DBrush brushBuy, brushSell, brushNeutral;
        private D2DBrush brushActive, brushTransition, brushQuiet;
        private D2DBrush brushWhiteSoft, brushRailBase, brushGridLine, brushMeterBg;
        private D2DBrush brushAmbientBuy, brushAmbientSell;
        private D2DBrush brushAccentBuy, brushAccentSell, brushAccentNeutral;
        private DWTextFormat tfTiny, tfSmall, tfBody, tfHeader, tfDominant, tfHero;

        private class SignalPoint
        {
            public string Bias;
            public double Strength;
            public string State;
            public DateTime Time;
        }


        private class TimeframeData
        {
            public string Bias = "neutral";
            public string State = "quiet";
            public double Strength = 0.0;
            public string AsOf = "";
        }

        private class MultiFrameSnapshot
        {
            public TimeframeData Tf1s = new TimeframeData();
            public TimeframeData Tf1m = new TimeframeData();
            public TimeframeData Tf5m = new TimeframeData();
            public double AlignmentScore = 0.0;
            public string Consensus = "neutral";
        }

        private MultiFrameSnapshot lastMultiFrame = new MultiFrameSnapshot();
        private double alignmentFrom = 0.0;
        private double alignmentTarget = 0.0;
        private double alignmentDisplay = 0.0;
        private DateTime alignmentAnimStart = DateTime.MinValue;

        //------------------------------------------------------------------
        // PARÃƒâ€šMETROS PÃƒÅ¡BLICOS
        //------------------------------------------------------------------
        [NinjaScriptProperty]
        [Display(Name = "ApiKey", Order = 1, GroupName = "RSQ Signal")]
        public string ApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Symbol", Order = 2, GroupName = "RSQ Signal")]
        public string Symbol { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ApiBaseUrl", Order = 3, GroupName = "RSQ Signal")]
        public string ApiBaseUrl { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Min Strength", Order = 4, GroupName = "RSQ Signal")]
        public double MinStrength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3600)]
        [Display(Name = "Refresh Seconds", Order = 5, GroupName = "RSQ Signal")]
        public int RefreshSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Full HUD", Order = 6, GroupName = "RSQ Signal")]
        public bool ShowFullHud { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show All Timeframes", Order = 7, GroupName = "RSQ Signal")]
        public bool ShowAllTimeframes { get; set; }

        //------------------------------------------------------------------
        // CICLO DE VIDA
        //------------------------------------------------------------------
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description       = "RSQ multi-frame signal HUD. Diagnostic display only.";
                Name              = "RSQSignalMultiFrame";
                Calculate         = Calculate.OnEachTick;
                IsOverlay         = true;
                DisplayInDataBox  = false;
                PaintPriceMarkers = false;
                ApiKey            = "";
                Symbol            = "BTCUSDT";
                ApiBaseUrl        = "";
                MinStrength       = 0.65;
                RefreshSeconds    = 1;
                ShowFullHud       = true;
                ShowAllTimeframes = true;
            }
            else if (State == State.DataLoaded)
            {
                disposed = false;
                webClient = new WebClient();
                webClient.Headers.Add("User-Agent", "RSQ/2.1");

                motionTimer = new System.Timers.Timer(250);
                motionTimer.AutoReset = true;
                motionTimer.Elapsed += OnMotionTimer;
                motionTimer.Start();

                Print("RSQ: HUD initialized refresh=" + RefreshSeconds + "s");
            }
            else if (State == State.Terminated)
            {
                disposed = true;
                if (motionTimer != null)
                {
                    motionTimer.Stop();
                    motionTimer.Elapsed -= OnMotionTimer;
                    motionTimer.Dispose();
                    motionTimer = null;
                }
                if (webClient != null)
                {
                    webClient.Dispose();
                    webClient = null;
                }
                DisposeDxResources();
            }
        }

        private void OnMotionTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            TryFetchIfDue("timer");
            RequestMotionRefresh();
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2 || disposed) return;

            Snapshot snap = BuildSnapshot();
            if (CanEmitPriceMarker(snap))
                DrawPriceMarker(snap.Bias, snap.State, snap.Strength);
            else
                RemoveDrawObject("RSQMF_MARKER_GLOW");

            RequestMotionRefresh();
        }

        //------------------------------------------------------------------
        // RENDERING PRINCIPAL
        //------------------------------------------------------------------
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (ChartPanel == null || RenderTarget == null) return;

            if (brushPanel == null)
            {
                try { CreateDxResources(); }
                catch (Exception ex)
                {
                    if (!renderResourceErrorLogged)
                    {
                        renderResourceErrorLogged = true;
                        Print("RSQ render error: " + ex.Message);
                    }
                    return;
                }
            }

            Snapshot snap = BuildSnapshot();
            Semantic sem = BuildSemantic(snap);
            UpdateSemanticTransition(sem);

            if (!ShowFullHud)
            {
                DrawCompactBadge(snap, sem);
                return;
            }

            DrawAmbientContext(snap, sem);
            DrawCommandHud(snap, sem);
            DrawMemoryRail(snap);
            DrawPriceLink(snap, sem, chartScale);
        }

        public override void OnRenderTargetChanged()
        {
            DisposeDxResources();
            if (RenderTarget != null) CreateDxResources();
        }

        //------------------------------------------------------------------
        // COMMAND HUD Ã¢â‚¬â€ painel principal (392 Ãƒâ€” 240)
        //------------------------------------------------------------------
        private void DrawCommandHud(Snapshot s, Semantic sem)
        {
            DrawMultiFrameHud(s, sem);
        }

        private void DrawMultiFrameHud(Snapshot s, Semantic sem)
        {
            MultiFrameSnapshot mf;
            lock (signalLock)
                mf = CloneMultiFrame(lastMultiFrame);

            UpdateAlignmentAnimation(mf.AlignmentScore);

            float x = ChartPanel.X + 18f;
            float y = ChartPanel.Y + 18f;
            bool triple = mf.AlignmentScore >= 0.999
                && NormalizeBias(mf.Consensus) != "NEUTRAL";
            float w = 392f;
            float h = triple ? 322f : 286f;

            RectangleF panel = new RectangleF(x, y, w, h);
            RenderTarget.FillRectangle(panel, triple ? brushPanelAlt : brushPanel);
            RenderTarget.DrawRectangle(panel, triple ? ConsensusBrush(mf.Consensus) : brushBorder, triple ? 1.8f : 1.1f);
            FillAccentStrip(x, y, w, h, sem);

            DrawText("RSQ SIGNAL ? MULTI-FRAME", tfHeader, brushText, x + 18, y + 12, 230, 22);
            DrawLiveDot(x + w - 108, y + 22, s, sem);
            DrawText(FreshnessLabel(s), tfTiny, StatusBrush(s), x + w - 100, y + 16, 82, 18, DWTextAlignment.Trailing);
            DrawText(SymbolText(), tfSmall, brushText, x + w - 118, y + 40, 100, 18, DWTextAlignment.Trailing);

            RenderTarget.DrawLine(new Vector2(x + 18, y + 62), new Vector2(x + w - 18, y + 62), brushGridLine, 1f);

            string consensus = NormalizeBias(mf.Consensus);
            float cy;
            if (triple)
            {
                DrawText("TRIPLE", tfHero, ConsensusBrush(mf.Consensus), x + 18, y + 74, 180, 28);
                DrawText("CONFLUENCE", tfHero, ConsensusBrush(mf.Consensus), x + 18, y + 98, 220, 28);
                cy = y + 132;
            }
            else
            {
                DrawText("CONSENSUS", tfHeader, brushMuted, x + 18, y + 76, 180, 20);
                cy = y + 104;
            }

            DrawText(consensus, tfDominant, ConsensusBrush(mf.Consensus), x + 18, cy, 160, 38);
            DrawAlignmentBar(x + 178, cy + 10, 186, 13, alignmentDisplay);
            DrawText(AlignmentText(mf), tfTiny, brushMuted, x + 178, cy + 28, 186, 14, DWTextAlignment.Trailing);

            float rowsY = triple ? y + 194 : y + 156;
            if (ShowAllTimeframes)
            {
                DrawTfRow("1s", mf.Tf1s, x + 18, rowsY, w - 36);
                DrawTfRow("1m", mf.Tf1m, x + 18, rowsY + 30, w - 36);
                DrawTfRow("5m", mf.Tf5m, x + 18, rowsY + 60, w - 36);
            }

            RenderTarget.DrawLine(new Vector2(x + 18, y + h - 48), new Vector2(x + w - 18, y + h - 48), brushGridLine, 1f);
            DrawText("ALIGNMENT: " + mf.AlignmentScore.ToString("0.00"), tfSmall, AlignmentBrush(mf.AlignmentScore), x + 18, y + h - 38, 160, 18);
            DrawText("Diagnostic display.", tfTiny, brushMuted, x + w - 190, y + h - 36, 172, 16, DWTextAlignment.Trailing);
        }

        private void DrawTfRow(string label, TimeframeData tf, float x, float y, float w)
        {
            string bias = NormalizeBias(tf == null ? "neutral" : tf.Bias);
            string state = NormalizeState(tf == null ? "quiet" : tf.State);
            double strength = Clamp01(tf == null ? 0.0 : tf.Strength);
            D2DBrush sideBrush = ConsensusBrush(bias);

            DrawText(label, tfSmall, brushText, x, y, 28, 18);
            DrawText(bias, tfSmall, sideBrush, x + 38, y, 62, 18);
            DrawText(((int)Math.Round(strength * 100)).ToString() + "%", tfSmall, brushText, x + 106, y, 44, 18, DWTextAlignment.Trailing);
            DrawMiniTfBar(x + 158, y + 4, 134, 10, strength, sideBrush);
            DrawStateDot(x + 306, y + 9, state);
            DrawText(StateShort(state), tfTiny, StateBrush(state), x + 316, y + 1, 54, 16);
        }

        private void DrawMiniTfBar(float x, float y, float w, float h, double value, D2DBrush fill)
        {
            RenderTarget.FillRectangle(new RectangleF(x, y, w, h), brushMeterBg);
            RenderTarget.FillRectangle(new RectangleF(x, y, (float)Clamp01(value) * w, h), fill);
            RenderTarget.DrawRectangle(new RectangleF(x, y, w, h), brushGridLine, 0.6f);
        }

        private void DrawAlignmentBar(float x, float y, float w, float h, double value)
        {
            D2DBrush fill = AlignmentBrush(value);
            RenderTarget.FillRectangle(new RectangleF(x, y, w, h), brushMeterBg);
            RenderTarget.FillRectangle(new RectangleF(x, y, (float)Clamp01(value) * w, h), fill);
            RenderTarget.DrawRectangle(new RectangleF(x, y, w, h), brushGridLine, 0.8f);
        }

        private void DrawStateDot(float x, float y, string state)
        {
            RenderTarget.FillEllipse(new SharpDX.Direct2D1.Ellipse(new Vector2(x, y), 4f, 4f), StateBrush(state));
        }

        private string StateShort(string state)
        {
            if (state == "ACTIVE") return "active";
            if (state == "TRANSITIONING") return "trans";
            return "quiet";
        }

        private string AlignmentText(MultiFrameSnapshot mf)
        {
            int aligned = (int)Math.Round(Clamp01(mf.AlignmentScore) * 3.0);
            if (mf.AlignmentScore >= 0.99) aligned = 3;
            else if (mf.AlignmentScore >= 0.60) aligned = 2;
            else if (mf.AlignmentScore > 0.0) aligned = 1;
            return aligned + "/3 aligned";
        }

        private D2DBrush AlignmentBrush(double score)
        {
            if (score >= 0.9) return brushActive;
            if (score >= 0.6) return brushTransition;
            return brushMuted;
        }

        private D2DBrush ConsensusBrush(string bias)
        {
            string b = NormalizeBias(bias);
            if (b == "BUY") return brushBuy;
            if (b == "SELL") return brushSell;
            return brushNeutral;
        }

        private void UpdateAlignmentAnimation(double target)
        {
            target = Clamp01(target);
            if (alignmentAnimStart == DateTime.MinValue)
            {
                alignmentFrom = alignmentTarget = alignmentDisplay = target;
                alignmentAnimStart = DateTime.Now;
                return;
            }
            if (Math.Abs(target - alignmentTarget) > 0.002)
            {
                alignmentFrom = alignmentDisplay;
                alignmentTarget = target;
                alignmentAnimStart = DateTime.Now;
            }
            double t = Clamp01((DateTime.Now - alignmentAnimStart).TotalMilliseconds / 650.0);
            double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
            alignmentDisplay = alignmentFrom + (alignmentTarget - alignmentFrom) * eased;
        }

        //------------------------------------------------------------------
        // PRESSURE BARS Ã¢â‚¬â€ 100% RSQ, sem Volume local
        //------------------------------------------------------------------
        private void DrawDualPressureBars(float x, float y, float w, Snapshot s, Semantic sem)
        {
            double buy, sell;
            ComputePressureFromRsq(s, out buy, out sell);
            UpdatePressureAnimation(buy, sell);

            bool degraded = IsDataStale(s) || StatusLabel(s) == "ERROR";

            DrawPressureBar("BUY",  x, y,      w, 15f, pressureDisplayBuy,
                degraded ? brushAccentBuy : brushBuy, brushAccentBuy, degraded);
            DrawPressureBar("SELL", x, y + 22, w, 15f, pressureDisplaySell,
                degraded ? brushAccentSell : brushSell, brushAccentSell, degraded);
        }

        private void DrawPressureBar(string label, float x, float y, float w, float h,
            double value, D2DBrush main, D2DBrush accent, bool degraded)
        {
            float labelW = 56f;
            float barX = x + labelW;
            float barW = w - labelW - 54f;
            float fillW = Math.Max(3f, (float)Clamp01(value) * barW);
            if (degraded) fillW *= 0.65f;

            // Label
            DrawText(label, tfSmall, degraded ? brushMuted : brushText,
                x, y, labelW - 4f, h, DWTextAlignment.Leading);

            // Trilho
            RenderTarget.FillRectangle(
                new RectangleF(barX, y + 2f, barW, h - 4f), brushMeterBg);

            // Preenchimento
            RenderTarget.FillRectangle(
                new RectangleF(barX, y + 2f, fillW, h - 4f), main);

            // Linha de accent na base
            RenderTarget.FillRectangle(
                new RectangleF(barX, y + h - 3f, fillW, 1.6f), accent);

            // Borda sutil
            RenderTarget.DrawRectangle(
                new RectangleF(barX, y + 2f, barW, h - 4f), brushGridLine, 0.6f);

            // Percentual
            string pctText = ((int)Math.Round(Clamp01(value) * 100)) + "%";
            DrawText(pctText, tfTiny, degraded ? brushMuted : brushText,
                barX + barW + 6f, y, 48f, h, DWTextAlignment.Trailing);
        }

        private void ComputePressureFromRsq(
            Snapshot s, out double buy, out double sell)
        {
            if (s == null || s.History == null || s.History.Count == 0)
            {
                buy = 0.5; sell = 0.5; return;
            }

            var h = s.History;
            int n = h.Count;

            // Warm-up: evita travar no primeiro pico com histÃ³rico curto.
            if (n < 3)
            {
                string warmBias = NormalizeBias(s.Bias);
                double warmStr = Clamp01(s.Strength);
                double warmSigned = warmBias == "BUY" ? warmStr : warmBias == "SELL" ? -warmStr : 0.0;
                double warmSoft = Math.Tanh(warmSigned * 1.4);
                buy = 0.5 + (warmSoft * 0.36);
                sell = 1.0 - buy;
                return;
            }

            // Decaimento rÃ¡pido: remove memÃ³ria longa que gera delay.
            int depth = Math.Min(8, n);
            const double decay = 0.60;
            const double currentWeight = 2.2;

            double score = 0.0;
            double totalWeight = 0.0;

            for (int i = 0; i < depth; i++)
            {
                var pt = h[n - depth + i];
                string b = NormalizeBias(pt.Bias);
                double str = Clamp01(pt.Strength);
                double weight = Math.Pow(decay, depth - 1 - i);
                double signed = b == "BUY" ? 1.0 : b == "SELL" ? -1.0 : 0.0;

                // MantÃ©m micro variaÃ§Ã£o mesmo com forÃ§a baixa.
                double intensity = 0.20 + (0.80 * str);
                score += signed * intensity * weight;
                totalWeight += weight;
            }

            string currBias = NormalizeBias(s.Bias);
            double currStr = Clamp01(s.Strength);
            double currSigned = currBias == "BUY" ? 1.0 : currBias == "SELL" ? -1.0 : 0.0;
            double currIntensity = 0.20 + (0.80 * currStr);

            score += currSigned * currIntensity * currentWeight;
            totalWeight += currentWeight;

            if (totalWeight < 0.0001)
            {
                buy = 0.5; sell = 0.5; return;
            }

            double norm = score / totalWeight; // [-1, +1]
            norm = Math.Max(-1.0, Math.Min(1.0, norm));

            // Dead-zone pequeno para evitar jitter visual perto do centro.
            if (Math.Abs(norm) < 0.03) norm = 0.0;

            double softened = Math.Tanh(norm * 1.55);

            // Faixa Ãºtil sem saturar em extremos rÃ­gidos (sem "colar").
            buy = 0.5 + (softened * 0.38); // ~12% a ~88%
            sell = 1.0 - buy;
        }

        private void UpdatePressureAnimation(double buyT, double sellT)
        {
            if (!pressureInitialized)
            {
                pressureInitialized = true;
                pressureTargetBuy = pressureFromBuy = pressureDisplayBuy = buyT;
                pressureTargetSell = pressureFromSell = pressureDisplaySell = sellT;
                pressureAnimStart = DateTime.Now;
                return;
            }

            if (Math.Abs(buyT - pressureTargetBuy) > 0.002
                || Math.Abs(sellT - pressureTargetSell) > 0.002)
            {
                pressureFromBuy = pressureDisplayBuy;
                pressureFromSell = pressureDisplaySell;
                pressureTargetBuy = buyT;
                pressureTargetSell = sellT;
                pressureAnimStart = DateTime.Now;
            }

            double elapsed = (DateTime.Now - pressureAnimStart).TotalMilliseconds;
            double t = Clamp01(elapsed / 650.0);
            double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
            pressureDisplayBuy  = pressureFromBuy  + (pressureTargetBuy  - pressureFromBuy)  * eased;
            pressureDisplaySell = pressureFromSell + (pressureTargetSell - pressureFromSell) * eased;
        }

        private string PressureHint(Snapshot s)
        {
            string status = StatusLabel(s);
            if (status == "STALE") return "data stale";
            if (status == "ERROR") return "feed error";
            if (s == null) return "balanced";
            string side = InferStructureSide(s);
            if (side == "BUY") return "buy context";
            if (side == "SELL") return "sell context";
            if (NormalizeState(s.State) == "ACTIVE") return "confirmed active";
            if (NormalizeState(s.State) == "TRANSITIONING") return "building";
            return "quiet regime";
        }

        private D2DBrush PressureHintBrush(Snapshot s)
        {
            string status = StatusLabel(s);
            if (status == "STALE" || status == "ERROR") return brushMuted;
            if (NormalizeState(s.State) == "ACTIVE") return brushActive;
            if (NormalizeState(s.State) == "TRANSITIONING") return brushTransition;
            return brushMuted;
        }

        //------------------------------------------------------------------
        // SESSION MEMORY Ã¢â‚¬â€ ÃƒÂºltimos 24 sinais
        //------------------------------------------------------------------
        private void DrawMemoryRail(Snapshot s)
        {
            MultiFrameSnapshot mf;
            lock (signalLock)
                mf = CloneMultiFrame(lastMultiFrame);

            bool triple = mf.AlignmentScore >= 0.999
                && NormalizeBias(mf.Consensus) != "NEUTRAL";

            float x = ChartPanel.X + 18f;
            float panelTop = ChartPanel.Y + 18f;
            float panelHeight = triple ? 322f : 286f;
            float y = panelTop + panelHeight + 10f;
            float w = 392f;
            float h = 52f;

            RectangleF box = new RectangleF(x, y, w, h);
            RenderTarget.FillRectangle(box, brushPanelAlt);
            RenderTarget.DrawRectangle(box, brushBorder, 0.9f);

            DrawText("SESSION MEMORY", tfTiny, brushMuted, x + 12, y + 8, 120, 14);
            DrawText(HistoryCountLabel(s), tfTiny, brushMuted, x + w - 100, y + 8, 88, 14, DWTextAlignment.Trailing);

            int count = s.History == null ? 0 : Math.Min(s.History.Count, HistorySize);
            if (count == 0)
            {
                DrawText("waiting for signals", tfTiny, brushMuted, x + 12, y + 28, w - 24, 14);
                return;
            }

            float gap = 3f;
            float cellW = (w - 24f - (HistorySize - 1) * gap) / HistorySize;
            float baseY = y + 33f;
            double t = MotionSeconds();
            bool staleMemory = IsDataStale(s);

            for (int i = 0; i < HistorySize; i++)
            {
                int srcIndex = s.History.Count - HistorySize + i;
                if (srcIndex < 0) continue;

                SignalPoint pt = s.History[srcIndex];
                float recency = (i + 1f) / HistorySize;
                float intensity = 0.30f + (float)Clamp01(pt.Strength) * 0.70f;

                D2DBrush brush = staleMemory ? brushQuiet : MemoryBrush(pt);
                float cellH = 5f + 10f * intensity * (0.60f + 0.40f * recency);

                if (NormalizeState(pt.State) == "TRANSITIONING") cellH *= 0.85f;
                if (NormalizeState(pt.State) == "QUIET") cellH *= 0.60f;

                // ÃƒÅ¡ltima cÃƒÂ©lula pulsa levemente
                if (i == HistorySize - 1)
                    cellH += 1.4f * (float)((Math.Sin(t * 2.4) + 1.0) * 0.5);

                float cx = x + 12f + i * (cellW + gap);
                float cy = baseY + (12f - cellH);
                RenderTarget.FillRectangle(new RectangleF(cx, cy, cellW, cellH), brush);
            }
        }

        private string HistoryCountLabel(Snapshot s)
        {
            int n = s.History == null ? 0 : s.History.Count;
            return n + " / " + HistorySize;
        }

        private D2DBrush MemoryBrush(SignalPoint pt)
        {
            string b = NormalizeBias(pt.Bias);
            string st = NormalizeState(pt.State);
            if (st == "ACTIVE" && b == "BUY"  && pt.Strength >= MinStrength) return brushBuy;
            if (st == "ACTIVE" && b == "SELL" && pt.Strength >= MinStrength) return brushSell;
            if (st == "TRANSITIONING" && b == "BUY")  return brushAccentBuy;
            if (st == "TRANSITIONING" && b == "SELL") return brushAccentSell;
            if (st == "QUIET") return brushQuiet;
            return brushNeutral;
        }

        //------------------------------------------------------------------
        // COMPACT BADGE (quando ShowFullHud = false)
        //------------------------------------------------------------------
        private void DrawCompactBadge(Snapshot s, Semantic sem)
        {
            float x = ChartPanel.X + 18f;
            float y = ChartPanel.Y + 18f;
            float w = 260f;
            float h = 88f;

            RectangleF panel = new RectangleF(x, y, w, h);
            RenderTarget.FillRectangle(panel, brushPanel);
            RenderTarget.DrawRectangle(panel, brushBorder, 1.0f);
            FillAccentStrip(x, y, w, h, sem);

            DrawText("RSQ", tfSmall, brushText, x + 12, y + 8, 40, 16);
            DrawText(FreshnessLabel(s), tfTiny, StatusBrush(s), x + w - 88, y + 9, 76, 14, DWTextAlignment.Trailing);

            DrawText(sem.Headline, tfHeader, SemanticBrush(sem), x + 12, y + 26, w - 24, 22);
            DrawText(sem.Phase + "  Ã‚Â·  " + sem.Permission, tfTiny, brushMuted, x + 12, y + 54, w - 24, 14);
            DrawText(sem.Reason, tfTiny, brushMuted, x + 12, y + 70, w - 24, 14);
        }

        //------------------------------------------------------------------
        // AMBIENT CONTEXT Ã¢â‚¬â€ tint lateral sutil
        //------------------------------------------------------------------
        private void DrawAmbientContext(Snapshot s, Semantic sem)
        {
            if (StatusLabel(s) == "STALE" || StatusLabel(s) == "ERROR") return;
            if (NormalizeState(s.State) == "QUIET") return;
            if (s.Strength < 0.50) return;

            D2DBrush src = sem.StructureSide == "BUY" ? brushAmbientBuy
                         : sem.StructureSide == "SELL" ? brushAmbientSell
                         : null;
            if (src == null) return;

            float alphaWidth = NormalizeState(s.State) == "ACTIVE" ? 48f : 28f;
            RenderTarget.FillRectangle(
                new RectangleF(ChartPanel.X, ChartPanel.Y, alphaWidth, ChartPanel.H),
                src);
        }

        //------------------------------------------------------------------
        // PRICE LINK Ã¢â‚¬â€ linha do HUD ao preÃƒÂ§o atual
        //------------------------------------------------------------------
        private void DrawPriceLink(Snapshot s, Semantic sem, ChartScale chartScale)
        {
            if (ChartBars == null || ChartControl == null || CurrentBar < 2) return;
            if (StatusLabel(s) == "STALE" || StatusLabel(s) == "ERROR") return;
            if (sem.Permission != "WATCH" && sem.Permission != "ENABLED" && sem.Permission != "NO TRADE") return;

            try
            {
                float hudX = ChartPanel.X + 410f;
                float hudY = ChartPanel.Y + 118f;
                float px = (float)ChartControl.GetXByBarIndex(ChartBars, CurrentBar);
                float py = (float)chartScale.GetYByValue(Close[0]);
                if (px < hudX + 12f) return;

                D2DBrush b = sem.Permission == "ENABLED" ? SemanticBrush(sem) : brushGridLine;
                float thickness = sem.Permission == "ENABLED" ? 1.1f : 0.55f;
                RenderTarget.DrawLine(new Vector2(hudX, hudY), new Vector2(px - 10f, py), b, thickness);

                float r = sem.Permission == "ENABLED" ? 5f : 3.5f;
                RenderTarget.DrawEllipse(
                    new SharpDX.Direct2D1.Ellipse(new Vector2(px, py), r, r),
                    SemanticBrush(sem), 1.0f);
            }
            catch { /* silent */ }
        }

        //------------------------------------------------------------------
        // SEMANTIC Ã¢â‚¬â€ decide o que o HUD comunica
        //------------------------------------------------------------------
        private class Semantic
        {
            public string Phase;
            public string Structure;
            public string Permission;
            public string Action;
            public string Headline;
            public string Subline;
            public string Reason;
            public string StructureSide;
            public bool Execute;
            public string Key;
            public string TemporalPhase;
            public DateTime PhaseStartedAt;
        }

        private string DetectBiasDivergence(Snapshot s)
        {
            if (s == null || s.History == null
                || s.History.Count < 4)
                return "ALIGNED";

            var h = s.History;
            int n = h.Count;
            string currBias = NormalizeBias(s.Bias);

            int buyVotes = 0, sellVotes = 0;
            double recentStrengthAvg = 0;
            int count = Math.Min(4, n);

            for (int i = n - count; i < n; i++)
            {
                string b = NormalizeBias(h[i].Bias);
                if (b == "BUY") buyVotes++;
                else if (b == "SELL") sellVotes++;
                recentStrengthAvg += Clamp01(h[i].Strength);
            }
            recentStrengthAvg /= count;

            if (currBias == "SELL" && buyVotes >= 3)
                return "SELL_WEAKENING";
            if (currBias == "BUY" && sellVotes >= 3)
                return "BUY_WEAKENING";

            string side = InferStructureSide(s);
            if (currBias == "SELL" && side == "BUY")
                return "SELL_WEAKENING";
            if (currBias == "BUY" && side == "SELL")
                return "BUY_WEAKENING";

            if (n >= 3)
            {
                string twoAgo = NormalizeBias(h[n - 3].Bias);
                string oneAgo = NormalizeBias(h[n - 2].Bias);
                if (twoAgo == "SELL" && oneAgo != "SELL"
                    && currBias == "BUY")
                    return "BUY_RESPONSE";
                if (twoAgo == "BUY" && oneAgo != "BUY"
                    && currBias == "SELL")
                    return "SELL_RESPONSE";
            }

            return "ALIGNED";
        }

        private string RecentDirectionalContext(Snapshot s)
        {
            if (s == null || s.History == null || s.History.Count == 0)
                return "BALANCED";

            int count = Math.Min(8, s.History.Count);
            double score = 0.0;
            double total = 0.0;

            for (int i = 0; i < count; i++)
            {
                SignalPoint pt = s.History[s.History.Count - count + i];
                string b = NormalizeBias(pt.Bias);
                double recency = 0.75 + (i + 1.0) / Math.Max(1.0, count);
                double w = Math.Max(0.12, Clamp01(pt.Strength)) * recency;

                if (b == "BUY")
                {
                    score += w;
                    total += w;
                }
                else if (b == "SELL")
                {
                    score -= w;
                    total += w;
                }
                else
                {
                    // Neutral does not erase recent context; it only softens it.
                    total += w * 0.25;
                }
            }

            string current = NormalizeBias(s.Bias);
            double currentW = Math.Max(0.12, Clamp01(s.Strength)) * 1.25;
            if (current == "BUY")
            {
                score += currentW;
                total += currentW;
            }
            else if (current == "SELL")
            {
                score -= currentW;
                total += currentW;
            }

            if (total <= 0.0)
                return "BALANCED";

            double normalized = score / total;
            if (normalized > 0.10) return "BUY";
            if (normalized < -0.10) return "SELL";
            return "BALANCED";
        }

        private void ApplyDirectionalIntermediate(
            Semantic sem, string side, string phase,
            string bias, double strength)
        {
            bool isBuy = side == "BUY";
            string word = isBuy ? "BUY" : "SELL";
            sem.StructureSide = side;
            sem.Structure = word + " PRESSURE";
            sem.Action = "NONE";

            if (phase == "QUIET")
            {
                sem.Headline = word + " FORMING";
                sem.Subline = "early pressure";
                sem.Permission = "BLOCKED";
                sem.Reason = "quiet regime";
                return;
            }

            if (phase == "TRANSITIONING")
            {
                if (strength < 0.50)
                {
                    sem.Headline = word + " FORMING";
                    sem.Subline = "early pressure";
                    sem.Permission = "NO TRADE";
                    sem.Reason = "regime forming";
                }
                else if (strength < Math.Max(0.58, MinStrength * 0.92))
                {
                    sem.Headline = "WATCH " + word;
                    sem.Subline = "pressure building";
                    sem.Permission = "NO TRADE";
                    sem.Reason = "awaiting activation";
                }
                else
                {
                    sem.Headline = word + " ARMING";
                    sem.Subline = "near action zone";
                    sem.Permission = "NO TRADE";
                    sem.Reason = "regime not active";
                }
                return;
            }

            // ACTIVE, but not yet allowed to execute by the protected gate.
            if (bias == side && strength >= Math.Max(0.58, MinStrength * 0.92))
            {
                sem.Headline = word + " ARMING";
                sem.Subline = "active pressure";
                sem.Permission = "WATCH";
                sem.Reason = "strength below threshold";
            }
            else if (strength >= 0.50)
            {
                sem.Headline = "WATCH " + word;
                sem.Subline = "pressure active";
                sem.Permission = "WATCH";
                sem.Reason = bias == side ? "strength building" : "direction unconfirmed";
            }
            else
            {
                sem.Headline = word + " FORMING";
                sem.Subline = "early pressure";
                sem.Permission = "WATCH";
                sem.Reason = "awaiting confirmation";
            }
        }

        private Semantic BuildSemantic(Snapshot s)
        {
            string phase = s == null ? "QUIET" : NormalizeState(s.State);
            string bias = s == null ? "NEUTRAL" : NormalizeBias(s.Bias);
            double strength = s == null ? 0.0 : Clamp01(s.Strength);
            string side = InferStructureSide(s);
            string status = StatusLabel(s);
            string divergence = DetectBiasDivergence(s);

            Semantic sem = new Semantic();
            sem.Phase = phase;
            sem.StructureSide = side;
            sem.Structure = side == "BUY" ? "BUY PRESSURE"
                          : side == "SELL" ? "SELL PRESSURE"
                          : "BALANCED";
            sem.Execute = phase == "ACTIVE"
                          && (bias == "BUY" || bias == "SELL")
                          && strength >= MinStrength;
            sem.Action = sem.Execute ? bias : "NONE";
            sem.Key = phase + "|" + side + "|" + sem.Action + "|" + bias + "|" + status;

            string temporalPhase = DeriveTemporalPhase(s);
            sem.TemporalPhase = temporalPhase;

            if (temporalPhase != lastTemporalPhase)
            {
                lastTemporalPhase = temporalPhase;
                temporalPhaseStartedAt = DateTime.Now;
            }
            sem.PhaseStartedAt = temporalPhaseStartedAt;

            if (IsDataStale(s))
            {
                sem.Headline = "DATA STALE";
                sem.Subline  = "snapshot expired";
                sem.Permission = "STALE";
                sem.Action = "NONE";
                sem.Reason = "awaiting refresh";
                return sem;
            }

            if (status == "ERROR")
            {
                sem.Headline = "FEED ERROR";
                sem.Subline  = "last snapshot";
                sem.Permission = "NO TRADE";
                sem.Action = "NONE";
                sem.Reason = "fetch failed";
                return sem;
            }

            if (divergence == "SELL_WEAKENING"
                && phase != "QUIET")
            {
                sem.Headline = "SELL WEAKENING";
                sem.Subline = "buy response building";
                sem.Permission = "NO TRADE";
                sem.Reason = "sell context losing strength";
                return sem;
            }

            if (divergence == "BUY_WEAKENING"
                && phase != "QUIET")
            {
                sem.Headline = "BUY WEAKENING";
                sem.Subline = "sell response building";
                sem.Permission = "NO TRADE";
                sem.Reason = "buy context losing strength";
                return sem;
            }

            if (divergence == "BUY_RESPONSE"
                && phase != "QUIET")
            {
                sem.Headline = "BUY RESPONSE";
                sem.Subline = "countertrend push";
                sem.Permission = "WATCH";
                sem.Reason = "reversal emerging";
                sem.StructureSide = "BUY";
                return sem;
            }

            if (divergence == "SELL_RESPONSE"
                && phase != "QUIET")
            {
                sem.Headline = "SELL RESPONSE";
                sem.Subline = "countertrend push";
                sem.Permission = "WATCH";
                sem.Reason = "reversal emerging";
                sem.StructureSide = "SELL";
                return sem;
            }

            if (sem.Execute && bias == "BUY")
            {
                sem.Headline = "EXECUTE BUY";
                sem.Subline  = "active regime";
                sem.Permission = "ENABLED";
                sem.Reason = "buy confirmed";
            }
            else if (sem.Execute && bias == "SELL")
            {
                sem.Headline = "EXECUTE SELL";
                sem.Subline  = "active regime";
                sem.Permission = "ENABLED";
                sem.Reason = "sell confirmed";
            }
            else if (side == "BUY" || side == "SELL")
            {
                ApplyDirectionalIntermediate(sem, side, phase, bias, strength);
            }
            else if (phase == "QUIET")
            {
                sem.Headline = "QUIET";
                sem.Subline  = "balanced structure";
                sem.Permission = "BLOCKED";
                sem.Reason = "quiet market";
            }
            else
            {
                string contextSide = RecentDirectionalContext(s);
                if (contextSide == "BUY" || contextSide == "SELL")
                    ApplyDirectionalIntermediate(sem, contextSide, phase, bias, strength);
                else
                {
                    sem.Headline = "BALANCED";
                    sem.Subline  = "two-sided structure";
                    sem.Permission = phase == "ACTIVE" ? "WATCH" : "NO TRADE";
                    sem.Reason = strength < 0.40 ? "awaiting activation" : "direction unconfirmed";
                }
            }

            return sem;
        }

        private string DeriveTemporalPhase(Snapshot s)
        {
            if (s == null || s.History == null || s.History.Count < 2)
                return "QUIET";

            var h = s.History;
            int n = h.Count;
            double curr = Clamp01(s.Strength);
            string currBias = NormalizeBias(s.Bias);
            string currState = NormalizeState(s.State);

            double[] recent = new double[Math.Min(5, n)];
            for (int i = 0; i < recent.Length; i++)
                recent[i] = Clamp01(h[n - recent.Length + i].Strength);

            double avgRecent = 0;
            double maxRecent = 0;
            for (int i = 0; i < recent.Length; i++)
            {
                avgRecent += recent[i];
                if (recent[i] > maxRecent) maxRecent = recent[i];
            }
            avgRecent /= recent.Length;

            double trend = recent[recent.Length - 1] - recent[0];

            if (currState == "QUIET" || (avgRecent < 0.35 && currBias == "NEUTRAL"))
                return "QUIET";

            if (n >= 2 && currBias != "NEUTRAL")
            {
                string prevBias = NormalizeBias(h[n - 2].Bias);
                if (prevBias == "NEUTRAL" && curr >= 0.55 && trend > 0.15)
                    return "TRIGGER";
            }

            if (currState == "ACTIVE" && currBias != "NEUTRAL" && curr >= MinStrength)
                return "EXECUTE";

            if (maxRecent >= 0.75 && curr < maxRecent - 0.20 && trend < -0.15)
                return "FLUSH";

            if (maxRecent >= 0.70 && curr < maxRecent - 0.10 && Math.Abs(trend) < 0.08)
                return "POST-SHOCK";

            if (n >= 3)
            {
                string b1 = NormalizeBias(h[n - 3].Bias);
                string b2 = NormalizeBias(h[n - 2].Bias);
                if (b1 != "NEUTRAL" && currBias != "NEUTRAL" && b1 != currBias)
                    return "REBOUND";
            }

            if (trend > 0.05 && currBias != "NEUTRAL")
                return "BUILDING";

            if (currState == "TRANSITIONING") return "BUILDING";
            if (currState == "ACTIVE") return "EXECUTE";
            return "QUIET";
        }

        private string InferStructureSide(Snapshot s)
        {
            if (s == null) return "BALANCED";

            double score = 0.0, total = 0.0;
            int count = s.History == null ? 0 : Math.Min(10, s.History.Count);

            for (int i = 0; i < count; i++)
            {
                SignalPoint pt = s.History[s.History.Count - count + i];
                string b = NormalizeBias(pt.Bias);
                double recency = 1.0 + (i / (double)Math.Max(1, count - 1));
                double weight = Math.Max(0.10, Clamp01(pt.Strength)) * recency;

                if (b == "BUY") score += weight;
                else if (b == "SELL") score -= weight;
                else score *= 0.985;

                total += weight;
            }

            string currentBias = NormalizeBias(s.Bias);
            double currentWeight = Math.Max(0.10, Clamp01(s.Strength)) * 1.5;
            if (currentBias == "BUY") { score += currentWeight; total += currentWeight; }
            else if (currentBias == "SELL") { score -= currentWeight; total += currentWeight; }

            string context = RecentDirectionalContext(s);
            if (total <= 0.0) return context;

            double normalized = score / total;
            if (normalized > 0.10) return "BUY";
            if (normalized < -0.10) return "SELL";

            // Correction 1: do not show false BALANCED while a recent directional context is still alive.
            if (context == "BUY" || context == "SELL")
                return context;

            return "BALANCED";
        }


        //------------------------------------------------------------------
        // PRICE MARKER (setas no preÃƒÂ§o)
        //------------------------------------------------------------------
        private void DrawPriceMarker(string biasUp, string state, double strength)
        {
            if (strength < MinStrength || state != "ACTIVE")
            {
                RemoveDrawObject("RSQMF_MARKER_GLOW");
                return;
            }

            string tag = "RSQ_MARKER_" + CurrentBar + "_" + biasUp;
            if (lastArrowBar == tag) return;

            if (biasUp == "BUY")
            {
                Brush glow = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(70, 0, 230, 118));
                glow.Freeze();
                Draw.ArrowUp(this, "RSQMF_MARKER_GLOW", false, 0, Low[0] - (4 * TickSize), glow);

                Brush main = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(255, 0, 230, 118));
                main.Freeze();
                Draw.ArrowUp(this, tag, false, 0, Low[0] - (2 * TickSize), main);
                lastArrowBar = tag;
            }
            else if (biasUp == "SELL")
            {
                Brush glow = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(70, 255, 82, 82));
                glow.Freeze();
                Draw.ArrowDown(this, "RSQMF_MARKER_GLOW", false, 0, High[0] + (4 * TickSize), glow);

                Brush main = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(255, 255, 82, 82));
                main.Freeze();
                Draw.ArrowDown(this, tag, false, 0, High[0] + (2 * TickSize), main);
                lastArrowBar = tag;
            }
        }

        //------------------------------------------------------------------
        // ELEMENTOS DE DESIGN
        //------------------------------------------------------------------
        private void FillAccentStrip(float x, float y, float w, float h, Semantic sem)
        {
            D2DBrush b = SemanticBrush(sem);
            RenderTarget.FillRectangle(new RectangleF(x, y, 5f, h), b);
            RenderTarget.FillRectangle(new RectangleF(x, y, w, 2f), b);
        }

        private void DrawLiveDot(float x, float y, Snapshot s, Semantic sem)
        {
            float pulse = MotionPulse(sem);
            float radius = 2.5f + pulse * (sem.Phase == "QUIET" ? 1.0f : 2.0f);
            D2DBrush b = StatusBrush(s);

            if (StatusLabel(s) == "LIVE")
            {
                RenderTarget.FillEllipse(
                    new SharpDX.Direct2D1.Ellipse(new Vector2(x, y), radius + 3f, radius + 3f),
                    brushWhiteSoft);
            }
            RenderTarget.FillEllipse(
                new SharpDX.Direct2D1.Ellipse(new Vector2(x, y), radius, radius), b);
        }

        private void DrawMotionUnderline(float x, float y, float w, Semantic sem)
        {
            float transition = TransitionPulse();
            float width = w * (0.42f + transition * 0.30f);
            if (sem.Permission == "ENABLED") width = w * (0.72f + transition * 0.18f);
            else if (sem.Permission == "BLOCKED") width = w * 0.22f;
            RenderTarget.FillRectangle(new RectangleF(x, y, width, 2f), SemanticBrush(sem));
        }

        //------------------------------------------------------------------
        // BRUSH SELECTORS
        //------------------------------------------------------------------
        private D2DBrush SemanticBrush(Semantic sem)
        {
            if (sem == null) return brushNeutral;
            if (sem.Permission == "STALE") return brushMuted;
            if (sem.Headline == "SELL WEAKENING" || sem.Headline == "BUY WEAKENING")
                return brushTransition;
            if (sem.Headline.IndexOf("BUY", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (sem.Headline.IndexOf("FORMING", StringComparison.OrdinalIgnoreCase) >= 0)
                    return brushAccentBuy;
                return brushBuy;
            }
            if (sem.Headline.IndexOf("SELL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (sem.Headline.IndexOf("FORMING", StringComparison.OrdinalIgnoreCase) >= 0)
                    return brushAccentSell;
                return brushSell;
            }
            if (sem.Phase == "TRANSITIONING") return brushTransition;
            return brushNeutral;
        }

        private D2DBrush StructureBrush(Semantic sem)
        {
            if (sem.Permission == "STALE") return brushMuted;
            if (sem.StructureSide == "BUY") return brushAccentBuy;
            if (sem.StructureSide == "SELL") return brushAccentSell;
            return brushAccentNeutral;
        }

        private D2DBrush PermissionBrush(Semantic sem)
        {
            if (sem.Permission == "ENABLED")
            {
                if (sem.Action == "SELL") return brushSell;
                if (sem.Action == "BUY") return brushBuy;
                return brushActive;
            }
            if (sem.Permission == "WATCH" || sem.Permission == "NO TRADE") return brushTransition;
            if (sem.Permission == "STALE") return brushMuted;
            return brushNeutral;
        }

        private D2DBrush StateBrush(string state)
        {
            if (state == "ACTIVE") return brushActive;
            if (state == "TRANSITIONING") return brushTransition;
            return brushQuiet;
        }

        private D2DBrush StatusBrush(Snapshot s)
        {
            string status = StatusLabel(s);
            if (status == "LIVE") return StateBrush(s.State);
            if (status == "REFRESHING") return brushTransition;
            if (status == "ERROR") return brushSell;
            if (status == "STALE") return brushMuted;
            return brushMuted;
        }

        //------------------------------------------------------------------
        // FETCH Ã¢â‚¬â€ WebClient sÃƒÂ­ncrono com agendamento
        //------------------------------------------------------------------
        private void TryFetchIfDue(string source)
        {
            if (disposed || webClient == null) return;

            DateTime now = DateTime.Now;
            bool shouldFetch = false;

            lock (signalLock)
            {
                if (fetchInProgress) return;
                if (lastFetchAttemptAt == DateTime.MinValue
                    || (now - lastFetchAttemptAt).TotalSeconds >= Math.Max(0.95, RefreshSeconds))
                {
                    fetchInProgress = true;
                    lastFetchAttemptAt = now;
                    shouldFetch = true;
                }
            }

            if (!shouldFetch) return;
            DoFetch(source, now);
        }

        private void DoFetch(string source, DateTime attemptAt)
        {
            string apiKey = ApiKey ?? "";
            string sym = (Symbol ?? "BTCUSDT").Trim();
            string apiBaseUrl = (ApiBaseUrl ?? "").Trim();

            try
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    lock (signalLock)
                    {
                        consecutiveFetchFailures++;
                        lastFetchError = "no key";
                        lastHttpStatus = 0;
                    }
                    return;
                }

                if (string.IsNullOrWhiteSpace(apiBaseUrl))
                {
                    lock (signalLock)
                    {
                        consecutiveFetchFailures++;
                        lastFetchError = "no base url";
                        lastHttpStatus = 0;
                    }
                    return;
                }
                apiBaseUrl = apiBaseUrl.TrimEnd('/');

                string url = apiBaseUrl + "/signal/multi?symbol=" + Uri.EscapeDataString(sym);
                string json = DownloadSignalJson(url, apiKey);

                MultiFrameSnapshot mf = ParseMultiFrameSnapshot(json);
                string bias = (mf.Tf1s == null || string.IsNullOrEmpty(mf.Tf1s.Bias))
                    ? (string.IsNullOrEmpty(mf.Consensus) ? "neutral" : mf.Consensus)
                    : mf.Tf1s.Bias;
                string state = mf.Tf1s == null ? "quiet" : mf.Tf1s.State;
                string asOf = mf.Tf1s == null ? "" : mf.Tf1s.AsOf;
                double strength = mf.Tf1s == null ? 0.0 : mf.Tf1s.Strength;

                bias = bias.Trim().ToLowerInvariant();
                state = state.Trim().ToLowerInvariant();

                DateTime successAt = DateTime.Now;
                lock (signalLock)
                {
                    lastMultiFrame = mf;
                    lastBias = bias;
                    lastMarketState = state;
                    lastStrength = Clamp01(strength);
                    lastAsOf = asOf;
                    lastHttpStatus = 200;
                    lastFetchError = "";
                    lastSuccessfulFetchAt = successAt;
                    consecutiveFetchFailures = 0;
                    fetchCount++;

                    history.Add(new SignalPoint
                    {
                        Bias = bias,
                        Strength = Clamp01(strength),
                        State = state,
                        Time = successAt
                    });
                    while (history.Count > HistorySize) history.RemoveAt(0);
                }
            }
            catch (WebException wex)
            {
                int code = 0;
                if (wex.Response != null)
                    code = (int)((HttpWebResponse)wex.Response).StatusCode;
                lock (signalLock)
                {
                    consecutiveFetchFailures++;
                    lastFetchError = "HTTP " + code;
                    lastHttpStatus = code;
                }
                Print("RSQ fetch fail HTTP " + code);
            }
            catch (Exception ex)
            {
                lock (signalLock)
                {
                    consecutiveFetchFailures++;
                    lastFetchError = ex.Message;
                    lastHttpStatus = 0;
                }
                Print("RSQ fetch fail: " + ex.Message);
            }
            finally
            {
                lock (signalLock) fetchInProgress = false;
            }
        }

        private string DownloadSignalJson(string url, string apiKey)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 5000;
            req.ReadWriteTimeout = 5000;
            req.Proxy = null;
            req.UserAgent = "RSQ/2.1";
            req.Headers["X-API-Key"] = apiKey.Trim();

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        //------------------------------------------------------------------
        // SNAPSHOT THREAD-SAFE
        //------------------------------------------------------------------
        private Snapshot BuildSnapshot()
        {
            lock (signalLock)
            {
                return new Snapshot
                {
                    Bias = NormalizeBias(lastBias),
                    State = NormalizeState(lastMarketState),
                    AsOf = lastAsOf ?? "",
                    Error = lastFetchError ?? "",
                    Strength = Clamp01(lastStrength),
                    HttpStatus = lastHttpStatus,
                    FetchCount = fetchCount,
                    History = new List<SignalPoint>(history),
                    LastFetchAttemptAt = lastFetchAttemptAt,
                    LastSuccessfulFetchAt = lastSuccessfulFetchAt,
                    LastFetchError = lastFetchError ?? "",
                    ConsecutiveFetchFailures = consecutiveFetchFailures,
                    FetchInProgress = fetchInProgress
                };
            }
        }

        private class Snapshot
        {
            public string Bias, State, AsOf, Error, LastFetchError;
            public double Strength;
            public int HttpStatus, FetchCount, ConsecutiveFetchFailures;
            public DateTime LastFetchAttemptAt, LastSuccessfulFetchAt;
            public bool FetchInProgress;
            public List<SignalPoint> History;
        }

        //------------------------------------------------------------------
        // HELPERS
        //------------------------------------------------------------------
        private bool CanEmitPriceMarker(Snapshot s)
        {
            if (s == null || IsDataStale(s)) return false;
            if (StatusLabel(s) == "ERROR") return false;
            MultiFrameSnapshot mf;
            lock (signalLock)
                mf = CloneMultiFrame(lastMultiFrame);
            string consensus = NormalizeBias(mf.Consensus);
            return mf.AlignmentScore >= 0.66
                && consensus != "NEUTRAL"
                && NormalizeState(mf.Tf1s.State) == "ACTIVE"
                && Clamp01(mf.Tf1s.Strength) >= MinStrength;
        }

        private string FreshnessLabel(Snapshot s)
        {
            string status = StatusLabel(s);
            int age = LastSuccessAgeSeconds(s);
            if (age >= 0) return status + " " + age.ToString("00") + "s";
            return status;
        }

        private string StatusLabel(Snapshot s)
        {
            if (s == null) return "ERROR";
            if (IsDataStale(s)) return "STALE";
            if (s.FetchInProgress && s.LastSuccessfulFetchAt == DateTime.MinValue)
                return "REFRESHING";
            if (!string.IsNullOrWhiteSpace(s.LastFetchError)
                && s.LastSuccessfulFetchAt == DateTime.MinValue)
                return "ERROR";
            if (s.LastSuccessfulFetchAt != DateTime.MinValue) return "LIVE";
            return "REFRESHING";
        }

        private bool IsDataStale(Snapshot s)
        {
            if (s == null || s.FetchCount <= 0 || s.LastSuccessfulFetchAt == DateTime.MinValue)
                return false;
            return (DateTime.Now - s.LastSuccessfulFetchAt).TotalSeconds > StaleSeconds();
        }

        private double StaleSeconds() { return Math.Max(RefreshSeconds * 3, 35); }

        private int LastSuccessAgeSeconds(Snapshot s)
        {
            if (s == null || s.LastSuccessfulFetchAt == DateTime.MinValue) return -1;
            return (int)Math.Max(0, (DateTime.Now - s.LastSuccessfulFetchAt).TotalSeconds);
        }

        private string FormatAsOf(string asOf)
        {
            if (string.IsNullOrWhiteSpace(asOf)) return "--";
            DateTime parsed;
            if (DateTime.TryParse(asOf, out parsed))
                return parsed.ToUniversalTime().ToString("HH:mm'Z'");
            return asOf;
        }

        private string SymbolText()
        {
            return string.IsNullOrWhiteSpace(Symbol) ? "BTCUSDT" : Symbol.Trim().ToUpperInvariant();
        }

        private double MotionSeconds() { return (DateTime.Now - DateTime.Today).TotalSeconds; }

        private float MotionPulse(Semantic sem)
        {
            double speed = sem.Phase == "QUIET" ? 0.8
                         : sem.Permission == "ENABLED" ? 1.1
                         : 1.55;
            return (float)((Math.Sin(MotionSeconds() * speed) + 1.0) * 0.5);
        }

        private float TransitionPulse()
        {
            double age = (DateTime.Now - lastSemanticChange).TotalMilliseconds;
            if (age < 0 || age > 650) return 0f;
            double p = 1.0 - (age / 650.0);
            return (float)(p * p);
        }

        private void UpdateSemanticTransition(Semantic sem)
        {
            if (sem == null) return;
            if (string.IsNullOrEmpty(lastSemanticKey))
            {
                lastSemanticKey = sem.Key;
                lastSemanticChange = DateTime.Now;
                return;
            }
            if (lastSemanticKey != sem.Key)
            {
                lastSemanticKey = sem.Key;
                lastSemanticChange = DateTime.Now;
            }
        }

        private void RequestMotionRefresh()
        {
            if (ChartControl == null || disposed) return;
            if ((DateTime.Now - lastVisualRefresh).TotalMilliseconds < 160) return;
            lastVisualRefresh = DateTime.Now;
            try
            {
                ChartControl.Dispatcher.BeginInvoke(new Action(delegate()
                {
                    if (!disposed) ForceRefresh();
                }));
            }
            catch { /* best effort */ }
        }

        private string ParseField(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field)) return "";
            string search = "\"" + field + "\"";
            int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            int colon = json.IndexOf(':', idx + search.Length);
            if (colon < 0) return "";
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length) return "";
            if (json[start] == '"')
            {
                start++;
                int end = json.IndexOf('"', start);
                return end < 0 ? "" : json.Substring(start, end - start).Trim();
            }
            int endVal = start;
            while (endVal < json.Length && json[endVal] != ',' && json[endVal] != '}') endVal++;
            return json.Substring(start, endVal - start).Trim();
        }

        private MultiFrameSnapshot CloneMultiFrame(MultiFrameSnapshot src)
        {
            MultiFrameSnapshot dst = new MultiFrameSnapshot();
            if (src == null) return dst;
            dst.Tf1s = CloneTimeframe(src.Tf1s);
            dst.Tf1m = CloneTimeframe(src.Tf1m);
            dst.Tf5m = CloneTimeframe(src.Tf5m);
            dst.AlignmentScore = src.AlignmentScore;
            dst.Consensus = src.Consensus ?? "neutral";
            return dst;
        }

        private TimeframeData CloneTimeframe(TimeframeData src)
        {
            TimeframeData dst = new TimeframeData();
            if (src == null) return dst;
            dst.Bias = src.Bias ?? "neutral";
            dst.State = src.State ?? "quiet";
            dst.Strength = Clamp01(src.Strength);
            dst.AsOf = src.AsOf ?? "";
            return dst;
        }

        private MultiFrameSnapshot ParseMultiFrameSnapshot(string json)
        {
            MultiFrameSnapshot mf = new MultiFrameSnapshot();
            string timeframes = ParseObjectField(json, "timeframes");
            mf.Tf1s = ParseTimeframe(ParseObjectField(timeframes, "tf_1s"));
            mf.Tf1m = ParseTimeframe(ParseObjectField(timeframes, "tf_1m"));
            mf.Tf5m = ParseTimeframe(ParseObjectField(timeframes, "tf_5m"));

            double align = 0.0;
            double.TryParse(ParseField(json, "alignment_score"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out align);
            mf.AlignmentScore = Clamp01(align);
            mf.Consensus = ParseField(json, "consensus");
            if (string.IsNullOrWhiteSpace(mf.Consensus)) mf.Consensus = "neutral";
            return mf;
        }

        private TimeframeData ParseTimeframe(string json)
        {
            TimeframeData tf = new TimeframeData();
            if (string.IsNullOrEmpty(json)) return tf;
            tf.Bias = ParseField(json, "bias");
            tf.State = ParseField(json, "market_state");
            tf.AsOf = ParseField(json, "as_of");
            double strength = 0.0;
            double.TryParse(ParseField(json, "signal_strength"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out strength);
            tf.Strength = Clamp01(strength);
            if (string.IsNullOrWhiteSpace(tf.Bias)) tf.Bias = "neutral";
            if (string.IsNullOrWhiteSpace(tf.State)) tf.State = "quiet";
            return tf;
        }

        private string ParseObjectField(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field)) return "";
            string search = "\"" + field + "\"";
            int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            int colon = json.IndexOf(':', idx + search.Length);
            if (colon < 0) return "";
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start] != '{') return "";
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) return json.Substring(start, i - start + 1);
                }
            }
            return "";
        }

        private string NormalizeBias(string bias)
        {
            string b = (bias ?? "neutral").Trim().ToLowerInvariant();
            if (b == "buy") return "BUY";
            if (b == "sell") return "SELL";
            return "NEUTRAL";
        }

        private string NormalizeState(string state)
        {
            string s = (state ?? "quiet").Trim().ToLowerInvariant();
            if (s == "active") return "ACTIVE";
            if (s == "transitioning") return "TRANSITIONING";
            return "QUIET";
        }

        private double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        private string FormatPhaseAge(DateTime startedAt)
        {
            if (startedAt == DateTime.MinValue) return "--";

            double seconds = (DateTime.Now - startedAt).TotalSeconds;

            if (seconds < 3) return "NEW";
            if (seconds < 60) return ((int)seconds) + "s";
            if (seconds < 3600)
            {
                int m = (int)(seconds / 60);
                int s = (int)(seconds % 60);
                return m + "m " + s.ToString("00") + "s";
            }
            return "stable";
        }

        private D2DBrush TemporalPhaseBrush(string phase)
        {
            if (phase == "TRIGGER") return brushTransition;
            if (phase == "EXECUTE") return brushActive;
            if (phase == "FLUSH") return brushSell;
            if (phase == "POST-SHOCK") return brushMuted;
            if (phase == "REBOUND") return brushTransition;
            if (phase == "BUILDING") return brushAccentBuy;
            return brushQuiet;
        }

        //------------------------------------------------------------------
        // DX RESOURCES
        //------------------------------------------------------------------
        private void CreateDxResources()
        {
            DisposeDxResources();

            brushPanel         = NewBrush(10, 14, 22, 236);
            brushPanelAlt      = NewBrush(16, 22, 33, 238);
            brushBorder        = NewBrush(118, 132, 154, 135);
            brushText          = NewBrush(244, 247, 250, 248);
            brushMuted         = NewBrush(164, 174, 190, 220);
            brushBuy           = NewBrush(0, 232, 118, 246);
            brushSell          = NewBrush(255, 54, 78, 246);
            brushNeutral       = NewBrush(198, 205, 216, 232);
            brushActive        = NewBrush(0, 232, 118, 218);
            brushTransition    = NewBrush(244, 191, 69, 216);
            brushQuiet         = NewBrush(120, 128, 144, 160);
            brushWhiteSoft     = NewBrush(255, 255, 255, 50);
            brushRailBase      = NewBrush(42, 48, 58, 120);
            brushGridLine      = NewBrush(255, 255, 255, 38);
            brushMeterBg       = NewBrush(31, 38, 50, 226);
            brushAmbientBuy    = NewBrush(0, 232, 118, 26);
            brushAmbientSell   = NewBrush(255, 54, 78, 26);
            brushAccentBuy     = NewBrush(0, 232, 118, 205);
            brushAccentSell    = NewBrush(255, 54, 78, 205);
            brushAccentNeutral = NewBrush(188, 194, 204, 160);

            tfTiny     = NewTextFormat("Segoe UI",          10, DWFontWeight.Medium);
            tfSmall    = NewTextFormat("Segoe UI Semibold", 12, DWFontWeight.SemiBold);
            tfBody     = NewTextFormat("Segoe UI",          13, DWFontWeight.Normal);
            tfHeader   = NewTextFormat("Segoe UI Semibold", 15, DWFontWeight.SemiBold);
            tfDominant = NewTextFormat("Segoe UI Semibold", 28, DWFontWeight.Bold);
            tfHero     = NewTextFormat("Segoe UI Semibold", 22, DWFontWeight.Bold);
        }

        private D2DBrush NewBrush(byte r, byte g, byte b, byte a)
        {
            return new D2DBrush(RenderTarget, new SharpDX.Color(r, g, b, a));
        }

        private DWTextFormat NewTextFormat(string family, float size, DWFontWeight weight)
        {
            return new DWTextFormat(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                family, weight, DWFontStyle.Normal, size);
        }

        private void DrawText(string text, DWTextFormat format, D2DBrush brush,
            float x, float y, float w, float h)
        {
            DrawText(text, format, brush, x, y, w, h, DWTextAlignment.Leading);
        }

        private void DrawText(string text, DWTextFormat format, D2DBrush brush,
            float x, float y, float w, float h, DWTextAlignment alignment)
        {
            if (format == null || brush == null) return;
            format.TextAlignment = alignment;
            format.ParagraphAlignment = DWParagraphAlignment.Near;
            RenderTarget.DrawText(text ?? "", format, new RectangleF(x, y, w, h), brush);
        }

        private void DisposeDxResources()
        {
            DisposeResource(ref brushPanel);
            DisposeResource(ref brushPanelAlt);
            DisposeResource(ref brushBorder);
            DisposeResource(ref brushText);
            DisposeResource(ref brushMuted);
            DisposeResource(ref brushBuy);
            DisposeResource(ref brushSell);
            DisposeResource(ref brushNeutral);
            DisposeResource(ref brushActive);
            DisposeResource(ref brushTransition);
            DisposeResource(ref brushQuiet);
            DisposeResource(ref brushWhiteSoft);
            DisposeResource(ref brushRailBase);
            DisposeResource(ref brushGridLine);
            DisposeResource(ref brushMeterBg);
            DisposeResource(ref brushAmbientBuy);
            DisposeResource(ref brushAmbientSell);
            DisposeResource(ref brushAccentBuy);
            DisposeResource(ref brushAccentSell);
            DisposeResource(ref brushAccentNeutral);
            DisposeResource(ref tfTiny); 
            DisposeResource(ref tfSmall);
            DisposeResource(ref tfBody);
            DisposeResource(ref tfHeader);
            DisposeResource(ref tfDominant);
            DisposeResource(ref tfHero);
        }

        private void DisposeResource<T>(ref T resource) where T : class, IDisposable
        {
            if (resource != null) { resource.Dispose(); resource = null; }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RSQSignalMultiFrame[] cacheRSQSignalMultiFrame;
		public RSQSignalMultiFrame RSQSignalMultiFrame(string apiKey, string symbol, string apiBaseUrl, double minStrength, int refreshSeconds, bool showFullHud, bool showAllTimeframes)
		{
			return RSQSignalMultiFrame(Input, apiKey, symbol, apiBaseUrl, minStrength, refreshSeconds, showFullHud, showAllTimeframes);
		}

		public RSQSignalMultiFrame RSQSignalMultiFrame(ISeries<double> input, string apiKey, string symbol, string apiBaseUrl, double minStrength, int refreshSeconds, bool showFullHud, bool showAllTimeframes)
		{
			if (cacheRSQSignalMultiFrame != null)
				for (int idx = 0; idx < cacheRSQSignalMultiFrame.Length; idx++)
					if (cacheRSQSignalMultiFrame[idx] != null && cacheRSQSignalMultiFrame[idx].ApiKey == apiKey && cacheRSQSignalMultiFrame[idx].Symbol == symbol && cacheRSQSignalMultiFrame[idx].ApiBaseUrl == apiBaseUrl && cacheRSQSignalMultiFrame[idx].MinStrength == minStrength && cacheRSQSignalMultiFrame[idx].RefreshSeconds == refreshSeconds && cacheRSQSignalMultiFrame[idx].ShowFullHud == showFullHud && cacheRSQSignalMultiFrame[idx].ShowAllTimeframes == showAllTimeframes && cacheRSQSignalMultiFrame[idx].EqualsInput(input))
						return cacheRSQSignalMultiFrame[idx];
			return CacheIndicator<RSQSignalMultiFrame>(new RSQSignalMultiFrame(){ ApiKey = apiKey, Symbol = symbol, ApiBaseUrl = apiBaseUrl, MinStrength = minStrength, RefreshSeconds = refreshSeconds, ShowFullHud = showFullHud, ShowAllTimeframes = showAllTimeframes }, input, ref cacheRSQSignalMultiFrame);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RSQSignalMultiFrame RSQSignalMultiFrame(string apiKey, string symbol, string apiBaseUrl, double minStrength, int refreshSeconds, bool showFullHud, bool showAllTimeframes)
		{
			return indicator.RSQSignalMultiFrame(Input, apiKey, symbol, apiBaseUrl, minStrength, refreshSeconds, showFullHud, showAllTimeframes);
		}

		public Indicators.RSQSignalMultiFrame RSQSignalMultiFrame(ISeries<double> input , string apiKey, string symbol, string apiBaseUrl, double minStrength, int refreshSeconds, bool showFullHud, bool showAllTimeframes)
		{
			return indicator.RSQSignalMultiFrame(input, apiKey, symbol, apiBaseUrl, minStrength, refreshSeconds, showFullHud, showAllTimeframes);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RSQSignalMultiFrame RSQSignalMultiFrame(string apiKey, string symbol, string apiBaseUrl, double minStrength, int refreshSeconds, bool showFullHud, bool showAllTimeframes)
		{
			return indicator.RSQSignalMultiFrame(Input, apiKey, symbol, apiBaseUrl, minStrength, refreshSeconds, showFullHud, showAllTimeframes);
		}

		public Indicators.RSQSignalMultiFrame RSQSignalMultiFrame(ISeries<double> input , string apiKey, string symbol, string apiBaseUrl, double minStrength, int refreshSeconds, bool showFullHud, bool showAllTimeframes)
		{
			return indicator.RSQSignalMultiFrame(input, apiKey, symbol, apiBaseUrl, minStrength, refreshSeconds, showFullHud, showAllTimeframes);
		}
	}
}

#endregion
