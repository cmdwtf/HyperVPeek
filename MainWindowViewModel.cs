using Microsoft.Management.Infrastructure;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Security;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HyperVPeek
{
	[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
	public partial class MainWindowViewModel : INotifyPropertyChanged
	{
		#region Settings

		[JsonProperty]
		public string TargetHostname { get; set; } = string.Empty;
		[JsonProperty]
		public string TargetDomain { get; set; } = string.Empty;
		[JsonProperty]
		public string Username { get; set; } = string.Empty;
		[JsonProperty]
		public bool AutoRefresh { get; set; } = true;

		#endregion Settings

		#region State

		public string Status { get; set; } = "Disconnected";
		public bool IsDisconnected { get; private set; } = true;
		public bool IsConnected { get; private set; }
		public bool ExceededMaxEnvelopeSize { get; private set; } = false;
		public ObservableCollection<string> VirtualMachines { get; } = new();

		private readonly RemoteHyperVModel _model = new();

		#endregion State

		private const string ERROR_WSMAN_MAX_ENVELOPE_SIZE_EXCEEDED = "0x80338048";
		private const string SettingsFileName = "settings.conf";

		private readonly JsonSerializerSettings _jsonSettings = new()
		{
			Formatting = Formatting.Indented,
			DefaultValueHandling = DefaultValueHandling.Include
		};

		public bool Connect(SecureString password)
		{
			if (!IsDisconnected)
			{
				return false;
			}

			Status = $"Connecting...";

			bool didConnect = _model.Connect(TargetDomain, TargetHostname, Username, password);

			IsConnected = _model.ConnectionState == ConnectionState.Connected;
			IsDisconnected = _model.ConnectionState == ConnectionState.Disconnected;
			ExceededMaxEnvelopeSize = false;

			Status = $"Connected";

			return didConnect;
		}

		public bool Disconnect()
		{
			if (!IsConnected)
			{
				return false;
			}

			Status = $"Disconnecting...";

			bool didDisconnect = _model.Disconnect();

			IsConnected = _model.ConnectionState == ConnectionState.Connected;
			IsDisconnected = _model.ConnectionState == ConnectionState.Disconnected;
			ExceededMaxEnvelopeSize = false;

			Status = $"Disconnected";

			return didDisconnect;
		}

		public void UpdateVirtualMachineList()
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

		public BitmapSource? GetVirtualMachinePreview(string systemName, double imageWidth, double imageHeight)
		{
			if (!IsConnected)
			{
				Status = $"Can't get image for {systemName}, not connected.";
				return null;
			}

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

		public bool SetMaxEnvelopeSize(uint sizeKilobytes)
		{
			if (!IsConnected)
			{
				Status = $"Can't send SetmaxEnvelopeSize command, not connected";
				return false;
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

			return sent;
		}

		private static IsolatedStorageFile GetIsoStore()
			=> IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);

		internal void LoadSettings()
		{
			using IsolatedStorageFile isoStore = GetIsoStore();

			if (!isoStore.FileExists(SettingsFileName))
			{
				return;
			}

			try
			{
				using IsolatedStorageFileStream stream = isoStore.OpenFile(SettingsFileName, FileMode.Open, FileAccess.Read);
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

		internal void SaveSettings()
		{
			try
			{
				using IsolatedStorageFile isoStore = GetIsoStore();
				using IsolatedStorageFileStream stream = isoStore.OpenFile(SettingsFileName, FileMode.Create, FileAccess.Write);
				using StreamWriter writer = new(stream);
				writer.Write(JsonConvert.SerializeObject(this, _jsonSettings));
			}
			catch (Exception ex)
			{
				Status = $"Unable to save settings: {ex.Message}";
			}
		}
	}
}
