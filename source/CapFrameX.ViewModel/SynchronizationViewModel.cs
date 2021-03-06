﻿using CapFrameX.Contracts.Configuration;
using CapFrameX.Contracts.Data;
using CapFrameX.EventAggregation.Messages;
using CapFrameX.Data;
using CapFrameX.Statistics;
using LiveCharts;
using MathNet.Numerics.Statistics;
using OxyPlot;
using OxyPlot.Axes;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CapFrameX.ViewModel
{
	public class SynchronizationViewModel : BindableBase, INavigationAware
	{
		private readonly IStatisticProvider _frametimeStatisticProvider;
		private readonly IEventAggregator _eventAggregator;
		private readonly IAppConfiguration _appConfiguration;

		private PlotModel _synchronizationModel;
		private SeriesCollection _histogramCollection;
		private SeriesCollection _droppedFramesStatisticCollection;
		private string[] _droppedFramesLabels;
		private string[] _histogramLabels;
		private bool _useUpdateSession;
		private Session _session;
		private IFileRecordInfo _recordInfo;
		private string _frametimeDisplayChangedTimeCorrelation = "0%";
		private string _currentGameName;
		private string _syncRangePercentage = "0%";
		private string _syncRangeLower;
		private string _syncRangeUpper;

		/// <summary>
		/// https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings
		/// </summary>
		public Func<double, string> HistogramFormatter { get; } =
			value => value.ToString("N", CultureInfo.InvariantCulture);

		/// <summary>
		/// https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings
		/// </summary>
		public Func<ChartPoint, string> PieChartPointLabel { get; } =
			chartPoint => string.Format(CultureInfo.InvariantCulture, "{0} ({1:P})", chartPoint.Y, chartPoint.Participation);

		public PlotModel SynchronizationModel
		{
			get { return _synchronizationModel; }
			set
			{
				_synchronizationModel = value;
				RaisePropertyChanged();
			}
		}

		public SeriesCollection HistogramCollection
		{
			get { return _histogramCollection; }
			set
			{
				_histogramCollection = value;
				RaisePropertyChanged();
			}
		}

		public SeriesCollection DroppedFramesStatisticCollection
		{
			get { return _droppedFramesStatisticCollection; }
			set
			{
				_droppedFramesStatisticCollection = value;
				RaisePropertyChanged();
			}
		}

		public string[] DroppedFramesLabels
		{
			get { return _droppedFramesLabels; }
			set
			{
				_droppedFramesLabels = value;
				RaisePropertyChanged();
			}
		}

		public string[] HistogramLabels
		{
			get { return _histogramLabels; }
			set
			{
				_histogramLabels = value;
				RaisePropertyChanged();
			}
		}

		public string FrametimeDisplayChangedTimeCorrelation
		{
			get { return _frametimeDisplayChangedTimeCorrelation; }
			set
			{
				_frametimeDisplayChangedTimeCorrelation = value;
				RaisePropertyChanged();
			}
		}

		public string CurrentGameName
		{
			get { return _currentGameName; }
			set
			{
				_currentGameName = value;
				RaisePropertyChanged();
			}
		}

		public string SyncRangeLower
		{
			get { return _syncRangeLower; }
			set
			{
				_syncRangeLower = value;
				_appConfiguration.SyncRangeLower = value;
				RaisePropertyChanged();
				OnSyncRangeChanged();
			}
		}

		public string SyncRangeUpper
		{
			get { return _syncRangeUpper; }
			set
			{
				_syncRangeUpper = value;
				_appConfiguration.SyncRangeUpper = value;
				RaisePropertyChanged();
				OnSyncRangeChanged();
			}
		}

		public string SyncRangePercentage
		{
			get { return _syncRangePercentage; }
			set
			{
				_syncRangePercentage = value;
				RaisePropertyChanged();
			}
		}

		public ICommand CopyDisplayChangeTimeValuesCommand { get; }

		public ICommand CopyHistogramDataCommand { get; }

		public SynchronizationViewModel(IStatisticProvider frametimeStatisticProvider,
										IEventAggregator eventAggregator,
										IAppConfiguration appConfiguration)
		{
			_frametimeStatisticProvider = frametimeStatisticProvider;
			_eventAggregator = eventAggregator;
			_appConfiguration = appConfiguration;

			CopyDisplayChangeTimeValuesCommand = new DelegateCommand(OnCopyDisplayChangeTimeValues);
			CopyHistogramDataCommand = new DelegateCommand(CopyHistogramData);

			_syncRangeLower = _appConfiguration.SyncRangeLower;
			_syncRangeUpper = _appConfiguration.SyncRangeUpper;

			SubscribeToUpdateSession();

			SynchronizationModel = new PlotModel
			{
				PlotMargins = new OxyThickness(40, 10, 0, 40),
				PlotAreaBorderColor = OxyColor.FromArgb(64, 204, 204, 204),
			};
		}

		private void SubscribeToUpdateSession()
		{
			_eventAggregator.GetEvent<PubSubEvent<ViewMessages.UpdateSession>>()
							.Subscribe(msg =>
							{
								_session = msg.CurrentSession;
								_recordInfo = msg.RecordInfo;

								if (_useUpdateSession)
								{
									// Do update actions
									CurrentGameName = msg.RecordInfo.GameName;
									UpdateCharts();
									FrametimeDisplayChangedTimeCorrelation =
										GetCorrelation(msg.CurrentSession);
									SyncRangePercentage = GetSyncRangePercentageString();
								}
							});
		}

		private string GetCorrelation(Session currentSession)
		{
			var frametimes = currentSession.FrameTimes.Where((x,i) => !_session.AppMissed[i]);
			var displayChangedTimes = currentSession.Displaytimes.Where((x, i) => !_session.AppMissed[i]);

			if (frametimes.Count() != displayChangedTimes.Count())
				return "NaN";

			var correlation = Correlation.Pearson(frametimes, displayChangedTimes);
			return Math.Round(correlation * 100, 0).ToString(CultureInfo.InvariantCulture) + "%";
		}

		private void OnCopyDisplayChangeTimeValues()
		{
			if (_session == null)
				return;

			StringBuilder builder = new StringBuilder();

			foreach (var dcTime in _session.Displaytimes)
			{
				builder.Append(dcTime + Environment.NewLine);
			}

			Clipboard.SetDataObject(builder.ToString(), false);
		}

		private void CopyHistogramData()
		{
			if (HistogramCollection == null || HistogramLabels == null)
				return;

			StringBuilder builder = new StringBuilder();
			var chartValues = HistogramCollection.First().Values;

			foreach (var bin in HistogramLabels.Select((value, i) => new { i, value }))
			{
				builder.Append(bin.value.ToString(CultureInfo.InvariantCulture) + "\t" + chartValues[bin.i]
					.ToString() + Environment.NewLine);
			}

			Clipboard.SetDataObject(builder.ToString(), false);
		}

		private void OnSyncRangeChanged()
			=> SyncRangePercentage = GetSyncRangePercentageString();

		private void UpdateCharts()
		{
			if (_session == null)
				return;

			// Do not run on background thread, leads to errors on analysis page
			SetFrameDisplayTimesChart(_session.FrameTimes, _session.Displaytimes);
			Task.Factory.StartNew(() => SetHistogramChart(_session.Displaytimes));
			Task.Factory.StartNew(() => SetDroppedFramesChart(_session.AppMissed));
		}

		private void SetFrameDisplayTimesChart(IList<double> frametimes, IList<double> displaytimes)
		{
			var yMin = Math.Min(frametimes.Min(), displaytimes.Min());
			var yMax = Math.Max(frametimes.Max(), displaytimes.Max());

			var frametimeSeries = new OxyPlot.Series.LineSeries
			{
				Title = "Frametimes",
				StrokeThickness = 1,
				LegendStrokeThickness = 4,
				Color = ColorRessource.FrametimeStroke
			};

			var displayChangedTimesSeries = new OxyPlot.Series.LineSeries
			{
				Title = "Display changed times",
				StrokeThickness = 1,
				LegendStrokeThickness = 4,
				Color = ColorRessource.FrametimeMovingAverageStroke
			};

			frametimeSeries.Points.AddRange(frametimes.Select((x, i) => new DataPoint(i, x)));
			displayChangedTimesSeries.Points.AddRange(displaytimes.Select((x, i) => new DataPoint(i, x)));

			Application.Current.Dispatcher.Invoke(new Action(() =>
			{
				var tmp = new PlotModel
				{
					PlotMargins = new OxyThickness(40, 10, 0, 40),
					PlotAreaBorderColor = OxyColor.FromArgb(64, 204, 204, 204),
					LegendPosition = LegendPosition.TopCenter,
					LegendOrientation = LegendOrientation.Horizontal
				};

				tmp.Series.Add(frametimeSeries);
				tmp.Series.Add(displayChangedTimesSeries);

				//Axes
				//X
				tmp.Axes.Add(new LinearAxis()
				{
					Key = "xAxis",
					Position = OxyPlot.Axes.AxisPosition.Bottom,
					Title = "Samples",
					Minimum = 0,
					Maximum = frametimes.Count,
					MajorGridlineStyle = LineStyle.Solid,
					MajorGridlineThickness = 1,
					MajorGridlineColor = OxyColor.FromArgb(64, 204, 204, 204),
					MinorTickSize = 0,
					MajorTickSize = 0
				});

				//Y
				tmp.Axes.Add(new LinearAxis()
				{
					Key = "yAxis",
					Position = OxyPlot.Axes.AxisPosition.Left,
					Title = "Frametime + display change time [ms]",
					Minimum = yMin - (yMax - yMin) / 6,
					Maximum = yMax + (yMax - yMin) / 6,
					MajorGridlineStyle = LineStyle.Solid,
					MajorGridlineThickness = 1,
					MajorGridlineColor = OxyColor.FromArgb(64, 204, 204, 204),
					MinorTickSize = 0,
					MajorTickSize = 0
				});

				SynchronizationModel = tmp;
			}));
		}

		private void SetHistogramChart(List<double> displaytimes)
		{
			var discreteDistribution = _frametimeStatisticProvider.GetDiscreteDistribution(displaytimes);
			var histogram = new Histogram(displaytimes, discreteDistribution.Length);

			var bins = new List<double>();
			var histogramValues = new ChartValues<double>();

			for (int i = 0; i < discreteDistribution.Length; i++)
			{
				var count = discreteDistribution[i].Count;
				var avg = count > 0 ?
						  discreteDistribution[i].Average() :
						  (histogram[i].UpperBound + histogram[i].LowerBound) / 2;

				bins.Add(avg);
				histogramValues.Add(count);
			}

			Application.Current.Dispatcher.BeginInvoke(new Action(() =>
			{
				HistogramCollection = new SeriesCollection
				{
					new LiveCharts.Wpf.ColumnSeries
					{
						Title = "Display changed time distribution",
						Values = histogramValues,
						Fill = new SolidColorBrush(Color.FromRgb(241, 125, 32)),
						DataLabels = true,
					}
				};

				HistogramLabels = bins.Select(bin => Math.Round(bin, 2)
					.ToString(CultureInfo.InvariantCulture)).ToArray();
			}));
		}

		private void SetDroppedFramesChart(List<bool> appMissed)
		{
			if (!appMissed.Any())
				return;

			Application.Current.Dispatcher.BeginInvoke(new Action(() =>
			{
				DroppedFramesStatisticCollection = new SeriesCollection()
				{
					new LiveCharts.Wpf.PieSeries
					{
						Title = "Synced frames",
						Values = new ChartValues<int>(){ appMissed.Count(flag => flag == false) },
						DataLabels = true,
						Foreground = Brushes.Black,
						LabelPosition = PieLabelPosition.InsideSlice,
						LabelPoint = PieChartPointLabel,
						FontSize = 12
					},
					new LiveCharts.Wpf.PieSeries
					{
						Title = "Dropped frames",
						Values = new ChartValues<int>(){ appMissed.Count(flag => flag == true) },
						DataLabels = true,
						Foreground = Brushes.Black,
						LabelPosition = PieLabelPosition.InsideSlice,
						LabelPoint = PieChartPointLabel,
						FontSize = 12
					}
				};
			}));
		}

		private string GetSyncRangePercentageString()
		{
			if (string.IsNullOrWhiteSpace(SyncRangeLower) ||
				string.IsNullOrWhiteSpace(SyncRangeUpper))
				return "NaN";

			int lowerValue = Convert.ToInt32(SyncRangeLower);
			int upperValue = Convert.ToInt32(SyncRangeUpper);
			var percentage = _session.GetSyncRangePercentage(lowerValue, upperValue);

			return (Math.Round(percentage * 100, 0))
				.ToString() + "%";
		}

		public void OnNavigatedTo(NavigationContext navigationContext)
		{
			_useUpdateSession = true;

			if (_session != null && _recordInfo != null)
			{
				CurrentGameName = _recordInfo.GameName;
				UpdateCharts();
				FrametimeDisplayChangedTimeCorrelation =
					GetCorrelation(_session);
				SyncRangePercentage = GetSyncRangePercentageString();
			}
		}

		public bool IsNavigationTarget(NavigationContext navigationContext)
		{
			return true;
		}

		public void OnNavigatedFrom(NavigationContext navigationContext)
		{
			_useUpdateSession = false;
		}
	}
}
