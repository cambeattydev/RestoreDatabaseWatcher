using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace RestoreDatabaseWatcher
{
    public partial class FileWatcherService : ServiceBase
    {
        private string _folderPath;
        private string _fullFilePath;
        public FileWatcherService()
        {
            InitializeComponent();
            _folderPath = ConfigurationManager.AppSettings["WatchPath"];

            //Setup Service
            this.ServiceName = "FileWatcherService";
            this.CanStop = true;
            CanPauseAndContinue = true;

            //Setup logging
            this.AutoLog = false;

            ((ISupportInitialize)this.EventLog).BeginInit();
            if (!EventLog.SourceExists(this.ServiceName))
            {
                EventLog.CreateEventSource(this.ServiceName, "Application");
            }
        ((ISupportInitialize)this.EventLog).EndInit();

            this.EventLog.Source = this.ServiceName;
            this.EventLog.Log = "Application";

        }

        protected override void OnStart(string[] args)
        {
            try
            {
                fileSystemWatcher1.Path = _folderPath;
                fileSystemWatcher1.Created += FileCreatedHandler;
            }
            catch (Exception ex)
            {
                this.EventLog.WriteEntry("Error has occurred: " + ex.Message);
                throw ex;

            }
        }

        private void FileCreatedHandler(object sender, FileSystemEventArgs e)
        {
            if (CheckFileExistance(e.Name))
            {
                _fullFilePath = Path.Combine(_folderPath, e.Name);
                if (FileCompletelyCopied())
                {
                    RestoreDatabase(e.Name);
                }
            }
        }
        //Completely
        private bool FileCompletelyCopied()
        {
            var hasBeenCopied = false;
            while (!hasBeenCopied)
            {
                Thread.Sleep(2000);
                try
                {
                    var file = new FileInfo(_fullFilePath).Open(FileMode.Open);
                    //this.EventLog.WriteEntry("Setting hasBeenCopied");
                    hasBeenCopied = true;
                    file.Close();
                    //this.EventLog.WriteEntry("Set hasBeenCopied");
                }
                catch (Exception ex)
                {
                    //this.EventLog.WriteEntry("Error has occurred: " + ex.Message + " could not open");

                }
                this.EventLog.WriteEntry("hasBeenCopied;" + hasBeenCopied);
            }
            return hasBeenCopied;
        }

        private bool CheckFileExistance(string FileName)
        {
            // Get the subdirectories for the specified directory.'  
            bool IsFileExist = false;
            DirectoryInfo dir = new DirectoryInfo(_folderPath);
            if (!dir.Exists)
                IsFileExist = false;
            else
            {
                string FileFullPath = Path.Combine(_folderPath, FileName);
                if (File.Exists(FileFullPath))
                    IsFileExist = true;
            }
            return IsFileExist;

        }

        public void RestoreDatabase(string FileName)
        {
            //Contect to the database
            using (var connection = GetDatabaseConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(GetFileListCommand(FileName), connection))
                {
                    var listOfNames = new List<string>(2);
                    var reader = command.ExecuteReader();
                    while (reader.Read())
                    {

                        listOfNames.Add(reader["LogicalName"]?.ToString());

                    }
                    reader.Close();
                    command.CommandTimeout = int.MaxValue;
                    command.CommandText = GetRestoreCommand(FileName, listOfNames);
                    command.ExecuteNonQuery();
                }


                connection.Close();
            }

        }

        private string GetRestoreCommand(string FileName, List<string> logicalNames)
        {
            //RESTORE DATABASE [ASPIREDiversifiedSTG] 
            //FROM  DISK = N'C:\Temp\DiversifiedSTG.bak' WITH  FILE = 1,
            //MOVE N'ASPIREDiversified' TO N'C:\Program Files\Microsoft SQL Server\MSSQL12.MSSQLSERVER\MSSQL\DATA\ASPIREDiversifiedSTG.mdf', 
            //MOVE N'ASPIREDiversified_log' TO N'C:\Program Files\Microsoft SQL Server\MSSQL12.MSSQLSERVER\MSSQL\DATA\ASPIREDiversifiedSTG_Log.ldf', 
            //NOUNLOAD,  STATS = 5
            var databaseName = FileName.Replace(".bak", "");
            var sql = "RESTORE DATABASE [" + databaseName + "] FROM DISK = N'" + _fullFilePath + "' WITH FILE = 1, "
                + "MOVE N'" + logicalNames.ElementAt(0) + "' TO N'C:\\Program Files\\Microsoft SQL Server\\MSSQL12.MSSQLSERVER\\MSSQL\\DATA\\" + databaseName + ".mdf', "
                + "MOVE N'" + logicalNames.ElementAt(1) + "' TO N'C:\\Program Files\\Microsoft SQL Server\\MSSQL12.MSSQLSERVER\\MSSQL\\DATA\\" + databaseName + "_Log.ldf', "
                + "NOUNLOAD, STATS = 5;";
            return sql;
        }

        private string GetFileListCommand(string FileName)
        {
            var sql = "RESTORE FILELISTONLY FROM DISK = N'" + _fullFilePath + "'";
            return sql;
        }
        private static SqlConnection GetDatabaseConnection()
        {
            if (string.IsNullOrWhiteSpace(ConfigurationManager.ConnectionStrings["Default"]?.ToString()))
            {
                string server = "";
                var powershellLocation = ConfigurationManager.AppSettings["powershellLocation"];
                var powershellName = Path.Combine(powershellLocation, "PSLeaseTeamSettings.ps1");
                if (File.Exists(powershellName))
                {
                    using (var streamReader = new StreamReader(powershellName))
                    {
                        var text = streamReader.ReadToEnd();
                        if (text.Contains("DefaultDatabaseServer"))
                        {
                            var serverIndex = text.IndexOf("DefaultDatabaseServer");
                            var newLineEnd = text.IndexOf('\n', serverIndex);
                            var line = text.Substring(serverIndex, newLineEnd - serverIndex);
                            var splitValues = line.Split('=');
                            server = splitValues[1].Replace("\"", "");
                        }

                    }

                }
                //    return "Data Source=MSSQL1;Initial Catalog=AdventureWorks;"
                //+"Integrated Security=true;";
                var connectionString = "Data Source="
                    + (string.IsNullOrWhiteSpace(server) ? "(local);" : server + ";")
                    + "Initial Catalog=master;Integrated Security=true;";
                return new SqlConnection(connectionString);

            }
            return new SqlConnection(ConfigurationManager.ConnectionStrings["Default"].ToString());
        }

        protected override void OnStop()
        {
        }
    }
}
