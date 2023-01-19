using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;


using static cmdwtf.Toolkit.IEnumerable;

namespace HyperVPeek
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private readonly MainWindowViewModel _viewModel;
		private readonly DispatcherTimer _timer;

		public MainWindow()
		{
			InitializeComponent();
			_viewModel = DataContext as MainWindowViewModel ?? throw new Exception("Bad data context.");
			SetPreviewSizes(VmImagePanel.ActualWidth, VmImagePanel.ActualHeight);

			_timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(1)
			};
			_timer.Tick += Timer_Tick;
			_timer.Start();
		}

		private void Timer_Tick(object? sender, EventArgs e) => UpdateVirtualSystemImage();

		private void UpdateVirtualSystemImage()
		{
			if ((!_viewModel.AutoRefresh || !_viewModel.IsConnected))
			{
				return;
			}

			_viewModel.RefreshVirtualMachineImageCommand.Execute(null);
		}

		private void VmImagePanel_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			FrameworkElement? element = sender as FrameworkElement;
			SetPreviewSizes(element?.ActualWidth ?? 0, element?.ActualHeight ?? 0);
		}

		private void SetPreviewSizes(double width, double height)
		{
			_viewModel.VirtualMachinePreviewWidth = width;
			_viewModel.VirtualMachinePreviewHeight = height;
		}
	}
}
