using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HyperVPeek
{
	/// <summary>
	/// An exception for when something went wrong with a remote Hyper-V operation.
	/// </summary>
	public class RemoteHyperVException : ApplicationException
	{
		/// <inheritdoc/>
		public RemoteHyperVException(string? message) : base(message)
		{
		}

		/// <inheritdoc/>
		public RemoteHyperVException(string? message, Exception? innerException) : base(message, innerException)
		{
		}
	}
}
