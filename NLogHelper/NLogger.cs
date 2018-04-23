using BaseHelper;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NLogHelper
{
    class NLogMsg
    {
        public NLogger.LogLevel level = NLogger.LogLevel.Debug;
        public string msg = null;
    }
    public class NLogger
    {
        public enum LogLevel
        {
            Debug = 0,
            Error = 1,
            Info = 2,
            Fatal = 3,
            Trace = 4,
            Warn = 5,
            Off = 6
        }
        Logger logger { get; set; } = null;
        bool isInitialized { get; set; } = false;
        List<Task> writethread { get; } = new List<Task>();
        bool isRunning { get; set; } = false;
        //private static object obj{ get; } = log4net.Config.BasicConfigurator.Configure();
        objPool<NLogMsg> Pool { get; } = new objPool<NLogMsg>(() => new NLogMsg());
        SpinQueue<NLogMsg> queue { get; } = new SpinQueue<NLogMsg>();

        public Logger GetLogger()
        {
            return logger;
        }

        public static NLogger Instance { get; } = new NLogger();

        public void Init(string DirectoryPath, string LogName, Encoding encoding, LogLevel loglevel, long MaximumFileSize = 1024 * 1024 * 20)
        {
            isRunning = true;

            // Step 1. Create configuration object 
            var config = new LoggingConfiguration();

            // Step 2. Create targets and add them to the configuration 
            var fileTarget = new FileTarget();
            config.AddTarget(LogName, fileTarget);

			// Step 3. Set target properties 
			fileTarget.Layout = "${message}";
			fileTarget.FileName = DirectoryPath + "/" + LogName + ".log";
            fileTarget.ArchiveAboveSize = MaximumFileSize;
            fileTarget.ArchiveEvery = FileArchivePeriod.Day;
            fileTarget.ArchiveFileName = DirectoryPath + "/${shortdate}/" + LogName + "_{#}.log";
            fileTarget.ArchiveNumbering = ArchiveNumberingMode.Sequence;
            fileTarget.MaxArchiveFiles = 1000;
            fileTarget.Encoding = encoding;

			NLog.LogLevel lv = NLog.LogLevel.Debug;
            if (loglevel == LogLevel.Debug)
                lv = NLog.LogLevel.Debug;
            else if (loglevel == LogLevel.Error)
                lv = NLog.LogLevel.Error;
            else if (loglevel == LogLevel.Fatal)
                lv = NLog.LogLevel.Fatal;
            else if (loglevel == LogLevel.Info)
                lv = NLog.LogLevel.Info;
            else if (loglevel == LogLevel.Off)
                lv = NLog.LogLevel.Off;
            else if (loglevel == LogLevel.Trace)
                lv = NLog.LogLevel.Trace;
            else if (loglevel == LogLevel.Warn)
                lv = NLog.LogLevel.Warn;

            // Step 4. Define rules
            var rule = new LoggingRule(LogName, lv, fileTarget);
            config.LoggingRules.Add(rule);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;
            logger = LogManager.GetLogger(LogName);
            writethread.Add(Task.Factory.StartNew(() => WriteToFile(), TaskCreationOptions.LongRunning));

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                foreach (var ex in args.Exception.InnerExceptions)
                {
                    logger.Fatal(ex.ToString());
                }
                args.SetObserved();
            };

            isInitialized = true;

            logger.Info(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff Program Started!"));
        }

        public void Init(string DirectoryPath, string LogName, LogLevel lv, long MaximumFileSize = 1024 * 1024 * 20)
        {
            Init(DirectoryPath, LogName, Encoding.ASCII, lv, MaximumFileSize);
        }

        public bool IsLogLevelEnabled(LogLevel lv)
        {
            if (lv == LogLevel.Debug)
                return logger.IsEnabled(NLog.LogLevel.Debug);
            else if (lv == LogLevel.Error)
                return logger.IsEnabled(NLog.LogLevel.Error);
            else if (lv == LogLevel.Fatal)
                return logger.IsEnabled(NLog.LogLevel.Fatal);
            else if (lv == LogLevel.Info)
                return logger.IsEnabled(NLog.LogLevel.Info);
            else if (lv == LogLevel.Off)
                return logger.IsEnabled(NLog.LogLevel.Off);
            else if (lv == LogLevel.Trace)
                return logger.IsEnabled(NLog.LogLevel.Trace);
            else if (lv == LogLevel.Warn)
                return logger.IsEnabled(NLog.LogLevel.Warn);
            else return false;
        }

        public void WriteLog(LogLevel level, string msg)
        {
            if (isInitialized && isRunning)
            {
                if (IsLogLevelEnabled(level))
                {
                    try
                    {
                        NLogMsg logmsg = Pool.Checkout();

                        if (logmsg == null)
                        {
                            logger.Fatal("LibraryLogger logmsg == null");
                            return;
                        }

                        logmsg.level = level;
						logmsg.msg = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "|" + level + "|" + Thread.CurrentThread.ManagedThreadId + " " + msg;

                        queue.Enqueue(logmsg);
                    }
                    catch (Exception ex)
                    {
                        logger.Fatal(ex, "LibraryLogger Exception");
                    }
                }
            }
        }

        private void WriteToFile()
        {
			StringBuilder sb = new StringBuilder();
			NLogMsg logmsg = null;

			while (isRunning || !queue.IsQueueEmpty)
            {
                try
                {
					sb.Clear();
					bool gotItem = true;
					while (gotItem)
					{
						gotItem = queue.TryDequeue(out logmsg);
						if (gotItem && logmsg != null && !string.IsNullOrEmpty(logmsg.msg))
						{
							if (sb.Length > 0)
								sb.AppendLine();
							sb.Append(logmsg.msg);
						}
						if (sb.Length > 1024 * 1024 || queue.IsQueueEmpty)
							break;
					}

					if (sb.Length > 0)
						logger.Info(sb.ToString());
				}
                catch (OperationCanceledException)
                {
                    logger.Info("LibraryLogger Shutdown in progress");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "LibraryLogger Exception");
                }
                finally
                {
                    if (logmsg != null)
                    {
                        Pool.Checkin(logmsg);
                    }
                }
            }
        }

        public void Shutdown()
        {
            if (logger != null)
            {
                isRunning = false;
                queue.ShutdownGracefully();
                Task.WaitAll(writethread.ToArray());
                logger.Info(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff LibraryLogger Shutdown"));
                queue.Dispose();
            }
        }
    }
}
