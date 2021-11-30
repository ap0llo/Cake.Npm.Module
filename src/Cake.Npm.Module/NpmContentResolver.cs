using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Cake.Core;
using Cake.Core.Configuration;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Packaging;
using JetBrains.Annotations;

namespace Cake.Npm.Module
{
    /// <summary>
    /// A content resolver for packages installed with the npm package manager.
    /// </summary>
    [UsedImplicitly]
    public class NpmContentResolver : INpmContentResolver
    {
        private readonly IFileSystem _fileSystem;
        private readonly ICakeEnvironment _environment;
        private readonly ICakeLog _log;
        private readonly ICakeConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="NpmContentResolver"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="environment">The environment.</param>
        /// <param name="log">The log.</param>
        /// <param name="configuration">The Configuration.</param>
        public NpmContentResolver(
            IFileSystem fileSystem,
            ICakeEnvironment environment,
            ICakeLog log,
            ICakeConfiguration configuration)
        {
            _fileSystem = fileSystem;
            _environment = environment;
            _log = log;
            _configuration = configuration;
        }

        /// <summary>
        /// Returns the files installed by the given package.
        /// </summary>
        /// <param name="package">The package.</param>
        /// <param name="type">The package type.</param>
        /// <param name="installationLocation">The location in which to install.</param>
        /// <returns>The files installed by the given package.</returns>
        public IReadOnlyCollection<IFile> GetFiles(PackageReference package, PackageType type, ModulesInstallationLocation installationLocation)
        {
            if (type == PackageType.Addin)
            {
                throw new InvalidOperationException("NPM Module does not support Addins'");
            }

            if (type == PackageType.Tool)
            {
                return GetToolFiles(package, installationLocation);
            }

            throw new InvalidOperationException("Unknown resource type.");
        }

        private IReadOnlyCollection<IFile> GetToolFiles(PackageReference package, ModulesInstallationLocation modulesInstallationLocation = ModulesInstallationLocation.Workdir)
        {
            DirectoryPath modulesPath;
            switch (modulesInstallationLocation)
            {
                case ModulesInstallationLocation.Global:
                    modulesPath = GetGlobalPrefix()?.Combine("./bin/") ?? "./bin";
                    _log.Verbose($"Found global npm path at: {modulesPath.FullPath}");
                    _log.Verbose(
                        "Using global npm binaries folder: installation may succeed without binaries being installed");
                    break;
                case ModulesInstallationLocation.Workdir:
                    modulesPath = GetLocalInstallPath(package);
                    _log.Verbose("Using local install path: " + modulesPath?.FullPath);
                    break;
                case ModulesInstallationLocation.Tools:
                    modulesPath = GetToolsLocalInstallPath(package);
                    _log.Verbose("Using tools install path: " + modulesPath?.FullPath);
                    break;
                default:
                    throw new ArgumentException("not a known value of InstallationLocation.", nameof(modulesInstallationLocation));
            }

            if (modulesPath == null || !_fileSystem.GetDirectory(modulesPath).Exists)
            {
                throw new System.IO.DirectoryNotFoundException("Could not determine install path!");
            }

            var installRoot = _fileSystem.GetDirectory(modulesPath);
            if (installRoot.Exists)
            {
                return new ReadOnlyCollection<IFile>(installRoot.GetFiles("*", SearchScope.Recursive).ToList());
            }

            return new ReadOnlyCollection<IFile>(new List<IFile>());
        }

        private DirectoryPath GetToolsLocalInstallPath(PackageReference package)
        {
            var toolsFolder = _fileSystem.GetDirectory(
                _configuration.GetToolPath(_environment.WorkingDirectory, _environment));

            if (!toolsFolder.Exists)
            {
                toolsFolder.Create();
            }

            var modules = toolsFolder.Path.Combine("./node_modules/");
            return GetPackagePath(modules, package);
        }

        private DirectoryPath GetLocalInstallPath(PackageReference package)
        {
            var modules = _environment.WorkingDirectory.Combine("./node_modules/");
            return GetPackagePath(modules, package);
        }

        private DirectoryPath GetPackagePath(DirectoryPath modules, PackageReference package)
        {
            var packagePath = modules.Combine("./" + package.Package);
            if (_fileSystem.GetDirectory(packagePath).Exists)
            {
                return packagePath;
            }

            var scopedPackages = _fileSystem.GetDirectory(modules).GetDirectories("@*", SearchScope.Current);
            foreach (var scopedPackage in scopedPackages)
            {
                if (scopedPackage.GetDirectories("./" + package.Package, SearchScope.Current).Any())
                {
                    return scopedPackage.GetDirectories("./" + package.Package, SearchScope.Current).First().Path;
                }
            }

            return null;
        }

        private DirectoryPath GetGlobalPrefix()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("npm_config_prefix");
                if (!string.IsNullOrWhiteSpace(env))
                {
                    return new DirectoryPath(env);
                }

                if (_fileSystem.Exist(GetNpmConfigPath()))
                {
                    var config = _fileSystem.GetFile(GetNpmConfigPath()).ReadLines(System.Text.Encoding.UTF8);
                    var lines = config as IList<string> ?? config.ToList();
                    if (lines.Any(l => l.StartsWith("prefix=")))
                    {
                        return lines.First(l => l.StartsWith("prefix=")).Split('=').Last();
                    }
                }

                return GetDefaultPath();
            }
            catch
            {
                // time for some reasonable defaults
                return GetDefaultPath();
            }
        }

        private DirectoryPath GetDefaultPath()
        {
            switch (_environment.Platform.Family)
            {
                case PlatformFamily.Linux:
                case PlatformFamily.OSX:
                    return new DirectoryPath("/usr/local");
                case PlatformFamily.Windows:
                    return new DirectoryPath("C:\\Program Files\\nodejs");
                default:
                    return _environment.WorkingDirectory;
            }
        }

        private FilePath GetNpmConfigPath()
        {
            switch (_environment.Platform.Family)
            {
                case PlatformFamily.Linux:
                case PlatformFamily.OSX:
                    return new DirectoryPath(Environment.GetEnvironmentVariable("HOME"))
                        .CombineWithFilePath("./.npmrc");
                case PlatformFamily.Windows:
                    return new DirectoryPath(Environment.GetEnvironmentVariable("HOMEPATH")).CombineWithFilePath(
                        "./.npmrc");
                default:
                    return _environment.WorkingDirectory.CombineWithFilePath("./.npmrc");
            }
        }
    }
}
