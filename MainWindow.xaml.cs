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
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;


using static cmdwtf.Toolkit.IEnumerable;

namespace HyperVPeek
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private readonly MainWindowViewModel _viewModel;
		private string _lastVmTargeted = string.Empty;
		private readonly DispatcherTimer _timer;

		public MainWindow()
		{
			InitializeComponent();
			_viewModel = DataContext as MainWindowViewModel ?? throw new Exception("Bad data context.");

			_timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(1)
			};
			_timer.Tick += Timer_Tick;
		}

		private void Window_Loaded(object sender, RoutedEventArgs e) => _viewModel.LoadSettings();

		private void Window_Closed(object sender, EventArgs e) => _viewModel.SaveSettings();

		private void Timer_Tick(object? sender, EventArgs e) => UpdateVirtualSystemImage();

		private void Refresh_Click(object sender, RoutedEventArgs e) => UpdateVirtualSystemImage();

		private void Connect_Click(object sender, RoutedEventArgs e)
		{
			_viewModel.Connect(PasswordTextBox.SecurePassword);
			_viewModel.UpdateVirtualMachineList();
		}

		private void Disconnect_Click(object sender, RoutedEventArgs e)
		{
			_timer.Stop();
			_viewModel.Disconnect();
			UpdateVirtualSystemImage(null);
		}

		private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			_lastVmTargeted = (e.Source as ListBox)?.SelectedItem as string ?? string.Empty;
			UpdateVirtualSystemImage();
		}

		private void UpdateVirtualSystemImage()
		{
			if (!_viewModel.AutoRefresh && _timer.IsEnabled)
			{
				_timer.Stop();
				return;
			}

			UpdateVirtualSystemImage(_lastVmTargeted);

			if (_viewModel.AutoRefresh && _timer.IsEnabled == false)
			{
				_timer.Start();
			}
		}

		private void UpdateVirtualSystemImage(string? vmName)
		{
			_lastVmTargeted = vmName ?? string.Empty;

			if (string.IsNullOrEmpty(vmName))
			{
				VmImage.Source = null;
				return;
			}

			BitmapSource? img = _viewModel.GetVirtualMachinePreview(vmName, VmImagePanel.ActualWidth, VmImagePanel.ActualHeight);
			VmImage.Source = img;
		}

		public static BitmapImage ToImage(byte[] array)
		{
			using var ms = new System.IO.MemoryStream(array);
			var image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = ms;
			image.EndInit();
			return image;
		}

		private void Debug_Click(object sender, RoutedEventArgs e) => _viewModel.SetMaxEnvelopeSize(51200);

		private void EnlargeMaxEnvelopeSize_Click(object sender, RoutedEventArgs e) =>
			// hopefully 2mb is big enough?
			_viewModel.SetMaxEnvelopeSize(2048);
	}
}
