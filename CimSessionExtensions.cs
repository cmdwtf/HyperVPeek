using Microsoft.Management.Infrastructure;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HyperVPeek
{
	/// <summary>
	/// Small extensions to make dealing with <see cref="CimSession"/> nicer.
	/// </summary>
	public static class CimSessionExtensions
	{
		private const string Wildcard = "*";
		private const string NamespaceCim = @"root\cimv2";
		private const string NamespaceVirtualization = @"root\virtualization\v2";
		private const string WqlDialect = "wql";

		private const string Win32Process = "Win32_Process";
		private const string Win32ProcessCreateMethod = "Create";
		private const string Win32ProcessArgumentCommandLine = "CommandLine";

		public static IEnumerable<CimInstance> SelectAll(this CimSession session, string query) => session.Select(Wildcard, query);

		public static IEnumerable<CimInstance> Select(this CimSession session, string what, string query) => session.QueryInstances(NamespaceVirtualization, WqlDialect, $"select {what} from {query}");

		public static uint ExecuteRemoteProcess(this CimSession session, string cmd)
		{
			CimMethodParametersCollection p = new()
			{
				CimMethodParameter.Create(Win32ProcessArgumentCommandLine, cmd, CimFlags.In),
			};

			CimInstance w32Process = new(Win32Process, NamespaceCim);
			CimMethodResult results = session.InvokeMethod(w32Process, Win32ProcessCreateMethod, p);

			return Convert.ToUInt32(results.ReturnValue.Value);
		}

		public static uint SetMaxEnvelopeSize(this CimSession session, uint maxEnvelopeSizekb)
		{
			string powershellCommand = @$"Set-Item -Path WSMan:\localhost\MaxEnvelopeSizekb -Value {maxEnvelopeSizekb}";
			string fullCommand = $"powershell {powershellCommand}";

			return session.ExecuteRemoteProcess(fullCommand);
		}
	}
}
