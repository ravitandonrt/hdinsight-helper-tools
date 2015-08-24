using log4net;
using Microsoft.Azure.Management.HDInsight.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HDInsightManagementCLI
{
    public static class HDInsightManagementCLIHelpers
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(HDInsightManagementCLI));
        /// <summary>
        /// Regex to validate cluster user password.
        /// Checks for following
        /// - Minimum of 10 characters
        /// - Atleast one upper case character
        /// - Atleast one lower case character
        /// - Atleast one number
        /// - Atleast one non whitespace special character
        /// DONT CHANGE this without corresponding change to the AUX code base.
        /// </summary>
        public const string HDInsightPasswordValidationRegex =
            @"^.*(?=.{10,})(?=.*[a-z])(?=.*\d)(?=.*[A-Z])(?=.*[^A-Za-z0-9]).*$";

        public static string GetResourceGroupNameFromClusterId(string clusterId)
        {
            var resourceGroupUriPortion = "resourceGroups/";
            var startIndex = clusterId.IndexOf(resourceGroupUriPortion, StringComparison.OrdinalIgnoreCase) + resourceGroupUriPortion.Length;
            var endIndex = clusterId.IndexOf("/", startIndex, StringComparison.OrdinalIgnoreCase);

            return clusterId.Substring(startIndex, endIndex - startIndex);
        }

        public static string GetResourceGroupName(string subscriptionId, string location, string extensionPrefix = "hdinsight")
        {
            string hashedSubId = string.Empty;
            using (SHA256 sha256 = SHA256Managed.Create())
            {
                hashedSubId = Base32NoPaddingEncode(sha256.ComputeHash(UTF8Encoding.UTF8.GetBytes(subscriptionId)));
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}{1}-{2}", extensionPrefix, hashedSubId, location.Replace(' ', '-'));
        }

        private static string Base32NoPaddingEncode(byte[] data)
        {
            const string Base32StandardAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

            StringBuilder result = new StringBuilder(Math.Max((int)Math.Ceiling(data.Length * 8 / 5.0), 1));

            byte[] emptyBuffer = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] workingBuffer = new byte[8];

            // Process input 5 bytes at a time
            for (int i = 0; i < data.Length; i += 5)
            {
                int bytes = Math.Min(data.Length - i, 5);
                Array.Copy(emptyBuffer, workingBuffer, emptyBuffer.Length);
                Array.Copy(data, i, workingBuffer, workingBuffer.Length - (bytes + 1), bytes);
                Array.Reverse(workingBuffer);
                ulong val = BitConverter.ToUInt64(workingBuffer, 0);

                for (int bitOffset = ((bytes + 1) * 8) - 5; bitOffset > 3; bitOffset -= 5)
                {
                    result.Append(Base32StandardAlphabet[(int)((val >> bitOffset) & 0x1f)]);
                }
            }

            return result.ToString();
        }

        public static void CreateRdpFile(string clusterDnsName, string rdpUsername, string rdpPassword)
        {
            Logger.InfoFormat("Creating RDP file...");

            try
            {
                var roleName = "IsotopeHeadNode";
                var rdpFileName = clusterDnsName + String.Format("_{0}_IN_0.rdp", roleName);
                var rdpFileContents = new StringBuilder();
                rdpFileContents.AppendLine(String.Format("full address:s:{0}.cloudapp.net", clusterDnsName));
                rdpFileContents.AppendLine(String.Format("username:s:{0}", rdpUsername));
                rdpFileContents.AppendLine(String.Format("LoadBalanceInfo:s:Cookie: mstshash={0}#{0}_IN_0",
                                                         roleName));
                rdpFileContents.AppendLine(String.Format("#RdpPassword={0}", rdpPassword));
                File.WriteAllText(rdpFileName, rdpFileContents.ToString());

                Logger.InfoFormat("RDP file for Cluster: {0} is available at: {1} (password is available in the file contents).", clusterDnsName, Path.Combine(Directory.GetCurrentDirectory(), rdpFileName));
            }
            catch (Exception)
            {
                Logger.InfoFormat("Unable to create rdp file for cluster: {0}", clusterDnsName);
                throw;
            }
        }

        public static System.Reflection.PropertyInfo GetPropertyInfo<TObj, TProp>(
            this TObj obj,
            Expression<Func<TObj, TProp>> propertyAccessor)
        {
            var memberExpression = propertyAccessor.Body as MemberExpression;
            if (memberExpression != null)
            {
                var propertyInfo = memberExpression.Member as System.Reflection.PropertyInfo;
                if (propertyInfo != null)
                {
                    return propertyInfo;
                }
            }
            throw new ArgumentException("propertyAccessor");
        }

        public static string ToDisplayString<T>(this T item, bool multiline = true, bool ignoreInheritedProperties = false)
        {
            if (item == null)
            {
                return "null";
            }

            Type itemType = item.GetType();

            ICollection collection = item as ICollection;
            if (collection != null)
            {
                List<string> itemDisplayStrings = new List<string>();

                foreach (var obj in collection)
                {
                    itemDisplayStrings.Add(ToDisplayString(obj, multiline, ignoreInheritedProperties));
                }

                return string.Join(multiline ? "," + Environment.NewLine : ", ", itemDisplayStrings);
            }
            else if (itemType.IsEnum)
            {
                if (itemType.GetCustomAttributes(typeof(FlagsAttribute), false).Any())
                {
                    return string.Join(" | ", Enum.GetValues(itemType).Cast<Enum>().Where((item as Enum).HasFlag));
                }
                else
                {
                    return item.ToString();
                }
            }
            else if (itemType.GetMethod("ToString", System.Type.EmptyTypes).DeclaringType.Equals(itemType))
            {
                try
                {
                    return item.ToString();
                }
                catch (Exception e)
                {
                    return string.Format("Error({0})", e.Message);
                }
            }

            System.Reflection.BindingFlags bindingFlags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.ExactBinding | System.Reflection.BindingFlags.GetProperty;

            if (ignoreInheritedProperties)
            {
                bindingFlags |= System.Reflection.BindingFlags.DeclaredOnly;
            }

            return string.Join(
                multiline ? Environment.NewLine : " ",
                itemType.GetProperties(bindingFlags)
                    .Select(p =>
                    {
                        string displayString;
                        try
                        {
                            if (p.GetIndexParameters().Length > 0)
                            {
                                displayString = "Indexed Property (unavailable)";
                            }
                            else
                            {
                                displayString = ToDisplayString(p.GetValue(item, null), multiline);
                            }
                        }
                        catch (Exception e)
                        {
                            displayString = string.Format("Error({0})", e.Message);
                        }
                        return string.Format("{0}: {1}", p.Name, displayString);
                    }));
        }

        public static KeyValuePair<string, string> GenerateSshKeyPair(string keyName, string keyPassword)
        {
            var privateKeyName = keyName + ".key";
            if (File.Exists(privateKeyName))
            {
                throw new ApplicationException("A Private key already exists with same name, please move it before generating a new one. Name: " + privateKeyName);
            }
            Logger.InfoFormat("Generating a new Ssh key pair. Name: {0}, Passphrase: {1}", privateKeyName, keyPassword);
            try
            {
                RunExecutable("ssh-keygen.exe", String.Format("-t rsa -C {0} -f {1} -N {2}", keyName, privateKeyName, keyPassword));
            }
            catch (Exception)
            {
                Logger.Error("Failed to generate keys, please make sure you are running this executable from Git shell as it uses the ssh-keygen.exe provided by Git." + 
                    Environment.NewLine);
                throw;
            }
            return new KeyValuePair<string, string>(privateKeyName + ".pub", privateKeyName);
        }

        /// <summary>
        /// Blocking executable launch
        /// </summary>
        /// <param name="exePath"></param>
        /// <param name="exeArgs"></param>
        /// <param name="workingDir"></param>
        public static void RunExecutable(string exePath, string exeArgs, string workingDir = null)
        {
            Logger.InfoFormat("Running executable - Path: {0}, Args: {1}, WorkingDir: {2}",
                exePath, exeArgs, workingDir);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = false;
            startInfo.UseShellExecute = false;
            startInfo.FileName = exePath;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.Arguments = exeArgs;

            if (!String.IsNullOrWhiteSpace(workingDir))
            {
                startInfo.WorkingDirectory = workingDir;
            }

            try
            {
                using (Process exeProcess = Process.Start(startInfo))
                {
                    exeProcess.WaitForExit();
                    if (exeProcess.ExitCode != 0)
                    {
                        var message = String.Format("Executable returned non-zero exit code. Path: {0}, Code: {1}",
                            exePath, exeProcess.ExitCode);
                        throw new ApplicationException(message);
                    }
                }
            }
            catch (Exception ex)
            {
                var message = String.Format("Executable failed. Path: {0}, Args: {1}, WorkingDir: {2}",
                    exePath, exeArgs, workingDir);
                Logger.Error(message, ex);
                throw new ApplicationException(message, ex);
            }

            Logger.InfoFormat("Executable run successfully - Path: {0}, Args: {1}, WorkingDir: {2}",
                exePath, exeArgs, workingDir);
        }

        /// <summary>
        /// Non blocking process launch
        /// </summary>
        /// <param name="exePath"></param>
        /// <param name="exeArgs"></param>
        /// <param name="workingDir"></param>
        public static void LaunchProcess(string exePath, string exeArgs, string workingDir = null)
        {
            Logger.InfoFormat("Launch Process - Path: {0}, Args: {1}, WorkingDir: {2}",
                exePath, exeArgs, workingDir);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = exePath;
            startInfo.Arguments = exeArgs;

            if (!String.IsNullOrWhiteSpace(workingDir))
            {
                startInfo.WorkingDirectory = workingDir;
            }

            try
            {
                Process exeProcess = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                var message = String.Format("Launch failed. Path: {0}, Args: {1}, WorkingDir: {2}",
                    exePath, exeArgs, workingDir);
                Logger.Error(message, ex);
                throw new ApplicationException(message, ex);
            }

            Logger.InfoFormat("Process launched successfully - Path: {0}, Args: {1}, WorkingDir: {2}",
                exePath, exeArgs, workingDir);
        }

        public static string GetUserClusterTablePrefix(Cluster cluster)
        {
            Regex alphaNumRgx = new Regex("[^a-zA-Z0-9]");
            string sanitizedClusterName = alphaNumRgx.Replace(cluster.Name, string.Empty);
            if (sanitizedClusterName.Length > 20)
            {
                sanitizedClusterName = sanitizedClusterName.Substring(0, 20);
            }
            return String.Format("u{0}{1}", sanitizedClusterName, cluster.Properties.CreatedDate.AddMinutes(1).ToString("ddMMMyyyyATHH")).ToLowerInvariant();
        }
    }
}
