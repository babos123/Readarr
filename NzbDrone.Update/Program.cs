﻿using System;
using System.IO;
using System.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Ninject;
using NzbDrone.Common;
using NzbDrone.Update.Providers;

namespace NzbDrone.Update
{
    public class Program
    {
        private readonly UpdateProvider _updateProvider;
        private readonly ProcessProvider _processProvider;
        private static StandardKernel _kernel;

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public Program(UpdateProvider updateProvider, ProcessProvider processProvider)
        {
            _updateProvider = updateProvider;
            _processProvider = processProvider;
        }

        public static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Starting NzbDrone Update Client");

                InitLoggers();
                _kernel = new StandardKernel();

                logger.Info("Updating NzbDrone to version {0}", _kernel.Get<EnviromentProvider>().Version);
                _kernel.Get<Program>().Start(args);
            }
            catch (Exception e)
            {
                logger.FatalException("An error has occurred while applying update package.", e);
            }

            TransferUpdateLogs();

        }

        private static void TransferUpdateLogs()
        {
            try
            {
                var enviromentProvider = _kernel.Get<EnviromentProvider>();
                var diskProvider = _kernel.Get<DiskProvider>();
                logger.Info("Copying log tiles to application directory.");
                diskProvider.CopyDirectory(enviromentProvider.GetSandboxLogFolder(), enviromentProvider.GetUpdateLogFolder());
            }
            catch (Exception e)
            {
                logger.FatalException("Can't copy upgrade log files to target folder", e);
            }
        }

        private static void InitLoggers()
        {
            LogConfiguration.RegisterConsoleLogger(LogLevel.Trace);
            LogConfiguration.RegisterUdpLogger();


            var lastUpgradeLog = new FileTarget();
            lastUpgradeLog.AutoFlush = true;
            lastUpgradeLog.ConcurrentWrites = false;
            lastUpgradeLog.FileName = Path.Combine(PathExtentions.UPDATE_LOG_FOLDER_NAME, DateTime.Now.ToString("yyyy.MM.dd-H-mm") + ".txt");
            lastUpgradeLog.KeepFileOpen = false;
            lastUpgradeLog.Layout = "${longdate} - ${logger}: ${message} ${exception:format=ToString}";

            LogManager.Configuration.AddTarget(lastUpgradeLog.GetType().Name, lastUpgradeLog);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, lastUpgradeLog));

            LogConfiguration.RegisterExceptioneer();
            LogConfiguration.Reload();
        }

        public void Start(string[] args)
        {
            VerfityArguments(args);
            int processId = ParseProcessId(args);

            FileInfo exeFileInfo = new FileInfo(_processProvider.GetProcessById(processId).StartPath);
            string appPath = exeFileInfo.Directory.FullName;

            logger.Info("Starting update process");
            _updateProvider.Start(appPath);
        }

        private int ParseProcessId(string[] args)
        {
            int id = 0;
            if (!Int32.TryParse(args[0], out id) || id <= 0)
            {
                throw new ArgumentOutOfRangeException("Invalid process id: " + args[0]);
            }

            return id;
        }

        private void VerfityArguments(string[] args)
        {
            if (args == null || args.Length != 2)
                throw new ArgumentException("Wrong number of parameters were passed in.");
        }
    }
}
