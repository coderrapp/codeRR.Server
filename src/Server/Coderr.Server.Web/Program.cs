﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using log4net;
using log4net.Config;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Coderr.Server.Web2
{
    public class Program
    {
        private static ILog _logger;

        public static void Main(string[] args)
        {
            ConfigureLog4Net();
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .Build();

        private static void ConfigureLog4Net()
        {
            var env = Environment.GetEnvironmentVariable("WEB_ENVIRONMENT")?.ToLower();

            string logPath;
            if (string.IsNullOrEmpty(env))
            {
                logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.config");
            }
            else
            {
                logPath = $"log4net.{env}.config";
                if (!File.Exists(logPath))
                {
                    logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"log4net.{env}.config");
                    if (!File.Exists(logPath))
                        logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.config");
                }
            }


            var repos = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.ConfigureAndWatch(repos, new FileInfo(logPath));
            _logger = LogManager.GetLogger(typeof(Program));
            _logger.Info("Started " + env + " from path " + logPath);
        }
    }


}
