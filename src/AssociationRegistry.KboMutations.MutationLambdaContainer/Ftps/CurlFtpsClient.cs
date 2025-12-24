using System.Diagnostics;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Abstractions;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Configuration;
using Microsoft.Extensions.Logging;

namespace AssociationRegistry.KboMutations.MutationLambdaContainer.Ftps;

public class CurlFtpsClient : IFtpsClient
{
    private readonly KboMutationsConfiguration _kboMutationsConfiguration;
    private readonly ILogger _logger;

    public CurlFtpsClient(
        ILogger logger,
        KboMutationsConfiguration kboMutationsConfiguration)
    {
        _logger = logger;
        _kboMutationsConfiguration = kboMutationsConfiguration;
    }

    public string GetListing(string sourceDirectory)
    {
        _logger.LogInformation($"Fetching mutation files from folder {sourceDirectory}");

        using (var process = new Process
               {
                   StartInfo = new ProcessStartInfo
                   {
                       FileName = _kboMutationsConfiguration.CurlLocation,
                       Arguments = $"--ssl-reqd " +
                                   $"{_kboMutationsConfiguration.AdditionalParams} " +
                                   $"--user {_kboMutationsConfiguration.Username}:{_kboMutationsConfiguration.Password} " +
                                   $"--cert {_kboMutationsConfiguration.CertPath} " +
                                   $"--key {_kboMutationsConfiguration.KeyPath} --key-type {_kboMutationsConfiguration.KeyType} " +
                                   (!string.IsNullOrEmpty(_kboMutationsConfiguration.CaCertPath) ? $"--cacert {_kboMutationsConfiguration.CaCertPath} " : "") +
                                   $"{sourceDirectory.TrimEnd('/') + "/"} --fail --silent --show-error",
                       RedirectStandardOutput = true,
                       RedirectStandardError = true,
                       UseShellExecute = false,
                       CreateNoWindow = true
                   }
               })
        {
            _logger.LogInformation($"Executing: {process.StartInfo.Arguments}");
            
            process.Start();
            process.WaitForExit();
            var result = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
                throw new Exception($"Could not list files at {sourceDirectory}:\nError: {error}\n\nResult: {result}");

            return result;
        }
    }
    
    public bool Download(Stream stream, string ftpSourceFilePath, string localDestinationFilePath)
    {
        using (var process = new Process
               {
                   StartInfo = new ProcessStartInfo
                   {
                       FileName = _kboMutationsConfiguration.CurlLocation,
                       Arguments = $"--ssl-reqd " +
                                   $"{_kboMutationsConfiguration.AdditionalParams} " +
                                   $"--user {_kboMutationsConfiguration.Username}:{_kboMutationsConfiguration.Password} " +
                                   $"--cert {_kboMutationsConfiguration.CertPath} " +
                                   $"--key {_kboMutationsConfiguration.KeyPath} --key-type {_kboMutationsConfiguration.KeyType} " +
                                   (!string.IsNullOrEmpty(_kboMutationsConfiguration.CaCertPath) ? $"--cacert {_kboMutationsConfiguration.CaCertPath} " : "") +
                                   $"{ftpSourceFilePath} " +
                                   $"-o {localDestinationFilePath} --fail -v --show-error",
                       RedirectStandardOutput = true,
                       RedirectStandardError = true,
                       UseShellExecute = false,
                       CreateNoWindow = true,
                   }
               })
        {
            _logger.LogInformation($"Executing: {process.StartInfo.Arguments}");

            process.Start();
            process.WaitForExit();
            var error = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                _logger.LogError($"Could not download file {ftpSourceFilePath}:\n{error}");
                return false;
            }

            var readAllBytes = File.ReadAllBytes(localDestinationFilePath);
            stream.Write(readAllBytes);
            File.Delete(localDestinationFilePath);
            
            return true;
        }
    }

    public void MoveFile(string baseUri, string ftpSourceFilePath, string ftpDestinationFilePath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _kboMutationsConfiguration.CurlLocation,
                Arguments = $"--ssl-reqd " +
                            $"{_kboMutationsConfiguration.AdditionalParams} " +
                            $"--user {_kboMutationsConfiguration.Username}:{_kboMutationsConfiguration.Password} " +
                            $"--cert {_kboMutationsConfiguration.CertPath} " +
                            $"--key {_kboMutationsConfiguration.KeyPath} --key-type {_kboMutationsConfiguration.KeyType} " +
                            (!string.IsNullOrEmpty(_kboMutationsConfiguration.CaCertPath) ? $"--cacert {_kboMutationsConfiguration.CaCertPath} " : "") +
                            $"{baseUri} " +
                            $"-Q \"-RNFR {ftpSourceFilePath.TrimStart('/')}\" " +
                            $"-Q \"-RNTO {ftpDestinationFilePath.TrimStart('/')}\" --fail --silent --show-error",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        _logger.LogInformation($"Executing: {process.StartInfo.Arguments}");

        process.Start();
        process.WaitForExit();

        var error = process.StandardError.ReadToEnd();

        if (process.ExitCode != 0)
            _logger.LogError($"Could not move file {ftpSourceFilePath} to {ftpDestinationFilePath}:\n{error}");
    }
}