using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security;

using static cmdwtf.Toolkit.IEnumerable;

namespace HyperVPeek
{
	public class RemoteHyperVModel
	{
		// WIM Class Names
		private const string MsvmVirtualSystemManagementService = "Msvm_VirtualSystemManagementService";
		private const string MsvmComputerSystem = "Msvm_ComputerSystem";

		// Property Names
		private const string ElementName = "ElementName";
		private const string Caption = "Caption";

		// Method Names
		private const string GetVirtualSystemThumbnailImage = "GetVirtualSystemThumbnailImage";

		// Values
		private const string VirtualMachineCaption = "Virtual Machine";

		// Argument names
		private const string TargetSystem = "TargetSystem";
		private const string WidthPixels = "WidthPixels";
		private const string HeightPixels = "HeightPixels";
		private const string ImageData = "ImageData";

		public ConnectionState ConnectionState
		{
			get
			{
				if (_session is not null && _virtualSystemManagementService is not null)
				{
					return ConnectionState.Connected;
				}
				else if (_session is not null)
				{
					return ConnectionState.Connecting;
				}

				return ConnectionState.Disconnected;
			} 
		}

		public bool IsLocal { get; private set; }
		internal string LastVmTargeted { get; set; } = string.Empty;
		private CimSession? _session;
		private CimInstance? _virtualSystemManagementService;

		public bool Connect(string domain, string hostname, string username, SecureString password)
		{
			if (ConnectionState == ConnectionState.Connected)
			{
				return false;
			}

			CimCredential creds = new(PasswordAuthenticationMechanism.Default, domain, username, password);

			using WSManSessionOptions options = new()
			{
				Timeout = TimeSpan.FromSeconds(1.0),
				MaxEnvelopeSize = (1024 * 1024), // 1MB envelope size.
			};

			// compare local domain / machine name to target
			var ipgp = IPGlobalProperties.GetIPGlobalProperties();

			bool noDomain = string.IsNullOrWhiteSpace(domain)
				|| domain == "."
				|| domain.Equals(ipgp.DomainName, StringComparison.OrdinalIgnoreCase);
			bool noTargetMachine = string.IsNullOrWhiteSpace(hostname)
				|| hostname == "."
				|| hostname.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
				|| hostname.Equals(ipgp.HostName, StringComparison.OrdinalIgnoreCase);
			IsLocal = noDomain && noTargetMachine;

			// can't use creds on the local machine
			if (!IsLocal)
			{
				options.AddDestinationCredentials(creds);
			}

			_session = IsLocal
				? CimSession.Create(null, options)
				: CimSession.Create(hostname, options);

			_virtualSystemManagementService = _session.SelectAll(MsvmVirtualSystemManagementService).First();

			return true;
		}

		public bool Disconnect()
		{
			if (ConnectionState == ConnectionState.Disconnected)
			{
				return false;
			}

			_session?.Dispose();
			_session = null;

			_virtualSystemManagementService?.Dispose();
			_virtualSystemManagementService = null;

			return true;
		}

		public IEnumerable<string> GetVirtualMachineList()
		{
			IEnumerable<CimInstance> systemsQuery = _session?.Select(ElementName, $"{MsvmComputerSystem} where {Caption} = '{VirtualMachineCaption}'")
				?? Array.Empty<CimInstance>();
			return systemsQuery.Select
					(s => s.CimInstanceProperties[ElementName]?.Value?.ToString())
				.NotNull();
		}

		public byte[] GetVirtualMachinePreview(string systemName, ushort imageWidth, ushort imageHeight)
		{
			using CimInstance? vm = _session?.SelectAll($"{MsvmComputerSystem} where {ElementName} = '{systemName}'").First();

			if (vm == null)
			{
				throw new RemoteHyperVException($"Failed to find virtual machine named {systemName}");
			}

			CimMethodParametersCollection parameters = new()
			{
				CimMethodParameter.Create(TargetSystem, vm, CimType.Reference, CimFlags.In),
				CimMethodParameter.Create(WidthPixels, imageWidth, CimFlags.In),
				CimMethodParameter.Create(HeightPixels, imageHeight, CimFlags.In),
			};

			CimMethodResult? result = _session?.InvokeMethod(_virtualSystemManagementService, GetVirtualSystemThumbnailImage, parameters);

			return result?.OutParameters[ImageData].Value is not byte[] imageData || imageData.Length == 0
				? throw new RemoteHyperVException($"Failed to get image data for {systemName}")
				: imageData;
		}

		public bool SetMaxEnvelopeSize(uint sizeKilobytes)
			=> ConnectionState == ConnectionState.Connected
				&& _session?.SetMaxEnvelopeSize(sizeKilobytes) == 0;
	}
}
