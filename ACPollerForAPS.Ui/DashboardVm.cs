using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ACPollerForAPS.Core;

namespace PipelineConfigWpf
{
    /// <summary>
    /// ViewModel du dashboard : lit l'historique des passages (stats/ à côté de
    /// l'exe) et expose des agrégats pour l'exploitation — volumétrie, taux
    /// d'erreur, par jour et par pipeline. Lecture seule, rafraîchissable.
    /// </summary>
    public class DashboardVm : ObservableObject
    {
        private readonly StatsStore _store;
        private int _rangeDays = 30;

        public DashboardVm()
        {
            var exeDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            _store = new StatsStore(Path.Combine(exeDir, "stats"));
            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        public RelayCommand RefreshCommand { get; }

        public int RangeDays
        {
            get => _rangeDays;
            set { if (Set(ref _rangeDays, value)) Refresh(); }
        }
        public int[] RangeOptions => new[] { 7, 30, 90 };

        // cartes de synthèse (période courante)
        private int _totalRuns, _totalFiles, _totalDelivered, _totalPending, _totalProblems;
        public int TotalRuns { get => _totalRuns; private set => Set(ref _totalRuns, value); }
        public int TotalFiles { get => _totalFiles; private set => Set(ref _totalFiles, value); }
        public int TotalDelivered { get => _totalDelivered; private set => Set(ref _totalDelivered, value); }
        public int TotalPending { get => _totalPending; private set => Set(ref _totalPending, value); }
        public int TotalProblems { get => _totalProblems; private set => Set(ref _totalProblems, value); }
        public string ErrorRatePct { get; private set; } = "0 %";
        public string LastRunText { get; private set; } = "no run yet";

        // séries pour les barres (avec hauteur relative pré-calculée en 0..100)
        public ObservableCollection<BarVm> ByDayBars { get; } = new ObservableCollection<BarVm>();
        public ObservableCollection<BarVm> ByPipelineBars { get; } = new ObservableCollection<BarVm>();
        public ObservableCollection<StatsBucket> ByPipelineRows { get; } = new ObservableCollection<StatsBucket>();

        public bool HasData => TotalRuns > 0;
        public string EmptyHint => "No statistics yet. The dashboard fills in as the service processes files (stats/ folder next to the service executable).";

        public void Refresh()
        {
            List<RunStats> rows;
            try { rows = _store.ReadSince(_rangeDays); }
            catch { rows = new List<RunStats>(); }

            TotalRuns = rows.Count;
            TotalFiles = rows.Sum(r => r.FilesProcessed);
            TotalDelivered = rows.Sum(r => r.OutputsDelivered);
            TotalPending = rows.Sum(r => r.OutputsPending);
            TotalProblems = rows.Sum(r => r.Problems);
            ErrorRatePct = TotalRuns == 0 ? "0 %"
                : (100.0 * TotalProblems / TotalRuns).ToString("0.#") + " %";
            var last = rows.OrderByDescending(r => r.Timestamp).FirstOrDefault();
            LastRunText = last == null ? "no run yet"
                : string.Format("{0:yyyy-MM-dd HH:mm} — {1} ({2} files, {3} problem(s))",
                    last.Timestamp, last.Pipeline, last.FilesProcessed, last.Problems);

            // barres par jour (volumétrie = fichiers traités)
            var byDay = StatsStore.ByDay(rows);
            FillBars(ByDayBars, byDay, b => b.FilesProcessed, b => b.Label);

            // barres + table par pipeline
            var byPipe = StatsStore.ByPipeline(rows);
            FillBars(ByPipelineBars, byPipe, b => b.FilesProcessed, b => b.Label);
            ByPipelineRows.Clear();
            foreach (var b in byPipe) ByPipelineRows.Add(b);

            OnPropertyChanged(nameof(ErrorRatePct));
            OnPropertyChanged(nameof(LastRunText));
            OnPropertyChanged(nameof(HasData));
        }

        private static void FillBars(ObservableCollection<BarVm> target,
            List<StatsBucket> buckets, Func<StatsBucket, int> value, Func<StatsBucket, string> label)
        {
            target.Clear();
            int max = buckets.Count == 0 ? 0 : buckets.Max(value);
            foreach (var b in buckets)
            {
                int v = value(b);
                double h = max == 0 ? 0 : (100.0 * v / max);
                target.Add(new BarVm
                {
                    Label = label(b),
                    Value = v,
                    HeightPct = h,
                    HasProblems = b.Problems > 0
                });
            }
        }
    }

    /// <summary>Une barre du graphe (hauteur relative 0..100 pré-calculée).</summary>
    public class BarVm
    {
        public string Label { get; set; }
        public int Value { get; set; }
        public double HeightPct { get; set; }   // 0..100
        public bool HasProblems { get; set; }    // colore la barre si anomalies
    }
}
