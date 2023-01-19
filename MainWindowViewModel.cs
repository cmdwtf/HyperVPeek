using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Management.Infrastructure;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Xceed.Wpf.Toolkit;

namespace HyperVPeek
{
	[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
	public partial class MainWindowViewModel : ObservableObject
	{
		#region Settings

		[ObservableProperty]
		[property: JsonProperty]
		private string _targetHostname = string.Empty;
		[ObservableProperty]
		[property: JsonProperty]
		private string _targetDomain = string.Empty;
		[ObservableProperty]
		[property: JsonProperty]
		private string _username = string.Empty;
		[ObservableProperty]
		[property: JsonProperty]
		private bool _autoRefresh = true;

		#endregion Settings

		#region State

		[ObservableProperty]
		private string _status = "Disconnected";
		[ObservableProperty]
		private bool _isDisconnected = true;
		[ObservableProperty]
		private bool _isConnected = false;
		[ObservableProperty]
		private bool _exceededMaxEnvelopeSize = false;
		[ObservableProperty]
		private ObservableCollection<string> _virtualMachines = new();
		[ObservableProperty]
		private BitmapSource? _lastVirtualMachineImage;

		[ObservableProperty]
		private string _selectedVirtualMachine = string.Empty;

		[ObservableProperty]
		private double _virtualMachinePreviewWidth = 0d;
		[ObservableProperty]
		private double _virtualMachinePreviewHeight = 0d;
		[ObservableProperty]
		private Transform _virtualMachineRenderTransform = Transform.Identity;

		private readonly RemoteHyperVModel _model = new();

		#endregion State

		private const string ERROR_WSMAN_MAX_ENVELOPE_SIZE_EXCEEDED = "0x80338048";
		private const string SettingsFileName = "settings.conf";

		private readonly JsonSerializerSettings _jsonSettings = new()
		{
			Formatting = Formatting.Indented,
			DefaultValueHandling = DefaultValueHandling.Include
		};

		private static readonly IsolatedStorageFile IsoStore =
			IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);

		[RelayCommand]
		private void Connect(WatermarkPasswordBox pwBox)
		{
			if (!IsDisconnected)
			{
				return;
			}

			Status = $"Connecting...";

			try
			{
				bool didConnect = _model.Connect(TargetDomain, TargetHostname, Username, pwBox.SecurePassword);

				IsConnected = _model.ConnectionState == ConnectionState.Connected;
				IsDisconnected = _model.ConnectionState == ConnectionState.Disconnected;
				ExceededMaxEnvelopeSize = false;
			}
			catch (Exception ex)
			{
				Status = $"Failed to connect: {ex.Message}";
				return;
			}

			Status = $"Connected";
		}

		[RelayCommand]
		private void Disconnect()
		{
			if (!IsConnected)
			{
				return;
			}

			Status = $"Disconnecting...";

			bool didDisconnect = _model.Disconnect();

			IsConnected = _model.ConnectionState == ConnectionState.Connected;
			IsDisconnected = _model.ConnectionState == ConnectionState.Disconnected;
			ExceededMaxEnvelopeSize = false;

			Status = $"Disconnected";
		}


		[RelayCommand]
		private void UpdateVirtualMachineList()
		{
			Status = $"Getting virtual machine list...";

			VirtualMachines.Clear();

			IEnumerable<string> machines = GetVirtualMachineList();

			foreach (string machine in machines)
			{
				VirtualMachines.Add(machine);
			}

			Status = $"Got virtual machine list ({VirtualMachines.Count})";
		}

		public IEnumerable<string> GetVirtualMachineList()
		{
			if (!IsConnected)
			{
				return Array.Empty<string>();
			}

			try
			{
				return _model.GetVirtualMachineList();
			}
			catch (RemoteHyperVException rhvex)
			{
				Status = rhvex.Message;
			}
			catch (CimException cex)
			{
				Status = $"Cim Error: {cex.Message}";
			}

			return Array.Empty<string>();
		}

		[RelayCommand]
		private void RefreshVirtualMachineImage()
		{
			if (VirtualMachinePreviewWidth == 0 ||
				VirtualMachinePreviewHeight == 0)
			{
				return;
			}

			LastVirtualMachineImage = GetVirtualMachinePreview(
				SelectedVirtualMachine,
				VirtualMachinePreviewWidth,
				VirtualMachinePreviewHeight);
		}

		public BitmapSource? GetVirtualMachinePreview(string systemName, double imageWidth, double imageHeight)
		{
			if (!IsConnected)
			{
				Status = $"Can't get image for {systemName}, not connected";
				return null;
			}

			if (string.IsNullOrEmpty(systemName))
			{
				Status = "Please select a VM";
				return null;
			}

			if (!SelectedVirtualMachine.Equals(systemName, StringComparison.InvariantCultureIgnoreCase))
			{
				SelectedVirtualMachine = systemName;
			}

			VirtualMachinePreviewWidth = imageWidth;
			VirtualMachinePreviewHeight = imageHeight;

			Status = $"Getting image for {systemName}";

			ushort width = (ushort)imageWidth;
			ushort height = (ushort)imageHeight;

			try
			{
				byte[] imageData = _model.GetVirtualMachinePreview(systemName, width, height);

				if (imageData.Length > 0) 
				{
					ExceededMaxEnvelopeSize = false;
				}

				double dpiX = 96d;
				double dpiY = 96d;
				PixelFormat pixelFormat = PixelFormats.Bgr565;
				int stride = ((width * pixelFormat.BitsPerPixel) + 7) / 8;

				Status = $"{systemName}: last updated {DateTimeOffset.Now}";

				return BitmapSource.Create(width, height, dpiX, dpiY, pixelFormat, null, imageData, stride);
			}
			catch (RemoteHyperVException rhvex)
			{
				Status = rhvex.Message;
			}
			catch (CimException cex)
			{
				if (cex.MessageId.Contains(ERROR_WSMAN_MAX_ENVELOPE_SIZE_EXCEEDED))
				{
					ExceededMaxEnvelopeSize = true;
					Status = "Error: message envelope size exceeded the maximum allowed value";
				}
				else
				{
					Status = $"Cim Error: {cex.Message}";
				}
			}

			return null;
		}

		[RelayCommand]
		private void SetMaxEnvelopeSize(uint sizeKilobytes)
		{
			Guard.IsGreaterThan(sizeKilobytes, 0);

			if (!IsConnected)
			{
				Status = $"Can't send SetmaxEnvelopeSize command, not connected";
				return;
			}

			ExceededMaxEnvelopeSize = false;

			bool sent = false;

			try
			{
				sent = _model.SetMaxEnvelopeSize(sizeKilobytes);

				Status = sent
					? "Sent SetmaxEnvelopeSize command"
					: "Failed to send SetmaxEnvelopeSize command";
			}
			catch (RemoteHyperVException rhvex)
			{
				Status = rhvex.Message;
			}
			catch (CimException cex)
			{
				Status = $"Cim Error: {cex.Message}";
			}
		}

		[RelayCommand]
		private void LoadSettings()
		{
			if (!IsoStore.FileExists(SettingsFileName))
			{
				return;
			}

			try
			{
				using IsolatedStorageFileStream stream = IsoStore.OpenFile(SettingsFileName, FileMode.Open, FileAccess.Read);
				using StreamReader reader = new(stream);
				using JsonTextReader jsonReader = new(reader);
				var serializer = JsonSerializer.Create(_jsonSettings);
				serializer.Populate(jsonReader, this);
			}
			catch (Exception ex)
			{
				Status = $"Unable to load settings: {ex.Message}";
			}
		}

		[RelayCommand]
		private void SaveSettings()
		{
			try
			{
				using IsolatedStorageFileStream stream = IsoStore.OpenFile(SettingsFileName, FileMode.Create, FileAccess.Write);
				using StreamWriter writer = new(stream);
				string serializedSettings = JsonConvert.SerializeObject(this, _jsonSettings);
				writer.Write(serializedSettings);
			}
			catch (Exception ex)
			{
				Status = $"Unable to save settings: {ex.Message}";
			}
		}

		partial void OnSelectedVirtualMachineChanged(string value) => RefreshVirtualMachineImage();
	}
}
