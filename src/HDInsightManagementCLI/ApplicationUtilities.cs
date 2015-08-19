using System;
using System.Collections.Generic;
using System.Linq;

namespace HDInsightManagementCLI
{
	internal class ApplicationUtilities
	{
		public static bool TryGetArgumentValue(IList<string> args, string argumentKey, out string value)
		{	
			value = null;

			string matchingArgument;

			if (TryFindMatchingArgument(args, argumentKey, out matchingArgument))
			{
				int nameStartIndex = matchingArgument.IndexOf(":", StringComparison.OrdinalIgnoreCase) + 1;

				value = matchingArgument.Substring(nameStartIndex, matchingArgument.Length - nameStartIndex).Trim();

				if (string.IsNullOrWhiteSpace(value))
				{
					value = null;
					throw new ApplicationException(string.Format("A value was not provided for the argument {0} ", argumentKey));
				}

				return true;
			}
			return false;
		}

		public static bool HasMatchingArgument(IList<string> args, string argumentKey)
		{
			string arg;
			return TryFindMatchingArgument(args, argumentKey, out arg);
		}

		private static bool TryFindMatchingArgument(IList<string> args, string argumentKey, out string arg)
		{
			arg = null;

			//if the key ends with a colon (:) that means it has an associated value, otherwise there should be no additional characters
			//and the key is the entire argument
			//
			
			IEnumerable<string> matchingArgs;
			if(argumentKey.EndsWith(":", StringComparison.OrdinalIgnoreCase) )
			{
				matchingArgs = args.Where(a => a.StartsWith(argumentKey, StringComparison.OrdinalIgnoreCase));
			}
			else 
			{
				matchingArgs = args.Where(a => a.Equals(argumentKey, StringComparison.OrdinalIgnoreCase));
			}

			if (matchingArgs.Count() > 1)
			{
				throw new ApplicationException(string.Format("Multiple arguments of name {0} specified", argumentKey));
			}
			else if (matchingArgs.Count() == 1)
			{
				arg = matchingArgs.First();

				return true;
			}

			return false;
		}


	}
}
