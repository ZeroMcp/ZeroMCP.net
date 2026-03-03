using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;

var config = DefaultConfig.Instance
    .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Trend));

BenchmarkSwitcher.FromAssembly(typeof(Runner).Assembly).Run(args, config);

internal static class Runner { }
